using System.Collections.Generic;

namespace Balsoft.Hive.EInvoice;

/// <summary>BG-16 Payment instructions.</summary>
public sealed class PaymentInstructions
{
    /// <summary>BT-81 Payment means type code (UNTDID 4461). Defaults to 58, SEPA credit transfer.</summary>
    public string MeansCode { get; set; } = PaymentMeansCode.SepaCreditTransfer;

    /// <summary>BT-82 Payment means text.</summary>
    public string? MeansText { get; set; }

    /// <summary>BT-83 Remittance information (payment reference, "Verwendungszweck").</summary>
    public string? RemittanceInformation { get; set; }

    /// <summary>BG-17 Credit transfer accounts. One is typical; each is written as its own payment means.</summary>
    public List<CreditTransferAccount> CreditTransfers { get; } = new();

    /// <summary>BG-18 Payment card information.</summary>
    public PaymentCard? Card { get; set; }

    /// <summary>BG-19 Direct debit.</summary>
    public DirectDebit? DirectDebit { get; set; }

    /// <summary>Creates SEPA credit transfer instructions to one account.</summary>
    public static PaymentInstructions SepaCreditTransfer(string iban, string? bic = null, string? accountName = null,
        string? remittanceInformation = null)
    {
        var p = new PaymentInstructions { MeansCode = PaymentMeansCode.SepaCreditTransfer, RemittanceInformation = remittanceInformation };
        p.CreditTransfers.Add(new CreditTransferAccount { AccountIdentifier = iban, ServiceProviderIdentifier = bic, AccountName = accountName });
        return p;
    }
}

/// <summary>BG-17 Credit transfer.</summary>
public sealed class CreditTransferAccount
{
    /// <summary>
    /// BT-84 Payment account identifier. A valid IBAN (spaces allowed) is written as IBAN,
    /// anything else as a proprietary account number.
    /// </summary>
    public string AccountIdentifier { get; set; } = "";

    /// <summary>BT-85 Payment account name.</summary>
    public string? AccountName { get; set; }

    /// <summary>BT-86 Payment service provider identifier (BIC).</summary>
    public string? ServiceProviderIdentifier { get; set; }
}

/// <summary>BG-18 Payment card information.</summary>
public sealed class PaymentCard
{
    /// <summary>BT-87 Payment card primary account number: only the last 4 to 6 digits may be given.</summary>
    public string PrimaryAccountNumber { get; set; } = "";

    /// <summary>BT-88 Payment card holder name.</summary>
    public string? HolderName { get; set; }
}

/// <summary>BG-19 Direct debit.</summary>
public sealed class DirectDebit
{
    /// <summary>BT-89 Mandate reference identifier.</summary>
    public string? MandateReference { get; set; }

    /// <summary>BT-90 Bank assigned creditor identifier (SEPA creditor ID).</summary>
    public string? CreditorIdentifier { get; set; }

    /// <summary>BT-91 Debited account identifier (IBAN).</summary>
    public string? DebitedAccountIdentifier { get; set; }
}
