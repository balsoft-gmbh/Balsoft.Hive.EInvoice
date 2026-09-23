using System.Diagnostics;
using System.Xml.Linq;

namespace Hive.EInvoice.Tests;

/// <summary>
/// Runs the official validators on files: the KoSIT validator with the XRechnung
/// configuration (XRechnung and EN 16931 scenarios, CII and UBL) and the Mustang CLI (Factur-X
/// / ZUGFeRD profile rules and PDF/A-3 via veraPDF). The tools live in the directory named by
/// HIVE_VALIDATORS (validator.jar, xrechnung/scenarios.xml, mustang.jar); eng/get-validators.ps1
/// and the CI workflow download them. Without it the external checks are skipped.
/// </summary>
public static class ExternalValidators
{
    public static string? Directory
    {
        get
        {
            string? dir = Environment.GetEnvironmentVariable("HIVE_VALIDATORS");
            return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "validator.jar")) ? dir : null;
        }
    }

    public sealed record Finding(string Level, string Code, string Text);

    public sealed record Outcome(bool Accepted, IReadOnlyList<Finding> Findings)
    {
        public override string ToString()
            => (Accepted ? "ACCEPT" : "REJECT") + Environment.NewLine + string.Join(Environment.NewLine, Findings.Select(f => $"  {f.Level} {f.Code}: {f.Text}"));
    }

    /// <summary>Validates every file with KoSIT in one JVM run; returns the outcome per file name (without extension).</summary>
    public static Dictionary<string, Outcome> Kosit(string dir, IReadOnlyCollection<string> files)
    {
        string reports = Path.Combine(Path.GetTempPath(), "hive-einvoice-kosit-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(reports);
        var args = new List<string>
        {
            "-jar", Path.Combine(dir, "validator.jar"),
            "-s", Path.Combine(dir, "xrechnung", "scenarios.xml"),
            "-r", Path.Combine(dir, "xrechnung"),
            "-o", reports,
        };
        args.AddRange(files);
        Run("java", args, out _);

        var result = new Dictionary<string, Outcome>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string report = Path.Combine(reports, name + "-report.xml");
            if (!File.Exists(report))
            {
                result[name] = new Outcome(false, new[] { new Finding("error", "NO-REPORT", "KoSIT produced no report (no matching scenario?)") });
                continue;
            }
            XNamespace rep = "http://www.xoev.de/de/validator/varl/1";
            var doc = XDocument.Load(report);
            bool accepted = doc.Descendants(rep + "accept").Any();
            var findings = doc.Descendants(rep + "message")
                .Select(m => new Finding((string?)m.Attribute("level") ?? "", (string?)m.Attribute("code") ?? "", m.Value.Trim()))
                .ToList();
            result[name] = new Outcome(accepted, findings);
        }
        try { System.IO.Directory.Delete(reports, true); } catch (IOException) { }
        return result;
    }

    /// <summary>Validates one XML or PDF file with Mustang (Factur-X / ZUGFeRD rules, PDF/A-3 for PDFs).</summary>
    public static Outcome Mustang(string dir, string file)
    {
        string jar = Path.Combine(dir, "mustang.jar");
        Run("java", new[] { "-jar", jar, "--disable-file-logging", "--no-notices", "--action", "validate", "--source", file }, out string stdout);
        int start = stdout.IndexOf("<validation", StringComparison.Ordinal);
        if (start < 0) return new Outcome(false, new[] { new Finding("error", "NO-REPORT", stdout.Trim()) });
        var doc = XDocument.Parse(stdout.Substring(start));
        bool valid = (string?)doc.Root!.Elements("summary").LastOrDefault()?.Attribute("status") == "valid";
        var findings = doc.Descendants()
            .Where(e => e.Name.LocalName is "error" or "warning" or "notice")
            .Select(e => new Finding(e.Name.LocalName, (string?)e.Attribute("type") ?? (string?)e.Attribute("location") ?? "", e.Value.Trim()))
            .ToList();
        return new Outcome(valid, findings);
    }

    private static void Run(string exe, IEnumerable<string> args, out string stdout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();   // the validators probe stdin; give them an empty, closed stream
        var err = p.StandardError.ReadToEndAsync();
        stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        _ = err.Result;
    }
}
