using System.Xml.Linq;
using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Reading;
using Balsoft.Hive.EInvoice.Validation;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>The terms only EXTENDED has: written and read back there, ignored and reported everywhere else.</summary>
public class ExtendedProfileTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private const InvoiceProfile Extended = InvoiceProfile.FacturXExtended;

    private static XDocument Cii(Invoice invoice, InvoiceProfile profile) => XDocument.Parse(CiiWriter.WriteToString(invoice, profile));

    [Fact]
    public void WritingReadingAndWritingAgainIsIdentical()
    {
        byte[] first = CiiWriter.Write(Samples.ExtendedFull(), Extended);
        var read = InvoiceReader.Read(first);
        Assert.Equal(Extended, read.Profile);
        Assert.NotNull(read.Invoice.Extended);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(first), System.Text.Encoding.UTF8.GetString(CiiWriter.Write(read.Invoice, Extended)));
    }

    public static TheoryData<InvoiceProfile> OtherProfiles() => new()
    {
        InvoiceProfile.XRechnung, InvoiceProfile.EN16931, InvoiceProfile.PeppolBis3,
        InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXBasicWL, InvoiceProfile.FacturXMinimum,
    };

    private static Invoice WithoutExtendedTerms()
    {
        var inv = Samples.ExtendedFull();
        inv.Extended = null;
        foreach (var party in new[] { inv.Seller, inv.Buyer })
        {
            party.RoleCode = null;
            party.LegalAddress = null;
            party.AdditionalContacts.Clear();
            party.Contact!.TypeCode = null;
            party.Contact.Fax = null;
        }
        foreach (var note in inv.Notes) note.ContentCode = null;
        foreach (var preceding in inv.PrecedingInvoices) preceding.TypeCode = null;
        foreach (var document in inv.SupportingDocuments) document.IssueDate = null;
        foreach (var line in inv.Lines)
        {
            line.Extended = null;
            foreach (var attribute in line.Item.Attributes)
            {
                attribute.TypeCode = null;
                attribute.ValueMeasure = null;
                attribute.ValueMeasureUnitCode = null;
            }
            foreach (var classification in line.Item.Classifications) classification.ClassName = null;
            foreach (var ac in line.AllowancesAndCharges) ac.ReasonCodeListId = null;
        }
        foreach (var ac in inv.AllowancesAndCharges)
        {
            ac.SequenceNumber = null;
            ac.BaseQuantity = null;
            ac.BaseQuantityUnitCode = null;
            ac.ReasonCodeListId = null;
            ac.ExemptionReason = null;
            ac.ExemptionReasonCode = null;
        }
        return inv;
    }

    [Theory]
    [MemberData(nameof(OtherProfiles))]
    public void OtherProfilesWriteTheSameWithOrWithoutExtendedTerms(InvoiceProfile profile)
        => Assert.Equal(CiiWriter.WriteToString(WithoutExtendedTerms(), profile), CiiWriter.WriteToString(Samples.ExtendedFull(), profile));

    [Theory]
    [MemberData(nameof(OtherProfiles))]
    public void OtherProfilesWarnThatExtendedTermsAreNotWritten(InvoiceProfile profile)
        => Assert.Contains(InvoiceValidator.Validate(Samples.ExtendedFull(), profile).Issues, i => i.RuleId == "PROFILE-NOT-WRITTEN" && i.BusinessTerm == "EXTENDED");

    [Fact]
    public void ExtendedItselfDoesNotWarn()
        => Assert.DoesNotContain(InvoiceValidator.Validate(Samples.ExtendedFull(), Extended).Issues, i => i.BusinessTerm == "EXTENDED");

    [Fact]
    public void AnInvoiceWithoutExtendedTermsReadsBackUnchanged()
    {
        string first = CiiWriter.WriteToString(Samples.FullDetails(), Extended);
        var read = InvoiceReader.Read(System.Text.Encoding.UTF8.GetBytes(first)).Invoice;
        Assert.Equal(first, CiiWriter.WriteToString(read, Extended));
    }

    [Fact]
    public void EachRoleKeepsItsOwnData()
    {
        var doc = Cii(Samples.ExtendedFull(), Extended);
        Assert.Equal("Sales Agent GmbH", doc.Descendants(Ram + "SalesAgentTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Equal("Buyer Agent GmbH", doc.Descendants(Ram + "BuyerAgentTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Equal("Payer GmbH", doc.Descendants(Ram + "PayerTradeParty").Single().Element(Ram + "Name")!.Value);
    }

    [Fact]
    public void TheExtendedPayeeReplacesTheSimpleOne()
    {
        var inv = Samples.ExtendedFull();
        inv.Payee = new Payee { Name = "Simple Payee" };
        Assert.Equal("Payee GmbH", Cii(inv, Extended).Descendants(Ram + "ApplicableHeaderTradeSettlement").Elements(Ram + "PayeeTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Equal("Simple Payee", Cii(inv, InvoiceProfile.EN16931).Descendants(Ram + "ApplicableHeaderTradeSettlement").Elements(Ram + "PayeeTradeParty").Single().Element(Ram + "Name")!.Value);
    }

    [Fact]
    public void TheExtendedShipToKeepsTheDeliveryDate()
    {
        var doc = Cii(Samples.ExtendedFull(), Extended);
        var delivery = doc.Descendants(Ram + "ApplicableHeaderTradeDelivery").Single();
        Assert.Equal("Ship To GmbH", delivery.Elements(Ram + "ShipToTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Single(delivery.Elements(Ram + "ActualDeliverySupplyChainEvent"));
    }

    [Fact]
    public void AllContactsOfTheSellerAreWritten()
        => Assert.Equal(2, Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "SellerTradeParty").Elements(Ram + "DefinedTradeContact").Count());

    [Fact]
    public void TheVatBasisDetailsAddUpToTheVatBasis()
    {
        foreach (var tax in Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "ApplicableHeaderTradeSettlement").Elements(Ram + "ApplicableTradeTax"))
        {
            decimal Part(string name) => decimal.Parse(tax.Element(Ram + name)!.Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(Part("BasisAmount"), Part("LineTotalBasisAmount") + Part("AllowanceChargeBasisAmount"));
        }
    }

    [Fact]
    public void WithoutVatBasisDetailsTheyAreNotWritten()
    {
        var inv = Samples.ExtendedFull();
        inv.Extended!.WriteVatBasisDetails = false;
        Assert.Empty(Cii(inv, Extended).Descendants(Ram + "LineTotalBasisAmount"));
    }

    [Fact]
    public void ExtendedPaymentTermsTakeThePlaceOfTheSimpleOnes()
    {
        var terms = Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "SpecifiedTradePaymentTerms").ToList();
        Assert.Single(terms);
        Assert.Equal("Zahlbar innerhalb von 30 Tagen.", terms[0].Element(Ram + "Description")!.Value);
    }

    [Fact]
    public void TheOtherProfilesKeepTheSimplePaymentTerms()
    {
        var terms = Cii(Samples.ExtendedFull(), InvoiceProfile.EN16931).Descendants(Ram + "SpecifiedTradePaymentTerms").Single();
        Assert.Null(terms.Element(Ram + "PartialPaymentAmount"));
    }

    [Fact]
    public void LogisticsChargesCountAsChargesAndInTheVatBasisUnderExtendedOnly()
    {
        var inv = Samples.ExtendedFull();
        var with = InvoiceCalculator.Calculate(inv, Extended);
        var other = InvoiceCalculator.Calculate(inv, InvoiceProfile.EN16931);
        inv.Extended!.LogisticsCharges.Clear();
        var without = InvoiceCalculator.Calculate(inv, Extended);
        Assert.Equal(without.ChargeTotal + 17m, with.ChargeTotal);
        Assert.Equal(without.TaxExclusiveAmount + 17m, with.TaxExclusiveAmount);
        Assert.Equal(without.VatTotal + InvoiceCalculator.Round(12m * 19m / 100m), with.VatTotal);
        Assert.Equal(InvoiceCalculator.Calculate(inv, InvoiceProfile.EN16931).ChargeTotal, other.ChargeTotal);
    }

    [Fact]
    public void ThirdPartyChargesAddToTheAmountDueUnderExtended()
    {
        var inv = Samples.ExtendedFull();
        var with = InvoiceCalculator.Calculate(inv, Extended);
        inv.Extended!.FinancialAdjustments.Clear();
        var without = InvoiceCalculator.Calculate(inv, Extended);
        Assert.Equal(without.AmountDue + 3m, with.AmountDue);
        Assert.Equal(without.TaxInclusiveAmount, with.TaxInclusiveAmount);
    }

    [Fact]
    public void AdvancePaymentsDoNotChangeTheTotals()
    {
        var inv = Samples.ExtendedFull();
        var with = InvoiceCalculator.Calculate(inv, Extended);
        inv.Extended!.AdvancePayments.Clear();
        Assert.Equal(with.AmountDue, InvoiceCalculator.Calculate(inv, Extended).AmountDue);
    }

    [Fact]
    public void TheCalculatorWithoutAProfileIgnoresExtendedTerms()
        => Assert.Equal(InvoiceCalculator.Calculate(Samples.ExtendedFull(), InvoiceProfile.XRechnung).AmountDue, InvoiceCalculator.Calculate(Samples.ExtendedFull()).AmountDue);

    [Fact]
    public void TheStatedTotalsOfAnExtendedDocumentReadBackWithoutDiscrepancies()
        => Assert.Empty(InvoiceReader.Read(CiiWriter.Write(Samples.ExtendedFull(), Extended)).TotalsDiscrepancies());

    [Fact]
    public void GroupAndInformationLinesAreNotInTheTotalsUnderExtended()
    {
        var inv = Samples.ExtendedFull();
        var totals = InvoiceCalculator.Calculate(inv, Extended);
        decimal expected = inv.Lines.Where(l => l.Extended?.LineStatusReasonCode is null or "DETAIL").Sum(InvoiceCalculator.LineNetAmount);
        Assert.Equal(expected, totals.LineNetTotal);
        Assert.Equal(inv.Lines.Count, totals.LineNetAmounts.Count);
    }

    [Fact]
    public void AGroupLineStaysInTheTotalsOfOtherProfiles()
    {
        var inv = Samples.ExtendedFull();
        Assert.Equal(inv.Lines.Sum(InvoiceCalculator.LineNetAmount), InvoiceCalculator.Calculate(inv, InvoiceProfile.EN16931).LineNetTotal);
    }

    [Fact]
    public void LineTermsAreWrittenOnlyForTheirLine()
    {
        var lines = Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "IncludedSupplyChainTradeLineItem").ToList();
        var detail = lines.Single(l => l.Descendants(Ram + "LineID").First().Value == "G1-1");
        Assert.Equal("G1", detail.Descendants(Ram + "ParentLineID").Single().Value);
        Assert.Single(lines, l => l.Descendants(Ram + "ManufacturerTradeParty").Any());
    }

    [Fact]
    public void ALineLevelPartyIgnoresALegalAddress()
    {
        var manufacturer = Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "ManufacturerTradeParty").Single();
        Assert.Empty(manufacturer.Elements(Ram + "SpecifiedLegalOrganization").Elements(Ram + "PostalTradeAddress"));
    }

    [Fact]
    public void AnAttributeWritesAValueOrAMeasureNeverBoth()
    {
        var attributes = Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "ApplicableProductCharacteristic").ToList();
        Assert.All(attributes, a => Assert.True(a.Element(Ram + "Value") is null != (a.Element(Ram + "ValueMeasure") is null)));
        Assert.Contains(attributes, a => a.Element(Ram + "ValueMeasure") is not null);
    }

    [Fact]
    public void OtherProfilesWriteTheValueOfAnAttributeWithAMeasure()
        => Assert.Contains(Cii(Samples.ExtendedFull(), InvoiceProfile.EN16931).Descendants(Ram + "Value"), v => v.Value == "2 kg");

    [Fact]
    public void AFalseIndicatorIsWritten()
    {
        var inv = Samples.ExtendedFull();
        inv.Extended!.IsCopy = false;
        Assert.Equal("false", Cii(inv, Extended).Descendants(Ram + "CopyIndicator").Single().Elements().Single().Value);
    }
}
