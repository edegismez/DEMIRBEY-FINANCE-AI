using System.Globalization;
using Demirbey.Application;
using Demirbey.Domain;
using Demirbey.Infrastructure.Logo;

namespace Demirbey.Tests;

public class ChequeTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static Cheque C(long id, int off, decimal amt, ChequeCategory cat, ChequeDocType doc = ChequeDocType.MusteriCeki) =>
        new(id, $"{id}", "Banka", "Firma", Today.AddDays(off), amt, doc, 0, cat);

    [Fact]
    public void Agirlikli_ortalama_vade_tutara_gore_hesaplanir()
    {
        // (10×100.000 + 40×300.000) / 400.000 = 32,5 gün
        var list = new[] { C(1, 10, 100_000, ChequeCategory.Tahsilatta), C(2, 40, 300_000, ChequeCategory.Portfoyde) };
        Assert.Equal(32.5m, ChequeAnalytics.WeightedAverageDays(list, Today));
    }

    [Fact]
    public void Agirlikli_vade_kendi_ceklerimizi_ve_kapali_cekleri_dahil_etmez()
    {
        var list = new[]
        {
            C(1, 10, 100_000, ChequeCategory.Tahsilatta),
            C(2, 99, 900_000, ChequeCategory.Odenecek, ChequeDocType.KendiCekimiz),
            C(3, 99, 900_000, ChequeCategory.Kapali),
        };
        Assert.Equal(10m, ChequeAnalytics.WeightedAverageDays(list, Today));
    }

    [Fact]
    public void Teminat_iskonto_ve_portfoy_cekleri_nakit_akisina_girmez()
    {
        var list = new[]
        {
            C(1, 1, 100, ChequeCategory.Teminatta), C(2, 1, 100, ChequeCategory.Iskontoda), C(3, 1, 100, ChequeCategory.Portfoyde),
            C(4, 1, 100, ChequeCategory.Tahsilatta), C(5, 1, 100, ChequeCategory.Odenecek, ChequeDocType.KendiCekimiz),
        };
        var ev = ChequeAnalytics.ToCashEvents(list).ToList();
        Assert.Equal(2, ev.Count);
        Assert.Contains(ev, e => e.Kind == CashEventKind.CekTahsilat && e.Certainty == Certainty.Beklenen);
        Assert.Contains(ev, e => e.Kind == CashEventKind.CekOdeme);
    }

    [Fact]
    public void Ayni_cek_iki_kez_gelse_de_bir_kez_sayilir()
    {
        var list = new[] { C(7, 1, 500, ChequeCategory.Tahsilatta), C(7, 1, 500, ChequeCategory.Tahsilatta) };
        Assert.Single(ChequeAnalytics.ToCashEvents(list));
    }

    [Fact]
    public void Tanimsiz_logo_durumu_riskli_isaretlenir_kendi_cekimiz_odenecektir()
    {
        var r = new ChequeCategoryResolver(ChequeCategoryResolver.DefaultMap);
        Assert.Equal(ChequeCategory.Riskli, r.Resolve(ChequeDocType.MusteriCeki, 999));
        Assert.Equal(ChequeCategory.Tahsilatta, r.Resolve(ChequeDocType.MusteriCeki, 4));
        Assert.Equal(ChequeCategory.Odenecek, r.Resolve(ChequeDocType.KendiCekimiz, 9));
    }
}

public class CashFlowTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static CashEvent E(int off, CashEventKind k, decimal amt, Scope s = Scope.Ticari, string? src = null) =>
        new(Today.AddDays(off), k, amt, s, Certainty.Kesin, src ?? Guid.NewGuid().ToString(), "");

    [Fact]
    public void Sahsi_odemeler_ticari_nakit_akisina_eklenmez()
    {
        var f = CashFlowEngine.Forecast([E(1, CashEventKind.SahsiKart, 999_999, Scope.Sahsi), E(2, CashEventKind.CekOdeme, 100)], 1_000, Today, 7);
        Assert.Equal(100, f.Outflow);
        Assert.Equal(900, f.ClosingBalance);
    }

    [Fact]
    public void Ilk_nakit_acigi_gunu_bulunur()
    {
        var f = CashFlowEngine.Forecast([E(2, CashEventKind.CekTahsilat, 500), E(5, CashEventKind.CekOdeme, 2_000), E(6, CashEventKind.CekTahsilat, 3_000)], 1_000, Today, 10);
        Assert.NotNull(f.FirstDeficit);
        Assert.Equal(Today.AddDays(5), f.FirstDeficit!.Date);
        Assert.Equal(-500, f.FirstDeficit.Balance);
        Assert.Equal(2_500, f.ClosingBalance);
    }

    [Fact]
    public void Ufuk_disindaki_olaylar_ve_tekrar_eden_kaynak_sayilmaz()
    {
        var f = CashFlowEngine.Forecast([E(3, CashEventKind.KrediTaksit, 100, src: "k1"), E(3, CashEventKind.KrediTaksit, 100, src: "k1"), E(40, CashEventKind.KrediTaksit, 100)], 0, Today, 30);
        Assert.Equal(100, f.Outflow);
        Assert.Equal(31, f.Daily.Count);
    }
}

public class WeeklyReportTests
{
    [Fact]
    public void Rapor_haftasi_bir_sonraki_pazartesi_baslar()
    {
        Assert.Equal(new DateOnly(2026, 10, 19), WeeklyReportBuilder.NextReportWeekStart(new DateOnly(2026, 10, 18))); // Pazar
        Assert.Equal(new DateOnly(2026, 10, 19), WeeklyReportBuilder.NextReportWeekStart(new DateOnly(2026, 10, 19))); // Pazartesi
        Assert.Equal(new DateOnly(2026, 10, 26), WeeklyReportBuilder.NextReportWeekStart(new DateOnly(2026, 10, 21))); // Çarşamba
    }

