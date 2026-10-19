using System.Globalization;
using System.Text.RegularExpressions;
using Demirbey.Domain;

namespace Demirbey.Application;

/// <summary>Takvimde olması gereken ödeme (çek, kredi taksiti, kart, planlı ödeme).</summary>
public sealed record CalendarDue(string Key, DateOnly Date, CashEventKind Kind, string Party, decimal Amount, Scope Scope);

/// <summary>Google Takvim'deki etkinlik. AppKey: uygulamanın eklediği etkinliklere konan gizli anahtar (extendedProperties.private).</summary>
public sealed record CalendarEvent(string Id, DateOnly Date, string Summary, string? Description, string? AppKey);

public sealed record CalendarMatch(CalendarDue Due, CalendarEvent Event, string Reason);

public sealed record NewCalendarEvent(string AppKey, DateOnly Date, string Summary, string Description, int ReminderMinutesBefore);

public sealed record CalendarSyncPlan(IReadOnlyList<CalendarMatch> AlreadyThere, IReadOnlyList<NewCalendarEvent> ToCreate);

/// <summary>
/// Google Takvim senkronu: takvimde olmayan ödemeleri ekler, olanlara dokunmaz.
/// Kullanıcının kendi etkinlikleri hiçbir zaman güncellenmez veya silinmez.
/// Eşleşme sırası: (1) gizli uygulama anahtarı, (2) aynı gün + tutar metinde,
/// (3) aynı gün + banka/firma adı + ödeme türü kelimesi.
/// </summary>
public static partial class CalendarSyncPlanner
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<CashEventKind, Regex> Keywords = new()
    {
        [CashEventKind.CekOdeme] = new(@"çek", RegexOptions.IgnoreCase),
        [CashEventKind.KrediTaksit] = new(@"kredi|taksit", RegexOptions.IgnoreCase),
        [CashEventKind.TicariKart] = new(@"kart|ekstre", RegexOptions.IgnoreCase),
        [CashEventKind.SahsiKart] = new(@"kart|ekstre", RegexOptions.IgnoreCase),
        [CashEventKind.DigerPlanli] = new(@"maaş|sgk|vergi|kira", RegexOptions.IgnoreCase),
        [CashEventKind.CekTahsilat] = new(@"tahsil", RegexOptions.IgnoreCase),
    };

    private static readonly Dictionary<CashEventKind, string> Titles = new()
    {
        [CashEventKind.CekOdeme] = "Çek ödemesi", [CashEventKind.KrediTaksit] = "Kredi taksiti",
        [CashEventKind.TicariKart] = "Ticari kart ödemesi", [CashEventKind.SahsiKart] = "Kredi kartı ödemesi",
        [CashEventKind.DigerPlanli] = "Planlı ödeme", [CashEventKind.CekTahsilat] = "Çek tahsilatı",
    };

    public static CalendarSyncPlan Plan(IEnumerable<CalendarDue> dues, IEnumerable<CalendarEvent> events,
        bool includeAmounts = true, int reminderMinutesBefore = 15 * 60)
    {
        var byDate = events.ToLookup(e => e.Date);
        var used = new HashSet<string>(); // bir etkinlik yalnızca bir ödemeyle eşleşir
        var matched = new List<CalendarMatch>();
        var create = new List<NewCalendarEvent>();

        foreach (var d in dues.DistinctBy(d => d.Key).OrderBy(d => d.Date))
        {
            var match = FindMatch(d, byDate[d.Date].Where(e => !used.Contains(e.Id)));
            if (match is not null) { used.Add(match.Event.Id); matched.Add(match); continue; }

            var summary = $"{Titles[d.Kind]} – {d.Party}" + (includeAmounts ? $" · {d.Amount.ToString("N0", Tr)} TL" : "");
            var description = $"DEMİRBEY FINANCE AI tarafından eklendi.\n{(d.Scope == Scope.Sahsi ? "Şahsi" : "Ticari")} ödeme.";
            create.Add(new NewCalendarEvent(d.Key, d.Date, summary, description, reminderMinutesBefore));
        }
        return new CalendarSyncPlan(matched, create);
    }

    private static CalendarMatch? FindMatch(CalendarDue d, IEnumerable<CalendarEvent> sameDay)
    {
        var list = sameDay.ToList();
        if (list.FirstOrDefault(e => e.AppKey == d.Key) is { } own) return new(d, own, "Uygulama tarafından eklenmiş");

        var amount = Digits(Math.Round(d.Amount).ToString("N0", Tr));
        if (amount.Length >= 4 && list.FirstOrDefault(e => Digits(Text(e)).Contains(amount)) is { } byAmount)
            return new(d, byAmount, "Aynı gün + tutar");

        var name = d.Party.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLower(Tr);
        if (name is { Length: >= 3 } && list.FirstOrDefault(e => Text(e).ToLower(Tr).Contains(name) && Keywords[d.Kind].IsMatch(Text(e))) is { } byName)
            return new(d, byName, "Aynı gün + isim + ödeme türü");

        return null;
    }

    private static string Text(CalendarEvent e) => e.Summary + " " + e.Description;
    private static string Digits(string s) => DigitsRx().Replace(s, "");

    [GeneratedRegex(@"\D")] private static partial Regex DigitsRx();
}

