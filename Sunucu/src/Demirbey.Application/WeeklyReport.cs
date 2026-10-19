using System.Globalization;
using System.Text;
using Demirbey.Domain;

namespace Demirbey.Application;

public sealed record WeeklyItem(CashEventKind Kind, Scope Scope, string Label, decimal Amount, bool IsInflow, Certainty Certainty);

/// <summary>Bir günün kalemleri: tahsilatlar önce, ardından çek, kredi, kart ve diğer ödemeler.</summary>
public sealed record WeeklyDay(DateOnly Date, IReadOnlyList<WeeklyItem> Items, IReadOnlyList<Cheque> DiscountedOrCollateralDue)
{
    public decimal Inflow => Items.Where(i => i.IsInflow).Sum(i => i.Amount);
    public decimal CommercialOutflow => Items.Where(i => !i.IsInflow && i.Scope == Scope.Ticari).Sum(i => i.Amount);
    public decimal PersonalOutflow => Items.Where(i => !i.IsInflow && i.Scope == Scope.Sahsi).Sum(i => i.Amount);
}

public sealed record WeeklyReport(
    DateOnly Start,
    DateOnly End,
    bool IncludesPersonal,
    IReadOnlyDictionary<CashEventKind, decimal> Totals,
    decimal CommercialPayments,
    decimal PersonalPayments,
    decimal ExpectedCommercialCollections,
    IReadOnlyList<WeeklyDay> Days,
    IReadOnlyList<Cheque> DiscountedAndCollateralCheques)
{
    public decimal CommercialNet => ExpectedCommercialCollections - CommercialPayments;
}

public static class WeeklyReportBuilder
{
    private static readonly CashEventKind[] Order =
        [CashEventKind.CekTahsilat, CashEventKind.CekOdeme, CashEventKind.KrediTaksit, CashEventKind.TicariKart, CashEventKind.SahsiKart, CashEventKind.DigerPlanli];

    /// <summary>Rapor Pazartesi 08:00'de gönderilir: Pazartesi ise o hafta, değilse bir sonraki Pazartesi.</summary>
    public static DateOnly NextReportWeekStart(DateOnly today)
    {
        var diff = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(diff);
    }

    public static WeeklyReport Build(DateOnly weekStart, IEnumerable<CashEvent> events, IEnumerable<Cheque> cheques, bool includePersonal)
    {
        var end = weekStart.AddDays(6);
        var inWeek = events.Where(e => e.Date >= weekStart && e.Date <= end)
                           .Where(e => includePersonal || e.Scope == Scope.Ticari)
                           .DistinctBy(e => e.SourceRef)
                           .ToList();

        var held = cheques.DistinctBy(c => c.LogicalRef)
                          .Where(c => c.Category is ChequeCategory.Iskontoda or ChequeCategory.Teminatta)
                          .OrderBy(c => c.DueDate).ToList();

        var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).Select(d => new WeeklyDay(d,
            inWeek.Where(e => e.Date == d)
                  .OrderBy(e => Array.IndexOf(Order, e.Kind)).ThenByDescending(e => e.Amount)
                  .Select(e => new WeeklyItem(e.Kind, e.Scope, e.Label, e.Amount, e.IsInflow, e.Certainty)).ToList(),
            held.Where(c => c.DueDate == d).ToList())).ToList();

        var totals = Order.ToDictionary(k => k, k => inWeek.Where(e => e.Kind == k).Sum(e => e.Amount));

        return new WeeklyReport(weekStart, end, includePersonal, totals,
            CommercialPayments: inWeek.Where(e => e.Scope == Scope.Ticari && !e.IsInflow).Sum(e => e.Amount),
            PersonalPayments: inWeek.Where(e => e.Scope == Scope.Sahsi && !e.IsInflow).Sum(e => e.Amount),
            ExpectedCommercialCollections: inWeek.Where(e => e.Scope == Scope.Ticari && e.IsInflow).Sum(e => e.Amount),
            days, held);
    }
}