    [Fact]
    public void Gun_gun_siralar_tahsilat_once_ve_iskonto_teminat_ceklerini_listeler()
    {
        var mon = new DateOnly(2026, 10, 19);
        var events = new[]
        {
            new CashEvent(mon.AddDays(1), CashEventKind.KrediTaksit, 200, Scope.Ticari, Certainty.Kesin, "a", "Şekerbank"),
            new CashEvent(mon.AddDays(1), CashEventKind.CekTahsilat, 500, Scope.Ticari, Certainty.Beklenen, "b", "XYZ"),
            new CashEvent(mon.AddDays(3), CashEventKind.SahsiKart, 80, Scope.Sahsi, Certainty.Kesin, "c", "Bonus"),
            new CashEvent(mon.AddDays(9), CashEventKind.CekOdeme, 999, Scope.Ticari, Certainty.Kesin, "d", "Hafta dışı"),
        };
        var cheques = new[]
        {
            new Cheque(1, "1", "Akbank", "Delta", mon.AddDays(2), 340, ChequeDocType.MusteriCeki, 13, ChequeCategory.Iskontoda),
            new Cheque(2, "2", "Akbank", "Karya", mon.AddDays(30), 450, ChequeDocType.MusteriCeki, 3, ChequeCategory.Teminatta),
            new Cheque(3, "3", "Akbank", "Sera", mon.AddDays(2), 275, ChequeDocType.MusteriCeki, 1, ChequeCategory.Portfoyde),
        };

        var r = WeeklyReportBuilder.Build(mon, events, cheques, includePersonal: true);

        Assert.Equal(7, r.Days.Count);
        Assert.Equal(CashEventKind.CekTahsilat, r.Days[1].Items[0].Kind);
        Assert.Equal(200, r.CommercialPayments);
        Assert.Equal(80, r.PersonalPayments);
        Assert.Equal(300, r.CommercialNet);
        Assert.Equal(2, r.DiscountedAndCollateralCheques.Count);          // portföy çeki listelenmez
        Assert.Single(r.Days[2].DiscountedOrCollateralDue);              // iskontodaki çek Çarşamba
    }

    [Fact]
    public void Sahsi_dahil_degilse_raporda_sahsi_kalem_yer_almaz()
    {
        var mon = new DateOnly(2026, 10, 19);
        var r = WeeklyReportBuilder.Build(mon, [new CashEvent(mon, CashEventKind.SahsiKart, 80, Scope.Sahsi, Certainty.Kesin, "c", "Bonus")], [], includePersonal: false);
        Assert.Equal(0, r.PersonalPayments);
        Assert.DoesNotContain("Şahsi", WeeklyReportWhatsAppFormatter.Format(r));
    }
}

public class NotificationTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static DueItem Card(string key, int off, decimal amt, Scope s = Scope.Ticari, PaymentStatus st = PaymentStatus.Bekliyor) =>
        new(key, s == Scope.Ticari ? DueKind.TicariKart : DueKind.SahsiKart, s, Today.AddDays(off), amt, key, st);

    [Fact]
    public void Kural_gunlerinde_hatirlatma_uretilir_diger_gunlerde_uretilmez()
    {
        var n = ReminderPlanner.PlanForDay(Today, [Card("a", 3, 10), Card("b", 5, 10)], ReminderRules.Default, new HashSet<string>());
        Assert.Single(n);
        Assert.Equal("a", n[0].Items[0].Key);
    }

    [Fact]
    public void Odendi_beyani_hatirlatmayi_durdurur()
    {
        var n = ReminderPlanner.PlanForDay(Today, [Card("a", 3, 10, st: PaymentStatus.KullaniciBeyani)], ReminderRules.Default, new HashSet<string>());
        Assert.Empty(n);
    }

    [Fact]
    public void Ayni_gun_vadeli_kartlar_tek_ozet_olur_sahsi_ticari_ayri_kalir()
    {
        var n = ReminderPlanner.PlanForDay(Today, [Card("a", 1, 10), Card("b", 1, 20), Card("c", 1, 30, Scope.Sahsi)], ReminderRules.Default, new HashSet<string>());
        Assert.Equal(2, n.Count);
        Assert.Contains(n, x => x.Scope == Scope.Ticari && x.Items.Count == 2);
        Assert.Contains(n, x => x.Scope == Scope.Sahsi && x.Items.Count == 1);
    }

    [Fact]
    public void Gonderilmis_hatirlatma_tekrar_uretilmez_yuksek_tutar_oncelikli()
    {
        var first = ReminderPlanner.PlanForDay(Today, [Card("a", 1, 600_000)], ReminderRules.Default, new HashSet<string>());
        Assert.True(first[0].HighPriority);
        var again = ReminderPlanner.PlanForDay(Today, [Card("a", 1, 600_000)], ReminderRules.Default, new HashSet<string> { first[0].DedupeKey });
        Assert.Empty(again);
    }

    [Fact]
    public void Gecikme_uyarisi_uretilir()
    {
        var n = ReminderPlanner.PlanForDay(Today, [new DueItem("k", DueKind.Kredi, Scope.Ticari, Today.AddDays(-2), 100, "", PaymentStatus.Bekliyor)], ReminderRules.Default, new HashSet<string>());
        Assert.True(n.Single().IsOverdue);
    }

    [Fact]
    public void Odendi_yaniti_dogrulanmis_sayilmaz()
    {
        Assert.Equal(PaymentStatus.KullaniciBeyani, PaymentReplyHandler.ApplyPaidReply(PaymentStatus.Bekliyor));
        Assert.Equal(PaymentStatus.Dogrulandi, PaymentReplyHandler.ApplyPaidReply(PaymentStatus.Dogrulandi));
    }
}

public class DataEntryReminderTests
{
    static readonly TimeOnly Nine = new(9, 0);
    // Ekstre her ayın 14'ü, son ödeme 22'si. 22.10.2026 Perşembe → o haftanın Pazartesi'si 19.10.2026.
    static CardSchedule Card(DateOnly? entered = null) => new(Guid.Empty, Scope.Sahsi, "Garanti Bonus •••• 4582", 14, 22, entered);

    [Fact]
    public void Odeme_haftasinin_pazartesi_bilgi_girilmemisse_hatirlatir()
    {
        var r = DataEntryReminderPlanner.PlanForDay(new DateOnly(2026, 10, 19), [Card()], Nine, new HashSet<string>());
        Assert.Equal("Bu hafta son ödeme günü", r.Single().Reason);
        Assert.Equal(new DateOnly(2026, 10, 22), r[0].CycleDueDate);
    }

