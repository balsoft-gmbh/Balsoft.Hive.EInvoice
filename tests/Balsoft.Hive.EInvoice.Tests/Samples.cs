namespace Balsoft.Hive.EInvoice.Tests;

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
        ["18-credit-note-exempt"] = CreditNoteExempt,
        ["19-card-tax-point-rounding"] = CardTaxPointRounding,
        ["20-department-contact"] = DepartmentContact,
        ["21-credit-note-invoiced-object"] = CreditNoteInvoicedObject,
        ["22-extended-terms"] = ExtendedFull,
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
        inv.Number = "RE-2026-0002";
        inv.PurchaseOrderReference = "PO-2026-0815";
        inv.ContractReference = "0042";
        inv.Seller.Identifiers.Add(new SchemedIdentifier("99001"));
        inv.Seller.Identifiers.Add(new SchemedIdentifier("4000001000005", IdentifierScheme.Gln));
        inv.Buyer.Identifiers.Add(new SchemedIdentifier("99002"));
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
                DebitedAccountIdentifier = "DE89370400440532013000",
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
        inv.ProjectName = "Wartung Rechenzentrum Süd";
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
        line.Item.Description = "Wartung und Support für Hive BDC";
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

    public static Invoice CreditNoteExempt()
    {
        var inv = Exempt();
        inv.Number = "GS-2026-0018";
        inv.TypeCode = InvoiceTypeCode.CreditNote;
        inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = "RE-2026-0009", IssueDate = Issue.AddDays(-14) });
        inv.VatExemptions[0].ReasonCode = "VATEX-EU-132-1I";
        return inv;
    }

    public static Invoice CardTaxPointRounding()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0019";
        inv.TaxPointDate = Issue;
        inv.RoundingAmount = 0.01m;
        inv.PaymentInstructions = new PaymentInstructions
        {
            MeansCode = "48",
            MeansText = "Kreditkarte",
            Card = new PaymentCard { PrimaryAccountNumber = "123456", HolderName = "Muster Kunde GmbH" },
        };
        inv.PaymentTerms = "Der Betrag wurde der Kreditkarte belastet.";
        return inv;
    }

    public static Invoice DepartmentContact()
    {
        var inv = Standard();
        inv.Number = "RE-2026-0020";
        inv.Seller.Contact!.Name = null;
        inv.Seller.Contact.Department = "Buchhaltung";
        return inv;
    }

    public static Invoice CreditNoteInvoicedObject()
    {
        var inv = CreditNote();
        inv.Number = "GS-2026-0021";
        inv.InvoicedObjectIdentifier = new SchemedIdentifier("ZAEHLER-0815", "AAG");
        return inv;
    }

    /// <summary>Every EXTENDED term the library has, on an invoice that is also complete for the other profiles.</summary>
    public static Invoice ExtendedFull()
    {
        static PostalAddress Address(string city) => new()
        {
            Line1 = "Hauptstr. 1", Line2 = "Haus 2", Line3 = "Etage 3", City = city, PostCode = "50667", CountrySubdivision = "NRW", CountryCode = "DE",
        };

        static ExtendedParty Party(string name)
        {
            var party = new ExtendedParty
            {
                Identifier = "ID-" + name.Length,
                Name = name,
                RoleCode = "DL",
                LegalRegistrationIdentifier = new SchemedIdentifier("HRB 1", "0002"),
                TradingName = name + " Handel",
                LegalAddress = Address("Bonn"),
                Address = Address("Köln"),
                ElectronicAddress = new SchemedIdentifier("party@example.com", ElectronicAddressScheme.Email),
                VatIdentifier = "DE123456789",
            };
            party.GlobalIdentifiers.Add(new SchemedIdentifier("4000001000005", IdentifierScheme.Gln));
            party.Contacts.Add(new Contact { Name = "Name", Department = "Einkauf", TypeCode = "IC", Phone = "+49 221 12345", Fax = "+49 221 12346", Email = "contact@example.com" });
            return party;
        }

        var inv = FullDetails();
        inv.Number = "RE-2026-0022";
        inv.ContractReference = "K-2026-1";
        inv.TenderOrLotReference = "LOS-3";
        inv.Notes[0].ContentCode = "ST1";
        inv.PrecedingInvoices.Add(new PrecedingInvoiceReference { Number = "RE-2026-0001", IssueDate = Issue.AddDays(-30), TypeCode = "380" });
        inv.SupportingDocuments.Add(new SupportingDocument { Reference = "Vertrag-42", ExternalLocation = "https://balsoft.de/vertraege/42", IssueDate = Issue.AddDays(-60) });
        foreach (var party in new[] { inv.Seller, inv.Buyer })
        {
            party.RoleCode = "AB";
            party.LegalRegistrationIdentifier = new SchemedIdentifier("HRB 9", "0002");
            party.LegalAddress = Address("Bonn");
            party.AdditionalContacts.Add(new Contact { Name = "Zweiter Kontakt", TypeCode = "SR", Phone = "+49 221 99", Fax = "+49 221 98", Email = "second@example.com" });
        }
        inv.Seller.Contact!.TypeCode = "IC";
        inv.Seller.Contact.Fax = "+49 221 1234568";
        inv.Buyer.Contact!.TypeCode = "IC";
        inv.Buyer.Contact.Fax = "+49 30 556";
        inv.Buyer.AdditionalLegalInformation = "Amtsgericht Berlin";

        inv.Extended = new InvoiceExtension
        {
            IsTest = true,
            DocumentName = "Rechnung",
            IsCopy = false,
            LanguageCode = "deu",
            ContractualDueDate = Issue.AddDays(30),
            SalesAgent = Party("Sales Agent GmbH"),
            BuyerTaxRepresentative = Party("Buyer Tax Rep GmbH"),
            SellerTaxRepresentative = Party("Seller Tax Rep GmbH"),
            ProductEndUser = Party("End User GmbH"),
            BuyerAgent = Party("Buyer Agent GmbH"),
            ShipTo = Party("Ship To GmbH"),
            UltimateShipTo = Party("Ultimate Ship To GmbH"),
            ShipFrom = Party("Ship From GmbH"),
            Invoicer = Party("Invoicer GmbH"),
            Invoicee = Party("Invoicee GmbH"),
            Payee = Party("Payee GmbH"),
            Payer = Party("Payer GmbH"),
            DeliveryTerms = new DeliveryTerms { Code = "FCA", LocationCountryCode = "DE", LocationName = "Köln" },
            InvoiceIssuerReference = "AUSSTELLER-1",
            SalesOrderDate = Issue.AddDays(-20),
            PurchaseOrderDate = Issue.AddDays(-21),
            ContractDate = Issue.AddDays(-100),
            ContractTypeCode = "CT",
            TenderOrLotDate = Issue.AddDays(-110),
            InvoicedObjectDate = Issue.AddDays(-5),
            DespatchAdviceDate = Issue.AddDays(-3),
            ReceivingAdviceDate = Issue.AddDays(-2),
            Quotation = new ExtendedDocumentReference { Number = "ANG-1", Date = Issue.AddDays(-40) },
            DeliveryNote = new ExtendedDocumentReference { Number = "LN-1", Date = Issue.AddDays(-3) },
        };
        inv.Extended.UltimateCustomerOrders.Add(new ExtendedDocumentReference { Number = "EK-1", Date = Issue.AddDays(-50) });
        inv.Extended.TransportModeCodes.Add("3");

        inv.Currency = "EUR";
        inv.TaxCurrency = "USD";
        inv.VatTotalInTaxCurrency = 100m;
        inv.Extended.TaxCurrencyExchange = new CurrencyExchange { SourceCurrencyCode = "EUR", TargetCurrencyCode = "USD", ConversionRate = 1.08m, Date = Issue };
        inv.Extended.WriteVatBasisDetails = true;
        inv.Extended.InvoicingPeriodDescription = "August 2026";
        inv.Extended.BuyerAccountingTypeCode = "1";
        var logistics = new LogisticsCharge { Description = "Fracht", Amount = 12m };
        logistics.Taxes.Add(new ExtendedTax { Category = VatCategory.StandardRate, Rate = 19m });
        inv.Extended.LogisticsCharges.Add(logistics);
        logistics = new LogisticsCharge { Description = "Zollabwicklung", Amount = 5m };
        logistics.Taxes.Add(new ExtendedTax { Category = VatCategory.Exempt, Rate = 0m, ExemptionReason = "Steuerfrei", ExemptionReasonCode = "VATEX-EU-G" });
        inv.Extended.LogisticsCharges.Add(logistics);
        inv.Extended.PaymentTerms.Add(new ExtendedPaymentTerm
        {
            Description = "Zahlbar innerhalb von 30 Tagen.",
            DueDate = Issue.AddDays(30),
            PartialPaymentAmount = 100m,
            Penalty = new PaymentAdjustment { BasisDate = Issue, Period = 30m, PeriodUnitCode = "DAY", BaseAmount = 100m, Percentage = 5m, Amount = 5m },
            Discount = new PaymentAdjustment { BasisDate = Issue, Period = 10m, PeriodUnitCode = "DAY", BaseAmount = 100m, Percentage = 2m, Amount = 2m },
            Payee = Party("Terms Payee GmbH"),
        });
        inv.Extended.FinancialAdjustments.Add(new FinancialAdjustment { Reason = "Pfand", Amount = 3m });
        var advance = new AdvancePayment { PaidAmount = 10m, ReceivedDate = Issue.AddDays(-10), InvoiceNumber = "RE-2026-0002", InvoiceTypeCode = "386", InvoiceDate = Issue.AddDays(-12) };
        advance.Taxes.Add(new ExtendedTax { CalculatedAmount = 1.6m, Category = VatCategory.StandardRate, Rate = 19m });
        advance.Taxes.Add(new ExtendedTax { CalculatedAmount = 0m, Category = VatCategory.Exempt, Rate = 0m, ExemptionReason = "Steuerfrei", ExemptionReasonCode = "VATEX-EU-G" });
        inv.Extended.AdvancePayments.Add(advance);
        inv.AddLine(new InvoiceLine("Steuerfreie Leistung", 1m, UnitCode.Piece, 40m, 0m, VatCategory.Exempt));
        AddExtendedLines(inv, Party);
        inv.VatExemptions.Add(new VatExemption { Category = VatCategory.Exempt, Reason = "Steuerfrei", ReasonCode = "VATEX-EU-G" });
        inv.AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = false, Amount = 5m, Reason = "Treuerabatt", ReasonCode = "95", VatCategory = VatCategory.StandardRate, VatRate = 19m, SequenceNumber = 1m, BaseQuantity = 1m, BaseQuantityUnitCode = "C62" });
        inv.AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = true, Amount = 2m, Reason = "Fracht", ReasonCode = "FC", ReasonCodeListId = "UNTDID7161", VatCategory = VatCategory.Exempt, VatRate = 0m, SequenceNumber = 2m, BaseQuantity = 1m, BaseQuantityUnitCode = "C62", ExemptionReason = "Steuerfrei", ExemptionReasonCode = "VATEX-EU-G" });
        return inv;
    }

    /// <summary>A group line with a detail line under it, and an information line, with every line level EXTENDED term.</summary>
    private static void AddExtendedLines(Invoice inv, Func<string, ExtendedParty> party)
    {
        var group = inv.AddLine(new InvoiceLine("Baugruppe", 1m, UnitCode.Piece, 100m, 19m) { Id = "G1", Note = "Gruppe" });
        group.Extended = new InvoiceLineExtension { LineStatusCode = "1", LineStatusReasonCode = "GROUP" };

        var detail = inv.AddLine(new InvoiceLine("Türschließer", 2m, UnitCode.Piece, 50m, 19m)
        {
            Id = "G1-1",
            Note = "Position",
            OrderLineReference = "20",
            BuyerAccountingReference = "KST-1",
            GrossPrice = 55m,
        });
        detail.Item.Attributes.Add(new ItemAttribute { Name = "Gewicht", Value = "2 kg", TypeCode = "WEI", ValueMeasure = 2m, ValueMeasureUnitCode = "KGM" });
        detail.Item.Classifications.Add(new ItemClassification { Code = "72267000", Scheme = "STI", ClassName = "Wartung" });
        var lx = new InvoiceLineExtension
        {
            ParentLineId = "G1",
            LineStatusCode = "1",
            LineStatusReasonCode = "DETAIL",
            NoteContentCode = "ST1",
            NoteSubjectCode = "AAI",
            ItemIdentifier = "ARTIKEL-1",
            IndustryAssignedIdentifier = "IND-1",
            ModelIdentifier = "M-1",
            BrandName = "Marke",
            ModelName = "Modell",
            Manufacturer = party("Hersteller GmbH"),
            DeliveryTerms = new DeliveryTerms { Code = "FCA", LocationCountryCode = "DE", LocationName = "Köln" },
            SalesOrder = new LineDocumentReference { Number = "AB-1", LineId = "1", Date = Issue.AddDays(-20) },
            PurchaseOrderNumber = "BE-1",
            PurchaseOrderDate = Issue.AddDays(-21),
            Quotation = new LineDocumentReference { Number = "ANG-1", LineId = "1", Date = Issue.AddDays(-40) },
            Contract = new LineDocumentReference { Number = "K-1", LineId = "1", Date = Issue.AddDays(-100) },
            NetPriceIncludedTax = new ExtendedTax { CalculatedAmount = 9.5m, Category = VatCategory.StandardRate, Rate = 19m },
            ItemSeller = party("Zweitverkäufer GmbH"),
            ChargeFreeQuantity = 1m,
            ChargeFreeQuantityUnitCode = UnitCode.Piece,
            PackageQuantity = 2m,
            PackageQuantityUnitCode = UnitCode.Piece,
            PerPackageUnitQuantity = 1m,
            PerPackageUnitCode = UnitCode.Piece,
            ShipTo = party("Lieferort GmbH"),
            UltimateShipTo = party("Endempfänger GmbH"),
            DeliveryDate = Issue.AddDays(-3),
            DespatchAdvice = new LineDocumentReference { Number = "LS-1", LineId = "1", Date = Issue.AddDays(-3) },
            ReceivingAdvice = new LineDocumentReference { Number = "WE-1", LineId = "1", Date = Issue.AddDays(-2) },
            DeliveryNote = new LineDocumentReference { Number = "LN-1", LineId = "1", Date = Issue.AddDays(-3) },
            VatAmount = 19m,
            VatPointDateCode = "5",
            ChargeTotal = 0m,
            AllowanceTotal = 0m,
            VatTotal = 19m,
            VatTotalCurrency = "EUR",
            VatTotalInTaxCurrency = 20.52m,
            VatTotalInTaxCurrencyCode = "USD",
            GrandTotal = 119m,
            AllowanceChargeTotal = 0m,
            PrecedingInvoice = new LineDocumentReference { Number = "RE-2026-0001", LineId = "1", Date = Issue.AddDays(-30) },
            PrecedingInvoiceTypeCode = "380",
            AccountingTypeCode = "1",
        };
        lx.Manufacturer!.Description = "Herstellerangaben";
        lx.ItemSeller!.Description = "Verkäuferangaben";
        lx.BatchIdentifiers.Add("CHARGE-1");
        lx.ProductInstances.Add(new ProductInstance { BatchIdentifier = "CHARGE-1", SerialNumber = "SN-1" });
        var included = new IncludedProduct { Identifier = "TEIL-1", SellersIdentifier = "S-1", BuyersIdentifier = "B-1", IndustryAssignedIdentifier = "I-1", Name = "Schraube", Description = "M4", Quantity = 4m, QuantityUnitCode = UnitCode.Piece };
        included.GlobalIdentifiers.Add(new SchemedIdentifier("4012345000009", IdentifierScheme.Gtin));
        lx.IncludedProducts.Add(included);
        lx.SupportingDocuments.Add(new LineSupportingDocument { Number = "DOK-1", ExternalLocation = "https://balsoft.de/d/1", LineId = "1", TypeCode = "916", Description = "Datenblatt", Date = Issue.AddDays(-9) });
        lx.SupportingDocuments.Add(new LineSupportingDocument { Number = "OBJ-1", TypeCode = "130", ReferenceTypeCode = "AAG" });
        lx.SupportingDocuments.Add(new LineSupportingDocument { Number = "DOK-2", TypeCode = "916", Content = System.Text.Encoding.UTF8.GetBytes("a"), MimeCode = "text/csv", FileName = "a.csv" });
        lx.GrossPriceAllowancesAndCharges.Add(new AllowanceCharge { IsCharge = false, Percentage = 10m, BaseAmount = 60m, Amount = 6m, ReasonCode = "95", Reason = "Rabatt" });
        lx.GrossPriceAllowancesAndCharges.Add(new AllowanceCharge { IsCharge = true, Percentage = 2m, BaseAmount = 50m, Amount = 1m, ReasonCode = "FC", ReasonCodeListId = "UNTDID7161", Reason = "Zuschlag" });
        lx.UltimateCustomerOrders.Add(new LineDocumentReference { Number = "EK-1", LineId = "1", Date = Issue.AddDays(-50) });
        detail.Extended = lx;
        detail.AllowancesAndCharges.Add(new AllowanceCharge { IsCharge = true, Amount = 0m, Reason = "Abgabe", ReasonCode = "FC", ReasonCodeListId = "UNTDID7161" });

        var information = inv.AddLine(new InvoiceLine("Hinweiszeile", 1m, UnitCode.Piece, 25m, 19m) { Id = "G1-2", Note = "Info" });
        information.Extended = new InvoiceLineExtension
        {
            ParentLineId = "G1",
            LineStatusCode = "1",
            LineStatusReasonCode = "INFORMATION",
            VatExemptionReason = "Nicht steuerbar",
            VatExemptionReasonCode = "VATEX-EU-O",
            NetPriceIncludedTax = new ExtendedTax { CalculatedAmount = 0m, Category = VatCategory.Exempt, Rate = 0m, ExemptionReason = "Steuerfrei", ExemptionReasonCode = "VATEX-EU-G" },
        };
    }
}
