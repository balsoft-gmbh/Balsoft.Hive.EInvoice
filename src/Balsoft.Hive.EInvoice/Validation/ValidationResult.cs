using System;
using System.Collections.Generic;
using System.Linq;

namespace Balsoft.Hive.EInvoice.Validation;

/// <summary>Severity of a <see cref="ValidationIssue"/>.</summary>
public enum Severity
{
    /// <summary>The document would be rejected by a conforming validator.</summary>
    Error,
    /// <summary>The document is accepted, but a recommendation of the specification is not met.</summary>
    Warning,
}

/// <summary>One finding of <see cref="InvoiceValidator"/>.</summary>
public sealed class ValidationIssue
{
    internal ValidationIssue(string ruleId, Severity severity, string message, string? businessTerm)
    {
        RuleId = ruleId;
        Severity = severity;
        Message = message;
        BusinessTerm = businessTerm;
    }

    /// <summary>The rule identifier from the specification, e.g. "BR-DE-15" or "BR-CO-25".</summary>
    public string RuleId { get; }

    /// <summary>Error or warning.</summary>
    public Severity Severity { get; }

    /// <summary>What is wrong, in plain English.</summary>
    public string Message { get; }

    /// <summary>The business term concerned, e.g. "BT-10", when there is one.</summary>
    public string? BusinessTerm { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"[{RuleId}] {(Severity == Severity.Warning ? "warning: " : "")}{Message}{(BusinessTerm is null ? "" : $" ({BusinessTerm})")}";
}

/// <summary>The outcome of <see cref="InvoiceValidator.Validate"/>.</summary>
public sealed class ValidationResult
{
    internal ValidationResult(InvoiceProfile profile, IReadOnlyList<ValidationIssue> issues)
    {
        Profile = profile;
        Issues = issues;
    }

    /// <summary>The profile the invoice was checked against.</summary>
    public InvoiceProfile Profile { get; }

    /// <summary>All findings, errors first.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>The errors.</summary>
    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == Severity.Error);

    /// <summary>The warnings.</summary>
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);

    /// <summary>True when there are no errors (warnings allowed).</summary>
    public bool IsValid => !Errors.Any();

    /// <summary>Throws <see cref="InvoiceValidationException"/> when there are errors.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new InvoiceValidationException(this);
    }

    /// <inheritdoc />
    public override string ToString()
        => Issues.Count == 0 ? $"{Profile}: valid" : $"{Profile}: " + string.Join(Environment.NewLine, Issues);
}

/// <summary>Thrown when an invoice is written that violates the rules of its profile.</summary>
public sealed class InvoiceValidationException : Exception
{
    /// <summary>Creates the exception from a validation result.</summary>
    public InvoiceValidationException(ValidationResult result)
        : base($"The invoice is not valid for {result.Profile}:{Environment.NewLine}{string.Join(Environment.NewLine, result.Errors)}")
    {
        Result = result;
    }

    /// <summary>Creates the exception with a message.</summary>
    public InvoiceValidationException() : this(new ValidationResult(InvoiceProfile.EN16931, Array.Empty<ValidationIssue>())) { }

    /// <summary>Creates the exception with a message.</summary>
    public InvoiceValidationException(string message) : base(message)
    {
        Result = new ValidationResult(InvoiceProfile.EN16931, Array.Empty<ValidationIssue>());
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public InvoiceValidationException(string message, Exception innerException) : base(message, innerException)
    {
        Result = new ValidationResult(InvoiceProfile.EN16931, Array.Empty<ValidationIssue>());
    }

    /// <summary>The full validation result.</summary>
    public ValidationResult Result { get; }
}
