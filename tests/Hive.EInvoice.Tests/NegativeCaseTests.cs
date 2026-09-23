using Hive.EInvoice.Cii;
using Hive.EInvoice.Validation;

namespace Hive.EInvoice.Tests;

public sealed class NegativeCaseFixture
{
    public Dictionary<string, ExternalValidators.Outcome>? Kosit { get; }

    public NegativeCaseFixture()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "samples", "negative");
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var c in NegativeCases.All)
        {
            string file = Path.Combine(dir, c.Name + ".xml");
            File.WriteAllBytes(file, CiiWriter.Write(c.Build(), InvoiceProfile.XRechnung, new WriterOptions { ValidateBeforeWriting = false }));
            files.Add(file);
        }
        if (ExternalValidators.Directory is { } tools) Kosit = ExternalValidators.Kosit(tools, files);
    }
}

public sealed class NegativeCaseTests : IClassFixture<NegativeCaseFixture>
{
    private readonly NegativeCaseFixture _fx;

    public NegativeCaseTests(NegativeCaseFixture fx) => _fx = fx;

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in NegativeCases.All) data.Add(c.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void PreflightReportsTheRule(string name)
    {
        var c = NegativeCases.All.Single(x => x.Name == name);
        var result = InvoiceValidator.Validate(c.Build(), InvoiceProfile.XRechnung);
        if (c.ValidatorWarns)
        {
            Assert.Contains(result.Warnings, e => e.RuleId == c.ValidatorRule);
            return;
        }
        Assert.False(result.IsValid, $"{name}: expected an error, got none");
        Assert.Contains(result.Errors, e => e.RuleId == c.ValidatorRule);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void KositRejectsWithTheSameRule(string name)
    {
        if (_fx.Kosit is null) Assert.Skip("HIVE_VALIDATORS is not set.");
        var c = NegativeCases.All.Single(x => x.Name == name);
        var outcome = _fx.Kosit[name];
        switch (c.Behaviour)
        {
            case NegativeCases.Kosit.Rejects:
                Assert.False(outcome.Accepted, $"{name}: KoSIT accepted a broken invoice");
                Assert.True(outcome.Findings.Any(f => f.Code == c.KositRule && f.Level == "error"), $"{name}: expected {c.KositRule}, KoSIT said:{Environment.NewLine}{outcome}");
                break;
            case NegativeCases.Kosit.Warns:
                Assert.True(outcome.Accepted, outcome.ToString());
                Assert.True(outcome.Findings.Any(f => f.Code == c.KositRule && f.Level == "warning"), $"{name}: expected warning {c.KositRule}:{Environment.NewLine}{outcome}");
                break;
            case NegativeCases.Kosit.DoesNotCheck:
                // Documents that the pre-flight is stricter than the official artefact. If KoSIT
                // starts rejecting this, the case moves to Rejects.
                Assert.True(outcome.Accepted, $"{name}: KoSIT now checks this rule; move the case to Rejects.{Environment.NewLine}{outcome}");
                break;
        }
    }
}
