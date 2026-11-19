using System.Xml.Linq;
using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Reading;
using Balsoft.Hive.EInvoice.Ubl;
using Balsoft.Hive.EInvoice.Validation;

namespace Balsoft.Hive.EInvoice.Tests;

public class CalculatorTests
{
    [Fact]
    public void LineNetAmountUsesBaseQuantityAndLineAllowancesAndCharges()
    {
        var inv = Samples.AllowancesCharges();
        var totals = inv.CalculateTotals();

        // 1000 × 12.50 / 100 = 125.00, minus 10 % of 125 = 12.50, plus 5.00 packaging
        Assert.Equal(117.50m, totals.LineNetAmounts[0]);
        Assert.Equal(170.00m, totals.LineNetAmounts[1]);
        Assert.Equal(287.50m, totals.LineNetTotal);
        Assert.Equal(10m, totals.AllowanceTotal);
        Assert.Equal(15m, totals.ChargeTotal);
        Assert.Equal(292.50m, totals.TaxExclusiveAmount);
        Assert.Equal(55.58m, totals.VatTotal);            // 292.50 × 19 % = 55.575, rounded half away from zero
        Assert.Equal(348.08m, totals.TaxInclusiveAmount);
        Assert.Equal(348.08m, totals.AmountDue);
    }

    [Fact]
    public void VatIsRoundedPerCategoryNotPerLine()
    {
        var inv = Samples.Base();
        for (int i = 0; i < 3; i++) inv.AddLine(new InvoiceLine("Artikel", 1m, UnitCode.Piece, 0.05m, 19m));
        var totals = inv.CalculateTotals();

        // per line 0.0095 would round to 0.01 each (0.03); per category 0.15 × 19 % = 0.0285 -> 0.03
        Assert.Single(totals.VatBreakdown);
        Assert.Equal(0.15m, totals.VatBreakdown[0].TaxableAmount);
        Assert.Equal(0.03m, totals.VatTotal);
    }

    [Fact]
    public void MixedRatesProduceOneBreakdownEntryEach()
    {
        var totals = Samples.MixedVat().CalculateTotals();
        Assert.Equal(2, totals.VatBreakdown.Count);
        var standard = totals.VatBreakdown.Single(b => b.Rate == 19m);
        var reduced = totals.VatBreakdown.Single(b => b.Rate == 7m);
        Assert.Equal(204.95m, standard.TaxableAmount);
        Assert.Equal(38.94m, standard.TaxAmount);
        Assert.Equal(30m, reduced.TaxableAmount);
        Assert.Equal(2.10m, reduced.TaxAmount);
    }

    [Fact]
    public void PrepaidAndRoundingAdjustTheAmountDue()
    {
        var totals = Samples.PrepaidRounding().CalculateTotals();
        Assert.Equal(2616.81m, totals.TaxInclusiveAmount);
        Assert.Equal(1617.00m, totals.AmountDue);
    }

    [Fact]
    public void NotSubjectToVatHasNoLineRateButAZeroBreakdownRate()
    {
        var totals = Samples.NotSubject().CalculateTotals();
        var b = Assert.Single(totals.VatBreakdown);
        Assert.Equal(VatCategory.NotSubjectToVat, b.Category);
        Assert.Equal(0m, b.Rate);
        Assert.Equal(0m, b.TaxAmount);
        Assert.Equal("VATEX-EU-O", b.ExemptionReasonCode);
    }

    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("de89370400440532013000", true)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("1234567890", false)]
    [InlineData("GB82WEST12345698765432", true)]
    public void IbanCheck(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));
}

