using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Pdf;
using Balsoft.Hive.EInvoice.Reading;
using Balsoft.Hive.EInvoice.Ubl;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>Write, read, write again: the second document must equal the first byte for byte.</summary>
public class RoundTripTests
{
    public static TheoryData<string, InvoiceProfile> Cases()
    {
        var data = new TheoryData<string, InvoiceProfile>();
        foreach (var name in Samples.All.Keys)
            foreach (var profile in new[] { InvoiceProfile.XRechnung, InvoiceProfile.EN16931 })
                data.Add(name, profile);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CiiRoundTrips(string sample, InvoiceProfile profile)
    {
        byte[] first = CiiWriter.Write(Samples.All[sample](), profile);
        var read = InvoiceReader.Read(first);
        Assert.Equal(InvoiceSyntax.Cii, read.Syntax);
        Assert.Equal(profile, read.Profile);
        Assert.Empty(read.TotalsDiscrepancies());
        Assert.Equal(Text(first), Text(CiiWriter.Write(read.Invoice, profile)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UblRoundTrips(string sample, InvoiceProfile profile)
    {
        byte[] first = UblWriter.Write(Samples.All[sample](), profile);
        var read = InvoiceReader.Read(first);
        Assert.Equal(InvoiceSyntax.Ubl, read.Syntax);
        Assert.Equal(profile, read.Profile);
        Assert.Empty(read.TotalsDiscrepancies());
        Assert.Equal(Text(first), Text(UblWriter.Write(read.Invoice, profile)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CiiAndUblCarryTheSameInvoice(string sample, InvoiceProfile profile)
    {
        var fromCii = InvoiceReader.Read(CiiWriter.Write(Samples.All[sample](), profile)).Invoice;
        var fromUbl = InvoiceReader.Read(UblWriter.Write(Samples.All[sample](), profile)).Invoice;
        Assert.Equal(Text(CiiWriter.Write(fromCii, profile)), Text(CiiWriter.Write(fromUbl, profile)));
    }

    [Fact]
    public void HybridPdfReadsBackTheInvoice()
    {
        var invoice = Samples.SkontoAktivbank();
        byte[] pdf = HybridPdf.Create(Carriers.Vector(), invoice, InvoiceProfile.XRechnung);
        var read = HybridPdf.ReadInvoice(pdf)!;
        Assert.Equal(InvoiceProfile.XRechnung, read.Profile);
        Assert.Equal(invoice.Number, read.Invoice.Number);
        Assert.Equal(2, read.Invoice.CashDiscounts.Count);
    }

    [Fact]
    public void DiscrepanciesAreReportedForForeignArithmetic()
    {
        string xml = CiiWriter.WriteToString(Samples.Standard(), InvoiceProfile.EN16931)
            .Replace("<ram:DuePayableAmount>2616.81</ram:DuePayableAmount>", "<ram:DuePayableAmount>2616.80</ram:DuePayableAmount>", StringComparison.Ordinal);
        var read = InvoiceReader.Read(System.Text.Encoding.UTF8.GetBytes(xml));
        Assert.Equal("BT-115 stated 2616.80, calculated 2616.81", Assert.Single(read.TotalsDiscrepancies()));
    }

    private static string Text(byte[] xml) => System.Text.Encoding.UTF8.GetString(xml);
}