    [Fact]
    public void Ekstre_kesiminden_sonraki_gun_hatirlatir()
    {
        var r = DataEntryReminderPlanner.PlanForDay(new DateOnly(2026, 10, 15), [Card()], Nine, new HashSet<string>());
        Assert.Equal("Ekstre kesildi", r.Single().Reason);
    }

    [Fact]
    public void Bilgi_girildiyse_hatirlatmaz()
    {
        var r = DataEntryReminderPlanner.PlanForDay(new DateOnly(2026, 10, 19), [Card(new DateOnly(2026, 10, 22))], Nine, new HashSet<string>());
        Assert.Empty(r);
    }

    [Fact]
    public void Ay_sonu_gunleri_kisa_aylara_sikistirilir()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), DataEntryReminderPlanner.NextDueDate(new DateOnly(2027, 2, 10), 31));
        Assert.Equal(new DateOnly(2026, 11, 5), DataEntryReminderPlanner.NextDueDate(new DateOnly(2026, 10, 18), 5));
    }
}

public class SupplierPaymentTests
{
    static SupplierMovement Inv(int month, int day, decimal amt, string no) => new(1, new DateOnly(2026, month, day), amt, true, no);
    static SupplierMovement Pay(decimal amt) => new(1, new DateOnly(2026, 10, 1), amt, false, "ODEME");

    [Fact]
    public void Odemeler_en_eski_faturadan_kapanir_kalanin_agirlikli_tarihine_vade_eklenir()
    {
        // F1 01.08 100.000 (tamamı ödendi), F2 01.09 150.000 (50.000 ödendi → 100.000 açık), F3 11.09 100.000 açık
        var plan = SupplierPaymentPlanner.Plan([Inv(8, 1, 100_000, "F1"), Inv(9, 1, 150_000, "F2"), Inv(9, 11, 100_000, "F3"), Pay(150_000)], 60);

        Assert.Equal(200_000, plan.Balance);
        Assert.Equal(2, plan.OpenInvoices.Count);
        Assert.Equal(100_000, plan.OpenInvoices[0].OpenAmount);
        Assert.Equal(new DateOnly(2026, 9, 6), plan.WeightedAverageInvoiceDate);   // 01.09 ile 11.09'un eşit ağırlıklı ortası
        Assert.Equal(new DateOnly(2026, 11, 5), plan.SuggestedPaymentDate);        // + 60 gün
        Assert.Equal(new DateOnly(2026, 11, 10), plan.OpenInvoices[1].DueDate);
    }

    [Fact]
    public void Borc_yoksa_odeme_tarihi_onerilmez()
    {
        var plan = SupplierPaymentPlanner.Plan([Inv(9, 1, 100, "F1"), Pay(100)], 30);
        Assert.Null(plan.SuggestedPaymentDate);
        Assert.Equal(0, plan.Balance);
    }
}

public class CustomerRiskTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static Cheque Ck(long id, decimal amt, ChequeCategory cat) => new(id, $"{id}", "Akbank", "Delta", Today.AddDays(10), amt, ChequeDocType.MusteriCeki, 0, cat);
    static CustomerLimit Limit(RiskClass c = RiskClass.B, decimal l = 2_000_000) => new(1, "Delta Boya", c, l);

    [Fact]
    public void Risk_cari_bakiye_arti_tahsil_edilmemis_ceklerdir()
    {
        var r = CustomerRiskCalculator.Calculate(Limit(), 450_000,
            [Ck(1, 350_000, ChequeCategory.Tahsilatta), Ck(2, 540_000, ChequeCategory.Portfoyde), Ck(3, 340_000, ChequeCategory.Iskontoda),
             Ck(4, 200_000, ChequeCategory.Teminatta), Ck(5, 999_999, ChequeCategory.Kapali)]);
        Assert.Equal(1_430_000, r.UncollectedCheques);   // kapalı (tahsil edilmiş) çek dahil değil
        Assert.Equal(1_880_000, r.TotalRisk);
        Assert.Equal(CreditStatus.Uyari, r.Status);       // B sınıfı %85 eşiği: 1.700.000
    }

    [Fact]
    public void Limit_asilinca_satis_durdurulur_cek_tahsil_olunca_limit_acilir()
    {
        var before = CustomerRiskCalculator.Calculate(Limit(), 450_000, [Ck(1, 350_000, ChequeCategory.Tahsilatta), Ck(2, 1_300_000, ChequeCategory.Portfoyde)]);
        Assert.Equal(CreditStatus.SatisDurdur, before.Status);
        Assert.Equal(CreditEventKind.SatisDurduruldu, CustomerRiskCalculator.Transition(CreditStatus.Acik, before)!.Kind);

        // 350.000'lik çek tahsil edildi → risk 1.750.000 (limit altı)
        var after = CustomerRiskCalculator.Calculate(Limit(), 450_000, [Ck(1, 350_000, ChequeCategory.Kapali), Ck(2, 1_300_000, ChequeCategory.Portfoyde)]);
        Assert.NotEqual(CreditStatus.SatisDurdur, after.Status);
        Assert.Equal(CreditEventKind.LimitAcildi, CustomerRiskCalculator.Transition(before.Status, after)!.Kind);
    }

    [Fact]
    public void D_sinifi_ve_karsiliksiz_cekli_musteri_her_zaman_durdurulur()
    {
        Assert.Equal(CreditStatus.SatisDurdur, CustomerRiskCalculator.Calculate(Limit(RiskClass.D), 0, []).Status);
        Assert.Equal(CreditStatus.SatisDurdur, CustomerRiskCalculator.Calculate(Limit(RiskClass.A), 0, [Ck(1, 10, ChequeCategory.Riskli)]).Status);
    }

    [Fact]
    public void Durum_degismezse_tekrar_uyari_uretilmez()
    {
        var r = CustomerRiskCalculator.Calculate(Limit(RiskClass.D), 0, []);
        Assert.Null(CustomerRiskCalculator.Transition(CreditStatus.SatisDurdur, r));
    }
}

public class CustomerOverdueTests
{
    static readonly DateOnly AsOf = new(2026, 10, 18);

