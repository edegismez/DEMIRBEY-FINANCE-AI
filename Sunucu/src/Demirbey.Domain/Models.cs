namespace Demirbey.Domain;

/// <summary>Şahsi ve ticari veriler hiçbir hesaplamada otomatik birleştirilmez.</summary>
public enum Scope { Ticari, Sahsi }

/// <summary>Logo CSCARD.DOC alanı.</summary>
public enum ChequeDocType { MusteriCeki = 1, MusteriSenedi = 2, KendiCekimiz = 3, KendiSenedimiz = 4 }

public enum ChequeCategory
{
    Portfoyde,   // 🟡 sarı
    Tahsilatta,  // 🟢 yeşil
    Teminatta,   // 🔵 mavi
    Iskontoda,   // 🟠 turuncu
    Odenecek,    // 🔴 kırmızı
    Kapali,      // tahsil edildi / ödendi / ciro edildi
    Riskli       // karşılıksız / protesto
}

public sealed record Cheque(
    long LogicalRef,
    string SerialNo,
    string Bank,
    string Party,
    DateOnly DueDate,
    decimal Amount,
    ChequeDocType DocType,
    int LogoStatus,
    ChequeCategory Category);

public enum PaymentStatus { Bekliyor, KullaniciBeyani, Dogrulandi }

public sealed record CreditCard(
    Guid Id,
    Scope Scope,
    Guid OwnerUserId,
    string HolderName,
    string Bank,
    string Nickname,
    string Last4,
    decimal Limit,
    decimal CurrentDebt,
    decimal StatementDebt,
    decimal MinimumPayment,
    DateOnly StatementDate,
    DateOnly DueDate,
    PaymentStatus Status)
{
    public decimal AvailableLimit => Limit - CurrentDebt;
}

public sealed record LoanInstallment(DateOnly DueDate, decimal Amount, decimal Principal, PaymentStatus Status);

public sealed record Loan(
    Guid Id,
    Scope Scope,
    Guid OwnerUserId,
    string Bank,
    string LoanType,
    decimal Amount,
    DateOnly UsageDate,
    decimal MonthlyRatePercent,
    IReadOnlyList<LoanInstallment> Installments)
{
    public IEnumerable<LoanInstallment> Remaining => Installments.Where(i => i.Status != PaymentStatus.Dogrulandi);
    public decimal RemainingPrincipal => Remaining.Sum(i => i.Principal);
    public decimal RemainingTotal => Remaining.Sum(i => i.Amount);
    public decimal RemainingFinancingCost => RemainingTotal - RemainingPrincipal;
}

public enum CashEventKind { CekOdeme, CekTahsilat, KrediTaksit, TicariKart, SahsiKart, DigerPlanli }

/// <summary>Beklenen (doğrulanmamış) ve kesinleşmiş hareketler ayrı tutulur.</summary>
public enum Certainty { Beklenen, Kesin }

public sealed record CashEvent(
    DateOnly Date,
    CashEventKind Kind,
    decimal Amount,
    Scope Scope,
    Certainty Certainty,
    string SourceRef,
    string Label)
{
    public bool IsInflow => Kind == CashEventKind.CekTahsilat;
}

public sealed record PlannedPayment(Guid Id, Scope Scope, DateOnly Date, decimal Amount, string Label);
