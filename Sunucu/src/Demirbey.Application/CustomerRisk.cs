using Demirbey.Domain;

namespace Demirbey.Application;

public enum RiskClass { A, B, C, D }

public enum CreditStatus { Acik, Uyari, SatisDurdur }

/// <summary>Risk sınıfına göre uyarı eşiği. D sınıfı: yalnızca peşin / her zaman durdur.</summary>
public sealed record RiskPolicy(RiskClass Class, decimal WarnRatio, bool AlwaysStop)
{
    public static readonly IReadOnlyDictionary<RiskClass, RiskPolicy> Defaults = new Dictionary<RiskClass, RiskPolicy>
    {
        [RiskClass.A] = new(RiskClass.A, 0.90m, false),
        [RiskClass.B] = new(RiskClass.B, 0.85m, false),
        [RiskClass.C] = new(RiskClass.C, 0.75m, false),
        [RiskClass.D] = new(RiskClass.D, 0m, true),
    };
}

/// <summary>Kullanıcının uygulamada tanımladığı limit (Logo'ya yazılmaz).</summary>
public sealed record CustomerLimit(long CustomerRef, string Name, RiskClass Class, decimal Limit);

public sealed record CustomerRisk(
    CustomerLimit Customer,
    decimal OpenBalance,
    decimal UncollectedCheques,
    int RiskyChequeCount,
    CreditStatus Status,
    string Reason)
{
    public decimal TotalRisk => OpenBalance + UncollectedCheques;
    public decimal Utilization => Customer.Limit <= 0 ? 1 : TotalRisk / Customer.Limit;
    public decimal Headroom => Customer.Limit - TotalRisk;
}

public enum CreditEventKind { SatisDurduruldu, LimitAcildi, UyariEsigi }

public sealed record CreditEvent(CreditEventKind Kind, CustomerRisk Risk);

public static class CustomerRiskCalculator
{
    /// <summary>Tahsil edilene kadar riskte kalan çek kategorileri.</summary>
    public static bool IsUncollected(ChequeCategory c) =>
        c is ChequeCategory.Portfoyde or ChequeCategory.Tahsilatta or ChequeCategory.Teminatta or ChequeCategory.Iskontoda;

    /// <param name="openBalance">Logo cari bakiyesi (müşterinin bize borcu, çekler hariç).</param>
    /// <param name="customerCheques">Bu müşteriden alınan çekler (DOC = müşteri çeki/senedi).</param>
    public static CustomerRisk Calculate(CustomerLimit customer, decimal openBalance, IEnumerable<Cheque> customerCheques,
        IReadOnlyDictionary<RiskClass, RiskPolicy>? policies = null)
    {
        var policy = (policies ?? RiskPolicy.Defaults)[customer.Class];
        var cheques = customerCheques.DistinctBy(c => c.LogicalRef)
            .Where(c => c.DocType is ChequeDocType.MusteriCeki or ChequeDocType.MusteriSenedi).ToList();
        var uncollected = cheques.Where(c => IsUncollected(c.Category)).Sum(c => c.Amount);
        var risky = cheques.Count(c => c.Category == ChequeCategory.Riskli);
        var total = Math.Max(0, openBalance) + uncollected;

        var (status, reason) =
            policy.AlwaysStop ? (CreditStatus.SatisDurdur, "D sınıfı: yalnızca peşin satış") :
            risky > 0 ? (CreditStatus.SatisDurdur, $"{risky} adet karşılıksız / protestolu çek") :
            total >= customer.Limit ? (CreditStatus.SatisDurdur, "Limit aşıldı") :
            total >= customer.Limit * policy.WarnRatio ? (CreditStatus.Uyari, $"Limitin %{policy.WarnRatio * 100:0}'i aşıldı") :
            (CreditStatus.Acik, "");

        return new CustomerRisk(customer, Math.Max(0, openBalance), uncollected, risky, status, reason);
    }

    /// <summary>
    /// Önceki ve yeni durumu karşılaştırır: limit aşıldığında "satışı durdur",
    /// çek tahsil edilip risk limit altına indiğinde "limit açıldı" olayı üretir.
    /// </summary>
    public static CreditEvent? Transition(CreditStatus previous, CustomerRisk current) => (previous, current.Status) switch
    {
        (not CreditStatus.SatisDurdur, CreditStatus.SatisDurdur) => new(CreditEventKind.SatisDurduruldu, current),
        (CreditStatus.SatisDurdur, not CreditStatus.SatisDurdur) => new(CreditEventKind.LimitAcildi, current),
        (CreditStatus.Acik, CreditStatus.Uyari) => new(CreditEventKind.UyariEsigi, current),
        _ => null
    };
}