    [Fact]
    public void Tahsilatlar_fifo_mahsup_edilir_gecikme_ve_vade_farki_hesaplanir()
    {
        // F1: 01.07 100.000, 60 gün → vade 30.08. 15.09'da 100.000 ödendi → 16 gün geç.
        // F2: 01.08 200.000, 60 gün → vade 30.09. 50.000 ödendi (15.09'da kalan değil, 10.10'da), 150.000 açık → 18 gün.
        var inv = new[] { new SalesInvoice("F1", new(2026, 7, 1), 100_000, 60), new SalesInvoice("F2", new(2026, 8, 1), 200_000, 60) };
        var col = new[] { new Collection(new(2026, 9, 15), 100_000, "Havale"), new Collection(new(2026, 10, 10), 50_000, "Çek 7700145") };

        var a = CustomerOverdueAnalyzer.Analyze(inv, col, AsOf, monthlyRatePercent: 3);

        var f1 = a.Lines[0]; var f2 = a.Lines[1];
        Assert.Equal(0, f1.OpenAmount);
        Assert.Equal(16, f1.DaysOverdue);
        Assert.Equal(1_600m, f1.LateInterest);                 // 100.000 × 0,03/30 × 16
        Assert.Equal(150_000, f2.OpenAmount);
        Assert.Equal(18, f2.DaysOverdue);
        Assert.Equal(2_700m + 500m, f2.LateInterest);          // 150.000×0,001×18 + 50.000×0,001×10
        Assert.Equal(150_000, a.TotalOverdueAmount);           // yalnızca açık ve gecikmiş tutar
        Assert.Contains(f2.History, s => s.Text.Contains("Vade doldu"));
    }

    [Fact]
    public void Proforma_kdv_ile_toplanir()
    {
        var a = CustomerOverdueAnalyzer.Analyze([new SalesInvoice("F1", new(2026, 8, 19), 100_000, 30)], [], AsOf, 3);
        // vade 18.09 → 30 gün gecikme → 3.000 TL
        var p = CustomerOverdueAnalyzer.Proforma("Delta Boya", a, 20);
        Assert.Equal(3_000m, p.Subtotal);
        Assert.Equal(600m, p.Vat);
        Assert.Equal(3_600m, p.Total);
    }
}

