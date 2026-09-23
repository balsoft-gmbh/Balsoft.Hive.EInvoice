# Balsoft.Hive.EInvoice

Electronic invoices for .NET that pass the official validators: **XRechnung 3.0**,
**ZUGFeRD 2.5 / Factur-X 1.09**, **Peppol BIS 3.0** and plain **EN 16931**, written and read
as **CII** and **UBL**, and embedded into **PDF/A-3** hybrid invoices.

[![CI](https://github.com/ertugrulbalveren/Balsoft.Hive.EInvoice/actions/workflows/ci.yml/badge.svg)](https://github.com/ertugrulbalveren/Balsoft.Hive.EInvoice/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Balsoft.Hive.EInvoice.svg)](https://www.nuget.org/packages/Balsoft.Hive.EInvoice)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

- **A model that speaks EN 16931.** Every property is named after its business term and
  documented with its number (`BuyerReference` is BT-10, `Seller` is BG-4).
- **Totals you cannot get wrong.** Line net amounts, document totals and the VAT breakdown
  are calculated from the lines exactly as EN 16931 prescribes: rounding per VAT category,
  allowances and charges, prepaid and rounding amounts.
- **Rules before sending.** A pre-flight check reports what a receiver's validator would
  reject, with the official rule id: `[BR-DE-15] XRechnung requires the buyer reference`.
- **Read what you receive.** CII and UBL readers turn incoming documents back into the
  model, and report where the sender's totals disagree with EN 16931.
- **Hybrid PDFs.** `Balsoft.Hive.EInvoice.Pdf` turns your ERP's PDF into a PDF/A-3b with the XML
  attached, Factur-X XMP metadata and missing fonts embedded.
- **Tested against the real thing.** Every build validates the samples with the KoSIT
  validator (XRechnung 3.0.2 configuration, CII and UBL) and with Mustang (Factur-X
  profiles, PDF/A-3 via veraPDF).

| Package | Dependencies | Targets |
|---|---|---|
| `Balsoft.Hive.EInvoice` | none | `netstandard2.0`, `net8.0` |
| `Balsoft.Hive.EInvoice.Pdf` | `Balsoft.Hive.EInvoice`, PDFsharp (MIT) | `netstandard2.0`, `net8.0` |

## Install

```
dotnet add package Balsoft.Hive.EInvoice
dotnet add package Balsoft.Hive.EInvoice.Pdf      # only for hybrid PDF invoices
```

## Write an XRechnung

```csharp
using Balsoft.Hive.EInvoice;
using Balsoft.Hive.EInvoice.Cii;

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
```

The same invoice as UBL, or as a hybrid PDF from your ERP's printout:

```csharp
byte[] ubl = UblWriter.Write(invoice, InvoiceProfile.XRechnung);
byte[] pdf = HybridPdf.Create(File.ReadAllBytes("RE-2026-0001.pdf"), invoice, InvoiceProfile.EN16931);
```

`CiiWriter` and `UblWriter` check the invoice first and throw `InvoiceValidationException`
listing every violated rule. To check without writing:

```csharp
var result = InvoiceValidator.Validate(invoice, InvoiceProfile.XRechnung);
foreach (var issue in result.Issues)
    Console.WriteLine(issue);   // [BR-DE-6] XRechnung requires the seller contact telephone number. (BT-42)
```

## Read an incoming invoice

```csharp
using Balsoft.Hive.EInvoice.Reading;

var read = InvoiceReader.Read(File.ReadAllBytes("incoming.xml"));   // CII or UBL, detected
Console.WriteLine($"{read.Syntax} {read.Profile}: {read.Invoice.Number} from {read.Invoice.Seller.Name}");
foreach (var difference in read.TotalsDiscrepancies())
    Console.WriteLine(difference);                                    // "BT-115 stated 100.00, calculated 100.01"

var fromPdf = HybridPdf.ReadInvoice(File.ReadAllBytes("incoming.pdf")); // ZUGFeRD / Factur-X / XRechnung PDF
```

## Profiles

| `InvoiceProfile` | Specification | CII | UBL | Validated in CI by |
|---|---|---|---|---|
| `XRechnung` | XRechnung 3.0 (German CIUS) | yes | yes | KoSIT, XRechnung scenarios |
| `EN16931` | EN 16931, Factur-X / ZUGFeRD "EN 16931" | yes | yes | KoSIT, EN 16931 scenarios |
| `FacturXExtended` | Factur-X / ZUGFeRD EXTENDED (EN 16931 content) | yes | | Mustang |
| `FacturXBasic` | Factur-X / ZUGFeRD BASIC | yes | | Mustang |
| `FacturXBasicWL` | Factur-X / ZUGFeRD BASIC WL (no lines) | yes | | Mustang |
| `FacturXMinimum` | Factur-X / ZUGFeRD MINIMUM (no lines) | yes | | Mustang |
| `PeppolBis3` | Peppol BIS Billing 3.0 | yes | yes | pre-flight Peppol rules |

Hybrid PDFs are written for every Factur-X profile and XRechnung, and validated as PDF/A-3b
by veraPDF inside Mustang.

## What the pre-flight check covers

The EN 16931 core rules (BR-01 to BR-65), the VAT category rules (BR-S, BR-Z, BR-E, BR-AE,
BR-IC, BR-G, BR-O), the code lists (BR-CL, extracted from the official artefact), and for
XRechnung the German rules (BR-DE) plus the Peppol rules XRechnung 3.0 includes. The test
suite breaks valid invoices in 31 specific ways and asserts that the pre-flight reports the
same rule id as the KoSIT validator. Where the pre-flight is stricter than the official
artefact (BR-CL-23 is shadowed in the CII Schematron and never fires), a test documents it.
The official validators remain authoritative.

## Hybrid PDF details

- Pages are taken from the carrier PDF unchanged; the XML is attached as `xrechnung.xml` or
  `factur-x.xml` with relationship `Alternative` (`Data` for MINIMUM and BASIC WL).
- XMP metadata declares PDF/A-3 conformance B and carries the Factur-X extension schema
  (DocumentType, DocumentFileName, Version, ConformanceLevel).
- The sRGB output intent profile is generated from the IEC 61966-2-1 definition by
  `eng/icc/make_srgb_icc.py`, so the package ships no third-party ICC file.
- TrueType fonts the carrier uses without embedding them are embedded from the system font
  folders, found by PostScript name. Type 1 fonts cannot be embedded this way and are logged.

## Code lists

`CodeList.IsUnitCode("HUR")`, `CodeList.IsCountryCode("DE")` and the other lookups use the
lists embedded in the EN 16931 validation artefact, regenerated with
`eng/codelists/extract_codelists.py` whenever KoSIT publishes a new configuration.

## Building and testing

```
dotnet test
```

The external validation tests need Java 21 and the validators:

```
bash eng/get-validators.sh              # KoSIT validator, XRechnung configuration, Mustang
export HIVE_VALIDATORS=$PWD/.tools/validators
dotnet test
```

## Specifications

EN 16931-1:2017, XRechnung 3.0.2 (KoSIT, validator configuration 2026-08-31),
ZUGFeRD 2.5 / Factur-X 1.09 (FeRD / FNFE-MPE, June 2026), Peppol BIS Billing 3.0,
UN/CEFACT CII D16B, OASIS UBL 2.1, ISO 19005-3 (PDF/A-3).

## License

Apache License 2.0. Copyright Balsoft GmbH. Maintained by Ertugrul Balveren
([Balsoft GmbH](https://balsoft.de)). It is the e-invoice engine of Hive DocFlow.
Packages are author-signed by Balsoft GmbH; see [SECURITY.md](SECURITY.md).
