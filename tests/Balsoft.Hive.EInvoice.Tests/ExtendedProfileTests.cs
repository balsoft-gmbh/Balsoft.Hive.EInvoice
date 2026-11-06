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
        Assert.Equal("Payee GmbH", Cii(inv, Extended).Descendants(Ram + "PayeeTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Equal("Simple Payee", Cii(inv, InvoiceProfile.EN16931).Descendants(Ram + "PayeeTradeParty").Single().Element(Ram + "Name")!.Value);
    }

    [Fact]
    public void TheExtendedShipToKeepsTheDeliveryDate()
    {
        var doc = Cii(Samples.ExtendedFull(), Extended);
        Assert.Equal("Ship To GmbH", doc.Descendants(Ram + "ShipToTradeParty").Single().Element(Ram + "Name")!.Value);
        Assert.Single(doc.Descendants(Ram + "ActualDeliverySupplyChainEvent"));
    }

    [Fact]
    public void AllContactsOfTheSellerAreWritten()
        => Assert.Equal(2, Cii(Samples.ExtendedFull(), Extended).Descendants(Ram + "SellerTradeParty").Elements(Ram + "DefinedTradeContact").Count());

    [Fact]
    public void AFalseIndicatorIsWritten()
    {
        var inv = Samples.ExtendedFull();
        inv.Extended!.IsCopy = false;
        Assert.Equal("false", Cii(inv, Extended).Descendants(Ram + "CopyIndicator").Single().Elements().Single().Value);
    }
}
