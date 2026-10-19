using Demirbey.Domain;

namespace Demirbey.Application;

public sealed record CashFlowDay(DateOnly Date, decimal Inflow, decimal Outflow, decimal Balance, IReadOnlyList<CashEvent> Events);

public sealed record CashFlowForecast(
    DateOnly From,
    int Days,
    decimal OpeningBalance,
    decimal ExpectedInflow,
    decimal Outflow,
    decimal ClosingBalance,
    CashFlowDay? FirstDeficit,
    IReadOnlyList<CashFlowDay> Daily);

public static class CashEventSources
{
    public static IEnumerable<CashEvent> FromCards(IEnumerable<CreditCard> cards) =>
        cards.Where(c => c.Status == PaymentStatus.Bekliyor && c.StatementDebt > 0)
             .Select(c => new CashEvent(c.DueDate, c.Scope == Scope.Ticari ? CashEventKind.TicariKart : CashEventKind.SahsiKart,
                 c.StatementDebt, c.Scope, Certainty.Kesin, $"kart:{c.Id}", $"{c.Bank} {c.Nickname} •••• {c.Last4}"));

    public static IEnumerable<CashEvent> FromLoans(IEnumerable<Loan> loans) =>
        loans.SelectMany(l => l.Installments.Where(i => i.Status == PaymentStatus.Bekliyor)
             .Select(i => new CashEvent(i.DueDate, CashEventKind.KrediTaksit, i.Amount, l.Scope, Certainty.Kesin,
                 $"kredi:{l.Id}:{i.DueDate:yyyyMMdd}", $"{l.Bank} {l.LoanType} taksiti")));

    public static IEnumerable<CashEvent> FromPlanned(IEnumerable<PlannedPayment> planned) =>
        planned.Select(p => new CashEvent(p.Date, CashEventKind.DigerPlanli, p.Amount, p.Scope, Certainty.Kesin, $"plan:{p.Id}", p.Label));
}

/// <summary>
/// Ticari nakit akışı tahmini. Şahsi kalemler (Scope.Sahsi) bu hesaba hiçbir zaman girmez.
/// Aynı SourceRef'e sahip olaylar bir kez sayılır.
/// </summary>
public static class CashFlowEngine
{
    public static readonly int[] StandardHorizons = [7, 15, 30, 60, 90, 180];

    public static CashFlowForecast Forecast(IEnumerable<CashEvent> events, decimal openingBalance, DateOnly from, int days)
    {
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
        var to = from.AddDays(days);
        var ticari = events.Where(e => e.Scope == Scope.Ticari && e.Date >= from && e.Date <= to)
                           .DistinctBy(e => e.SourceRef)
                           .ToLookup(e => e.Date);

        var balance = openingBalance;
        var daily = new List<CashFlowDay>(days + 1);
        CashFlowDay? firstDeficit = null;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var ev = ticari[d].ToList();
            var inflow = ev.Where(e => e.IsInflow).Sum(e => e.Amount);
            var outflow = ev.Where(e => !e.IsInflow).Sum(e => e.Amount);
            balance += inflow - outflow;
            var day = new CashFlowDay(d, inflow, outflow, balance, ev);
            daily.Add(day);
            if (firstDeficit is null && balance < 0) firstDeficit = day;
        }

        return new CashFlowForecast(from, days, openingBalance, daily.Sum(x => x.Inflow), daily.Sum(x => x.Outflow),
            balance, firstDeficit, daily);
    }
}
