using Hive.EInvoice.Cii;
using Hive.EInvoice.Validation;

namespace Hive.EInvoice.Tests;

/// <summary>The README example, verbatim: it must stay valid XRechnung.</summary>
public class ReadmeExampleTests
{
    [Fact]
    public void ReadmeExampleIsValidXRechnung()
    {
        var invoice = new Invoice
        {
            Number = "RE-2026-0001",
            IssueDate = new DateTime(2026, 9, 15),
            DueDate = new DateTime(2026, 10, 15),
            BuyerReference = "04011000-12345-34",          // Leitweg-ID, BT-10
            Seller = new Party
            {
                Name = "Balsoft GmbH",
                VatIdentifier = "DE452983132",
                ElectronicAddress = new SchemedIdentifier("invoice@balsoft.de", ElectronicAddressScheme.Email),
                Address = new PostalAddress { Line1 = "Herler Str. 109", PostCode = "51067", City = "Köln", CountryCode = "DE" },
                Contact = new Contact { Name = "Buchhaltung", Phone = "+49 221 1234567", Email = "buchhaltung@balsoft.de" },
            },
            Buyer = new Party
            {
                Name = "Stadt Musterstadt",
                ElectronicAddress = new SchemedIdentifier("04011000-12345-34", ElectronicAddressScheme.LeitwegId),
                Address = new PostalAddress { Line1 = "Rathausplatz 1", PostCode = "53111", City = "Bonn", CountryCode = "DE" },
            },
            PaymentInstructions = PaymentInstructions.SepaCreditTransfer("DE89 3704 0044 0532 0130 00"),
        };
        invoice.AddLine(new InvoiceLine("Beratung", 10m, UnitCode.Hour, 120m, 19m));
        invoice.CashDiscounts.Add(new CashDiscount { Days = 7, Percent = 2m });   // written as #SKONTO#

        byte[] xml = CiiWriter.Write(invoice, InvoiceProfile.XRechnung);

        Assert.NotEmpty(xml);
        Assert.True(InvoiceValidator.Validate(invoice, InvoiceProfile.XRechnung).IsValid);
        if (ExternalValidators.Directory is { } tools)
        {
            string file = Path.Combine(AppContext.BaseDirectory, "samples", "readme-example.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, xml);
            var outcome = ExternalValidators.Kosit(tools, new[] { file })["readme-example"];
            Assert.True(outcome.Accepted, outcome.ToString());
        }
    }
}