/// <summary>Takvimde kullanıcının yazdığı etkinlikten çıkarılan ödeme (takvim → uygulama yönü).</summary>
public sealed record CalendarPayment(string EventId, DateOnly Date, CashEventKind Kind, Scope Scope, decimal? Amount, string Label, string? Bank = null, string? Party = null, string? ChequeNo = null)
{
    public bool IsInflow => Kind == CashEventKind.CekTahsilat;
}

/// <summary>Kullanıcının takvime serbest metinle yazdığı ödemeleri tanır: "Şekerbank kredi 200.426 TL", "Ofis kirası 45.000".</summary>
public static partial class CalendarEventParser
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, string> BankAlias = new(StringComparer.Ordinal)
    {
        ["YKB"] = "Yapı Kredi", ["YAPI KREDİ"] = "Yapı Kredi", ["İŞ"] = "İş Bankası", ["İŞ BANKASI"] = "İş Bankası", ["GARANTİ"] = "Garanti BBVA", ["GARANTİ BBVA"] = "Garanti BBVA",
        ["QNB"] = "QNB", ["FİNANSBANK"] = "QNB", ["HALK"] = "Halkbank", ["HALKBANK"] = "Halkbank", ["VAKIF"] = "Vakıfbank", ["VAKIFBANK"] = "Vakıfbank",
        ["ŞEKER"] = "Şekerbank", ["ŞEKERBANK"] = "Şekerbank", ["TEB"] = "TEB", ["AKBANK"] = "Akbank", ["ZİRAAT"] = "Ziraat", ["FİBA"] = "Fibabanka", ["FİBABANKA"] = "Fibabanka", ["ALBARAKA"] = "Albaraka Türk",
    };

    /// <summary>Kullanıcının biçimi: "ÇEK ÖDEME: QNB: AYTAŞ : 84.000 TL: ÇKN.: 0430125" (parça sırası esnek).</summary>
    public static CalendarPayment? ParseColonFormat(CalendarEvent e)
    {
        var parts = e.Summary.Split(':').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count < 3) return null;
        var type = parts[0].ToUpper(Tr);
        string? bank = null, no = null; decimal? amount = null; var party = new List<string>();
        for (var i = 1; i < parts.Count; i++)
        {
            var x = parts[i]; var u = x.ToUpper(Tr);
            if (u is "ÇKN" or "ÇKN." or "ÇEK NO" or "ÇEK NO.") { no = Digits().Replace(i + 1 < parts.Count ? parts[++i] : "", ""); continue; }
            if (InlineNo().Match(u) is { Success: true } n) { no = n.Groups[1].Value; continue; }
            if (MoneyPart().Match(x) is { Success: true } m) { amount = decimal.Parse(m.Groups[1].Value.Replace(".", "").Replace(',', '.'), CultureInfo.InvariantCulture); continue; }
            if (bank is null && BankAlias.TryGetValue(u, out var b)) { bank = b; continue; }
            party.Add(x);
        }
        var tah = type.Contains("TAHS");
        var kind = type.Contains("ÇEK") ? (tah ? CashEventKind.CekTahsilat : CashEventKind.CekOdeme)
            : tah ? CashEventKind.CekTahsilat
            : type.Contains("KART") ? (type.Contains("ŞAHS") ? CashEventKind.SahsiKart : CashEventKind.TicariKart)
            : Kredi().IsMatch(type) ? CashEventKind.KrediTaksit : CashEventKind.DigerPlanli;
        var p = string.Join(' ', party);
        var label = string.Join(" · ", new[] { p, bank, no is null ? null : $"Çek {no}" }.Where(s => !string.IsNullOrEmpty(s)));
        return new CalendarPayment(e.Id, e.Date, kind, kind == CashEventKind.SahsiKart ? Scope.Sahsi : Scope.Ticari, amount, label, bank, p, no);
    }

    /// <summary>Uygulamanın takvime yazdığı başlık – kullanıcının biçimiyle aynı.</summary>
    public static string FormatTitle(CashEventKind kind, string? bank, string party, decimal? amount, string? chequeNo)
    {
        var type = kind switch
        {
            CashEventKind.CekOdeme => "ÇEK ÖDEME", CashEventKind.CekTahsilat => "ÇEK TAHSİLAT", CashEventKind.KrediTaksit => "KREDİ TAKSİTİ",
            CashEventKind.TicariKart => "KART ÖDEMESİ", CashEventKind.SahsiKart => "KART ÖDEMESİ (ŞAHSİ)", _ => "ÖDEME"
        };
        return string.Join(": ", new[] { type, bank?.ToUpper(Tr), party.ToUpper(Tr), amount is { } a ? a.ToString("N0", Tr) + " TL" : null, chequeNo is null ? null : $"ÇKN.: {chequeNo}" }
            .Where(s => !string.IsNullOrEmpty(s)));
    }

    public static CalendarPayment? Parse(CalendarEvent e)
    {
        var t = e.Summary;
        if (!PayWord().IsMatch(t)) return null;   // randevu, toplantı vb. yok sayılır
        if (t.Count(c => c == ':') >= 2 && ParseColonFormat(e) is { } colon) return colon;
        decimal? amount = null;
        if (AmountRx().Match(t) is { Success: true } m)
            amount = decimal.Parse(m.Groups[1].Value.Replace(".", "").Replace(',', '.'), CultureInfo.InvariantCulture);
        var kind = Cek().IsMatch(t) ? CashEventKind.CekOdeme
            : Kart().IsMatch(t) ? (Sahsi().IsMatch(t) && !t.Contains("ticari", StringComparison.OrdinalIgnoreCase) ? CashEventKind.SahsiKart : CashEventKind.TicariKart)
            : Kredi().IsMatch(t) ? CashEventKind.KrediTaksit
            : CashEventKind.DigerPlanli;
        var label = TrailingAmount().Replace(t, "").Trim();
        return new CalendarPayment(e.Id, e.Date, kind, kind == CashEventKind.SahsiKart ? Scope.Sahsi : Scope.Ticari, amount, label.Length > 0 ? label : t);
    }

    [GeneratedRegex(@"ödeme|öde|kredi|taksit|kart|ekstre|çek|maaş|sgk|vergi|kira|fatura|borç|kmh|tahsil", RegexOptions.IgnoreCase)] private static partial Regex PayWord();
    [GeneratedRegex(@"\D")] private static partial Regex Digits();
    [GeneratedRegex(@"^(?:ÇKN|ÇEK NO)\.?\s*(\d+)")] private static partial Regex InlineNo();
    [GeneratedRegex(@"^(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:,\d{1,2})?)\s*(TL|₺)?$", RegexOptions.IgnoreCase)] private static partial Regex MoneyPart();
    [GeneratedRegex(@"(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d{4,}(?:,\d{1,2})?)")] private static partial Regex AmountRx();
    [GeneratedRegex(@"çek", RegexOptions.IgnoreCase)] private static partial Regex Cek();
    [GeneratedRegex(@"kart|ekstre", RegexOptions.IgnoreCase)] private static partial Regex Kart();
    [GeneratedRegex(@"şahsi|bonus|axess|world|maximum", RegexOptions.IgnoreCase)] private static partial Regex Sahsi();
    [GeneratedRegex(@"kredi|taksit|kmh", RegexOptions.IgnoreCase)] private static partial Regex Kredi();
    [GeneratedRegex(@"\s*[\d.,]+\s*(TL)?\s*$", RegexOptions.IgnoreCase)] private static partial Regex TrailingAmount();
}

