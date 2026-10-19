using System.Globalization;

namespace Demirbey.Application;

/// <summary>Müşteriye kesilen satış faturası.</summary>
public sealed record SalesInvoice(string DocNo, DateOnly Date, decimal Amount, int TermDays)
{
    public DateOnly DueDate => Date.AddDays(TermDays);
}

/// <summary>Tahsilat. Çekte <see cref="ValueDate"/> çekin vadesidir (vade farkı bu tarihe göre hesaplanır).</summary>
public sealed record Collection(DateOnly ValueDate, decimal Amount, string Description);

public sealed record OverdueStep(DateOnly Date, string Text);

public sealed record OverdueLine(
    SalesInvoice Invoice,
    decimal OpenAmount,
    int DaysOverdue,
    decimal LateInterest,            // KDV hariç vade farkı
    IReadOnlyList<OverdueStep> History);

public sealed record OverdueAnalysis(
    DateOnly AsOf,
    decimal MonthlyRatePercent,
    IReadOnlyList<OverdueLine> Lines)
{
    public IEnumerable<OverdueLine> Overdue => Lines.Where(l => l.DaysOverdue > 0 && l.OpenAmount > 0);
    public decimal TotalOverdueAmount => Overdue.Sum(l => l.OpenAmount);
    public decimal TotalLateInterest => Overdue.Sum(l => l.LateInterest);
    /// <summary>Tutar ağırlıklı ortalama gecikme (gün).</summary>
    public decimal WeightedAverageDelay => TotalOverdueAmount == 0 ? 0 :
        Math.Round(Overdue.Sum(l => l.DaysOverdue * l.OpenAmount) / TotalOverdueAmount, 1);
}

public sealed record ProformaInvoice(string Customer, DateOnly Date, IReadOnlyList<(string Text, decimal Amount)> Lines,
    decimal Subtotal, decimal VatRatePercent, decimal Vat, decimal Total);

/// <summary>
/// Müşteri vade aşım analizi:
/// - Tahsilatlar valör sırasıyla en erken vadeli faturaya mahsup edilir (FIFO).
/// - Gecikme günü = (fatura tamamen kapandığı valör tarihi ya da bugün) − vade tarihi.
/// - Vade farkı (basit faiz) = tutar × aylık oran / 30 × gecikme günü. Kısmi ödemelerde
///   her parça kendi ödeme tarihine kadar ayrı hesaplanır.
/// </summary>
public static class CustomerOverdueAnalyzer
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static string Tl(decimal v) => v.ToString("N2", Tr) + " TL";
    private static string D(DateOnly d) => d.ToString("dd.MM.yyyy", Tr);

    public static OverdueAnalysis Analyze(IEnumerable<SalesInvoice> invoices, IEnumerable<Collection> collections,
        DateOnly asOf, decimal monthlyRatePercent)
    {
        var daily = monthlyRatePercent / 100m / 30m;
        var pool = collections.Where(c => c.ValueDate <= asOf).OrderBy(c => c.ValueDate).ToList();
        var left = pool.Select(c => c.Amount).ToArray();   // her tahsilatın mahsup edilmemiş kısmı
        var next = 0;
        var lines = new List<OverdueLine>();

        foreach (var inv in invoices.OrderBy(i => i.DueDate).ThenBy(i => i.DocNo))
        {
            var steps = new List<OverdueStep>
            {
                new(inv.Date, $"Fatura kesildi: {Tl(inv.Amount)}, vade {inv.TermDays} gün → son ödeme {D(inv.DueDate)}.")
            };
            var open = inv.Amount;
            var interest = 0m;
            var lastPaymentDate = (DateOnly?)null;

            while (open > 0 && next < pool.Count)
            {
                var c = pool[next];
                var used = Math.Min(left[next], open);
                open -= used;
                left[next] -= used;
                if (left[next] == 0) next++;
                var late = Math.Max(0, c.ValueDate.DayNumber - inv.DueDate.DayNumber);
                interest += used * daily * late;
                lastPaymentDate = c.ValueDate;
                steps.Add(new(c.ValueDate, $"{c.Description}: {Tl(used)} bu faturaya mahsup edildi" +
                    (late > 0 ? $" ({late} gün gecikmeli)." : " (vadesinde).") + (open > 0 ? $" Kalan {Tl(open)}." : " Fatura kapandı.")));
            }

            var daysOverdue = open > 0
                ? Math.Max(0, asOf.DayNumber - inv.DueDate.DayNumber)
                : Math.Max(0, (lastPaymentDate ?? inv.DueDate).DayNumber - inv.DueDate.DayNumber);
            if (open > 0)
            {
                interest += open * daily * daysOverdue;
                if (inv.DueDate < asOf)
                    steps.Add(new(inv.DueDate, $"Vade doldu; {Tl(open)} tahsil edilmeden kaldı."));
                if (daysOverdue > 0)
                    steps.Add(new(asOf, $"Bugün itibarıyla {daysOverdue} gün gecikmede, açık tutar {Tl(open)}."));
            }

            lines.Add(new OverdueLine(inv, open, daysOverdue, Math.Round(interest, 2), steps.OrderBy(s => s.Date).ToList()));
        }

        return new OverdueAnalysis(asOf, monthlyRatePercent, lines);
    }

    /// <summary>Vade farkı proforma faturası (taslak; resmî fatura Logo'da kesilir).</summary>
    public static ProformaInvoice Proforma(string customer, OverdueAnalysis a, decimal vatRatePercent)
    {
        var lines = a.Lines.Where(l => l.LateInterest > 0)
            .Select(l => ($"{l.Invoice.DocNo} no.lu fatura vade farkı ({l.DaysOverdue} gün, aylık %{a.MonthlyRatePercent.ToString("0.##", Tr)})", l.LateInterest))
            .ToList();
        var sub = lines.Sum(l => l.LateInterest);
        var vat = Math.Round(sub * vatRatePercent / 100m, 2);
        return new ProformaInvoice(customer, a.AsOf, lines, sub, vatRatePercent, vat, sub + vat);
    }
}
