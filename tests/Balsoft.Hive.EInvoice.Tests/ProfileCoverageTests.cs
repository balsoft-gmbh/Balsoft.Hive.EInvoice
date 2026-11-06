using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Balsoft.Hive.EInvoice.Cii;
using Balsoft.Hive.EInvoice.Ubl;

namespace Balsoft.Hive.EInvoice.Tests;

/// <summary>
/// Measures the writers against the official definitions instead of our own comments: every term the KoSIT
/// XRechnung model binds must appear in the XML of an invoice that fills every property, and every element
/// written for a Factur-X profile must exist in that profile's schema. The definitions come with
/// eng/get-validators.sh (HIVE_VALIDATORS); without them the checks are skipped, CI always has them.
/// </summary>
public class ProfileCoverageTests
{
    private static readonly XNamespace Model = "http://semo-xml.org/2025/model";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly WriterOptions NoCheck = new() { ValidateBeforeWriting = false };

    /// <summary>Binding entries the writers leave out on purpose.</summary>
    private static readonly Dictionary<(string Binding, string Term), string> Accepted = new()
    {
        // The model binds BT-18 on credit notes to code ATS; the EN 16931 UBL rules test 130, which is what the writer uses.
        [("ubl-cn", "BT-18")] = "DocumentTypeCode = 'ATS'",
        // The reader takes this element; the writer uses the Creditor element the same term is also bound to.
        [("cii", "BT-86")] = "PayeeSpecifiedDebtorFinancialInstitution",
    };

    /// <summary>A definition file, or null when no validators are set up. A set up folder without the file fails: a skip here would hide a gap.</summary>
    private static string? Tool(params string[] path)
    {
        if (Environment.GetEnvironmentVariable("HIVE_VALIDATORS") is not { Length: > 0 } dir) return null;
        string full = Path.Combine(new[] { dir }.Concat(path).ToArray());
        Assert.True(File.Exists(full), $"{full} is missing; run eng/get-validators.sh.");
        return full;
    }

    private static IEnumerable<Invoice> FullInvoices()
    {
        foreach (var type in new[] { InvoiceTypeCode.CommercialInvoice, InvoiceTypeCode.CreditNote })
        {
            yield return Full(type, card: true, taxPointDate: true, payee: true);
            yield return Full(type, card: false, taxPointDate: false, payee: false);
            yield return Full(type, card: false, taxPointDate: false, payee: true);
        }
    }

