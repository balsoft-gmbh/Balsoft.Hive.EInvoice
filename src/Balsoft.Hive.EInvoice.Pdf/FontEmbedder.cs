using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Balsoft.Hive.EInvoice.Pdf;

/// <summary>
/// PDF/A requires every font used for rendering to be embedded. ERP print engines often
/// reference TrueType fonts such as Arial without embedding them. This finds such fonts in
/// the carrier's pages and form XObjects, looks the font program up on the system by its
/// PostScript name (then by full and family name), and embeds it as FontFile2.
/// </summary>
internal static class FontEmbedder
{
    public static void EmbedMissingFonts(PdfDocument doc, IEnumerable<string> extraDirectories, Action<string>? log)
    {
        var missing = new List<(string BaseFont, PdfDictionary Descriptor)>();
        var seen = new HashSet<PdfDictionary>();
        foreach (var page in doc.Pages)
            Collect(page.Elements.GetDictionary("/Resources"), missing, seen, depth: 0);
        if (missing.Count == 0) return;

        var index = FontIndex.Build(extraDirectories.Concat(FontIndex.SystemDirectories()));
        foreach (var (baseFont, descriptor) in missing)
        {
            string name = baseFont.TrimStart('/');
            int plus = name.IndexOf('+');
            if (plus == 6) name = name.Substring(7);   // subset tag "ABCDEF+"
            string? file = index.Find(name);
            if (file is null)
            {
                log?.Invoke($"Font '{name}' is not embedded in the carrier and was not found on this system; the PDF/A check will fail for it.");
                continue;
            }
            byte[] program = File.ReadAllBytes(file);
            var stream = new PdfDictionary(doc);
            stream.CreateStream(PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(program));
            stream.Elements["/Filter"] = new PdfName("/FlateDecode");
            stream.Elements["/Length1"] = new PdfInteger(program.Length);
            doc.Internals.AddObject(stream);
            descriptor.Elements["/FontFile2"] = stream.Reference;
            log?.Invoke($"Embedded font '{name}' from {file}.");
        }
    }

    private static void Collect(PdfDictionary? resources, List<(string, PdfDictionary)> missing, HashSet<PdfDictionary> seen, int depth)
    {
        if (resources is null || depth > 8) return;
        if (resources.Elements.GetDictionary("/Font") is { } fonts)
        {
            foreach (var key in fonts.Elements.Keys)
            {
                if (fonts.Elements.GetDictionary(key) is not { } font) continue;
                string subtype = font.Elements.GetName("/Subtype");
                var target = font;
                if (subtype == "/Type0" && font.Elements.GetArray("/DescendantFonts") is { Elements.Count: > 0 } kids)
                {
                    target = kids.Elements[0] is PdfReference r ? r.Value as PdfDictionary ?? font : kids.Elements[0] as PdfDictionary ?? font;
                    subtype = target.Elements.GetName("/Subtype");
                }
                // Only TrueType programs can be embedded as FontFile2 from a .ttf file.
                if (subtype is not ("/TrueType" or "/CIDFontType2")) continue;
                if (target.Elements.GetDictionary("/FontDescriptor") is not { } descriptor) continue;
                if (descriptor.Elements.ContainsKey("/FontFile") || descriptor.Elements.ContainsKey("/FontFile2") || descriptor.Elements.ContainsKey("/FontFile3")) continue;
                if (!seen.Add(descriptor)) continue;
                missing.Add((target.Elements.GetName("/BaseFont"), descriptor));
            }
        }
        if (resources.Elements.GetDictionary("/XObject") is { } xobjects)
        {
            foreach (var key in xobjects.Elements.Keys)
            {
                if (xobjects.Elements.GetDictionary(key) is { } xo && xo.Elements.GetName("/Subtype") == "/Form")
                    Collect(xo.Elements.GetDictionary("/Resources"), missing, seen, depth + 1);
            }
        }
    }
}

