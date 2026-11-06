using System.Xml.Linq;
using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Reading;
using Balsoft.Hive.EInvoice.Ubl;
using Balsoft.Hive.EInvoice.Validation;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>What each profile may carry, and that a value it cannot carry is reported, not lost silently.</summary>
public class ProfileLimitsTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly WriterOptions NoCheck = new() { ValidateBeforeWriting = false };

    private static XDocument Cii(Invoice invoice, InvoiceProfile profile) => XDocument.Parse(CiiWriter.WriteToString(invoice, profile, NoCheck));

    private static XElement SellerContact(XDocument d) => d.Descendants(Ram + "SellerTradeParty").Single().Element(Ram + "DefinedTradeContact")!;

    private static Invoice DepartmentOnly()
    {
        var inv = Samples.Standard();
        inv.Seller.Contact!.Name = null;
        inv.Seller.Contact.Department = "Buchhaltung";
        return inv;
    }

    [Fact]
    public void DepartmentWithoutANameWritesNoEmptyPersonNameAndReadsBack()
    {
        var doc = Cii(DepartmentOnly(), InvoiceProfile.XRechnung);
        Assert.Equal("Buchhaltung", SellerContact(doc).Element(Ram + "DepartmentName")!.Value);
        Assert.Null(SellerContact(doc).Element(Ram + "PersonName"));

        byte[] first = CiiWriter.Write(DepartmentOnly(), InvoiceProfile.XRechnung);
        var read = InvoiceReader.Read(first).Invoice;
        Assert.Null(read.Seller.Contact!.Name);
        Assert.Equal("Buchhaltung", read.Seller.Contact.Department);
        Assert.Equal(first, CiiWriter.Write(read, InvoiceProfile.XRechnung));
    }

    [Fact]
    public void NameAndDepartmentAreBothWrittenPersonNameFirst()
    {
        var inv = Samples.Standard();
        inv.Seller.Contact!.Department = "Buchhaltung";
        var names = SellerContact(Cii(inv, InvoiceProfile.XRechnung)).Elements().Select(e => e.Name.LocalName).Take(2);
        Assert.Equal(new[] { "PersonName", "DepartmentName" }, names);
    }

    [Fact]
    public void DepartmentSatisfiesBrDe5()
        => Assert.DoesNotContain(InvoiceValidator.Validate(DepartmentOnly(), InvoiceProfile.XRechnung).Issues, i => i.RuleId == "BR-DE-5");

    [Fact]
    public void AContactWithNeitherNameNorDepartmentStillFailsBrDe5()
    {
        var inv = Samples.Standard();
        inv.Seller.Contact!.Name = null;
        Assert.Contains(InvoiceValidator.Validate(inv, InvoiceProfile.XRechnung).Issues, i => i.RuleId == "BR-DE-5");
    }

    [Fact]
    public void UblHasOneContactElementSoTheDepartmentFillsItWhenThereIsNoName()
    {
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(UblWriter.Write(DepartmentOnly(), InvoiceProfile.XRechnung)));
        var contact = doc.Descendants(Cac + "AccountingSupplierParty").Single().Descendants(Cac + "Contact").Single();
        Assert.Equal("Buchhaltung", contact.Element(Cbc + "Name")!.Value);
    }

    private static string WithBic(string institutionElements)
    {
        var doc = Cii(Samples.Standard(), InvoiceProfile.XRechnung);
        var means = doc.Descendants(Ram + "SpecifiedTradeSettlementPaymentMeans").First();
        means.Elements(Ram + "PayeeSpecifiedCreditorFinancialInstitution").Remove();
        means.Add(XElement.Parse($"<r xmlns:ram=\"{Ram}\">{institutionElements}</r>").Elements());
        return doc.ToString();
    }

    [Fact]
    public void ABicUnderTheDebtorInstitutionIsRead()
    {
        string xml = WithBic("<ram:PayeeSpecifiedDebtorFinancialInstitution><ram:BICID>DEBTDEFF</ram:BICID></ram:PayeeSpecifiedDebtorFinancialInstitution>");
        var read = InvoiceReader.Read(System.Text.Encoding.UTF8.GetBytes(xml)).Invoice;
        Assert.Equal("DEBTDEFF", read.PaymentInstructions!.CreditTransfers.Single().ServiceProviderIdentifier);
    }

    [Fact]
    public void TheCreditorInstitutionWinsWhenBothCarryABic()
    {
        string xml = WithBic(
            "<ram:PayeeSpecifiedDebtorFinancialInstitution><ram:BICID>DEBTDEFF</ram:BICID></ram:PayeeSpecifiedDebtorFinancialInstitution>"
            + "<ram:PayeeSpecifiedCreditorFinancialInstitution><ram:BICID>CREDDEFF</ram:BICID></ram:PayeeSpecifiedCreditorFinancialInstitution>");
        var read = InvoiceReader.Read(System.Text.Encoding.UTF8.GetBytes(xml)).Invoice;
        Assert.Equal("CREDDEFF", read.PaymentInstructions!.CreditTransfers.Single().ServiceProviderIdentifier);
    }

    public static TheoryData<InvoiceProfile> ReducedProfiles() => new() { InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXBasicWL };

    public static TheoryData<InvoiceProfile> FullProfiles() => new() { InvoiceProfile.XRechnung, InvoiceProfile.EN16931, InvoiceProfile.FacturXExtended };

    private static readonly string[] ReducedOut = { "TaxPointDate", "RoundingAmount", "ApplicableTradeSettlementFinancialCard", "Information" };

    [Theory]
    [MemberData(nameof(ReducedProfiles))]
    public void BasicProfilesLeaveOutWhatTheirSchemaDoesNotHold(InvoiceProfile profile)
    {
        var doc = Cii(Samples.CardTaxPointRounding(), profile);
        foreach (string element in ReducedOut)
            Assert.Empty(doc.Descendants(Ram + element));
    }

    [Theory]
    [MemberData(nameof(ReducedProfiles))]
    public void BasicProfilesWarnForEveryValueTheyLeaveOut(InvoiceProfile profile)
    {
        var warned = InvoiceValidator.Validate(Samples.CardTaxPointRounding(), profile).Issues
            .Where(i => i.RuleId == "PROFILE-NOT-WRITTEN" && i.Severity == Severity.Warning).Select(i => i.BusinessTerm).ToList();
        Assert.Equal(new[] { "BG-18", "BT-114", "BT-7", "BT-82" }, warned.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void BasicWithNoneOfTheseValuesHasNoWarning()
        => Assert.DoesNotContain(InvoiceValidator.Validate(Samples.Standard(), InvoiceProfile.FacturXBasic).Issues, i => i.RuleId == "PROFILE-NOT-WRITTEN");

    [Theory]
    [MemberData(nameof(ReducedProfiles))]
    public void BasicProfilesKeepTheDueAmountConsistentWithoutRounding(InvoiceProfile profile)
    {
        var inv = Samples.CardTaxPointRounding();
        var totals = InvoiceCalculator.Calculate(inv);
        var sum = Cii(inv, profile).Descendants(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation").Single();
        Assert.Equal(CiiWriter.Amount(totals.TaxInclusiveAmount), sum.Element(Ram + "GrandTotalAmount")!.Value);
        Assert.Equal(CiiWriter.Amount(totals.TaxInclusiveAmount), sum.Element(Ram + "DuePayableAmount")!.Value);
    }

    [Theory]
    [MemberData(nameof(FullProfiles))]
    public void FullProfilesKeepThem(InvoiceProfile profile)
    {
        var doc = Cii(Samples.CardTaxPointRounding(), profile);
        foreach (string element in ReducedOut)
            Assert.NotEmpty(doc.Descendants(Ram + element));
        Assert.DoesNotContain(InvoiceValidator.Validate(Samples.CardTaxPointRounding(), profile).Issues, i => i.RuleId == "PROFILE-NOT-WRITTEN");
    }

    [Fact]
    public void MinimumWritesTheBusinessProcessAndBuyerCountry()
    {
        var inv = Samples.Standard();
        inv.BusinessProcess = "urn:example:process";
        var doc = Cii(inv, InvoiceProfile.FacturXMinimum);
        Assert.Equal("urn:example:process", doc.Descendants(Ram + "BusinessProcessSpecifiedDocumentContextParameter").Single().Element(Ram + "ID")!.Value);
        Assert.Equal("DE", doc.Descendants(Ram + "BuyerTradeParty").Single().Element(Ram + "PostalTradeAddress")!.Element(Ram + "CountryID")!.Value);
    }

    [Fact]
    public void MinimumOmitsWhatIsNotSet()
    {
        var inv = Samples.Standard();
        inv.BusinessProcess = null;
        inv.Buyer.Address.CountryCode = "";
        var doc = Cii(inv, InvoiceProfile.FacturXMinimum);
        Assert.Empty(doc.Descendants(Ram + "BusinessProcessSpecifiedDocumentContextParameter"));
        Assert.Null(doc.Descendants(Ram + "BuyerTradeParty").Single().Element(Ram + "PostalTradeAddress"));
    }
}