public interface IGoogleCalendarClient
{
    Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(string calendarId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task CreateAllDayEventAsync(string calendarId, NewCalendarEvent e, CancellationToken ct = default);
    /// <summary>Google push bildirim kanalı açar (events.watch); değişiklikler webhook adresine anında bildirilir.</summary>
    Task WatchAsync(string calendarId, Uri webhook, CancellationToken ct = default);
    /// <summary>Son senkron belirtecinden (syncToken) bu yana değişen etkinlikler ve yeni belirteç.</summary>
    Task<(IReadOnlyList<CalendarEvent> Changed, string NextSyncToken)> ListChangesAsync(string calendarId, string? syncToken, CancellationToken ct = default);
}

public interface IPaymentStore
{
    Task<IReadOnlyList<CalendarDue>> GetDuesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task AddFromCalendarAsync(CalendarPayment p, CancellationToken ct = default);
    Task<bool> IsCalendarEventKnownAsync(string eventId, CancellationToken ct = default); // daha önce eklendi / geri alındı
    Task<string?> GetSyncTokenAsync(CancellationToken ct = default);
    Task SaveSyncTokenAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// Anlık, iki yönlü senkron:
/// - Uygulamada ödeme / kredi / kart kaydı oluşunca <see cref="OnAppChangedAsync"/> çağrılır → eksik etkinlik hemen yazılır.
/// - Google webhook'u gelince <see cref="OnCalendarNotificationAsync"/> → yalnızca değişen etkinlikler okunur, ödeme olanlar uygulamaya eklenir.
/// - Gece yedek tam kontrol: <see cref="SyncAsync"/>. Kullanıcının etkinlikleri hiçbir zaman değiştirilmez veya silinmez.
/// </summary>
public sealed class GoogleCalendarSyncService(IGoogleCalendarClient client, IPaymentStore store)
{
    public async Task<CalendarSyncPlan> SyncAsync(string calendarId, IReadOnlyList<CalendarDue> dues, bool includeAmounts, CancellationToken ct = default)
    {
        if (dues.Count == 0) return new CalendarSyncPlan([], []);
        var events = await client.ListEventsAsync(calendarId, dues.Min(d => d.Date), dues.Max(d => d.Date), ct);
        var plan = CalendarSyncPlanner.Plan(dues, events, includeAmounts);
        foreach (var e in plan.ToCreate) await client.CreateAllDayEventAsync(calendarId, e, ct);
        return plan;
    }

