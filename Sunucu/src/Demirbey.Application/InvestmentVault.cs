using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Demirbey.Application;

public enum AssetType { Altin, Gumus, Doviz, Fon, Borsa, TlMevduat, Diger }

public sealed record Instrument(string Code, string Name, AssetType Type, string Unit);

public enum VaultTxKind { Ekleme, Cikis }

/// <summary>Yatırım kasası hareketi: ne, nerede (fiziki kasa / banka / aracı kurum), ne zaman, kaça.</summary>
public sealed record VaultTx(DateOnly Date, VaultTxKind Kind, string InstrumentCode, string Location, decimal Quantity, decimal UnitPriceTl, string? Note = null);

/// <summary>Bid = bozdurma (alış), Ask = satış. Piyasa değeri Bid ile hesaplanır.</summary>
public sealed record PriceQuote(string Code, string Name, decimal Bid, decimal Ask, string Source, DateTimeOffset AsOf);

public sealed record Holding(Instrument Instrument, string Location, decimal Quantity, decimal CostTl, decimal? PriceTl, string? PriceSource)
{
    public decimal AverageCost => Quantity == 0 ? 0 : CostTl / Quantity;
    public decimal? MarketValue => PriceTl * Quantity;
    public decimal? ProfitLoss => MarketValue - CostTl;
}

public sealed record Portfolio(IReadOnlyList<Holding> Holdings, decimal RealizedProfitLoss)
{
    public decimal TotalCost => Holdings.Sum(h => h.CostTl);
    /// <summary>Fiyatı olmayan varlık varsa null döner – eksik fiyat sessizce sıfır sayılmaz.</summary>
    public decimal? TotalMarketValue => Holdings.Any(h => h.MarketValue is null) ? null : Holdings.Sum(h => h.MarketValue!.Value);
    public IEnumerable<IGrouping<AssetType, Holding>> ByType => Holdings.GroupBy(h => h.Instrument.Type);
    public IEnumerable<IGrouping<string, Holding>> ByLocation => Holdings.GroupBy(h => h.Location);
}

/// <summary>
/// Ağırlıklı ortalama maliyet yöntemi. Her (varlık, yer) çifti ayrı izlenir; çıkışta ortalama
/// maliyetten düşülür ve gerçekleşen kâr/zarar ayrıca toplanır. Eldekinden fazla çıkış reddedilir.
/// </summary>
public static class PortfolioCalculator
{
    public static Portfolio Calculate(IEnumerable<VaultTx> txs, IReadOnlyDictionary<string, Instrument> instruments,
        IReadOnlyDictionary<string, PriceQuote> prices)
    {
        var book = new Dictionary<(string Code, string Loc), (decimal Qty, decimal Cost)>();
        var realized = 0m;
        foreach (var t in txs.OrderBy(t => t.Date))
        {
            if (t.Quantity <= 0) throw new ArgumentException($"Miktar sıfırdan büyük olmalı ({t.InstrumentCode}).");
            if (!instruments.ContainsKey(t.InstrumentCode)) throw new ArgumentException($"Tanımsız varlık: {t.InstrumentCode}");
            var key = (t.InstrumentCode, t.Location);
            var (qty, cost) = book.GetValueOrDefault(key);
            if (t.Kind == VaultTxKind.Ekleme) { qty += t.Quantity; cost += t.Quantity * t.UnitPriceTl; }
            else
            {
                if (t.Quantity > qty) throw new InvalidOperationException($"{t.Location} içinde yeterli {t.InstrumentCode} yok (mevcut {qty}).");
                var avg = cost / qty;
                realized += (t.UnitPriceTl - avg) * t.Quantity;
                qty -= t.Quantity; cost -= avg * t.Quantity;
            }
            book[key] = (qty, cost);
        }

        var holdings = book.Where(kv => kv.Value.Qty > 0).Select(kv =>
        {
            var p = prices.GetValueOrDefault(kv.Key.Code);
            return new Holding(instruments[kv.Key.Code], kv.Key.Loc, kv.Value.Qty, Math.Round(kv.Value.Cost, 2), p?.Bid, p?.Source);
        }).OrderByDescending(h => h.MarketValue ?? 0).ToList();

        return new Portfolio(holdings, Math.Round(realized, 2));
    }
}

