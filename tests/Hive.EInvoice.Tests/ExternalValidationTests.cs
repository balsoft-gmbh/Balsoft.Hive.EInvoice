using Hive.EInvoice.Cii;
using Hive.EInvoice.Ubl;

namespace Hive.EInvoice.Tests;

/// <summary>Writes every sample in every profile once and validates them with the official tools.</summary>
public sealed class ExternalValidationFixture
{
    public static readonly InvoiceProfile[] KositProfiles = { InvoiceProfile.XRechnung, InvoiceProfile.EN16931 };

    public static readonly InvoiceProfile[] MustangProfiles =
    {
        InvoiceProfile.FacturXMinimum, InvoiceProfile.FacturXBasicWL, InvoiceProfile.FacturXBasic, InvoiceProfile.FacturXExtended,
    };

    /// <summary>Samples checked with Mustang in the Factur-X profiles (Mustang runs one JVM per file).</summary>
    public static readonly string[] MustangSamples = { "01-standard", "02-skonto-aktivbank", "03-credit-note", "05-allowances-charges", "13-full-details" };

    public static readonly string[] Syntaxes = { "cii", "ubl" };

    public string OutputDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "samples");

    public Dictionary<string, ExternalValidators.Outcome>? Kosit { get; }

    public ExternalValidationFixture()
    {
        var batches = new List<(string Key, List<string> Files)>();
        foreach (var profile in KositProfiles.Concat(MustangProfiles))
        {
            foreach (var syntax in Syntaxes)
            {
                if (syntax == "ubl" && !profile.IsEN16931Compliant()) continue;
                string dir = Path.Combine(OutputDirectory, syntax, profile.ToString());
                Directory.CreateDirectory(dir);
                var files = new List<string>();
                foreach (var (name, build) in Samples.All)
                {
                    string file = Path.Combine(dir, name + ".xml");
                    File.WriteAllBytes(file, syntax == "ubl" ? UblWriter.Write(build(), profile) : CiiWriter.Write(build(), profile));
                    files.Add(file);
                }
                if (KositProfiles.Contains(profile)) batches.Add(($"{syntax}/{profile}", files));
            }
        }

        if (ExternalValidators.Directory is not { } tools) return;
        // KoSIT names reports after the file, so each folder is its own run.
        Kosit = new Dictionary<string, ExternalValidators.Outcome>(StringComparer.Ordinal);
        foreach (var (key, files) in batches)
            foreach (var (name, outcome) in ExternalValidators.Kosit(tools, files))
                Kosit[$"{key}/{name}"] = outcome;
    }
}

public sealed class ExternalValidationTests : IClassFixture<ExternalValidationFixture>
{
    private readonly ExternalValidationFixture _fx;

    public ExternalValidationTests(ExternalValidationFixture fx) => _fx = fx;

    public static TheoryData<string, string, InvoiceProfile> KositCases()
    {
        var data = new TheoryData<string, string, InvoiceProfile>();
        foreach (var syntax in ExternalValidationFixture.Syntaxes)
            foreach (var p in ExternalValidationFixture.KositProfiles)
                foreach (var n in Samples.All.Keys)
                    data.Add(syntax, n, p);
        return data;
    }

    public static TheoryData<string, InvoiceProfile> MustangCases()
    {
        var data = new TheoryData<string, InvoiceProfile>();
        foreach (var p in ExternalValidationFixture.MustangProfiles)
            foreach (var n in ExternalValidationFixture.MustangSamples)
                data.Add(n, p);
        return data;
    }

    [Theory]
    [MemberData(nameof(KositCases))]
    public void KositAccepts(string syntax, string sample, InvoiceProfile profile)
    {
        if (_fx.Kosit is null) Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.sh.");
        var outcome = _fx.Kosit[$"{syntax}/{profile}/{sample}"];
        Assert.True(outcome.Accepted, outcome.ToString());
        Assert.DoesNotContain(outcome.Findings, f => f.Level == "warning");
    }

    [Theory]
    [MemberData(nameof(MustangCases))]
    public void MustangAcceptsFacturXProfiles(string sample, InvoiceProfile profile)
    {
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.sh."); return; }
        string file = Path.Combine(_fx.OutputDirectory, "cii", profile.ToString(), sample + ".xml");
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }
}
