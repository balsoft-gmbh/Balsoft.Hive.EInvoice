using Hive.EInvoice.Cii;

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

    public string OutputDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "samples");

    public Dictionary<string, ExternalValidators.Outcome>? Kosit { get; }

    public ExternalValidationFixture()
    {
        foreach (var profile in KositProfiles.Concat(MustangProfiles))
        {
            string dir = Path.Combine(OutputDirectory, "cii", profile.ToString());
            Directory.CreateDirectory(dir);
            foreach (var (name, build) in Samples.All)
                File.WriteAllBytes(Path.Combine(dir, name + ".xml"), CiiWriter.Write(build(), profile));
        }

        if (ExternalValidators.Directory is not { } tools) return;
        var files = KositProfiles
            .SelectMany(p => Samples.All.Keys.Select(n => (Profile: p, Name: n)))
            .Select(x => Path.Combine(OutputDirectory, "cii", x.Profile.ToString(), x.Name + ".xml"))
            .ToList();
        // KoSIT names reports after the file, so run each profile folder separately.
        Kosit = new Dictionary<string, ExternalValidators.Outcome>(StringComparer.Ordinal);
        foreach (var profile in KositProfiles)
        {
            var batch = files.Where(f => f.Contains(Path.DirectorySeparatorChar + profile.ToString() + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
            foreach (var (name, outcome) in ExternalValidators.Kosit(tools, batch))
                Kosit[$"cii/{profile}/{name}"] = outcome;
        }
    }
}

public sealed class ExternalValidationTests : IClassFixture<ExternalValidationFixture>
{
    private readonly ExternalValidationFixture _fx;

    public ExternalValidationTests(ExternalValidationFixture fx) => _fx = fx;

    public static TheoryData<string, InvoiceProfile> KositCases()
    {
        var data = new TheoryData<string, InvoiceProfile>();
        foreach (var p in ExternalValidationFixture.KositProfiles)
            foreach (var n in Samples.All.Keys)
                data.Add(n, p);
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
    public void KositAcceptsCii(string sample, InvoiceProfile profile)
    {
        if (_fx.Kosit is null) Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.ps1.");
        var outcome = _fx.Kosit[$"cii/{profile}/{sample}"];
        Assert.True(outcome.Accepted, outcome.ToString());
        Assert.DoesNotContain(outcome.Findings, f => f.Level == "warning");
    }

    [Theory]
    [MemberData(nameof(MustangCases))]
    public void MustangAcceptsFacturXProfiles(string sample, InvoiceProfile profile)
    {
        if (ExternalValidators.Directory is not { } tools) { Assert.Skip("HIVE_VALIDATORS is not set; run eng/get-validators.ps1."); return; }
        string file = Path.Combine(_fx.OutputDirectory, "cii", profile.ToString(), sample + ".xml");
        var outcome = ExternalValidators.Mustang(tools, file);
        Assert.True(outcome.Accepted, outcome.ToString());
    }
}
