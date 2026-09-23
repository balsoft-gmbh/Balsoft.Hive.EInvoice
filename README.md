# Hive.EInvoice

Electronic invoices for .NET that pass the official validators: **XRechnung 3.0**,
**ZUGFeRD 2.5 / Factur-X 1.09** and plain **EN 16931**, written as UN/CEFACT **CII**.

[![CI](https://github.com/ertugrulbalveren/Hive.EInvoice/actions/workflows/ci.yml/badge.svg)](https://github.com/ertugrulbalveren/Hive.EInvoice/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Hive.EInvoice.svg)](https://www.nuget.org/packages/Hive.EInvoice)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

- **A model that speaks EN 16931.** Every property is named after its business term and
  documented with its number (`BuyerReference` is BT-10, `Seller` is BG-4).
- **Totals you cannot get wrong.** Line net amounts, document totals and the VAT breakdown
  are calculated from the lines exactly as EN 16931 prescribes: rounding per VAT category,
  allowances and charges, prepaid and rounding amounts.
- **Rules before sending.** A pre-flight check reports what a receiver's validator would
  reject, with the official rule id: `[BR-DE-15] XRechnung requires the buyer reference`.
- **Tested against the real thing.** Every sample in the test suite is validated by the
  KoSIT validator (XRechnung 3.0.2 configuration) and by Mustang (Factur-X profiles) on
  every build.
- **No dependencies.** `Hive.EInvoice` depends on nothing but the base class library.
  Targets `netstandard2.0` and `net8.0`.

## Install

```
dotnet add package Hive.EInvoice
```

## Write an XRechnung

```csharp
using Hive.EInvoice;
using Hive.EInvoice.Cii;

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

`CiiWriter.Write` checks the invoice first and throws `InvoiceValidationException` with
every violated rule. To check without writing:

```csharp
var result = InvoiceValidator.Validate(invoice, InvoiceProfile.XRechnung);
foreach (var issue in result.Issues)
    Console.WriteLine(issue);   // [BR-DE-6] XRechnung requires the seller contact telephone number. (BT-42)
```

## Profiles

| `InvoiceProfile` | Specification | Lines | Validated in CI by |
|---|---|---|---|
| `XRechnung` | XRechnung 3.0 (German CIUS) | yes | KoSIT, scenario "EN16931 XRechnung (CII)" |
| `EN16931` | EN 16931, Factur-X / ZUGFeRD "EN 16931" | yes | KoSIT, scenario "EN16931 (CII)" |
| `FacturXExtended` | Factur-X / ZUGFeRD EXTENDED (EN 16931 content) | yes | Mustang |
| `FacturXBasic` | Factur-X / ZUGFeRD BASIC | yes | Mustang |
| `FacturXBasicWL` | Factur-X / ZUGFeRD BASIC WL | no | Mustang |
| `FacturXMinimum` | Factur-X / ZUGFeRD MINIMUM | no | Mustang |

## What the pre-flight check covers

The EN 16931 core rules (BR-01 to BR-65), the VAT category rules (BR-S, BR-Z, BR-E, BR-AE,
BR-IC, BR-G, BR-O), the code lists (BR-CL, extracted from the official artefact), and for
XRechnung the German rules (BR-DE) plus the Peppol rules XRechnung 3.0 includes. The test
suite breaks valid invoices in 31 specific ways and asserts that the pre-flight reports the
same rule id as the KoSIT validator. The official validators remain authoritative.

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
bash eng/get-validators.sh              # downloads KoSIT, the XRechnung configuration and Mustang
export HIVE_VALIDATORS=$PWD/.tools/validators
dotnet test
```

## Specifications

EN 16931-1:2017, XRechnung 3.0.2 (KoSIT, validator configuration 2026-08-31),
ZUGFeRD 2.5 / Factur-X 1.09 (FeRD / FNFE-MPE, June 2026), UN/CEFACT CII D16B.

## License

Apache License 2.0. Copyright Balsoft GmbH. Maintained by Ertugrul Balveren
([Balsoft GmbH](https://balsoft.de)). It is the e-invoice engine of Hive DocFlow.
