namespace Demirbey.Application;

/// <summary>
/// Findeks Risk Raporu'ndaki banka satırı. TotalLimit, nakdi ve gayrinakdi için ortak genel limittir
/// (ikisinin toplamı değildir). Raporda "-" olan alanlar 0 kabul edilir.
/// </summary>
public sealed record BankLimit(
    string Bank,
    decimal CashLimit, decimal NonCashLimit, decimal TotalLimit,
    decimal CashRisk, decimal NonCashRisk, decimal TotalRisk,
    DateOnly RevisionDate)
{
    public decimal Available => TotalLimit - TotalRisk;
    public decimal? Utilization => TotalLimit > 0 ? TotalRisk / TotalLimit : null;
    public bool OverLimit => TotalRisk > TotalLimit && TotalLimit > 0;
}

/// <summary>Kullanıcının girdiği KMH (kredili mevduat hesabı) bilgisi.</summary>
public sealed record OverdraftAccount(string Bank, decimal Balance, decimal OverdraftLimit, decimal OverdraftUsed, decimal MonthlyRatePercent)
{
    public decimal OverdraftFree => Math.Max(0, OverdraftLimit - OverdraftUsed);
    public decimal MonthlyInterest => Math.Round(OverdraftUsed * MonthlyRatePercent / 100m, 2);
}

public enum RevisionStatus { Gecerli, Yaklasiyor, Gecti }

public sealed record BankLimitAlert(string Bank, string Message);

public static class BankLimitAnalyzer
{
    public const int RevisionWarningDays = 60;

    public static RevisionStatus Revision(BankLimit b, DateOnly today) =>
        b.RevisionDate < today ? RevisionStatus.Gecti
        : b.RevisionDate.DayNumber - today.DayNumber <= RevisionWarningDays ? RevisionStatus.Yaklasiyor
        : RevisionStatus.Gecerli;

    /// <summary>Limit aşımı ve revize vadesi uyarıları (panel ve WhatsApp için).</summary>
    public static IReadOnlyList<BankLimitAlert> Alerts(IEnumerable<BankLimit> banks, DateOnly today) =>
        banks.SelectMany(b =>
        {
            var list = new List<BankLimitAlert>();
            if (b.OverLimit) list.Add(new(b.Bank, $"Risk limiti aşıldı: {b.TotalRisk:N0} / {b.TotalLimit:N0} TL"));
            switch (Revision(b, today))
            {
                case RevisionStatus.Gecti: list.Add(new(b.Bank, $"Genel revize vadesi geçti ({b.RevisionDate:dd.MM.yyyy})")); break;
                case RevisionStatus.Yaklasiyor: list.Add(new(b.Bank, $"Genel revize vadesi yaklaşıyor ({b.RevisionDate:dd.MM.yyyy})")); break;
            }
            return list;
        }).ToList();

    /// <summary>Nakit açığının boş KMH limitleriyle karşılanıp karşılanamayacağı; karşılanamayan kısım döner.</summary>
    public static (bool Covered, decimal FreeOverdraft, decimal Shortfall) CoverDeficit(decimal deficit, IEnumerable<OverdraftAccount> accounts)
    {
        var free = accounts.Sum(a => a.OverdraftFree);
        var need = Math.Abs(deficit);
        return (free >= need, free, Math.Max(0, need - free));
    }
}
