using Demirbey.Application;
using Demirbey.Domain;

namespace Demirbey.Infrastructure.Sample;

/// <summary>Prototip aşaması için örnek veri. Logo bağlantısı yapılandırılmadığında kullanılır.</summary>
public sealed class SampleData(TimeProvider clock) : ILogoReader, IFinanceStore
{
    public static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private DateOnly D(int offset) => Today.AddDays(offset);

    public Task<IReadOnlyList<Cheque>> GetOpenChequesAsync(CancellationToken ct = default)
    {
        long id = 1;
        Cheque C(string no, string bank, string party, int off, decimal amt, ChequeCategory cat) =>
            new(id++, no, bank, party, D(off), amt,
                cat == ChequeCategory.Odenecek ? ChequeDocType.KendiCekimiz : ChequeDocType.MusteriCeki, 0, cat);

        IReadOnlyList<Cheque> list =
        [
            C("0024581", "QNB", "ABC Kimya", 2, 750_000, ChequeCategory.Odenecek),
            C("0031177", "TEB", "Mersin Lojistik", 5, 420_000, ChequeCategory.Odenecek),
            C("0045920", "İş Bankası", "Gebze Petrokimya", 11, 900_000, ChequeCategory.Odenecek),
            C("0051033", "Yapı Kredi", "Anadolu Solvent", 19, 310_000, ChequeCategory.Odenecek),
            C("7700112", "Yapı Kredi", "XYZ Ticaret", 8, 480_000, ChequeCategory.Tahsilatta),
            C("7700145", "İş Bankası", "Delta Boya", 4, 350_000, ChequeCategory.Tahsilatta),
            C("7700201", "Akbank", "Pınar Yapı", 14, 600_000, ChequeCategory.Tahsilatta),
            C("7700233", "QNB", "XYZ Ticaret", 22, 720_000, ChequeCategory.Tahsilatta),
            C("8800120", "Yapı Kredi", "Sera Plastik", 6, 275_000, ChequeCategory.Portfoyde),
            C("8800155", "İş Bankası", "Orion Boya", 18, 390_000, ChequeCategory.Portfoyde),
            C("9900131", "Yapı Kredi", "Delta Boya", 3, 340_000, ChequeCategory.Iskontoda),
            C("9900140", "Akbank", "Karya Kimya", 6, 450_000, ChequeCategory.Teminatta),
            C("9900015", "Garanti BBVA", "Karya Kimya", 40, 1_000_000, ChequeCategory.Teminatta),
            C("9900107", "Halkbank", "Pınar Yapı", 30, 560_000, ChequeCategory.Iskontoda),
        ];
        return Task.FromResult(list);
    }

    public Task<decimal> GetBankBalanceAsync(CancellationToken ct = default) => Task.FromResult(1_600_000m);

    public Task<IReadOnlyList<CreditCard>> GetCardsAsync(UserContext user, CancellationToken ct = default)
    {
        CreditCard K(Scope s, string holder, string bank, string name, string l4, decimal limit, decimal debt, decimal stmt, int kesim, int due) =>
            new(Guid.NewGuid(), s, OwnerId, holder, bank, name, l4, limit, debt, stmt, Math.Round(stmt / 4), D(kesim), D(due), PaymentStatus.Bekliyor);

        IReadOnlyList<CreditCard> list =
        [
            K(Scope.Sahsi, "Ercan Değişmez", "Garanti BBVA", "Bonus Platinum", "4582", 250_000, 96_300, 85_450, -8, 4),
            K(Scope.Sahsi, "Ercan Değişmez", "Akbank", "Axess", "1190", 150_000, 48_000, 41_200, -2, 16),
            K(Scope.Ticari, "Demirbey Kimya", "İş Bankası", "Ticari Maximum", "7821", 1_500_000, 410_000, 245_750, -6, 5),
            K(Scope.Ticari, "Demirbey Kimya", "Yapı Kredi", "Ticari World", "6034", 1_000_000, 230_000, 182_000, -1, 15),
        ];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<Loan>> GetLoansAsync(UserContext user, CancellationToken ct = default)
    {
        Loan L(Scope s, string bank, string type, decimal amt, decimal rate, int firstDue, int left, decimal inst, decimal principalEach) =>
            new(Guid.NewGuid(), s, OwnerId, bank, type, amt, D(-400), rate,
                Enumerable.Range(0, left).Select(i => new LoanInstallment(D(firstDue).AddMonths(i), inst, principalEach, PaymentStatus.Bekliyor)).ToList());

        IReadOnlyList<Loan> list =
        [
            L(Scope.Ticari, "Şekerbank", "Ticari kredi", 5_000_000, 4.19m, 3, 18, 200_426.68m, 137_777.78m),
            L(Scope.Ticari, "QNB", "Rotatif / Spot", 2_500_000, 3.95m, 7, 11, 125_000, 95_454.55m),
            L(Scope.Sahsi, "Ziraat", "İhtiyaç kredisi", 300_000, 3.29m, 12, 20, 18_500, 8_400),
        ];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<PlannedPayment>> GetPlannedPaymentsAsync(UserContext user, CancellationToken ct = default)
    {
        IReadOnlyList<PlannedPayment> list =
            Enumerable.Range(0, 6).Select(i => new PlannedPayment(Guid.NewGuid(), Scope.Ticari, D(9 + 30 * i), 180_000, "Maaş / SGK / vergi (planlı)")).ToList();
        return Task.FromResult(list);
    }
}
