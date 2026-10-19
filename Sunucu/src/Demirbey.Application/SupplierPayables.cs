using Demirbey.Domain;

namespace Demirbey.Application;

/// <summary>Logo'dan okunan tedarikçi cari kartı (CLCARD) ve hareket özeti.</summary>
public sealed record Supplier(long LogicalRef, string Code, string Name, string? TaxNo, string? Phone, string? Iban);

/// <summary>Cari borçlandıran belge (alış faturası) veya borç kapatan ödeme (çek, havale, nakit, iade).</summary>
public sealed record SupplierMovement(long SupplierRef, DateOnly Date, decimal Amount, bool IsPurchase, string DocNo);

public sealed record OpenInvoice(string DocNo, DateOnly Date, decimal OriginalAmount, decimal OpenAmount, DateOnly DueDate);

public sealed record SupplierPaymentPlan(
    decimal Balance,
    decimal OpenAmount,
    DateOnly? WeightedAverageInvoiceDate,
    int TermDays,
    DateOnly? SuggestedPaymentDate,
    IReadOnlyList<OpenInvoice> OpenInvoices);

/// <summary>
/// "Vade yazdığımda alışverişe göre ödeme tarihi ver":
/// 1) Ödemeler en eski faturadan başlanarak kapatılır (FIFO).
/// 2) Kalan açık faturaların tutar ağırlıklı ortalama tarihi bulunur.
/// 3) Ortalama tarih + vade günü = önerilen ödeme / çek vadesi.
/// </summary>
public static class SupplierPaymentPlanner
{
    public static SupplierPaymentPlan Plan(IEnumerable<SupplierMovement> movements, int termDays)
    {
        if (termDays < 0) throw new ArgumentOutOfRangeException(nameof(termDays));
        var list = movements.ToList();
        var purchases = list.Where(m => m.IsPurchase).OrderBy(m => m.Date).ThenBy(m => m.DocNo).ToList();
        var paid = list.Where(m => !m.IsPurchase).Sum(m => m.Amount);
        var balance = purchases.Sum(m => m.Amount) - paid;

        var open = new List<OpenInvoice>();
        foreach (var p in purchases)
        {
            var used = Math.Min(paid, p.Amount);
            paid -= used;
            var rest = p.Amount - used;
            if (rest > 0) open.Add(new OpenInvoice(p.DocNo, p.Date, p.Amount, rest, p.Date.AddDays(termDays)));
        }

        var openTotal = open.Sum(o => o.OpenAmount);
        if (openTotal == 0) return new SupplierPaymentPlan(balance, 0, null, termDays, null, open);

        // Σ(gün numarası × açık tutar) / Σ açık tutar → en yakın güne yuvarlanır
        var avgDay = (int)Math.Round(open.Sum(o => o.Date.DayNumber * o.OpenAmount) / openTotal, MidpointRounding.AwayFromZero);
        var avgDate = DateOnly.FromDayNumber(avgDay);
        return new SupplierPaymentPlan(balance, openTotal, avgDate, termDays, avgDate.AddDays(termDays), open);
    }
}