public class InvestmentVaultTests
{
    static readonly Dictionary<string, Instrument> Ins = new()
    {
        ["GA24"] = new("GA24", "Gram altın", AssetType.Altin, "gr"),
        ["USD"] = new("USD", "ABD Doları", AssetType.Doviz, "$"),
    };
    static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(3));
    static Dictionary<string, PriceQuote> Prices(decimal gold, decimal usd) => new()
    {
        ["GA24"] = new("GA24", "Gram altın", gold, gold + 100, "Altın piyasası", Now),
        ["USD"] = new("USD", "ABD Doları", usd, usd + 0.1m, "TCMB", Now),
    };

    [Fact]
    public void Agirlikli_ortalama_maliyet_yer_bazinda_ve_piyasa_degeri_bozdurma_fiyatiyla()
    {
        var txs = new[]
        {
            new VaultTx(new(2025, 1, 10), VaultTxKind.Ekleme, "GA24", "Fiziki kasa (ev)", 100, 3_000),
            new VaultTx(new(2025, 6, 10), VaultTxKind.Ekleme, "GA24", "Fiziki kasa (ev)", 100, 5_000),
            new VaultTx(new(2025, 8, 1), VaultTxKind.Ekleme, "GA24", "Kuveyt Türk", 50, 4_000),
            new VaultTx(new(2026, 3, 1), VaultTxKind.Cikis, "GA24", "Fiziki kasa (ev)", 50, 6_000),
        };
        var p = PortfolioCalculator.Calculate(txs, Ins, Prices(6_800, 49));

        var ev = p.Holdings.Single(h => h.Location == "Fiziki kasa (ev)");
        Assert.Equal(150, ev.Quantity);
        Assert.Equal(4_000, ev.AverageCost);                 // (100×3000 + 100×5000)/200
        Assert.Equal(600_000, ev.CostTl);
        Assert.Equal(1_020_000, ev.MarketValue);             // 150 × 6.800 (alış)
        Assert.Equal(100_000, p.RealizedProfitLoss);         // 50 × (6000 − 4000)
        Assert.Equal(800_000, p.TotalCost);
        Assert.Equal(1_360_000, p.TotalMarketValue);
    }

    [Fact]
    public void Eldekinden_fazla_cikis_reddedilir_eksik_fiyat_toplami_bos_birakir()
    {
        Assert.Throws<InvalidOperationException>(() => PortfolioCalculator.Calculate(
            [new VaultTx(new(2026, 1, 1), VaultTxKind.Ekleme, "USD", "Garanti BBVA", 100, 40),
             new VaultTx(new(2026, 2, 1), VaultTxKind.Cikis, "USD", "Fiziki kasa (ev)", 10, 45)], Ins, Prices(1, 1)));

        var p = PortfolioCalculator.Calculate([new VaultTx(new(2026, 1, 1), VaultTxKind.Ekleme, "USD", "Garanti BBVA", 100, 40)], Ins, new Dictionary<string, PriceQuote>());
        Assert.Null(p.TotalMarketValue);
    }

    [Fact]
    public void Tcmb_xml_okunur_100_birimlik_kur_birim_basina_cevrilir()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Tarih_Date Tarih="16.10.2026" Date="10/16/2026" Bulten_No="2026/196">
              <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD"><Unit>1</Unit><Isim>ABD DOLARI</Isim>
                <ForexBuying>49.3573</ForexBuying><ForexSelling>49.4463</ForexSelling></Currency>
              <Currency CrossOrder="12" Kod="JPY" CurrencyCode="JPY"><Unit>100</Unit><Isim>JAPON YENİ</Isim>
                <ForexBuying>31.1708</ForexBuying><ForexSelling>31.3773</ForexSelling></Currency>
              <Currency CrossOrder="20" Kod="XDR" CurrencyCode="XDR"><Unit>1</Unit><Isim>ÖZEL ÇEKME HAKKI</Isim>
                <ForexBuying/><ForexSelling/></Currency>
            </Tarih_Date>
            """;
        var q = TcmbPriceProvider.Parse(xml);
        Assert.Equal(2, q.Count);                                  // boş kur atlanır
        Assert.Equal(49.3573m, q[0].Bid);
        Assert.Equal(0.311708m, q[1].Bid);
        Assert.Equal(new DateTime(2026, 10, 16, 15, 30, 0), q[0].AsOf.DateTime);
    }

    [Fact]
    public void Kasa_sifresi_dogrulanir_bes_hatada_kilitlenir()
    {
        var v = VaultPassword.Create("gizli-kasa-2026");
        Assert.True(v.Verify("gizli-kasa-2026", Now));
        for (var i = 0; i < 5; i++) Assert.False(v.Verify("yanlis", Now));
        Assert.False(v.Verify("gizli-kasa-2026", Now.AddMinutes(1)));   // kilitli
        Assert.True(v.Verify("gizli-kasa-2026", Now.AddMinutes(6)));    // kilit süresi doldu
        Assert.Throws<ArgumentException>(() => VaultPassword.Create("123"));
    }
}

public class CalendarSyncTests
{
    static readonly DateOnly D = new(2026, 10, 21);
    static CalendarDue Due(string key, CashEventKind k, string party, decimal amt, int day = 0) => new(key, D.AddDays(day), k, party, amt, Scope.Ticari);

    [Fact]
    public void Takvimde_olan_odemeye_dokunulmaz_olmayan_eklenir()
    {
        var dues = new[]
        {
            Due("k1", CashEventKind.KrediTaksit, "Şekerbank Ticari kredi", 200_426.68m),
            Due("c1", CashEventKind.CekOdeme, "ABC Kimya", 750_000, -1),
            Due("kk", CashEventKind.SahsiKart, "Garanti BBVA Bonus", 85_450, 1),
            Due("yeni", CashEventKind.KrediTaksit, "Garanti BBVA Rotatif", 125_000, 5),
        };
        var events = new[]
        {
            new CalendarEvent("e1", D, "Şekerbank kredi taksiti", null, null),             // isim + tür
            new CalendarEvent("e2", D.AddDays(-1), "ABC çek 750.000 TL", null, null),      // tutar
            new CalendarEvent("e3", D.AddDays(1), "Kart ödemesi", "tutar 85450", null),    // açıklamadaki tutar
            new CalendarEvent("e4", D.AddDays(5), "Doktor randevusu", null, null),         // ilgisiz
        };

        var plan = CalendarSyncPlanner.Plan(dues, events);

        Assert.Equal(3, plan.AlreadyThere.Count);
        var create = Assert.Single(plan.ToCreate);
        Assert.Equal("yeni", create.AppKey);
        Assert.Contains("125.000 TL", create.Summary);
    }

    [Fact]
    public void Uygulamanin_ekledigi_etkinlik_tekrar_eklenmez_tutarsiz_baslik_secenegi()
    {
        var dues = new[] { Due("k1", CashEventKind.KrediTaksit, "Şekerbank", 200_000) };
        var plan = CalendarSyncPlanner.Plan(dues, [new CalendarEvent("x", D, "Kredi taksiti – Şekerbank", null, "k1")]);
        Assert.Empty(plan.ToCreate);
        Assert.Equal("Uygulama tarafından eklenmiş", plan.AlreadyThere[0].Reason);

        var noAmount = CalendarSyncPlanner.Plan(dues, [], includeAmounts: false);
        Assert.DoesNotContain("TL", noAmount.ToCreate[0].Summary);
    }

    [Fact]
    public void Farkli_gundeki_benzer_etkinlik_eslesmez_bir_etkinlik_iki_odemeyle_eslesmez()
    {
        var plan = CalendarSyncPlanner.Plan(
            [Due("a", CashEventKind.KrediTaksit, "Şekerbank", 100_000), Due("b", CashEventKind.KrediTaksit, "Şekerbank", 90_000)],
            [new CalendarEvent("e", D, "Şekerbank kredi", null, null), new CalendarEvent("f", D.AddDays(3), "Şekerbank kredi", null, null)]);
        Assert.Single(plan.AlreadyThere);
        Assert.Single(plan.ToCreate);
    }
}

public class StockRiskTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static StockMovement In(int daysAgo, decimal t) => new("IPA", Today.AddDays(-daysAgo), 1, t, false);
    static StockMovement Sale(int daysAgo, decimal t) => new("IPA", Today.AddDays(-daysAgo), 3, t, true);
    static StockThreshold T(decimal reorder = 120, decimal risk = 60, int lead = 21) => new("IPA", reorder, risk, lead);

    [Fact]
    public void Stok_giris_eksi_cikistir_satis_hizi_son_30_gunden()
    {
        // Girişler 300 t; çıkışlar 221 t (216 satış + 5 fire; son 30 günde 90 t satış) → stok 79 t, günlük 3 t, 26 gün yeter
        var m = new[] { In(200, 200), In(60, 100), Sale(100, 126), Sale(20, 60), Sale(5, 30),
                        new StockMovement("IPA", Today.AddDays(-3), 3, 5, false) };  // fire: stoktan düşer, satış hızına girmez
        var r = StockRiskCalculator.Calculate(m, T(), Today);

        Assert.Equal(300, r.In);
        Assert.Equal(221, r.Out);
        Assert.Equal(79, r.OnHand);
        Assert.Equal(3, r.DailySales);
        Assert.Equal(26, r.DaysOfCover);
        Assert.Equal(StockStatus.AlimYap, r.Status);
        Assert.Equal(79, r.SuggestedReorderPoint);   // 3 × 21 × 1,25 = 78,75 → 79
        Assert.False(r.RunsOutBeforeResupply);
    }

    [Fact]
    public void Risk_seviyesi_ve_tedarik_suresi_uyarilari()
    {
        var m = new[] { In(90, 100), Sale(10, 60), Sale(2, 15) };   // stok 25, günlük 2,5 → 10 gün
        var r = StockRiskCalculator.Calculate(m, T(lead: 30), Today);
        Assert.Equal(StockStatus.StokRiskinde, r.Status);
        Assert.True(r.RunsOutBeforeResupply);
        Assert.Equal(Today.AddDays(10), r.RunOutDate);

        Assert.Equal(StockStatus.StokYok, StockRiskCalculator.Calculate([In(5, 10), Sale(1, 10)], T(), Today).Status);
        Assert.Equal(StockStatus.Yeterli, StockRiskCalculator.Calculate([In(5, 500)], T(), Today).Status);
    }

    [Fact]
    public void Gecersiz_esik_reddedilir_durum_degisince_tek_uyari()
    {
        Assert.Throws<ArgumentException>(() => StockRiskCalculator.Calculate([], T(reorder: 50, risk: 60), Today));
        var r = StockRiskCalculator.Calculate([In(5, 50)], T(), Today);
        Assert.Equal(StockStatus.StokRiskinde, StockRiskCalculator.Transition(StockStatus.AlimYap, r)!.To);
        Assert.Null(StockRiskCalculator.Transition(StockStatus.StokRiskinde, r));
    }
}

public class BankLimitTests
{
    static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public void Findeks_satiri_kullanilabilir_limit_ve_asim_uyarisi()
    {
        // Findeks 09.10.2026: İş Bankası limit 5.536.551, risk 5.540.658
        var isb = new BankLimit("İş Bankası", 5_127_800, 408_751, 5_536_551, 5_131_908, 408_750, 5_540_658, new(2027, 8, 31));
        Assert.True(isb.OverLimit);
        Assert.Equal(-4_107, isb.Available);
        var a = Assert.Single(BankLimitAnalyzer.Alerts([isb], Today));
        Assert.Contains("aşıldı", a.Message);
    }

    [Fact]
    public void Revize_vadesi_gecti_yaklasiyor_gecerli()
    {
        BankLimit B(DateOnly rev) => new("X", 1, 0, 1, 0, 0, 0, rev);
        Assert.Equal(RevisionStatus.Gecti, BankLimitAnalyzer.Revision(B(new(2026, 9, 29)), Today));      // QNB
        Assert.Equal(RevisionStatus.Yaklasiyor, BankLimitAnalyzer.Revision(B(new(2026, 12, 1)), Today));
        Assert.Equal(RevisionStatus.Gecerli, BankLimitAnalyzer.Revision(B(new(2027, 7, 2)), Today));      // Şekerbank
        Assert.Null(new BankLimit("Garanti BBVA", 0, 0, 0, 0, 0, 0, new(2025, 2, 18)).Utilization);
    }

    [Fact]
    public void Nakit_acigi_bos_kmh_ile_karsilanir_mi()
    {
        var acc = new[] { new OverdraftAccount("Şekerbank", 0, 2_000_000, 600_000, 4.5m), new OverdraftAccount("QNB", 420_000, 500_000, 500_000, 4m) };
        Assert.Equal(27_000, acc[0].MonthlyInterest);
        var ok = BankLimitAnalyzer.CoverDeficit(-391_177, acc);
        Assert.True(ok.Covered);
        Assert.Equal(1_400_000, ok.FreeOverdraft);
        var no = BankLimitAnalyzer.CoverDeficit(-1_500_000, acc);
        Assert.False(no.Covered);
        Assert.Equal(100_000, no.Shortfall);
    }
}

public class DocumentImportTests
{
    static readonly Dictionary<string, string> Logos = new()
    {
        ["QNB"] = "0080000001e3258002e4b44003f4744001e46c4002e4a44001e1000000800000",
        ["İş Bankası"] = "00000000000000000000000002552440035cb540000000000001800000000000",
    };

    [Fact]
    public void Findeks_raporu_okunur_bankalar_logodan_taninir()
    {
        // 09.10.2026 raporundan pdf.js ile çıkarılan satırlar (kısaltılmış)
        var p1 = new PdfPage(["RİSK RAPORU", "RAPOR TARİHİ | 09.10.2026"], []);
        var p2 = new PdfPage(["Toplam Limit ve Borç | 91.872.557 TL | (B1) | 26.068.618 TL | (B2)", "Toplam Nakdi | 88.165.106 TL | (B3) | 22.057.568 TL | (B4)",
            "Toplam Gayrinakdi | 27.107.451 TL | (B5) | 4.011.050 TL | (B6)", "09/2026 | 93.284.987 | 22.239.192 | 4.092.800", "08/2026 | 93.538.493 | 18.622.185 | 3.912.950"], []);
        var p9 = new PdfPage([
            "Limit / Risk Tablosu", "Bankalar", "Limit (TL) | RİSK (TL)",
            "Grup | 17.250.000 | Grup | 9.589.711", "Nakdi | 13.374.346 | Nakdi | 8.221.860", "Gayri Nakdi | 2.400.000 | Gayri Nakdi | 1.212.550",
            "Toplam | 15.774.346 | Toplam | 9.434.410", "Geciken | 0 | Gecikmiş | -", "Genel | 29/09/2026",
            "Grup | 5.536.551 | Grup | 5.361.831", "Nakdi | 5.127.800 | Nakdi | 5.131.908", "Gayri Nakdi | 408.751 | Gayri Nakdi | 408.750",
            "Toplam | 5.536.551 | Toplam | 5.540.658", "Genel | 31/08/2027",
            "Grup | - | Grup | -", "Nakdi | 10.000.000 | Nakdi | 4.700.490", "Toplam | 10.000.000 | Toplam | 5.109.240", "Genel | 02/07/2027"],
            [Logos["QNB"], Logos["İş Bankası"], new string('f', 64)]);

        var r = FindeksParser.Parse([p1, p2, p9], Logos);

        Assert.Equal(new DateOnly(2026, 10, 9), r.ReportDate);
        Assert.Equal(91_872_557, r.TotalLimit);
        Assert.Equal(4_011_050, r.NonCashRisk);
        Assert.Equal(3, r.Banks.Count);
        Assert.Equal("QNB", r.Banks[0].Bank);
        Assert.Equal(9_434_410, r.Banks[0].TotalRisk);
        Assert.Equal(new DateOnly(2026, 9, 29), r.Banks[0].RevisionDate);
        Assert.Equal("İş Bankası", r.Banks[1].Bank);
        Assert.Null(r.Banks[2].Bank);                         // tanınmayan logo → kullanıcı seçer
        Assert.Equal(10_000_000, r.Banks[2].TotalLimit);
        Assert.Equal("08/2026", r.Trend[0].Period);            // eskiden yeniye
    }

    [Fact]
    public void Findeks_olmayan_dosya_reddedilir()
    {
        Assert.Throws<FormatException>(() => FindeksParser.Parse([new PdfPage(["Fatura"], [])], Logos));
        Assert.Equal(0, LogoSignature.Distance(Logos["QNB"], Logos["QNB"]));
        Assert.True(LogoSignature.Distance(Logos["QNB"], Logos["İş Bankası"]) > LogoSignature.MatchThreshold);
    }

    static List<(string Date, decimal Amt, decimal Pr, decimal Int, decimal Bsmv, decimal Rem)> Plan()
    {
        var list = new List<(string, decimal, decimal, decimal, decimal, decimal)>();
        decimal rem = 1_000_000, pay = 100_000;
        for (var i = 1; i <= 6; i++)
        {
            var interest = Math.Round(rem * 0.03m, 2); var bsmv = Math.Round(interest * 0.05m, 2); var pr = pay - interest; rem -= pr;
            list.Add((new DateOnly(2026, 10, 20).AddMonths(i).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), pr + interest + bsmv, pr, interest, bsmv, rem));
        }
        return list;
    }
    static string Tr(decimal v) => v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));

    [Fact]
    public void Odeme_plani_basliktan_okunur_toplam_satiri_atlanir()
    {
        var lines = new List<string> { "ŞEKERBANK KREDİ ÖDEME PLANI", "Taksit No\tVade Tarihi\tTaksit Tutarı\tAnapara\tFaiz\tBSMV\tKalan Anapara" };
        var n = 1;
        foreach (var p in Plan()) lines.Add($"{n++}\t{p.Date}\t{Tr(p.Amt)}\t{Tr(p.Pr)}\t{Tr(p.Int)}\t{Tr(p.Bsmv)}\t{Tr(p.Rem)}");
        lines.Add("Toplam\t\t600.000,00");

        var s = LoanScheduleParser.Parse(lines);

        Assert.Equal(6, s.Rows.Count);
        Assert.Equal([ScheduleColumn.Taksit, ScheduleColumn.Anapara, ScheduleColumn.Faiz, ScheduleColumn.Bsmv, ScheduleColumn.KalanAnapara], s.Columns);
        Assert.All(s.Rows, r => Assert.True(s.Check(r)));
        Assert.Equal(new DateOnly(2026, 11, 20), s.Rows[0].DueDate);
    }

    [Fact]
    public void Basliksiz_karisik_sirali_plan_davranistan_tahmin_edilir()
    {
        var lines = Plan().Select(p => $"{p.Date}   {Tr(p.Rem)}   {Tr(p.Int)}   {Tr(p.Amt)}   {Tr(p.Bsmv)}   {Tr(p.Pr)}");
        var s = LoanScheduleParser.Parse(lines);
        Assert.Equal([ScheduleColumn.KalanAnapara, ScheduleColumn.Faiz, ScheduleColumn.Taksit, ScheduleColumn.Bsmv, ScheduleColumn.Anapara], s.Columns);
        Assert.All(s.Rows, r => Assert.True(s.Check(r)));
    }
}

public class FinanceCostTests
{
    [Fact]
    public void Iskonto_kesintisi_efektif_oran_ve_yil_sonu_raporu()
    {
        // 800.000 TL çek, 120.000 TL kesinti, 90 gün → 120.000 / 680.000 × 365 / 90 = %71,6
        var isk = new FinanceCost(new(2026, 10, 16), FinanceCostKind.IskontoKesintisi, "TEB", "Çek iskontosu", 800_000, 120_000, 0, new(2027, 1, 14));
        Assert.Equal(71.6m, isk.EffectiveAnnualRate);
        var costs = new[]
        {
            isk,
            new FinanceCost(new(2026, 3, 31), FinanceCostKind.KmhDevreSonuFaizi, "Şekerbank", "KMH 1. çeyrek", 0, 58_200, 2_910),
            new FinanceCost(new(2026, 6, 30), FinanceCostKind.KmhDevreSonuFaizi, "Şekerbank", "KMH 2. çeyrek", 0, 61_400, 3_070),
            new FinanceCost(new(2026, 1, 21), FinanceCostKind.KrediFaizi, "Şekerbank", "Ticari kredi", 0, 108_000, 5_400),
            new FinanceCost(new(2025, 12, 31), FinanceCostKind.KmhDevreSonuFaizi, "QNB", "Önceki yıl", 0, 99_999, 0),
        };
        var bad = new[]
        {
            new BadDebt(new(2026, 4, 12), BadDebtKind.Konkordato, "Kuzey Boya", 680_000, 0, 408_000, BadDebtStatus.KarsilikAyrildi),
            new BadDebt(new(2026, 8, 14), BadDebtKind.KarsiliksizCek, "Mavi Tekstil", 175_000, 25_000, BadDebt.DefaultLoss(175_000, 25_000), BadDebtStatus.HukukiTakipte),
            new BadDebt(new(2026, 9, 1), BadDebtKind.KarsiliksizCek, "Tahsil Edilen", 50_000, 50_000, 50_000, BadDebtStatus.TahsilEdildi),
        };

        var r = FinanceCostLedger.YearEnd(2026, costs, bad);

        Assert.Equal(120_000, r.CostsByKind[FinanceCostKind.IskontoKesintisi]);
        Assert.Equal(125_580, r.CostsByKind[FinanceCostKind.KmhDevreSonuFaizi]);   // önceki yıl hariç
        Assert.Equal(358_980, r.TotalFinanceCost);
        Assert.Equal(11_380, r.TotalTax);
        Assert.Equal(113_400, r.CostsByMonth[0]);
        Assert.Equal(558_000, r.TotalBadDebtLoss);                                  // tahsil edilen zarar sayılmaz
        Assert.Equal(916_980, r.GrandTotal);
        Assert.True(bad[0].BlocksSales);
        Assert.False(bad[2].BlocksSales);
    }

    [Fact]
    public void Odeme_planindan_gecmis_taksit_faizleri_gidere_aktarilir()
    {
        var s = LoanScheduleParser.Parse(["Vade Tarihi\tTaksit Tutarı\tAnapara\tFaiz\tBSMV\tKalan Anapara",
            "20.09.2026\t105.000,00\t70.000,00\t30.000,00\t1.500,00\t930.000,00", "20.11.2026\t105.000,00\t72.000,00\t28.000,00\t1.400,00\t858.000,00"]);
        var c = FinanceCostLedger.FromSchedule("Şekerbank", "Ticari kredi", s, new DateOnly(2026, 10, 18)).ToList();
        var one = Assert.Single(c);
        Assert.Equal(30_000, one.Cost);
        Assert.Equal(1_500, one.Tax);
    }
}

public class ExemptPartyTests
{
    [Fact]
    public void Caliskanlar_farkli_yazimlarla_taninir_alislari_maliyete_girmez()
    {
        var ex = ExemptParties.Default;
        Assert.True(ex.IsExempt(null, "Çalışkanlar Kimyevi Maddeler Paz. San. ve Tic. A.Ş.", true));
        Assert.True(ex.IsExempt(null, "CALISKANLAR KIMYEVI MAD. PAZ. SAN.TIC.AS", true));
        Assert.True(ex.IsExempt("320.01.050", "herhangi", false));
        Assert.False(ex.IsExempt(null, "Orion Petrokimya A.Ş.", true));

        var cost = ProductCost.WeightedAveragePurchaseCost(
        [
            new(new(2026, 9, 1), null, "Orion Petrokimya", 60, 60 * 40_000m),
            new(new(2026, 9, 5), null, "Çalışkanlar Kimyevi Maddeler Pazarlama San. ve Tic. A.Ş.", 30, 30 * 30_000m),
        ], ex);
        Assert.Equal(40_000, cost);   // Çalışkanlar dahil olsaydı 36.666,67
    }

    [Fact]
    public void Muaf_firma_hareketleri_stoktan_ve_satis_hizindan_cikarilir()
    {
        var today = new DateOnly(2026, 10, 18);
        var m = new[]
        {
            new StockMovement("IPA", today.AddDays(-50), 1, 100, false, null, "Orion Petrokimya"),
            new StockMovement("IPA", today.AddDays(-40), 1, 30, false, null, "ÇALIŞKANLAR KİMYEVİ MADDELER PAZARLAMA SANAYİ VE TİCARET A.Ş."),
            new StockMovement("IPA", today.AddDays(-10), 3, 30, true, null, "Delta Boya"),
            new StockMovement("IPA", today.AddDays(-5), 3, 10, true, "320.01.050", "Çalışkanlar"),
        };
        var t = new StockThreshold("IPA", 50, 20, 21);
        Assert.Equal(90, StockRiskCalculator.Calculate(m, t, today).OnHand);                                  // muafiyet yok
        var r = StockRiskCalculator.Calculate(m, t, today, exempt: ExemptParties.Default);
        Assert.Equal(70, r.OnHand);                                                                          // 100 − 30
        Assert.Equal(1, r.DailySales);                                                                       // yalnızca Delta Boya satışı
    }
}

public class CalendarEventParserTests
{
    static CalendarEvent E(string t) => new("id", new DateOnly(2026, 10, 22), t, null, null);

    [Fact]
    public void Takvime_yazilan_odeme_taninir_randevu_yok_sayilir()
    {
        var k = CalendarEventParser.Parse(E("Halkbank kredi taksiti 64.500 TL"))!;
        Assert.Equal(CashEventKind.KrediTaksit, k.Kind);
        Assert.Equal(64_500, k.Amount);
        Assert.Equal("Halkbank kredi taksiti", k.Label);

        Assert.Equal(CashEventKind.TicariKart, CalendarEventParser.Parse(E("Ziraat Bankası kredi kartı 38.900 TL"))!.Kind);
        Assert.Equal(CashEventKind.SahsiKart, CalendarEventParser.Parse(E("Bonus kart ödemesi 85.450"))!.Kind);
        Assert.Equal(CashEventKind.CekOdeme, CalendarEventParser.Parse(E("ABC Kimya çek 750.000 TL"))!.Kind);
        var kira = CalendarEventParser.Parse(E("Ofis kirası ödemesi 45.000"))!;
        Assert.Equal(CashEventKind.DigerPlanli, kira.Kind);
        Assert.Equal(45_000, kira.Amount);
        Assert.Null(CalendarEventParser.Parse(E("Doktor randevusu")));
        Assert.Null(CalendarEventParser.Parse(E("Maaş ödemesi"))!.Amount);     // tutarsız da eklenir, kullanıcı tutarı girer
    }

    [Fact]
    public void Kullanicinin_iki_noktali_bicimi_okunur_ve_ayni_bicimde_yazilir()
    {
        var c = CalendarEventParser.Parse(E("ÇEK ÖDEME: QNB: AYTAŞ : 84.000 TL: ÇKN.: 0430125"))!;
        Assert.Equal(CashEventKind.CekOdeme, c.Kind);
        Assert.Equal("QNB", c.Bank);
        Assert.Equal("AYTAŞ", c.Party);
        Assert.Equal(84_000, c.Amount);
        Assert.Equal("0430125", c.ChequeNo);

        var t = CalendarEventParser.Parse(E("ÇEK TAHSİLAT: YKB: DELTA BOYA: 350.000,50 TL: ÇKN. 7700145"))!;
        Assert.True(t.IsInflow);
        Assert.Equal("Yapı Kredi", t.Bank);
        Assert.Equal(350_000.50m, t.Amount);
        Assert.Equal("7700145", t.ChequeNo);

        Assert.Equal("ÇEK ÖDEME: QNB: AYTAŞ: 84.000 TL: ÇKN.: 0430125", CalendarEventParser.FormatTitle(CashEventKind.CekOdeme, "QNB", "Aytaş", 84_000, "0430125"));
    }
}

public class LogoGuardTests
{
    [Theory]
    [InlineData("UPDATE LG_001_CLCARD SET CODE='x'")]
    [InlineData("SELECT 1; DELETE FROM LG_001_01_CSCARD")]
    [InlineData("EXEC sp_who")]
    public void Logoya_yazma_sorgusu_gonderilemez(string sql) =>
        Assert.Throws<InvalidOperationException>(() => LogoReadOnlyReader.EnsureSelect(sql));

    [Fact]
    public void Firma_ve_donem_numarasi_dogrulanir()
    {
        Assert.Equal("[LG_001_01_CSCARD]", new LogoTables("001", "01").Period("CSCARD"));
        Assert.Throws<ArgumentException>(() => new LogoTables("1; DROP", "01"));
    }
}
