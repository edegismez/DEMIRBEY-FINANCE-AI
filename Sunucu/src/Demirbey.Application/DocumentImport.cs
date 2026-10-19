using System.Globalization;
using System.Text.RegularExpressions;

namespace Demirbey.Application;

/// <summary>
/// PDF'ten çıkarılmış satırlar: her sayfa için yukarıdan aşağı satırlar, hücreler " | " ile ayrılmış
/// (PdfPig / pdf.js ile aynı biçim). Findeks banka blokları için sayfadaki logo imzaları da verilir.
/// </summary>
public sealed record PdfPage(IReadOnlyList<string> Lines, IReadOnlyList<string> LogoSignatures);

public sealed record FindeksBank(string? Bank, int LogoDistance, decimal CashLimit, decimal NonCashLimit, decimal TotalLimit,
    decimal CashRisk, decimal NonCashRisk, decimal TotalRisk, DateOnly? RevisionDate);

public sealed record FindeksReport(DateOnly ReportDate, decimal TotalLimit, decimal TotalRisk, decimal CashLimit, decimal CashRisk,
    decimal NonCashLimit, decimal NonCashRisk, IReadOnlyList<FindeksBank> Banks, IReadOnlyList<(string Period, decimal Limit, decimal CashDebt, decimal NonCashDebt)> Trend);

/// <summary>Banka logoları 32×8 siyah-beyaz imza (64 hex). Raporda banka adları yazı değil logodur.</summary>
public static class LogoSignature
{
    public const int MatchThreshold = 30; // 256 bitten en fazla 30 farklı bit

    public static int Distance(string a, string b)
    {
        if (a.Length != b.Length) return 256;
        var d = 0;
        for (var i = 0; i < a.Length; i++)
            d += int.PopCount(Convert.ToInt32(a[i].ToString(), 16) ^ Convert.ToInt32(b[i].ToString(), 16));
        return d;
    }

    public static (string? Bank, int Distance) Recognize(string? sig, IReadOnlyDictionary<string, string> known)
    {
        if (sig is null) return (null, 256);
        var best = known.Select(k => (Bank: k.Key, D: Distance(sig, k.Value))).OrderBy(x => x.D).FirstOrDefault();
        return best.Bank is not null && best.D <= MatchThreshold ? (best.Bank, best.D) : (null, best.Bank is null ? 256 : best.D);
    }
}

