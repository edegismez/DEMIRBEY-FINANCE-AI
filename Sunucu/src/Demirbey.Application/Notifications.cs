using Demirbey.Domain;

namespace Demirbey.Application;

public enum DueKind { Cek, Kredi, TicariKart, SahsiKart }

public sealed record DueItem(string Key, DueKind Kind, Scope Scope, DateOnly DueDate, decimal Amount, string Label, PaymentStatus Status);

public sealed record ReminderRules(
    IReadOnlyList<int> DaysBefore,   // ör. 7, 3, 1, 0 (0 = ödeme günü)
    TimeOnly SendTime,               // varsayılan 09:00
    bool OverdueReminder,
    decimal HighPriorityThreshold)
{
    public static ReminderRules Default => new([7, 3, 1, 0], new TimeOnly(9, 0), true, 500_000m);
}

public sealed record PlannedNotification(
    string DedupeKey,
    DateTime SendAt,
    Scope Scope,
    bool HighPriority,
    bool IsOverdue,
    IReadOnlyList<DueItem> Items)
{
    public bool IsGroupSummary => Items.Count > 1;
}

/// <summary>
/// Hangi gün hangi hatırlatmanın gideceğini belirler.
/// - Ödendi (kullanıcı beyanı dahil) olan kalemler için hatırlatma üretmez.
/// - Daha önce gönderilmiş anahtarları tekrar üretmez.
/// - Aynı gün vadesi gelen birden fazla kart için tek bir toplu özet üretir.
/// - Şahsi ve ticari kalemler aynı mesajda birleştirilmez.
/// </summary>
public static class ReminderPlanner
{
    public static IReadOnlyList<PlannedNotification> PlanForDay(
        DateOnly today, IEnumerable<DueItem> items, ReminderRules rules, IReadOnlySet<string> alreadySent)
    {
        var result = new List<PlannedNotification>();
        var sendAt = today.ToDateTime(rules.SendTime);

        var candidates = items
            .Where(i => i.Status == PaymentStatus.Bekliyor)
            .Select(i => (Item: i, Days: i.DueDate.DayNumber - today.DayNumber))
            .Where(x => rules.DaysBefore.Contains(x.Days) || (rules.OverdueReminder && x.Days < 0))
            .ToList();

        // Kartlar: aynı vade + aynı kapsam → toplu özet
        foreach (var g in candidates.Where(x => x.Item.Kind is DueKind.TicariKart or DueKind.SahsiKart)
                                    .GroupBy(x => (x.Item.Scope, x.Item.DueDate)))
        {
            var list = g.Select(x => x.Item).OrderByDescending(i => i.Amount).ToList();
            var days = g.First().Days;
            var key = $"kart:{g.Key.Scope}:{g.Key.DueDate:yyyyMMdd}:{(days < 0 ? $"gecikme:{today:yyyyMMdd}" : days)}:" +
                      string.Join(",", list.Select(i => i.Key).Order());
            Add(key, g.Key.Scope, list, days < 0);
        }

        foreach (var x in candidates.Where(x => x.Item.Kind is DueKind.Cek or DueKind.Kredi))
        {
            var key = $"{x.Item.Key}:{(x.Days < 0 ? $"gecikme:{today:yyyyMMdd}" : x.Days)}";
            Add(key, x.Item.Scope, [x.Item], x.Days < 0);
        }

        return result.OrderByDescending(n => n.HighPriority).ThenBy(n => n.Items[0].DueDate).ToList();

        void Add(string key, Scope scope, IReadOnlyList<DueItem> list, bool overdue)
        {
            if (alreadySent.Contains(key)) return;
            result.Add(new PlannedNotification(key, sendAt, scope, list.Sum(i => i.Amount) >= rules.HighPriorityThreshold, overdue, list));
        }
    }
}

/// <summary>
/// Manuel girilen kart takvimi: ekstre kesim ve son ödeme günü bir kez tanımlanır, her ay tekrar eder.
/// <paramref name="LastEnteredDueDate"/>: kullanıcının ekstre/ödeme bilgisini girdiği en son dönemin son ödeme tarihi.
/// </summary>
public sealed record CardSchedule(Guid CardId, Scope Scope, string Label, int StatementDay, int DueDay, DateOnly? LastEnteredDueDate);

public sealed record DataEntryReminder(string DedupeKey, DateTime SendAt, Scope Scope, CardSchedule Card, DateOnly CycleDueDate, string Reason);

/// <summary>
/// "Şu kartın ödeme bilgilerini girin" hatırlatmaları.
/// Bilgi girilmemiş dönem için: (1) ekstre kesiminden sonraki gün, (2) son ödeme gününün düştüğü haftanın Pazartesi'si.
/// Bilgi girildiğinde hatırlatma üretilmez.
/// </summary>
public static class DataEntryReminderPlanner
{
    /// <summary>Ayın gününü ay uzunluğuna sıkıştırır (ör. 31 → Şubat'ta 28/29).</summary>
    public static DateOnly OnDay(int year, int month, int day) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>Bugün veya sonrasındaki ilk son ödeme tarihi.</summary>
    public static DateOnly NextDueDate(DateOnly today, int dueDay)
    {
        var d = OnDay(today.Year, today.Month, dueDay);
        if (d >= today) return d;
        var n = today.AddMonths(1);
        return OnDay(n.Year, n.Month, dueDay);
    }

    /// <summary>Son ödeme tarihinden önceki (veya aynı aydaki) ekstre kesim tarihi.</summary>
    public static DateOnly StatementDateFor(DateOnly dueDate, int statementDay)
    {
        var s = OnDay(dueDate.Year, dueDate.Month, statementDay);
        if (s < dueDate) return s;
        var p = dueDate.AddMonths(-1);
        return OnDay(p.Year, p.Month, statementDay);
    }

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static IReadOnlyList<DataEntryReminder> PlanForDay(
        DateOnly today, IEnumerable<CardSchedule> cards, TimeOnly sendTime, IReadOnlySet<string> alreadySent)
    {
        var result = new List<DataEntryReminder>();
        foreach (var c in cards)
        {
            var due = NextDueDate(today, c.DueDay);
            if (c.LastEnteredDueDate is { } entered && entered >= due) continue; // bu dönem girilmiş

            var stmt = StatementDateFor(due, c.StatementDay);
            string? reason =
                today == MondayOf(due) && today > stmt ? "Bu hafta son ödeme günü" : // ekstre kesilmeden sorulmaz
                today == stmt.AddDays(1) ? "Ekstre kesildi" : null;
            if (reason is null) continue;

            var key = $"giris:{c.CardId}:{due:yyyyMMdd}:{today:yyyyMMdd}";
            if (!alreadySent.Contains(key))
                result.Add(new DataEntryReminder(key, today.ToDateTime(sendTime), c.Scope, c, due, reason));
        }
        return result;
    }
}

/// <summary>WhatsApp "✅ Ödendi" / "⏰ Yarın hatırlat" yanıtları.</summary>
public static class PaymentReplyHandler
{
    /// <summary>
    /// Kullanıcının "Ödendi" yanıtı yalnızca beyandır; banka/Logo kaydıyla doğrulanmadan
    /// <see cref="PaymentStatus.Dogrulandi"/> yapılmaz. Doğrulanmış bir kaydı geri almaz.
    /// </summary>
    public static PaymentStatus ApplyPaidReply(PaymentStatus current) =>
        current == PaymentStatus.Dogrulandi ? PaymentStatus.Dogrulandi : PaymentStatus.KullaniciBeyani;
}
