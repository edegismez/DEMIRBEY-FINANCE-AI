using System.Text.Json.Serialization;
using Demirbey.Application;
using Demirbey.Domain;
using Demirbey.Infrastructure.Logo;
using Demirbey.Infrastructure.Sample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5173", "http://tauri.localhost", "tauri://localhost")
    .AllowAnyHeader().AllowAnyMethod()));

// Logo bağlantısı tanımlıysa salt okunur okuyucu, değilse prototip örnek verisi.
var logo = builder.Configuration.GetSection("Logo").Get<LogoOptions>() ?? new LogoOptions();
builder.Services.AddSingleton<SampleData>();
builder.Services.AddSingleton<IFinanceStore>(sp => sp.GetRequiredService<SampleData>()); // TODO: PostgreSQL deposu
if (string.IsNullOrWhiteSpace(logo.ConnectionString))
    builder.Services.AddSingleton<ILogoReader>(sp => sp.GetRequiredService<SampleData>());
else
    builder.Services.AddSingleton<ILogoReader>(new LogoReadOnlyReader(logo));
builder.Services.AddSingleton<FinanceService>();

var app = builder.Build();
app.UseCors();

// TODO: Gerçek kimlik doğrulama (JWT + 2FA). Prototipte rol "X-Demo-Role" başlığından okunur.
static UserContext CurrentUser(HttpContext http) =>
    new(SampleData.OwnerId, Enum.TryParse<Role>(http.Request.Headers["X-Demo-Role"], out var r) ? r : Role.SistemYoneticisi);

var api = app.MapGroup("/api");

api.MapGet("/health", (ILogoReader l) => new { status = "ok", logo = l is LogoReadOnlyReader ? "canli-salt-okunur" : "ornek-veri" });

api.MapGet("/cheques", async (HttpContext http, ILogoReader logoReader, FinanceService fs) =>
{
    var user = CurrentUser(http);
    if (!user.CanSeeCommercial) return Results.Forbid();
    var cheques = await logoReader.GetOpenChequesAsync();
    return Results.Ok(new
    {
        weightedAverageDays = ChequeAnalytics.WeightedAverageDays(cheques, fs.Today),
        items = cheques.OrderBy(c => c.DueDate).Select(c => new { c.SerialNo, c.Bank, c.Party, c.DueDate, c.Amount, c.Category, daysToDue = ChequeAnalytics.DaysToDue(c.DueDate, fs.Today) })
    });
});

api.MapGet("/cards", async (HttpContext http, IFinanceStore store) =>
{
    var user = CurrentUser(http);
    var cards = (await store.GetCardsAsync(user)).Where(c => user.CanSee(c.Scope, c.OwnerUserId)).ToList();
    return new
    {
        // Şahsi ve ticari toplamlar ayrı döner, hiçbir zaman toplanmaz.
        personalStatementTotal = cards.Where(c => c.Scope == Scope.Sahsi).Sum(c => c.StatementDebt),
        commercialStatementTotal = cards.Where(c => c.Scope == Scope.Ticari).Sum(c => c.StatementDebt),
        items = cards.Select(c => new { c.Id, c.Scope, c.HolderName, c.Bank, c.Nickname, c.Last4, c.Limit, c.AvailableLimit, c.CurrentDebt, c.StatementDebt, c.MinimumPayment, c.StatementDate, c.DueDate, c.Status })
    };
});

api.MapGet("/cashflow", async (HttpContext http, FinanceService fs, int days = 30) =>
{
    if (!CashFlowEngine.StandardHorizons.Contains(days)) return Results.BadRequest("days: 7, 15, 30, 60, 90 veya 180");
    try { return Results.Ok(await fs.ForecastAsync(CurrentUser(http), days)); }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
});

api.MapGet("/reports/weekly", async (HttpContext http, FinanceService fs, DateOnly? start) =>
    await fs.WeeklyAsync(CurrentUser(http), start));

api.MapGet("/reports/weekly/whatsapp", async (HttpContext http, FinanceService fs, DateOnly? start) =>
    Results.Text(WeeklyReportWhatsAppFormatter.Format(await fs.WeeklyAsync(CurrentUser(http), start))));

app.Run();

public partial class Program;
