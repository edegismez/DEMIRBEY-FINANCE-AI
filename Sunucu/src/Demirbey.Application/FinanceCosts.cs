using System.Globalization;
using System.Text.RegularExpressions;

namespace Demirbey.Application;

public enum FinanceCostKind { IskontoKesintisi, KmhDevreSonuFaizi, KrediFaizi, KomisyonMasraf }

/// <summary>
/// Finansman gideri kaydı. Gross: iskontoda bankaya verilen çek tutarı; Cost: kesinti / faiz; Tax: BSMV + KKDF;
/// ChequesDueDate: iskontodaki çeklerin ortalama vadesi (efektif oran için, isteğe bağlı).
/// </summary>
public sealed record FinanceCost(DateOnly Date, FinanceCostKind Kind, string Bank, string Description, decimal Gross, decimal Cost, decimal Tax, DateOnly? ChequesDueDate = null)
{
    public decimal Total => Cost + Tax;

    /// <summary>İskonto yıllık basit efektif oranı (%) = kesinti ÷ net × 365 ÷ gün.</summary>
    public decimal? EffectiveAnnualRate =>
        Kind == FinanceCostKind.IskontoKesintisi && ChequesDueDate is { } due && due > Date && Gross > Cost
            ? Math.Round(Cost / (Gross - Cost) * 365m / (due.DayNumber - Date.DayNumber) * 100m, 1)
            : null;
}

public enum BadDebtKind { Konkordato, Iflas, KarsiliksizCek, OdenmeyenSenet }
public enum BadDebtStatus { HukukiTakipte, KarsilikAyrildi, Degersiz, TahsilEdildi }

/// <summary>Konkordato / iflas / karşılıksız çek gibi alacak zararları. Loss: ayrılan karşılık veya kesin zarar.</summary>
public sealed record BadDebt(DateOnly Date, BadDebtKind Kind, string Customer, decimal Amount, decimal Collected, decimal Loss, BadDebtStatus Status, string? Reference = null)
{
    public static decimal DefaultLoss(decimal amount, decimal collected) => Math.Max(0, amount - collected);
    public decimal EffectiveLoss => Status == BadDebtStatus.TahsilEdildi ? 0 : Loss;
    /// <summary>Kaydı açık olan müşteriye satış durdurulur.</summary>
    public bool BlocksSales => Status != BadDebtStatus.TahsilEdildi;
}

public sealed record YearEndFinanceReport(
    int Year,
    IReadOnlyDictionary<FinanceCostKind, decimal> CostsByKind,
    IReadOnlyDictionary<string, decimal> CostsByBank,
    decimal[] CostsByMonth,
    decimal TotalTax,
    IReadOnlyDictionary<BadDebtKind, decimal> LossesByKind,
    IReadOnlyList<FinanceCost> Entries)
{
    public decimal TotalFinanceCost => CostsByKind.Values.Sum();
    public decimal TotalBadDebtLoss => LossesByKind.Values.Sum();
    public decimal GrandTotal => TotalFinanceCost + TotalBadDebtLoss;
}

public static class FinanceCostLedger
{
    /// <summary>İçe aktarılan ödeme planından, tarihi geçmiş taksitlerin faiz ve vergisini kredi faizi kaydına çevirir.</summary>
    public static IEnumerable<FinanceCost> FromSchedule(string bank, string loanName, ParsedSchedule s, DateOnly today) =>
        s.Rows.Where(r => r.DueDate <= today)
            .Select(r => new FinanceCost(r.DueDate, FinanceCostKind.KrediFaizi, bank, $"{loanName} taksit faizi",
                0, s.Get(r, ScheduleColumn.Faiz), s.Get(r, ScheduleColumn.Bsmv) + s.Get(r, ScheduleColumn.Kkdf)))
            .Where(c => c.Total > 0);

    public static YearEndFinanceReport YearEnd(int year, IEnumerable<FinanceCost> costs, IEnumerable<BadDebt> badDebts)
    {
        var y = costs.Where(c => c.Date.Year == year).OrderBy(c => c.Date).ToList();
        var months = new decimal[12];
        foreach (var c in y) months[c.Date.Month - 1] += c.Total;
        var losses = badDebts.Where(b => b.Date.Year == year).ToList();
        return new YearEndFinanceReport(year,
            Enum.GetValues<FinanceCostKind>().ToDictionary(k => k, k => y.Where(c => c.Kind == k).Sum(c => c.Total)),
            y.GroupBy(c => c.Bank).ToDictionary(g => g.Key, g => g.Sum(c => c.Total)),
            months, y.Sum(c => c.Tax),
            Enum.GetValues<BadDebtKind>().ToDictionary(k => k, k => losses.Where(b => b.Kind == k).Sum(b => b.EffectiveLoss)),
            y);
    }
}

/// <summary>
/// Maliyet ve stok hesabından muaf tutulan cariler (ör. Çalışkanlar Kimyevi Maddeler).
/// Önce Logo cari koduyla, kod yoksa sadeleştirilmiş unvanla eşleşir.
/// </summary>
public sealed partial class ExemptParties(IEnumerable<(string? Code, string Name, bool Purchases, bool Sales)> parties)
{
    private readonly List<(string? Code, string Key, bool Purchases, bool Sales)> _list =
        parties.Select(p => (p.Code, Normalize(p.Name), p.Purchases, p.Sales)).ToList();

    public static ExemptParties Default => new([("320.01.050", "ÇALIŞKANLAR KİMYEVİ MADDELER PAZARLAMA SANAYİ VE TİCARET A.Ş.", true, true)]);

    private static readonly HashSet<string> Stop = ["a", "s", "as", "ltd", "sti", "limited", "sirketi", "san", "sanayi", "ve", "tic", "ticaret", "pazarlama", "paz"];

    /// <summary>Büyük/küçük harf, Türkçe karakter, noktalama ve şirket türü eklerini (A.Ş., LTD. ŞTİ., SAN. TİC.) yok sayar.</summary>
    public static string Normalize(string name)
    {
        var s = name.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        return string.Join(' ', NonAlnum().Split(s).Where(w => w.Length > 0 && !Stop.Contains(w)));
    }

    /// <summary>Unvanın ilk iki anlamlı kelimesi (ör. "caliskanlar kimyevi"); kısaltmalı yazımları da yakalar.</summary>
    private static string Head(string key) => string.Join(' ', key.Split(' ').Take(2));

    public bool IsExempt(string? code, string? name, bool isPurchase)
    {
        var head = name is null ? null : Head(Normalize(name));
        return _list.Any(p => (isPurchase ? p.Purchases : p.Sales) &&
                              ((code is not null && p.Code == code) || (head is { Length: > 0 } && Head(p.Key) == head)));
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonAlnum();
}

/// <summary>Muaf cari hareketlerini çıkararak ağırlıklı ortalama alış maliyeti.</summary>
public static class ProductCost
{
    public sealed record Purchase(DateOnly Date, string? PartyCode, string PartyName, decimal QuantityTon, decimal NetAmount);

    public static decimal? WeightedAveragePurchaseCost(IEnumerable<Purchase> purchases, ExemptParties exempt)
    {
        var list = purchases.Where(p => !exempt.IsExempt(p.PartyCode, p.PartyName, isPurchase: true)).ToList();
        var qty = list.Sum(p => p.QuantityTon);
        return qty == 0 ? null : Math.Round(list.Sum(p => p.NetAmount) / qty, 2);
    }
}
