using System.Text.RegularExpressions;
using Dapper;
using Demirbey.Application;
using Demirbey.Domain;
using Microsoft.Data.SqlClient;

namespace Demirbey.Infrastructure.Logo;

public sealed class LogoOptions
{
    public string ConnectionString { get; set; } = "";
    public string FirmNo { get; set; } = "001";
    public string PeriodNo { get; set; } = "01";
    /// <summary>CSCARD.CURRSTAT → kategori. Boşsa <see cref="ChequeCategoryResolver.DefaultMap"/> kullanılır.</summary>
    public Dictionary<int, ChequeCategory>? ChequeStatusMap { get; set; }
}

/// <summary>Logo tablo adlarını üretir. Firma/dönem numarası doğrulanır (SQL enjeksiyonuna karşı).</summary>
public sealed partial class LogoTables
{
    private readonly string _f, _fp;

    public LogoTables(string firmNo, string periodNo)
    {
        if (!FirmRx().IsMatch(firmNo)) throw new ArgumentException("Firma no 3 haneli olmalı (ör. 001).", nameof(firmNo));
        if (!PeriodRx().IsMatch(periodNo)) throw new ArgumentException("Dönem no 2 haneli olmalı (ör. 01).", nameof(periodNo));
        _f = $"LG_{firmNo}_";
        _fp = $"LG_{firmNo}_{periodNo}_";
    }

    public string Firm(string table) => $"[{_f}{table}]";
    public string Period(string table) => $"[{_fp}{table}]";

    [GeneratedRegex("^[0-9]{3}$")] private static partial Regex FirmRx();
    [GeneratedRegex("^[0-9]{2}$")] private static partial Regex PeriodRx();
}

/// <summary>
/// Logo GO 3 veritabanından YALNIZCA okuma yapar. Her sorgu <see cref="EnsureSelect"/> ile denetlenir,
/// bağlantı ApplicationIntent=ReadOnly ile açılır; veritabanı tarafında da kullanıcı db_datareader'dır.
/// </summary>
public sealed class LogoReadOnlyReader : ILogoReader
{
    private readonly LogoOptions _opt;
    private readonly LogoTables _t;
    private readonly ChequeCategoryResolver _resolver;

    public LogoReadOnlyReader(LogoOptions opt)
    {
        _opt = opt;
        _t = new LogoTables(opt.FirmNo, opt.PeriodNo);
        _resolver = new ChequeCategoryResolver(opt.ChequeStatusMap ?? ChequeCategoryResolver.DefaultMap.ToDictionary());
    }

    public async Task<IReadOnlyList<Cheque>> GetOpenChequesAsync(CancellationToken ct = default)
    {
        var sql = EnsureSelect($"""
            SELECT CAST(c.LOGICALREF AS bigint) AS LogicalRef, c.NEWSERINO AS SerialNo, c.BANKNAME AS Bank,
                   ISNULL(cl.DEFINITION_, c.OWING) AS Party, c.DUEDATE AS DueDate,
                   CAST(c.AMOUNT AS decimal(18,2)) AS Amount,   -- Logo tutarları FLOAT tutar
                   CAST(c.DOC AS int) AS Doc, CAST(c.CURRSTAT AS int) AS Status
            FROM {_t.Period("CSCARD")} c
            LEFT JOIN {_t.Period("CSTRANS")} t ON t.CSREF = c.LOGICALREF
                 AND t.LOGICALREF = (SELECT MAX(t2.LOGICALREF) FROM {_t.Period("CSTRANS")} t2 WHERE t2.CSREF = c.LOGICALREF)
            LEFT JOIN {_t.Period("CSROLL")} r ON r.LOGICALREF = t.ROLLREF
            LEFT JOIN {_t.Firm("CLCARD")} cl ON cl.LOGICALREF = r.CARDREF
            WHERE c.DUEDATE >= DATEADD(day, -30, CAST(GETDATE() AS date))
            """);

        await using var cn = Open();
        var rows = await cn.QueryAsync<ChequeRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r =>
        {
            var doc = (ChequeDocType)r.Doc;
            return new Cheque(r.LogicalRef, r.SerialNo ?? "", r.Bank ?? "", r.Party ?? "", DateOnly.FromDateTime(r.DueDate),
                r.Amount, doc, r.Status, _resolver.Resolve(doc, r.Status));
        }).Where(c => c.Category != ChequeCategory.Kapali).ToList();
    }

    public Task<decimal> GetBankBalanceAsync(CancellationToken ct = default)
    {
        // Banka bakiyesi BNFLINE toplamlarından hesaplanır; SIGN 0 = borç (giriş), 1 = alacak (çıkış) – doğrulanmalı.
        var sql = EnsureSelect($"""
            SELECT CAST(ISNULL(SUM(CASE WHEN SIGN = 0 THEN AMOUNT ELSE -AMOUNT END), 0) AS decimal(18,2))
            FROM {_t.Period("BNFLINE")} WHERE CANCELLED = 0
            """);
        return QueryScalarAsync(sql, ct);
    }

    private async Task<decimal> QueryScalarAsync(string sql, CancellationToken ct)
    {
        await using var cn = Open();
        return await cn.ExecuteScalarAsync<decimal>(new CommandDefinition(sql, cancellationToken: ct));
    }

    private SqlConnection Open()
    {
        var b = new SqlConnectionStringBuilder(_opt.ConnectionString) { ApplicationIntent = ApplicationIntent.ReadOnly };
        return new SqlConnection(b.ConnectionString);
    }

    /// <summary>Yalnızca SELECT ile başlayan ve yazma anahtar kelimesi içermeyen sorgulara izin verir.</summary>
    public static string EnsureSelect(string sql)
    {
        var s = sql.TrimStart();
        if (!s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(s, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT)\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Logo veritabanına yalnızca okuma sorgusu gönderilebilir.");
        return sql;
    }

    private sealed record ChequeRow(long LogicalRef, string? SerialNo, string? Bank, string? Party, DateTime DueDate, decimal Amount, int Doc, int Status);
}