/// <summary>Haftalık raporun WhatsApp metni (onaylı şablon gövdesine yerleştirilir).</summary>
public static class WeeklyReportWhatsAppFormatter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static string Tl(decimal v) => v.ToString("N0", Tr) + " TL";
    private static string D(DateOnly d) => d.ToString("dd.MM.yyyy", Tr);

    private static readonly Dictionary<CashEventKind, string> Icon = new()
    {
        [CashEventKind.CekTahsilat] = "🟢", [CashEventKind.CekOdeme] = "🔴", [CashEventKind.KrediTaksit] = "🟣",
        [CashEventKind.TicariKart] = "🔵", [CashEventKind.SahsiKart] = "💳", [CashEventKind.DigerPlanli] = "🟠",
    };

    public static string Format(WeeklyReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DEMİRBEY FINANCE AI").AppendLine();
        sb.AppendLine("📊 HAFTALIK FİNANS RAPORU");
        sb.AppendLine($"{D(r.Start)} – {D(r.End)}").AppendLine();
        sb.AppendLine($"🔴 Ödenecek çekler: {Tl(r.Totals[CashEventKind.CekOdeme])}");
        sb.AppendLine($"🟢 Beklenen çek tahsilatları: {Tl(r.Totals[CashEventKind.CekTahsilat])}");
        sb.AppendLine($"🟣 Kredi taksitleri: {Tl(r.Totals[CashEventKind.KrediTaksit])}");
        sb.AppendLine($"🔵 Ticari kredi kartları: {Tl(r.Totals[CashEventKind.TicariKart])}");
        if (r.IncludesPersonal) sb.AppendLine($"💳 Şahsi kredi kartları: {Tl(r.Totals[CashEventKind.SahsiKart])}");
        sb.AppendLine($"🟠 Diğer planlı ödemeler: {Tl(r.Totals[CashEventKind.DigerPlanli])}").AppendLine();
        sb.AppendLine($"Ticari toplam ödeme: {Tl(r.CommercialPayments)}");
        if (r.IncludesPersonal) sb.AppendLine($"Şahsi toplam ödeme: {Tl(r.PersonalPayments)}");
        sb.AppendLine($"Beklenen ticari tahsilat: {Tl(r.ExpectedCommercialCollections)}");
        sb.AppendLine($"Tahmini ticari net nakit hareketi: {Tl(r.CommercialNet)}").AppendLine();

        sb.AppendLine("— GÜNLÜK DAĞILIM —");
        foreach (var day in r.Days)
        {
            sb.AppendLine().AppendLine($"📅 {day.Date.ToString("dd MMMM dddd", Tr)}");
            if (day.Items.Count == 0 && day.DiscountedOrCollateralDue.Count == 0) { sb.AppendLine("— hareket yok"); continue; }
            foreach (var i in day.Items)
                sb.AppendLine($"{Icon[i.Kind]} {i.Label.Split(" · ")[0]}: {(i.IsInflow ? "+" : "")}{Tl(i.Amount)}");
            foreach (var c in day.DiscountedOrCollateralDue)
                sb.AppendLine($"{(c.Category == ChequeCategory.Iskontoda ? "🟠 İskontodaki" : "🔵 Teminattaki")} çek {c.Party}: {Tl(c.Amount)}");
        }

        if (r.DiscountedAndCollateralCheques.Count > 0)
        {
            sb.AppendLine().AppendLine("— İSKONTO / TEMİNATTAKİ ÇEKLER —");
            foreach (var c in r.DiscountedAndCollateralCheques)
                sb.AppendLine($"{(c.Category == ChequeCategory.Iskontoda ? "🟠" : "🔵")} {D(c.DueDate)} {c.Party} {Tl(c.Amount)}");
        }

        if (r.IncludesPersonal) sb.AppendLine().AppendLine("(Şahsi ödemeler ticari nakit akışına eklenmemiştir.)");
        return sb.ToString().TrimEnd();
    }
}