public interface IPriceProvider
{
    string Name { get; }
    Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken ct = default);
}

/// <summary>TCMB gösterge niteliğindeki döviz kurları (https://www.tcmb.gov.tr/kurlar/today.xml).</summary>
public sealed class TcmbPriceProvider(HttpClient http) : IPriceProvider
{
    public const string Url = "https://www.tcmb.gov.tr/kurlar/today.xml";
    public string Name => "TCMB";

    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken ct = default) =>
        Parse(await http.GetStringAsync(Url, ct));

    /// <summary>Unit 100 olan kurlar (ör. JPY) birim başına çevrilir. Döviz alış/satış kullanılır.</summary>
    public static IReadOnlyList<PriceQuote> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("TCMB XML boş.");
        var date = DateOnly.ParseExact((string?)root.Attribute("Tarih") ?? "", "dd.MM.yyyy", CultureInfo.InvariantCulture);
        var asOf = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 30)), TimeSpan.FromHours(3));
        static decimal? Num(XElement c, string n) =>
            decimal.TryParse((string?)c.Element(n), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

        return root.Elements("Currency").Select(c =>
        {
            var unit = Num(c, "Unit") ?? 1;
            var bid = Num(c, "ForexBuying");
            var ask = Num(c, "ForexSelling");
            return bid is null || ask is null ? null
                : new PriceQuote((string?)c.Attribute("Kod") ?? "", (string?)c.Element("Isim") ?? "", bid.Value / unit, ask.Value / unit, "TCMB", asOf);
        }).OfType<PriceQuote>().ToList();
    }
}

/// <summary>
/// Kuyumcu piyasası (gram, çeyrek, cumhuriyet, gümüş…) için sağlayıcı yeri.
/// Harem Altın verisi yalnızca sağlayıcının izni / resmi API anlaşmasıyla bağlanmalıdır;
/// izin alınana kadar lisanslı bir piyasa verisi sağlayıcısı kullanılır.
/// </summary>
public sealed class JewelryMarketPriceProvider : IPriceProvider
{
    public string Name => "Altın piyasası (yapılandırılacak)";
    public Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Altın fiyat sağlayıcısı sözleşmesi ve API bilgileri bekleniyor.");
}

/// <summary>
/// Yatırım kasasının ayrı şifresi. PBKDF2-SHA256 (600.000 tur, 16 bayt tuz) ile saklanır,
/// karşılaştırma sabit zamanlıdır; 5 hatalı denemede geçici kilit.
/// </summary>
public sealed class VaultPassword
{
    private const int Iterations = 600_000, SaltSize = 16, KeySize = 32;
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    public byte[] Salt { get; }
    public byte[] Hash { get; }
    public int Failures { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    private VaultPassword(byte[] salt, byte[] hash) { Salt = salt; Hash = hash; }

    public static VaultPassword Create(string password)
    {
        if (password.Length < 6) throw new ArgumentException("Kasa şifresi en az 6 karakter olmalı.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new VaultPassword(salt, Derive(password, salt));
    }

    public static VaultPassword Restore(byte[] salt, byte[] hash) => new(salt, hash);

    public bool Verify(string password, DateTimeOffset now)
    {
        if (LockedUntil is { } until && now < until) return false;
        var ok = CryptographicOperations.FixedTimeEquals(Derive(password, Salt), Hash);
        if (ok) { Failures = 0; LockedUntil = null; return true; }
        if (++Failures >= MaxFailures) { LockedUntil = now + LockoutDuration; Failures = 0; }
        return false;
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
