using Demirbey.Domain;

namespace Demirbey.Application;

/// <summary>Logo GO 3'ten okunan veriler. Uygulamanın Logo'ya yazma yolu yoktur.</summary>
public interface ILogoReader
{
    Task<IReadOnlyList<Cheque>> GetOpenChequesAsync(CancellationToken ct = default);
    Task<decimal> GetBankBalanceAsync(CancellationToken ct = default);
}

/// <summary>Uygulamanın kendi verileri (PostgreSQL): kartlar, krediler, planlı ödemeler.</summary>
public interface IFinanceStore
{
    Task<IReadOnlyList<CreditCard>> GetCardsAsync(UserContext user, CancellationToken ct = default);
    Task<IReadOnlyList<Loan>> GetLoansAsync(UserContext user, CancellationToken ct = default);
    Task<IReadOnlyList<PlannedPayment>> GetPlannedPaymentsAsync(UserContext user, CancellationToken ct = default);
}

public enum Role { SistemYoneticisi, FinansYoneticisi, Muhasebe, SatisYoneticisi, SahsiFinans }

public sealed record UserContext(Guid UserId, Role Role)
{
    public bool CanSeeCommercial => Role is Role.SistemYoneticisi or Role.FinansYoneticisi;

    /// <summary>Şahsi veriler varsayılan kapalı: yalnızca sahibi (ve sistem yöneticisi) görür.</summary>
    public bool CanSeePersonalOf(Guid ownerUserId) =>
        Role == Role.SistemYoneticisi || (Role == Role.SahsiFinans && ownerUserId == UserId);

    public bool CanSee(Scope scope, Guid ownerUserId) =>
        scope == Scope.Ticari ? CanSeeCommercial : CanSeePersonalOf(ownerUserId);
}

/// <summary>Ekranların ve WhatsApp'ın kullandığı tek giriş noktası.</summary>
public sealed class FinanceService(ILogoReader logo, IFinanceStore store, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<(IReadOnlyList<Cheque> Cheques, IReadOnlyList<CashEvent> Events)> LoadAsync(UserContext user, CancellationToken ct)
    {
        IReadOnlyList<Cheque> cheques = user.CanSeeCommercial ? await logo.GetOpenChequesAsync(ct) : [];
        var cards = (await store.GetCardsAsync(user, ct)).Where(c => user.CanSee(c.Scope, c.OwnerUserId));
        var loans = (await store.GetLoansAsync(user, ct)).Where(l => user.CanSee(l.Scope, l.OwnerUserId));
        var planned = (await store.GetPlannedPaymentsAsync(user, ct)).Where(p => p.Scope == Scope.Ticari ? user.CanSeeCommercial : true);

        var events = ChequeAnalytics.ToCashEvents(cheques)
            .Concat(CashEventSources.FromCards(cards))
            .Concat(CashEventSources.FromLoans(loans))
            .Concat(CashEventSources.FromPlanned(planned))
            .ToList();
        return (cheques, events);
    }

    public async Task<CashFlowForecast> ForecastAsync(UserContext user, int days, CancellationToken ct = default)
    {
        if (!user.CanSeeCommercial) throw new UnauthorizedAccessException("Nakit akışı için yetki yok.");
        var (_, events) = await LoadAsync(user, ct);
        return CashFlowEngine.Forecast(events, await logo.GetBankBalanceAsync(ct), Today, days);
    }

    public async Task<WeeklyReport> WeeklyAsync(UserContext user, DateOnly? weekStart = null, CancellationToken ct = default)
    {
        var (cheques, events) = await LoadAsync(user, ct);
        var includePersonal = events.Any(e => e.Scope == Scope.Sahsi) || user.Role == Role.SahsiFinans;
        return WeeklyReportBuilder.Build(weekStart ?? WeeklyReportBuilder.NextReportWeekStart(Today), events, cheques, includePersonal);
    }
}