    /// <summary>Every property filled. BT-7 excludes BT-8, and BG-18 and BG-19 are alternatives, hence the variants.</summary>
    private static Invoice Full(InvoiceTypeCode type, bool card, bool taxPointDate, bool payee)
    {
        static PostalAddress Address() => new()
        {
            Line1 = "Line 1", Line2 = "Line 2", Line3 = "Line 3", City = "Köln", PostCode = "50667", CountrySubdivision = "NRW", CountryCode = "DE",
        };

        var inv = Samples.FullDetails();
        inv.TypeCode = type;
        inv.ContractReference = "K-1";
        if (!payee) inv.Payee = null;
        if (payee) inv.Payee!.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 2");
        if (taxPointDate) inv.TaxPointDate = Samples.Issue; else inv.TaxPointDateCode = "5";
        inv.ProjectName = card ? inv.ProjectName : null;
        inv.TaxCurrency = "USD";
        inv.VatTotalInTaxCurrency = 10m;
        inv.PrepaidAmount = 1m;
        inv.RoundingAmount = 0.1m;

        foreach (var party in new[] { inv.Seller, inv.Buyer })
        {
            party.Address = Address();
            party.TradingName = "Trading";
            party.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 1");
            party.Identifiers.Add(new SchemedIdentifier("ID-1"));
            party.Identifiers.Add(new SchemedIdentifier("4000001000005", IdentifierScheme.Gln));
            party.Contact = new Contact { Name = "Name", Department = "Department", Phone = "+49 221 12345", Email = "a@example.com" };
        }
        inv.Buyer.TaxRegistrationIdentifier = "1/2/3";
        inv.SellerTaxRepresentative = new TaxRepresentative { Name = "Representative", VatIdentifier = "DE111111111", Address = Address() };
        inv.Delivery!.Address = Address();
        inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = "P-1", IssueDate = Samples.Issue });
        inv.SupportingDocuments.Add(new SupportingDocument { Reference = "R-1", Description = "D", ExternalLocation = "https://example.com/d" });
        inv.SupportingDocuments.Add(new SupportingDocument { Reference = "R-2", Content = new byte[] { 1 }, MimeCode = "text/csv", FileName = "a.csv" });
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.Exempt, Reason = "Steuerfrei", ReasonCode = "VATEX-EU-G" });
        inv.AddLine(new InvoiceLine("Steuerfrei", 1m, UnitCode.Piece, 10m, 0m, VatCategory.Exempt));

        foreach (bool charge in new[] { false, true })
        {
            var ac = new AllowanceCharge { IsCharge = charge, BaseAmount = 100m, Percentage = 10m, Amount = 10m, Reason = "Reason", ReasonCode = charge ? "FC" : "95", VatCategory = VatCategory.StandardRate, VatRate = 19m };
            inv.AllowancesAndCharges.Add(ac);
            inv.Lines[0].AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = charge, BaseAmount = 100m, Percentage = 10m, Amount = 10m, Reason = "Reason", ReasonCode = charge ? "FC" : "95" });
        }
        var line = inv.Lines[0];
        line.GrossPrice = 500m;
        line.PriceDiscount = 50m;
        line.PriceBaseQuantity = 1m;
        line.PriceBaseQuantityUnitCode = line.UnitCode;

        inv.PaymentInstructions = new PaymentInstructions { MeansCode = card ? "48" : "59", MeansText = "Text", RemittanceInformation = "Remittance" };
        if (card) inv.PaymentInstructions.Card = new PaymentCard { PrimaryAccountNumber = "123456", HolderName = "Holder" };
        else inv.PaymentInstructions.DirectDebit = new DirectDebit { MandateReference = "M-1", CreditorIdentifier = "DE98ZZZ09999999999", DebitedAccountIdentifier = "DE89370400440532013000" };
        inv.PaymentInstructions.CreditTransfers.Add(new CreditTransferAccount { AccountIdentifier = "DE89370400440532013000", AccountName = "Account", ServiceProviderIdentifier = "COBADEFFXXX" });
        return inv;
    }

    private static XDocument Xml(byte[] bytes) => XDocument.Parse(Encoding.UTF8.GetString(bytes));

    public static TheoryData<string> Bindings() => new() { "cii", "ubl-inv", "ubl-cn" };

    [Theory]
    [MemberData(nameof(Bindings))]
    public void EveryTermTheXRechnungModelBindsIsWritten(string bindingId)
    {
        if (Tool("xrechnung-model", "xrechnung-cius-model.xml") is not { } file) { Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.sh."); return; }
        var binding = XDocument.Load(file).Descendants(Model + "binding").Single(b => b.Attribute("id")!.Value == bindingId);
        var documents = FullInvoices()
            .Select(i => Xml(bindingId == "cii" ? CiiWriter.Write(i, InvoiceProfile.XRechnung, NoCheck) : UblWriter.Write(i, InvoiceProfile.XRechnung, NoCheck)))
            .Where(d => bindingId == "cii" || d.Root!.Name.LocalName == (bindingId == "ubl-cn" ? "CreditNote" : "Invoice"))
            .ToList();
        Assert.NotEmpty(documents);

        var missing = new List<string>();
        int terms = 0;
        foreach (var term in binding.Elements(Model + "term"))
        {
            string id = term.Attribute("ref")!.Value;
            if (term.Attribute("xpath")?.Value is not { } xpath) continue;
            terms++;
            if (Accepted.TryGetValue((bindingId, id), out var marker) && xpath.Contains(marker, StringComparison.Ordinal)) continue;
            if (bindingId != "cii") xpath = Regex.Replace(xpath, @"(^|/|\| )(Invoice|CreditNote)(?=/|$)", "$1u:$2");
            if (!documents.Any(d => Matches(d, binding, xpath))) missing.Add($"{id}  {xpath}");
        }
        Assert.True(terms > 150, $"only {terms} terms in the {bindingId} binding");
        Assert.True(missing.Count == 0, $"{missing.Count} of {terms} terms not written in {bindingId}:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    private static bool Matches(XDocument document, XElement binding, string xpath)
    {
        var ns = new XmlNamespaceManager(new NameTable());
        foreach (var a in binding.Attributes().Where(a => a.IsNamespaceDeclaration && a.Name.Namespace != XNamespace.None))
            ns.AddNamespace(a.Name.LocalName, a.Value);
        ns.AddNamespace("u", document.Root!.Name.NamespaceName);
        return document.XPathEvaluate(xpath, ns) is IEnumerable<object> nodes && nodes.Any();
    }

    public static TheoryData<InvoiceProfile, string> FacturXSchemas() => new()
    {
        { InvoiceProfile.FacturXMinimum, "MINIMUM" },
        { InvoiceProfile.FacturXBasicWL, "BASIC-WL" },
        { InvoiceProfile.FacturXBasic, "BASIC" },
        { InvoiceProfile.EN16931, "EN16931" },
        { InvoiceProfile.FacturXExtended, "EXTENDED" },
    };

    [Theory]
    [MemberData(nameof(FacturXSchemas))]
    public void EveryElementWrittenExistsInTheProfileSchema(InvoiceProfile profile, string folder)
    {
        if (Tool("facturx", folder, $"FACTUR-X_{folder.Replace("-", "")}.xsd") is not { } main) { Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.sh."); return; }
        var allowed = SchemaPaths(main);
        Assert.True(allowed.Count > 20, $"only {allowed.Count} paths read from {main}");

        var extra = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var invoice in FullInvoices())
            foreach (var element in Xml(CiiWriter.Write(invoice, profile, NoCheck)).Descendants())
            {
                string path = string.Join("/", element.AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName));
                if (!allowed.Contains(path)) extra.Add(path);
            }
        Assert.True(extra.Count == 0, $"not in the {folder} schema:{Environment.NewLine}{string.Join(Environment.NewLine, extra)}");
    }

    /// <summary>All element paths a Factur-X schema allows, following the complex types across its files.</summary>
    private static HashSet<string> SchemaPaths(string mainFile)
    {
        var types = new Dictionary<(string, string), XElement>();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(mainFile)!, "*.xsd"))
        {
            var root = XDocument.Load(file).Root!;
            string ns = root.Attribute("targetNamespace")!.Value;
            foreach (var type in root.Elements(Xs + "complexType"))
                types[(ns, type.Attribute("name")!.Value)] = type;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        void Walk(XElement declaration, string path, ImmutableStack visited)
        {
            paths.Add(path);
            string? typeName = declaration.Attribute("type")?.Value;
            if (typeName is null || !typeName.Contains(':')) return;
            var parts = typeName.Split(':');
            var key = (declaration.GetNamespaceOfPrefix(parts[0])!.NamespaceName, parts[1]);
            if (visited.Contains(key) || !types.TryGetValue(key, out var type)) return;
            foreach (var child in type.Descendants(Xs + "element").Where(e => e.Attribute("name") is not null))
                Walk(child, path + "/" + child.Attribute("name")!.Value, visited.Push(key));
        }

        var rootElement = XDocument.Load(mainFile).Root!.Elements(Xs + "element").Single();
        Walk(rootElement, rootElement.Attribute("name")!.Value, ImmutableStack.Empty);
        return paths;
    }

    private sealed class ImmutableStack
    {
        public static readonly ImmutableStack Empty = new(null, default);
        private readonly ImmutableStack? _next;
        private readonly (string, string) _value;

        private ImmutableStack(ImmutableStack? next, (string, string) value)
        {
            _next = next;
            _value = value;
        }

        public ImmutableStack Push((string, string) value) => new(this, value);

        public bool Contains((string, string) value)
        {
            for (var s = this; s._next is not null; s = s._next)
                if (s._value == value) return true;
            return false;
        }
    }
}