    public async Task<CalendarSyncPlan> OnAppChangedAsync(string calendarId, DateOnly today, int horizonDays, bool includeAmounts, CancellationToken ct = default) =>
        await SyncAsync(calendarId, await store.GetDuesAsync(today, today.AddDays(horizonDays), ct), includeAmounts, ct);

    public async Task<IReadOnlyList<CalendarPayment>> OnCalendarNotificationAsync(string calendarId, DateOnly today, CancellationToken ct = default)
    {
        var (changed, next) = await client.ListChangesAsync(calendarId, await store.GetSyncTokenAsync(ct), ct);
        var dues = await store.GetDuesAsync(today.AddDays(-1), today.AddYears(1), ct);
        var added = new List<CalendarPayment>();
        foreach (var e in changed.Where(e => e.AppKey is null && e.Date >= today))
        {
            if (await store.IsCalendarEventKnownAsync(e.Id, ct)) continue;
            // uygulamada zaten karşılığı varsa ekleme (aynı ödemeyi iki kez sayma)
            if (CalendarSyncPlanner.Plan(dues.Where(d => d.Date == e.Date), [e]).AlreadyThere.Count > 0) continue;
            if (CalendarEventParser.Parse(e) is not { } p) continue;
            await store.AddFromCalendarAsync(p, ct);
            added.Add(p);
        }
        await store.SaveSyncTokenAsync(next, ct);
        return added;
    }
}
