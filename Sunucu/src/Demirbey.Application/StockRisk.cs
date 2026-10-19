namespace Demirbey.Application;

/// <summary>
/// Logo STLINE hareketi (ana birime / tona çevrilmiş). IOCODE: 1-2 giriş, 3-4 çıkış.
/// Satış çıkışları (TRCODE 7, 8) satış hızı için ayrıca işaretlenir.
/// </summary>
public sealed record StockMovement(string ItemCode, DateOnly Date, int IoCode, decimal QuantityTon, bool IsSale, string? PartyCode = null, string? PartyName = null);

/// <summary>Kullanıcının ürün bazında belirlediği eşikler (uygulama veritabanında, Logo'ya yazılmaz).</summary>
public sealed record StockThreshold(string ItemCode, decimal ReorderPointTon, decimal RiskLevelTon, int LeadTimeDays)
{
    public void Validate()
    {
        if (ReorderPointTon < 0 || RiskLevelTon < 0 || LeadTimeDays < 0) throw new ArgumentException("Eşikler negatif olamaz.");
        if (RiskLevelTon > ReorderPointTon) throw new ArgumentException("Risk seviyesi alım noktasından büyük olamaz.");
    }
}

public enum StockStatus { Yeterli, AlimYap, StokRiskinde, StokYok }

public sealed record StockRisk(
    string ItemCode,
    decimal In,
    decimal Out,
    decimal OnHand,
    decimal DailySales,
    int? DaysOfCover,
    DateOnly? RunOutDate,
    StockStatus Status,
    bool RunsOutBeforeResupply,
    decimal SuggestedReorderPoint);

public sealed record StockAlert(string ItemCode, StockStatus From, StockStatus To);

public static class StockRiskCalculator
{
    /// <summary>Önerilen alım noktası = tedarik süresi boyunca beklenen satış × (1 + emniyet payı).</summary>
    public const decimal SafetyFactor = 0.25m;

    /// <param name="salesWindowDays">Satış hızı için bakılan dönem (varsayılan son 30 gün).</param>
    /// <param name="exempt">Maliyet ve stoktan muaf cariler (ör. Çalışkanlar); hareketleri stok ve satış hızına girmez.</param>
    public static StockRisk Calculate(IEnumerable<StockMovement> movements, StockThreshold t, DateOnly today, int salesWindowDays = 30, ExemptParties? exempt = null)
    {
        t.Validate();
        var list = movements.Where(m => m.ItemCode == t.ItemCode && m.Date <= today)
            .Where(m => exempt is null || !exempt.IsExempt(m.PartyCode, m.PartyName, isPurchase: m.IoCode is 1 or 2)).ToList();
        var inQty = list.Where(m => m.IoCode is 1 or 2).Sum(m => m.QuantityTon);
        var outQty = list.Where(m => m.IoCode is 3 or 4).Sum(m => m.QuantityTon);
        var onHand = inQty - outQty;

        var from = today.AddDays(-salesWindowDays + 1);
        var sold = list.Where(m => m.IsSale && m.IoCode is 3 or 4 && m.Date >= from).Sum(m => m.QuantityTon);
        var daily = salesWindowDays > 0 ? sold / salesWindowDays : 0;

        int? cover = daily > 0 ? (int)Math.Floor(Math.Max(0, onHand) / daily) : null;
        var status = onHand <= 0 ? StockStatus.StokYok
            : onHand <= t.RiskLevelTon ? StockStatus.StokRiskinde
            : onHand <= t.ReorderPointTon ? StockStatus.AlimYap
            : StockStatus.Yeterli;

        return new StockRisk(t.ItemCode, inQty, outQty, onHand, Math.Round(daily, 3), cover,
            cover is { } c ? today.AddDays(c) : null, status,
            RunsOutBeforeResupply: cover is { } d && d < t.LeadTimeDays,
            SuggestedReorderPoint: Math.Ceiling(daily * t.LeadTimeDays * (1 + SafetyFactor)));
    }

    /// <summary>Durum kötüleştiğinde veya düzeldiğinde bir kez bildirim üretir (WhatsApp / panel).</summary>
    public static StockAlert? Transition(StockStatus previous, StockRisk current) =>
        previous == current.Status ? null : new StockAlert(current.ItemCode, previous, current.Status);
}