/// <summary>An index of installed TrueType fonts by PostScript, full and family name.</summary>
internal sealed class FontIndex
{
    private readonly Dictionary<string, string> _postScript = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fullName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _family = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> SystemDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string windows = Environment.GetEnvironmentVariable("WINDIR") ?? "";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.Combine(windows, "Fonts"),
            Path.Combine(local, "Microsoft", "Windows", "Fonts"),
            "/usr/share/fonts", "/usr/local/share/fonts",
            Path.Combine(home, ".fonts"), Path.Combine(home, ".local", "share", "fonts"),
            "/Library/Fonts", "/System/Library/Fonts", Path.Combine(home, "Library", "Fonts"),
        }.Where(d => d.Length > 0 && Directory.Exists(d));
    }

    public static FontIndex Build(IEnumerable<string> directories)
    {
        var index = new FontIndex();
        foreach (string dir in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string file in files.Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryReadNames(file, out string? ps, out string? full, out string? family)) continue;
                if (ps is not null && !index._postScript.ContainsKey(ps)) index._postScript[ps] = file;
                if (full is not null && !index._fullName.ContainsKey(full)) index._fullName[full] = file;
                if (family is not null && !index._family.ContainsKey(family)) index._family[family] = file;
            }
        }
        return index;
    }

    /// <summary>
    /// The font file for a PDF BaseFont name: by PostScript name ("Arial-BoldMT"), then by full
    /// name with the PDF style suffix spelled out ("Arial,Bold" becomes "Arial Bold"), then by family.
    /// </summary>
    public string? Find(string baseFont)
    {
        if (_postScript.TryGetValue(baseFont, out var f)) return f;
        string full = baseFont.Replace(',', ' ').Replace('-', ' ');
        if (_fullName.TryGetValue(full, out f)) return f;
        string trimmed = full.EndsWith("MT", StringComparison.Ordinal) ? full.Substring(0, full.Length - 2).TrimEnd() : full;
        if (_fullName.TryGetValue(trimmed, out f)) return f;
        if (_fullName.TryGetValue(trimmed + " Regular", out f)) return f;
        return _family.TryGetValue(trimmed, out f) ? f : null;
    }

    /// <summary>Reads nameID 1 (family), 4 (full name) and 6 (PostScript name) from a TrueType 'name' table.</summary>
    internal static bool TryReadNames(string file, out string? postScript, out string? fullName, out string? family)
    {
        postScript = fullName = family = null;
        try
        {
            using var fs = File.OpenRead(file);
            using var r = new BinaryReader(fs);
            uint U32() => (uint)(r.ReadByte() << 24 | r.ReadByte() << 16 | r.ReadByte() << 8 | r.ReadByte());
            ushort U16() => (ushort)(r.ReadByte() << 8 | r.ReadByte());

            uint version = U32();
            if (version != 0x00010000 && version != 0x74727565) return false;   // TrueType outlines only
            int tables = U16();
            fs.Seek(6, SeekOrigin.Current);
            uint nameOffset = 0;
            for (int i = 0; i < tables; i++)
            {
                uint tag = U32(); U32(); uint offset = U32(); U32();
                if (tag == 0x6E616D65) nameOffset = offset;   // 'name'
            }
            if (nameOffset == 0) return false;
            fs.Seek(nameOffset, SeekOrigin.Begin);
            U16(); int count = U16(); int stringOffset = U16();
            var records = new List<(int Platform, int Encoding, int Language, int NameId, int Length, int Offset)>();
            for (int i = 0; i < count; i++)
                records.Add((U16(), U16(), U16(), U16(), U16(), U16()));

            string? Read(int nameId)
            {
                // Prefer Windows Unicode English, then any Windows Unicode, then Mac Roman.
                var rec = records.Where(x => x.NameId == nameId && x.Platform == 3 && (x.Encoding == 1 || x.Encoding == 0))
                    .OrderBy(x => x.Language == 0x409 ? 0 : 1).Select(x => (x, true)).FirstOrDefault();
                if (rec.x.Length == 0) rec = records.Where(x => x.NameId == nameId && x.Platform == 1).Select(x => (x, false)).FirstOrDefault();
                if (rec.x.Length == 0) return null;
                fs.Seek(nameOffset + stringOffset + rec.x.Offset, SeekOrigin.Begin);
                byte[] bytes = r.ReadBytes(rec.x.Length);
                return (rec.Item2 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.ASCII.GetString(bytes)).Trim();
            }

            family = Read(1);
            fullName = Read(4);
            postScript = Read(6);
            return postScript is not null || fullName is not null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
