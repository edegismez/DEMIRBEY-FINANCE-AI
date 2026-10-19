using Demirbey.Domain;

namespace Demirbey.Application;

/// <summary>
/// Logo CSCARD (DOC, CURRSTAT) çiftini uygulama kategorisine çevirir.
/// Eşleme yapılandırmadan gelir; Logo kodları kurulumda keşif betiğiyle doğrulanmalıdır.
/// Tanımsız bir durum sessizce bir kategoriye atılmaz, <see cref="ChequeCategory.Riskli"/> olarak işaretlenir.
/// </summary>
public sealed class ChequeCategoryResolver(IReadOnlyDictionary<int, ChequeCategory> customerChequeMap)
{
    /// <summary>Tipik Logo durum kodları – sahada doğrulanacak varsayılanlar.</summary>
    public static readonly IReadOnlyDictionary<int, ChequeCategory> DefaultMap = new Dictionary<int, ChequeCategory>
    {
        [1] = ChequeCategory.Portfoyde,
        [2] = ChequeCategory.Kapali,      // ciro edildi
        [3] = ChequeCategory.Teminatta,
        [4] = ChequeCategory.Tahsilatta,
        [5] = ChequeCategory.Tahsilatta,  // protestolu tahsile
        [6] = ChequeCategory.Kapali,      // iade edildi
        [7] = ChequeCategory.Riskli,      // protesto
        [8] = ChequeCategory.Kapali,      // tahsil edildi
        [11] = ChequeCategory.Riskli,     // karşılıksız
        [12] = ChequeCategory.Riskli,
        [13] = ChequeCategory.Iskontoda,  // kredi karşılığı / iskonto (kod doğrulanmalı)
    };

    public ChequeCategory Resolve(ChequeDocType doc, int logoStatus, bool paid = false)
    {
        if (doc is ChequeDocType.KendiCekimiz or ChequeDocType.KendiSenedimiz)
            return paid ? ChequeCategory.Kapali : ChequeCategory.Odenecek;
        return customerChequeMap.TryGetValue(logoStatus, out var c) ? c : ChequeCategory.Riskli;
    }
}

public static class ChequeAnalytics
{
    public static int DaysToDue(DateOnly due, DateOnly today) => due.DayNumber - today.DayNumber;

    /// <summary>
    /// Müşteri çeklerinin tutar ağırlıklı ortalama vadesi (gün). Kapalı ve riskli çekler hariç.
    /// Σ(gün × tutar) / Σ tutar
    /// </summary>
    public static decimal WeightedAverageDays(IEnumerable<Cheque> cheques, DateOnly today)
    {
        var open = cheques.Where(c => c.DocType is ChequeDocType.MusteriCeki or ChequeDocType.MusteriSenedi
                                      && c.Category is not (ChequeCategory.Kapali or ChequeCategory.Riskli)).ToList();
        var total = open.Sum(c => c.Amount);
        if (total == 0) return 0;
        return Math.Round(open.Sum(c => DaysToDue(c.DueDate, today) * c.Amount) / total, 1);
    }

    /// <summary>
    /// Nakit akışına girecek çek olayları. Her çek (LogicalRef) en fazla bir kez döner;
    /// portföy, teminat ve iskonto çekleri nakit olarak sayılmaz.
    /// </summary>
    public static IEnumerable<CashEvent> ToCashEvents(IEnumerable<Cheque> cheques) =>
        cheques.DistinctBy(c => c.LogicalRef).Select(c => c.Category switch
        {
            ChequeCategory.Odenecek => new CashEvent(c.DueDate, CashEventKind.CekOdeme, c.Amount, Scope.Ticari,
                Certainty.Kesin, $"cek:{c.LogicalRef}", $"{c.Party} · {c.Bank} · Çek {c.SerialNo}"),
            ChequeCategory.Tahsilatta => new CashEvent(c.DueDate, CashEventKind.CekTahsilat, c.Amount, Scope.Ticari,
                Certainty.Beklenen, $"cek:{c.LogicalRef}", $"{c.Party} · {c.Bank} · Çek {c.SerialNo}"),
            _ => null
        }).OfType<CashEvent>();
}
