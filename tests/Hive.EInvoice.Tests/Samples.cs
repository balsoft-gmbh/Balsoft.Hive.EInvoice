namespace Hive.EInvoice.Tests;

/// <summary>
/// Realistic invoices covering the cases a German seller meets. Every sample is complete for
/// XRechnung, so it must be accepted by the KoSIT validator in the XRechnung and EN 16931
/// scenarios, and by Mustang in the Factur-X profiles.
/// </summary>
public static class Samples
{
    public static readonly DateTime Issue = new(2026, 9, 15);

    public static IReadOnlyDictionary<string, Func<Invoice>> All { get; } = new Dictionary<string, Func<Invoice>>
    {
        ["01-standard"] = Standard,
        ["02-skonto-aktivbank"] = SkontoAktivbank,
        ["03-credit-note"] = CreditNote,
        ["04-mixed-vat"] = MixedVat,
        ["05-allowances-charges"] = AllowancesCharges,
        ["06-reverse-charge"] = ReverseCharge,
        ["07-intra-community"] = IntraCommunity,
        ["08-export"] = Export,
        ["09-exempt"] = Exempt,
        ["10-not-subject"] = NotSubject,
        ["11-direct-debit"] = DirectDebit,
        ["12-corrected-invoice"] = Corrected,
        ["13-full-details"] = FullDetails,
        ["14-public-sector-leitweg"] = PublicSector,
        ["15-prepaid-rounding"] = PrepaidRounding,
        ["16-foreign-currency"] = ForeignCurrency,
        ["17-attachment"] = Attachment,
    };

    public static Invoice Base(string number = "RE-2026-0001")
    {
        var inv = new Invoice
        {
            Number = number,
            IssueDate = Issue,
            DueDate = Issue.AddDays(30),
            BuyerReference = "04011000-12345-34",
            PaymentTerms = "Zahlbar innerhalb von 30 Tagen ohne Abzug.",
            Seller = new Party
            {
                Name = "Balsoft GmbH",
                VatIdentifier = "DE452983132",
                TaxRegistrationIdentifier = "215/5719/0815",
                ElectronicAddress = new SchemedIdentifier("invoice@balsoft.de", ElectronicAddressScheme.Email),
                Address = new PostalAddress { Line1 = "Herler Str. 109", PostCode = "51067", City = "Köln", CountryCode = "DE" },
                Contact = new Contact { Name = "Buchhaltung", Phone = "+49 221 1234567", Email = "buchhaltung@balsoft.de" },
            },
            Buyer = new Party
            {
                Name = "Muster Kunde GmbH",
                VatIdentifier = "DE123456789",
                ElectronicAddress = new SchemedIdentifier("ap@muster-kunde.de", ElectronicAddressScheme.Email),
                Address = new PostalAddress { Line1 = "Musterstr. 1", PostCode = "10115", City = "Berlin", CountryCode = "DE" },
            },
            PaymentInstructions = PaymentInstructions.SepaCreditTransfer("DE89 3704 0044 0532 0130 00", "COBADEFFXXX", "Balsoft GmbH", number),
        };
        return inv;
    }

    public static Invoice Standard()
    {
        var inv = Base();
        inv.Notes.Add(new InvoiceNote("Vielen Dank für Ihren Auftrag."));
        inv.AddLine(new InvoiceLine("Beratung D365FO", 10m, UnitCode.Hour, 120m, 19m));
        inv.AddLine(new InvoiceLine("Lizenz Modul", 2m, UnitCode.Piece, 499.50m, 19m));
        return inv;
    }

    public static Invoice SkontoAktivbank()
    {
        var inv = Standard();
        inv.Number = "6153469178";
        inv.PurchaseOrderReference = "PO-2230000192";
        inv.ContractReference = "0126";
        inv.Seller.Identifiers.Add(new SchemedIdentifier("88636"));
        inv.Seller.Identifiers.Add(new SchemedIdentifier("4067107000001", IdentifierScheme.Gln));
        inv.Buyer.Identifiers.Add(new SchemedIdentifier("10038"));
        inv.CashDiscounts.Add(new CashDiscount { Days = 7, Percent = 2m });
        inv.CashDiscounts.Add(new CashDiscount { Days = 14, Percent = 1m, BaseAmount = 2616.81m });
        return inv;
    }