public static partial class FindeksParser
{
    private static decimal Num(string s)
    {
        s = s.Replace("TL", "").Trim();
        if (s is "" or "-" or "—") return 0;
        return decimal.Parse(s.Replace(".", "").Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    private static (decimal L, decimal R)? Pair(string line, string label)
    {
        var p = line.Split(" | ");
        return p.Length >= 4 && p[0] == label ? (Num(p[1]), Num(p[3])) : null;
    }

    public static FindeksReport Parse(IReadOnlyList<PdfPage> pages, IReadOnlyDictionary<string, string> knownLogos)
    {
        var all = pages.SelectMany(p => p.Lines).ToList();
        if (!all.Any(l => l.Contains("RİSK RAPORU")) || !all.Any(l => l.StartsWith("Limit / Risk Tablosu")))
            throw new FormatException("Dosya bir Findeks Risk Raporu gibi görünmüyor.");

        var dateLine = all.FirstOrDefault(l => RaporTarihi().IsMatch(l)) ?? throw new FormatException("Rapor tarihi bulunamadı.");
        var date = DateOnly.ParseExact(RaporTarihi().Match(dateLine).Groups[1].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture);

        (decimal, decimal) Tl(string key)
        {
            var l = all.FirstOrDefault(x => x.StartsWith(key + " |"));
            var n = l?.Split(" | ").Where(x => x.EndsWith("TL")).Select(Num).ToList() ?? [];
            return (n.ElementAtOrDefault(0), n.ElementAtOrDefault(1));
        }
        var (lim, risk) = Tl("Toplam Limit ve Borç");
        var (nLim, nRisk) = Tl("Toplam Nakdi");
        var (gLim, gRisk) = Tl("Toplam Gayrinakdi");

        var banks = new List<FindeksBank>();
        foreach (var pg in pages.Where(p => p.Lines.Contains("Limit / Risk Tablosu") && !p.Lines.Any(l => l.Contains("Risk Tablosu - Toplam"))))
        {
            var logo = 0;
            Block? cur = null;
            foreach (var l in pg.Lines)
            {
                if (Pair(l, "Grup") is not null)
                {
                    if (cur is not null) banks.Add(cur.ToBank(knownLogos));
                    cur = new Block { Sig = pg.LogoSignatures.ElementAtOrDefault(logo++) };
                    continue;
                }
                if (cur is null) continue;
                if (Pair(l, "Nakdi") is { } n) (cur.NL, cur.NR) = n;
                if (Pair(l, "Gayri Nakdi") is { } g) (cur.GL, cur.GR) = g;
                if (Pair(l, "Toplam") is { } t) (cur.TL, cur.TR) = t;
                if (RevizeRx().Match(l) is { Success: true } m)
                    cur.Rev = DateOnly.ParseExact(m.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            if (cur is not null) banks.Add(cur.ToBank(knownLogos));
        }
        if (banks.Count == 0) throw new FormatException("Banka limit tablosu okunamadı.");

        var trend = all.Select(l => TrendRx().Match(l)).Where(m => m.Success).Take(12)
            .Select(m => (m.Groups[1].Value, Num(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value))).Reverse().ToList();

        return new FindeksReport(date, lim, risk, nLim, nRisk, gLim, gRisk, banks, trend);
    }

    private sealed class Block
    {
        public string? Sig;
        public decimal NL, GL, TL, NR, GR, TR;
        public DateOnly? Rev;
        public FindeksBank ToBank(IReadOnlyDictionary<string, string> known)
        {
            var (bank, d) = LogoSignature.Recognize(Sig, known);
            return new FindeksBank(bank, d, NL, GL, TL, NR, GR, TR, Rev);
        }
    }

    [GeneratedRegex(@"RAPOR TARİHİ \| (\d{2}\.\d{2}\.\d{4})")] private static partial Regex RaporTarihi();
    [GeneratedRegex(@"^Genel \| (\d{2}/\d{2}/\d{4})")] private static partial Regex RevizeRx();
    [GeneratedRegex(@"^(\d{2}/\d{4}) \| ([\d.]+) \| ([\d.]+) \| ([\d.]+)$")] private static partial Regex TrendRx();
}

public enum ScheduleColumn { Yok, Taksit, Anapara, Faiz, Bsmv, Kkdf, KalanAnapara }

public sealed record ScheduleRow(DateOnly DueDate, IReadOnlyList<decimal> Values, string SourceLine);

public sealed record ParsedSchedule(IReadOnlyList<ScheduleRow> Rows, int ColumnCount, ScheduleColumn[] Columns)
{
    public decimal Get(ScheduleRow r, ScheduleColumn c) => Array.IndexOf(Columns, c) is var i and >= 0 && i < r.Values.Count ? r.Values[i] : 0;

    /// <summary>Taksit = anapara + faiz + BSMV + KKDF kontrolü (1,5 TL tolerans). Anapara yoksa null.</summary>
    public bool? Check(ScheduleRow r) => !Columns.Contains(ScheduleColumn.Anapara) || !Columns.Contains(ScheduleColumn.Taksit) ? null
        : Math.Abs(Get(r, ScheduleColumn.Anapara) + Get(r, ScheduleColumn.Faiz) + Get(r, ScheduleColumn.Bsmv) + Get(r, ScheduleColumn.Kkdf) - Get(r, ScheduleColumn.Taksit)) <= 1.5m;
}

/// <summary>
/// Bankaların farklı biçimlerdeki ödeme planlarını okur (PDF / Excel / kopyala-yapıştır).
/// Başlık varsa sütunlar başlıktan, yoksa sayıların davranışından tahmin edilir:
/// kalan anapara azalır, taksit sabit kalır, anapara artar, faiz azalır, küçük olanlar vergidir.
/// Kullanıcı önizlemede sütunları düzeltebilir.
/// </summary>
public static partial class LoanScheduleParser
{
    private static readonly (Regex Re, ScheduleColumn Col)[] Headers =
    [
        (new(@"kalan|bakiye", RegexOptions.IgnoreCase), ScheduleColumn.KalanAnapara),
        (new(@"anapara|ana para|principal", RegexOptions.IgnoreCase), ScheduleColumn.Anapara),
        (new(@"bsmv|bitt", RegexOptions.IgnoreCase), ScheduleColumn.Bsmv),
        (new(@"kkdf", RegexOptions.IgnoreCase), ScheduleColumn.Kkdf),
        (new(@"faiz|kar pay|interest", RegexOptions.IgnoreCase), ScheduleColumn.Faiz),
        (new(@"taksit|ödenecek|toplam|tutar|installment|amount", RegexOptions.IgnoreCase), ScheduleColumn.Taksit),
    ];

    public static ParsedSchedule Parse(IEnumerable<string> rawLines)
    {
        var lines = rawLines.Select(l => MultiSpace().Replace(l.Replace("\t", " | "), " | ").Trim()).Where(l => l.Length > 0).ToList();
        var text = string.Join(" ", lines);
        var decimalComma = TrStyle().Matches(text).Count >= EnStyle().Matches(text).Count;

        string? header = null;
        var rows = new List<ScheduleRow>();
        foreach (var l in lines)
        {
            var dm = DateRx().Match(l);
            if (!dm.Success)
            {
                if (rows.Count == 0 && Headers.Count(h => h.Re.IsMatch(l)) >= 2) header = l;
                continue;
            }
            var date = ParseDate(dm);
            var before = l[..dm.Index];
            var tokens = NumberRx().Matches(l.Remove(dm.Index, dm.Length)).Select(m => m.Value).ToList();
            var nums = tokens.Select(t => (Raw: t, V: ToDecimal(t, decimalComma), Dec: DecRx().IsMatch(t))).ToList();
            if (nums.Count > 0 && !nums[0].Dec && nums[0].V <= 600 && before.Contains(nums[0].Raw)) nums.RemoveAt(0); // baştaki taksit no
            var values = nums.Where(n => n.Dec || n.V >= 1000).Select(n => n.V).ToList();
            if (values.Count > 0) rows.Add(new ScheduleRow(date, values, l));
        }
        if (rows.Count == 0) throw new FormatException("Listede tarih ve tutar içeren satır bulunamadı.");

        var ncol = rows.GroupBy(r => r.Values.Count).OrderByDescending(g => g.Count()).First().Key;
        var cols = Enumerable.Repeat(ScheduleColumn.Yok, ncol).ToArray();

        if (header is not null)
        {
            var fromHeader = header.Split(" | ").Select(c => Headers.FirstOrDefault(h => h.Re.IsMatch(c)).Col).Where(c => c != ScheduleColumn.Yok).Distinct().ToList();
            if (fromHeader.Count >= ncol) cols = fromHeader.Skip(fromHeader.Count - ncol).ToArray();
        }
        if (cols.All(c => c == ScheduleColumn.Yok)) Guess(rows.Where(r => r.Values.Count == ncol).ToList(), cols);
        return new ParsedSchedule(rows, ncol, cols);
    }

    private static void Guess(List<ScheduleRow> r, ScheduleColumn[] cols)
    {
        decimal[] Col(int j) => r.Select(x => x.Values[j]).ToArray();
        decimal Mean(int j) => Col(j).Average();
        decimal Spread(int j) { var c = Col(j); var mx = c.Max(); return mx == 0 ? 0 : (mx - c.Min()) / mx; }
        bool Dec(int j) { var c = Col(j); return c.Zip(c.Skip(1)).All(p => p.Second <= p.First + 0.01m); }
        bool Inc(int j) { var c = Col(j); return c.Zip(c.Skip(1)).All(p => p.Second >= p.First - 0.01m); }
        var idx = Enumerable.Range(0, cols.Length).ToList();
        IEnumerable<int> Free() => idx.Where(j => cols[j] == ScheduleColumn.Yok);

        if (idx.Where(j => Dec(j) && Spread(j) > 0.3m).OrderByDescending(Mean).Cast<int?>().FirstOrDefault() is { } rem) cols[rem] = ScheduleColumn.KalanAnapara;
        if (Free().OrderBy(Spread).ThenByDescending(Mean).Cast<int?>().FirstOrDefault() is { } amt) cols[amt] = ScheduleColumn.Taksit;
        if (Free().Where(Inc).OrderByDescending(Mean).Cast<int?>().FirstOrDefault() is { } pr) cols[pr] = ScheduleColumn.Anapara;
        var rest = Free().OrderByDescending(Mean).ToList();
        if (rest.Count > 0) cols[rest[0]] = ScheduleColumn.Faiz;
        if (rest.Count == 2) cols[rest[1]] = ScheduleColumn.Bsmv;
        else if (rest.Count > 2) { cols[rest[1]] = ScheduleColumn.Kkdf; cols[rest[2]] = ScheduleColumn.Bsmv; }
    }

    private static DateOnly ParseDate(Match m)
    {
        if (m.Groups[4].Success) return new DateOnly(int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value));
        var y = int.Parse(m.Groups[3].Value);
        return new DateOnly(y < 100 ? 2000 + y : y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
    }

    private static decimal ToDecimal(string t, bool comma) =>
        decimal.Parse(comma ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", ""), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(\d{1,2})[./-](\d{1,2})[./-](\d{4}|\d{2})|(\d{4})-(\d{2})-(\d{2})")] private static partial Regex DateRx();
    [GeneratedRegex(@"-?\d[\d.,]*\d|\d")] private static partial Regex NumberRx();
    [GeneratedRegex(@"[.,]\d{2}$")] private static partial Regex DecRx();
    [GeneratedRegex(@"\d\.\d{3},\d{2}")] private static partial Regex TrStyle();
    [GeneratedRegex(@"\d,\d{3}\.\d{2}")] private static partial Regex EnStyle();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpace();
}
