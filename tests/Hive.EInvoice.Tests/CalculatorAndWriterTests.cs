using System.Xml.Linq;
using Hive.EInvoice.Cii;
using Hive.EInvoice.Validation;

namespace Hive.EInvoice.Tests;

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