    public static Invoice CreditNote()
    {
        var inv = Base("GS-2026-0007");
        inv.TypeCode = InvoiceTypeCode.CreditNote;
        inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = "RE-2026-0001", IssueDate = Issue.AddDays(-10) });
        inv.AddLine(new InvoiceLine("Retoure Türschließer", 3m, UnitCode.Piece, 85m, 19m));
        return inv;
    }

    public static Invoice MixedVat()
    {
        var inv = Base("RE-2026-0004");
        inv.AddLine(new InvoiceLine("Ware 19 %", 2m, UnitCode.Piece, 100m, 19m));
        inv.AddLine(new InvoiceLine("Fachbuch 7 %", 1m, UnitCode.Piece, 30m, 7m));
        inv.AddLine(new InvoiceLine("Porto", 1m, UnitCode.One, 4.95m, 19m));
        return inv;
    }

    public static Invoice AllowancesCharges()
    {
        var inv = Base("RE-2026-0005");
        var line = inv.AddLine(new InvoiceLine("Schrauben M8", 1000m, UnitCode.Piece, 12.5m, 19m)
        {
            GrossPrice = 15m,
            PriceBaseQuantity = 100m,
        });
        line.AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = false, BaseAmount = 125m, Percentage = 10m, ReasonCode = "95", Reason = "Mengenrabatt" });
        line.AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = true, Amount = 5m, ReasonCode = "ABL", Reason = "Verpackung" });
        inv.AddLine(new InvoiceLine("Montage", 2m, UnitCode.Hour, 85m, 19m));
        inv.AllowancesAndCharges.Add(AllowanceCharge.Allowance(10m, "Treuerabatt", "95", VatCategory.StandardRate, 19m));
        inv.AllowancesAndCharges.Add(AllowanceCharge.Charge(15m, "Fracht", "FC", VatCategory.StandardRate, 19m));
        return inv;
    }

    public static Invoice ReverseCharge()
    {
        var inv = Base("RE-2026-0006");
        inv.Buyer = new Party
        {
            Name = "Bouwbedrijf B.V.",
            VatIdentifier = "NL123456789B01",
            ElectronicAddress = new SchemedIdentifier("factuur@bouwbedrijf.nl", ElectronicAddressScheme.Email),
            Address = new PostalAddress { Line1 = "Kerkstraat 1", PostCode = "1017 GC", City = "Amsterdam", CountryCode = "NL" },
        };
        inv.AddLine(new InvoiceLine("Bauleistung", 1m, UnitCode.LumpSum, 5000m, 0m, VatCategory.ReverseCharge));
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.ReverseCharge, Reason = "Steuerschuldnerschaft des Leistungsempfängers", ReasonCode = "VATEX-EU-AE" });
        return inv;
    }

    public static Invoice IntraCommunity()
    {
        var inv = Base("RE-2026-0007");
        inv.Buyer = new Party
        {
            Name = "Société Exemple SARL",
            VatIdentifier = "FR40303265045",
            ElectronicAddress = new SchemedIdentifier("compta@exemple.fr", ElectronicAddressScheme.Email),
            Address = new PostalAddress { Line1 = "1 Rue de la Paix", PostCode = "75002", City = "Paris", CountryCode = "FR" },
        };
        inv.Delivery = new DeliveryInformation
        {
            ActualDeliveryDate = Issue.AddDays(-2),
            PartyName = "Société Exemple SARL, Entrepôt",
            Address = new PostalAddress { Line1 = "5 Rue du Port", PostCode = "69002", City = "Lyon", CountryCode = "FR" },
        };
        inv.AddLine(new InvoiceLine("Industriepumpe", 2m, UnitCode.Piece, 1800m, 0m, VatCategory.IntraCommunitySupply));
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.IntraCommunitySupply, Reason = "Steuerfreie innergemeinschaftliche Lieferung", ReasonCode = "VATEX-EU-IC" });
        return inv;
    }

    public static Invoice Export()
    {
        var inv = Base("RE-2026-0008");
        inv.Buyer = new Party
        {
            Name = "Swiss Customer AG",
            ElectronicAddress = new SchemedIdentifier("billing@swiss-customer.ch", ElectronicAddressScheme.Email),
            Address = new PostalAddress { Line1 = "Bahnhofstrasse 1", PostCode = "8001", City = "Zürich", CountryCode = "CH" },
        };
        inv.AddLine(new InvoiceLine("Ersatzteil", 5m, UnitCode.Piece, 240m, 0m, VatCategory.ExportOutsideEu));
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.ExportOutsideEu, Reason = "Steuerfreie Ausfuhrlieferung", ReasonCode = "VATEX-EU-G" });
        return inv;
    }

    public static Invoice Exempt()
    {
        var inv = Base("RE-2026-0009");
        inv.AddLine(new InvoiceLine("Schulung (umsatzsteuerfrei nach § 4 Nr. 21 UStG)", 1m, UnitCode.Day, 1200m, 0m, VatCategory.Exempt));
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.Exempt, Reason = "Steuerfrei nach § 4 Nr. 21 UStG" });
        return inv;
    }

    public static Invoice NotSubject()
    {
        var inv = Base("RE-2026-0010");
        inv.Seller.VatIdentifier = null;
        inv.Seller.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 12345");
        inv.Buyer.VatIdentifier = null;
        inv.AddLine(new InvoiceLine("Schadenersatz", 1m, UnitCode.One, 750m, 0m, VatCategory.NotSubjectToVat) { VatRate = null });
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.NotSubjectToVat, Reason = "Nicht steuerbar", ReasonCode = "VATEX-EU-O" });
        return inv;
    }

    public static Invoice DirectDebit()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0011";
        inv.PaymentInstructions = new PaymentInstructions
        {
            MeansCode = PaymentMeansCode.SepaDirectDebit,
            RemittanceInformation = "RE-2026-0011",
            DirectDebit = new DirectDebit
            {
                MandateReference = "MANDAT-4711",
                CreditorIdentifier = "DE98ZZZ09999999999",
                DebitedAccountIdentifier = "DE02120300000000202051",
            },
        };
        inv.PaymentTerms = "Der Betrag wird zum Fälligkeitsdatum per SEPA-Lastschrift eingezogen.";
        return inv;
    }

    public static Invoice Corrected()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0012";
        inv.TypeCode = InvoiceTypeCode.CorrectedInvoice;
        inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = "RE-2026-0001", IssueDate = Issue.AddDays(-5) });
        return inv;
    }

    public static Invoice FullDetails()
    {
        var inv = Base("RE-2026-0013");
        inv.ProjectReference = "PRJ-77";
        inv.SalesOrderReference = "AB-2026-555";
        inv.PurchaseOrderReference = "BE-9981";
        inv.ReceivingAdviceReference = "WE-123";
        inv.DespatchAdviceReference = "LS-456";
        inv.TenderOrLotReference = "LOS-3";
        inv.InvoicedObjectIdentifier = new SchemedIdentifier("ZAEHLER-0815", "AAG");
        inv.BuyerAccountingReference = "KST-4711";
        inv.InvoicingPeriod = new Period(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        inv.Seller.TradingName = "Balsoft";
        inv.Seller.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 12345");
        inv.Seller.AdditionalLegalInformation = "Geschäftsführer: Ertugrul Balveren, Amtsgericht Köln";
        inv.Buyer.Contact = new Contact { Name = "Einkauf", Phone = "+49 30 555", Email = "einkauf@muster-kunde.de" };
        inv.Payee = new Payee { Name = "Balsoft Factoring GmbH", Identifier = new SchemedIdentifier("4000001000005", IdentifierScheme.Gln) };
        inv.Delivery = new DeliveryInformation
        {
            ActualDeliveryDate = new DateTime(2026, 8, 31),
            PartyName = "Muster Kunde GmbH, Lager Süd",
            LocationIdentifier = new SchemedIdentifier("4000001000012", IdentifierScheme.Gln),
            Address = new PostalAddress { Line1 = "Lagerweg 3", PostCode = "12345", City = "Berlin", CountryCode = "DE" },
        };
        inv.Notes.Add(new InvoiceNote("Balsoft GmbH, Herler Str. 109, 51067 Köln, HRB 12345", "REG"));
        var line = inv.AddLine(new InvoiceLine("Wartungsvertrag", 1m, UnitCode.Month, 450m, 19m)
        {
            Note = "Monatliche Pauschale",
            OrderLineReference = "10",
            BuyerAccountingReference = "KST-4711-01",
            Period = new Period(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31)),
            ObjectIdentifier = new SchemedIdentifier("VERTRAG-42", "AUV"),
        });
        line.Item.Description = "Wartung und Support für Hive DocFlow";
        line.Item.SellersIdentifier = "WART-01";
        line.Item.BuyersIdentifier = "K-99";
        line.Item.StandardIdentifier = new SchemedIdentifier("4012345000009", IdentifierScheme.Gtin);
        line.Item.Classifications.Add(new ItemClassification { Code = "72267000", Scheme = "STI", SchemeVersion = "2008" });
        line.Item.OriginCountryCode = "DE";
        line.Item.Attributes.Add(new ItemAttribute { Name = "Servicelevel", Value = "Gold" });
        return inv;
    }

    public static Invoice PublicSector()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0014";
        inv.BuyerReference = "991-01234-56";
        inv.Buyer = new Party
        {
            Name = "Stadt Musterstadt, Tiefbauamt",
            ElectronicAddress = new SchemedIdentifier("991-01234-56", ElectronicAddressScheme.LeitwegId),
            Address = new PostalAddress { Line1 = "Rathausplatz 1", PostCode = "53111", City = "Bonn", CountryCode = "DE" },
        };
        return inv;
    }

    public static Invoice PrepaidRounding()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0015";
        inv.PrepaidAmount = 1000m;
        inv.RoundingAmount = 0.19m;
        return inv;
    }

    public static Invoice ForeignCurrency()
    {
        var inv = Base("RE-2026-0016");
        inv.Currency = "USD";
        inv.TaxCurrency = "EUR";
        inv.AddLine(new InvoiceLine("Consulting", 8m, UnitCode.Hour, 150m, 19m));
        inv.VatTotalInTaxCurrency = 209.66m;
        return inv;
    }

    public static Invoice Attachment()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0017";
        inv.SupportingDocuments.Add(new SupportingDocument
        {
            Reference = "Stundennachweis-August",
            Description = "Stundennachweis August 2026",
            Content = System.Text.Encoding.UTF8.GetBytes("Datum;Stunden\n2026-08-03;8\n2026-08-04;2\n"),
            MimeCode = "text/csv",
            FileName = "stundennachweis.csv",
        });
        inv.SupportingDocuments.Add(new SupportingDocument { Reference = "Vertrag-42", ExternalLocation = "https://balsoft.de/vertraege/42" });
        return inv;
    }
}