public class WriterTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";

    private static XDocument Cii(Invoice inv, InvoiceProfile profile = InvoiceProfile.XRechnung)
        => XDocument.Parse(CiiWriter.WriteToString(inv, profile));

    [Fact]
    public void SkontoLinesFollowTheXRechnungSyntaxAndEndWithALineBreak()
    {
        var doc = Cii(Samples.SkontoAktivbank());
        string terms = doc.Descendants(Ram + "SpecifiedTradePaymentTerms").Single().Element(Ram + "Description")!.Value;
        Assert.Equal(
            "Zahlbar innerhalb von 30 Tagen ohne Abzug.\n#SKONTO#TAGE=7#PROZENT=2.00#\n#SKONTO#TAGE=14#PROZENT=1.00#BASISBETRAG=2616.81#\n",
            terms);
    }

    [Fact]
    public void OutputIsDeterministic()
        => Assert.Equal(CiiWriter.Write(Samples.FullDetails(), InvoiceProfile.XRechnung), CiiWriter.Write(Samples.FullDetails(), InvoiceProfile.XRechnung));

    [Fact]
    public void GuidelineIdentifierFollowsTheProfile()
    {
        foreach (var profile in new[] { InvoiceProfile.XRechnung, InvoiceProfile.EN16931, InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXMinimum })
        {
            var doc = Cii(Samples.Standard(), profile);
            var id = doc.Descendants(Ram + "GuidelineSpecifiedDocumentContextParameter").Single().Element(Ram + "ID")!.Value;
            Assert.Equal(profile.SpecificationIdentifier(), id);
        }
    }

    [Fact]
    public void MinimumAndBasicWlCarryNoLines()
    {
        Assert.Empty(Cii(Samples.Standard(), InvoiceProfile.FacturXMinimum).Descendants(Ram + "IncludedSupplyChainTradeLineItem"));
        Assert.Empty(Cii(Samples.Standard(), InvoiceProfile.FacturXBasicWL).Descendants(Ram + "IncludedSupplyChainTradeLineItem"));
        Assert.Equal(2, Cii(Samples.Standard(), InvoiceProfile.FacturXBasic).Descendants(Ram + "IncludedSupplyChainTradeLineItem").Count());
    }

    [Fact]
    public void ProjectNameIsWrittenNextToTheProjectReference()
    {
        var project = Cii(Samples.FullDetails()).Descendants(Ram + "SpecifiedProcuringProject").Single();
        Assert.Equal("PRJ-77", project.Element(Ram + "ID")!.Value);
        Assert.Equal("Wartung Rechenzentrum Süd", project.Element(Ram + "Name")!.Value);
    }

    [Fact]
    public void WithoutAProjectNameCiiGetsTheRequiredPlaceholder()
    {
        var inv = Samples.FullDetails();
        inv.ProjectName = " ";
        var project = Cii(inv).Descendants(Ram + "SpecifiedProcuringProject").Single();
        Assert.Equal("Project reference", project.Element(Ram + "Name")!.Value);
        Assert.Null(InvoiceReader.Read(CiiWriter.Write(inv, InvoiceProfile.XRechnung)).Invoice.ProjectName);
    }

    [Fact]
    public void ProjectNameWithoutAProjectReferenceIsNotWritten()
    {
        var inv = Samples.FullDetails();
        inv.ProjectReference = null;
        Assert.Empty(Cii(inv).Descendants(Ram + "SpecifiedProcuringProject"));
    }

    [Fact]
    public void ProjectNameIsReadBackFromCii()
        => Assert.Equal("Wartung Rechenzentrum Süd", InvoiceReader.Read(CiiWriter.Write(Samples.FullDetails(), InvoiceProfile.XRechnung)).Invoice.ProjectName);

    [Fact]
    public void UblCarriesOnlyTheProjectReference()
    {
        string ubl = UblWriter.WriteToString(Samples.FullDetails(), InvoiceProfile.XRechnung);
        Assert.Contains("PRJ-77", ubl);
        Assert.DoesNotContain("Wartung Rechenzentrum Süd", ubl);
    }

    [Fact]
    public void ValidIbanIsWrittenAsIbanAndOtherAccountsAsProprietary()
    {
        var inv = Samples.Standard();
        inv.PaymentInstructions = new PaymentInstructions { MeansCode = PaymentMeansCode.CreditTransfer };
        inv.PaymentInstructions.CreditTransfers.Add(new CreditTransferAccount { AccountIdentifier = "de89 3704 0044 0532 0130 00" });
        inv.PaymentInstructions.CreditTransfers.Add(new CreditTransferAccount { AccountIdentifier = "12345678" });
        var accounts = Cii(inv).Descendants(Ram + "PayeePartyCreditorFinancialAccount").ToList();
        Assert.Equal("DE89370400440532013000", accounts[0].Element(Ram + "IBANID")!.Value);
        Assert.Equal("12345678", accounts[1].Element(Ram + "ProprietaryID")!.Value);
    }

    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static XDocument Ubl(Invoice inv, InvoiceProfile profile = InvoiceProfile.XRechnung)
        => XDocument.Parse(UblWriter.WriteToString(inv, profile));

    private static Invoice WithAccounts(int count)
    {
        var inv = Samples.SeveralAccounts();
        if (count == 0)
        {
            inv.PaymentInstructions = null;
            return inv;
        }
        var accounts = inv.PaymentInstructions!.CreditTransfers.Take(count).ToList();
        inv.PaymentInstructions.CreditTransfers.Clear();
        inv.PaymentInstructions.CreditTransfers.AddRange(accounts);
        return inv;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void EveryAccountIsItsOwnPaymentMeansWithTheSameCode(int count)
    {
        var inv = WithAccounts(count);
        var cii = Cii(inv, InvoiceProfile.EN16931);
        var means = cii.Descendants(Ram + "SpecifiedTradeSettlementPaymentMeans").ToList();
        Assert.Equal(count, means.Count);
        Assert.All(means, m => Assert.Equal("58", m.Element(Ram + "TypeCode")!.Value));
        Assert.Equal(count == 0 ? 0 : 1, cii.Descendants(Ram + "PaymentReference").Count());
        Assert.Equal(new[] { "DE89370400440532013000", "DE02120300000000202051", "GB82WEST12345698765432", "NL91ABNA0417164300" }.Take(count),
            means.Select(m => m.Element(Ram + "PayeePartyCreditorFinancialAccount")!.Element(Ram + "IBANID")!.Value));

        var ubl = Ubl(inv, InvoiceProfile.EN16931).Descendants(Cac + "PaymentMeans").ToList();
        Assert.Equal(count, ubl.Count);
        Assert.All(ubl, m => Assert.Equal("58", m.Element(Cbc + "PaymentMeansCode")!.Value));
        Assert.All(ubl, m => Assert.Equal("RE-2026-0023", m.Element(Cbc + "PaymentID")!.Value));
        Assert.Equal(count, ubl.Count(m => m.Element(Cac + "PayeeFinancialAccount") is not null));
    }

    [Fact]
    public void AccountWithoutBicOrNameLeavesOnlyThatElementOut()
    {
        var cii = Cii(Samples.SeveralAccounts(), InvoiceProfile.EN16931).Descendants(Ram + "SpecifiedTradeSettlementPaymentMeans").ToList();
        Assert.Null(cii[1].Element(Ram + "PayeeSpecifiedCreditorFinancialInstitution"));
        Assert.NotNull(cii[1].Element(Ram + "PayeePartyCreditorFinancialAccount")!.Element(Ram + "AccountName"));
        Assert.Null(cii[2].Element(Ram + "PayeePartyCreditorFinancialAccount")!.Element(Ram + "AccountName"));
        Assert.NotNull(cii[2].Element(Ram + "PayeeSpecifiedCreditorFinancialInstitution"));

        var ubl = Ubl(Samples.SeveralAccounts()).Descendants(Cac + "PayeeFinancialAccount").ToList();
        Assert.Null(ubl[1].Element(Cac + "FinancialInstitutionBranch"));
        Assert.Null(ubl[2].Element(Cbc + "Name"));
    }

    [Fact]
    public void UblCreditNoteStatesTheDueDateOnceWhateverTheNumberOfAccounts()
    {
        var means = Ubl(Samples.CreditNoteTwoAccounts()).Descendants(Cac + "PaymentMeans").ToList();
        Assert.Equal(2, means.Count);
        Assert.Equal(1, means.Count(m => m.Element(Cbc + "PaymentDueDate") is not null));
        Assert.NotNull(means[0].Element(Cbc + "PaymentDueDate"));
        Assert.Equal("2026-10-15", means[0].Element(Cbc + "PaymentDueDate")!.Value);
    }

    [Fact]
    public void UblMeansTextOccursOnceWhileCiiRepeatsItPerPaymentMeans()
    {
        var inv = Samples.SeveralAccounts();
        inv.PaymentInstructions!.MeansText = "Überweisung";
        var codes = Ubl(inv).Descendants(Cac + "PaymentMeans").Select(m => m.Element(Cbc + "PaymentMeansCode")!).ToList();
        Assert.Equal(4, codes.Count);
        Assert.Equal("Überweisung", codes[0].Attribute("name")!.Value);
        Assert.All(codes.Skip(1), c => Assert.Null(c.Attribute("name")));
        Assert.Equal("Überweisung", UblReader.Read(UblWriter.Write(inv, InvoiceProfile.XRechnung)).Invoice.PaymentInstructions!.MeansText);

        var information = Cii(inv).Descendants(Ram + "SpecifiedTradeSettlementPaymentMeans").Select(m => m.Element(Ram + "Information")!.Value).ToList();
        Assert.Equal(Enumerable.Repeat("Überweisung", 4), information);
    }

    [Fact]
    public void InvalidInvoiceIsNotWrittenUnlessValidationIsSwitchedOff()
    {
        var inv = Samples.Standard();
        inv.BuyerReference = null;
        var ex = Assert.Throws<InvoiceValidationException>(() => CiiWriter.Write(inv, InvoiceProfile.XRechnung));
        Assert.Contains(ex.Result.Errors, e => e.RuleId == "BR-DE-15");
        Assert.NotEmpty(CiiWriter.Write(inv, InvoiceProfile.XRechnung, new WriterOptions { ValidateBeforeWriting = false }));
        Assert.NotEmpty(CiiWriter.Write(inv, InvoiceProfile.EN16931));
    }

    [Fact]
    public void EveryValidSamplePassesThePreflightWithoutErrors()
    {
        foreach (var (name, build) in Samples.All)
        {
            var result = InvoiceValidator.Validate(build(), InvoiceProfile.XRechnung);
            Assert.True(result.IsValid, $"{name}: {result}");
        }
    }
}
