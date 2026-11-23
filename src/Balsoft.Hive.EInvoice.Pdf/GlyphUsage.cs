using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Balsoft.Hive.EInvoice.Pdf;

/// <summary>
/// The character codes a document shows with each TrueType font, read from every content
/// stream a font can be painted from: pages, form XObjects, tiling patterns, soft mask groups,
/// Type3 glyph procedures and annotation appearances. A font is blocked (never subset) when it
/// sits in resources that were not read (form field defaults in /AcroForm /DR among them),
/// when its codes cannot be mapped to glyphs, or when a content stream could not be decoded.
/// </summary>
internal sealed class GlyphUsage
{
    private sealed class FontUse
    {
        public FontUse(PdfDictionary font, bool cid) { Font = font; Cid = cid; }
        public PdfDictionary Font { get; }
        public bool Cid { get; }
        public List<PdfDictionary> Parents { get; } = new();
        public HashSet<int> Codes { get; } = new();
        public bool Blocked { get; set; }
        public PdfDictionary? Descriptor => Font.Elements.GetDictionary("/FontDescriptor");
    }

    private static readonly string[] ReadableFilters = { "/FlateDecode", "/Fl", "/LZWDecode", "/LZW", "/ASCIIHexDecode", "/AHx", "/ASCII85Decode", "/A85" };

    private readonly Dictionary<PdfDictionary, FontUse> _uses = new(ByReference.Instance);
    private readonly HashSet<PdfDictionary> _visitedResources = new(ByReference.Instance);
    private readonly HashSet<(PdfDictionary, PdfDictionary?)> _visitedStreams = new(PairByReference.Instance);
    private bool _complete = true;

    public static GlyphUsage Collect(PdfDocument doc)
    {
        var usage = new GlyphUsage();
        foreach (var page in doc.Pages)
        {
            var resources = PageResources(page);
            usage.Visit(PageContent(page, ref usage._complete), resources, 0);
            if (page.Elements.GetArray("/Annots") is not { } annots) continue;
            foreach (var item in annots.Elements)
            {
                if (Resolve(item) is not PdfDictionary annot || annot.Elements.GetDictionary("/AP") is not { } ap) continue;
                foreach (string key in new[] { "/N", "/R", "/D" })
                {
                    var appearance = Resolve(ap.Elements[key]) as PdfDictionary;
                    if (appearance is null) continue;
                    if (appearance.Stream is not null) usage.VisitStream(appearance, resources, 1);
                    else
                        foreach (var state in appearance.Elements.Values)
                            if (Resolve(state) is PdfDictionary s && s.Stream is not null) usage.VisitStream(s, resources, 1);
                }
            }
        }

        // Fields may be filled later with any letter from their default resources.
        if (doc.Internals.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetDictionary("/DR") is { } dr)
            usage.BlockAll(dr);

        foreach (var item in doc.Internals.GetAllObjects())
        {
            if (item is not PdfDictionary dict) continue;
            if (Resolve(dict.Elements["/Resources"]) is PdfDictionary res && !usage._visitedResources.Contains(res)) usage.BlockAll(res);
            if (dict.Elements.ContainsKey("/Font") && Resolve(dict.Elements["/Font"]) is PdfDictionary
                && !usage._visitedResources.Contains(dict)) usage.BlockAll(dict);
        }
        return usage;
    }

    /// <summary>The descriptors of the TrueType fonts the document has, each once.</summary>
    public IEnumerable<PdfDictionary> Descriptors()
        => _uses.Values.Select(u => u.Descriptor).Where(d => d is not null).Select(d => d!).Distinct(ByReference.Instance);

    public bool IsCidFont(PdfDictionary descriptor) => UsesOf(descriptor).Any(u => u.Cid);

    /// <summary>
    /// The glyph ids the document shows with the fonts behind <paramref name="descriptor"/>, mapped
    /// through <paramref name="program"/>; null when the font must stay complete.
    /// </summary>
    public HashSet<int>? Glyphs(PdfDictionary descriptor, TrueTypeFont program)
    {
        var uses = UsesOf(descriptor).ToList();
        if (!_complete || uses.Count == 0 || uses.Any(u => u.Blocked) || uses.All(u => u.Codes.Count == 0)) return null;
        var glyphs = new HashSet<int> { 0 };
        IReadOnlyList<(int Platform, int Encoding, Dictionary<int, int> Map)>? cmaps = null;
        Dictionary<string, int>? postNames = null;
        foreach (var use in uses)
        {
            if (use.Cid)
            {
                if (!CidToGid(use.Font, out var map)) return null;
                foreach (int cid in use.Codes) glyphs.Add(map is null ? cid : (2 * cid + 1 < map.Length ? (map[2 * cid] << 8) | map[2 * cid + 1] : 0));
                continue;
            }
            cmaps ??= program.Cmaps();
            if (!SimpleEncoding(use.Font, out var differences)) return null;
            foreach (int code in use.Codes)
            {
                var found = SimpleGlyphs(code, differences, cmaps, () => postNames ??= program.PostNames());
                if (found.Count == 0) return null;
                glyphs.UnionWith(found);
            }
        }
        return glyphs;
    }

    /// <summary>Prefixes the subset tag to the names of every font behind the descriptor and to the descriptor's FontName.</summary>
    public void Tag(PdfDictionary descriptor, string tag)
    {
        foreach (var use in UsesOf(descriptor))
        {
            Rename(use.Font, "/BaseFont", tag);
            foreach (var parent in use.Parents) Rename(parent, "/BaseFont", tag);
        }
        Rename(descriptor, "/FontName", tag);
    }

    private static void Rename(PdfDictionary dict, string key, string tag)
    {
        string name = dict.Elements.GetName(key);
        if (name.Length > 1) dict.Elements[key] = new PdfName(FontSubsetter.WithTag(name, tag));
    }

    private IEnumerable<FontUse> UsesOf(PdfDictionary descriptor)
        => _uses.Values.Where(u => ReferenceEquals(u.Descriptor, descriptor));

    // ── reading content ───────────────────────────────────────────────────────

    private void VisitStream(PdfDictionary stream, PdfDictionary? inherited, int depth)
    {
        var resources = Resolve(stream.Elements["/Resources"]) as PdfDictionary ?? inherited;
        if (!_visitedStreams.Add((stream, resources))) return;
        Visit(Decode(stream, ref _complete), resources, depth);
    }

    private void Visit(byte[] content, PdfDictionary? resources, int depth)
    {
        if (depth > 16) { _complete = false; return; }
        if (resources is not null) _visitedResources.Add(resources);
        var fonts = resources?.Elements.GetDictionary("/Font");
        foreach (var key in fonts?.Elements.Keys ?? Enumerable.Empty<string>()) Register(Resolve(fonts!.Elements[key]) as PdfDictionary);

        var stack = new Stack<(FontUse? Use, bool TwoByte)>();
        (FontUse? Use, bool TwoByte) current = (null, false);
        try
        {
            ContentLexer.Scan(content, (op, operands) =>
            {
                switch (op)
                {
                    case "q": stack.Push(current); break;
                    case "Q": if (stack.Count > 0) current = stack.Pop(); break;
                    case "Tf":
                        if (operands.Count >= 2 && operands[0] is string name && fonts is not null)
                            current = Current(Resolve(fonts.Elements[name]) as PdfDictionary);
                        else current = (null, false);
                        break;
                    case "gs":
                        if (operands.Count >= 1 && operands[0] is string gsName
                            && resources?.Elements.GetDictionary("/ExtGState")?.Elements.GetDictionary(gsName) is { } gs
                            && gs.Elements.GetArray("/Font") is { Elements.Count: > 0 } fontRef)
                            current = Current(Resolve(fontRef.Elements[0]) as PdfDictionary);
                        break;
                    case "Tj": case "'": Show(current, operands.LastOrDefault() as byte[]); break;
                    case "\"": Show(current, operands.LastOrDefault() as byte[]); break;
                    case "TJ":
                        if (operands.LastOrDefault() is List<object> items)
                            foreach (var part in items) Show(current, part as byte[]);
                        break;
                }
            });
        }
        catch (FormatException)
        {
            _complete = false;
        }

        if (resources is null) return;
        foreach (var item in Values(resources.Elements.GetDictionary("/XObject")))
            if (item.Elements.GetName("/Subtype") == "/Form" && item.Stream is not null) VisitStream(item, resources, depth + 1);
        foreach (var item in Values(resources.Elements.GetDictionary("/Pattern")))
            if (item.Elements.GetInteger("/PatternType") == 1 && item.Stream is not null) VisitStream(item, resources, depth + 1);
        foreach (var item in Values(resources.Elements.GetDictionary("/ExtGState")))
            if (Resolve(item.Elements["/SMask"]) is PdfDictionary mask && Resolve(mask.Elements["/G"]) is PdfDictionary group && group.Stream is not null)
                VisitStream(group, resources, depth + 1);
        foreach (var item in Values(fonts))
        {
            if (item.Elements.GetName("/Subtype") != "/Type3") continue;
            var own = Resolve(item.Elements["/Resources"]) as PdfDictionary ?? resources;
            foreach (var proc in Values(item.Elements.GetDictionary("/CharProcs")))
                if (proc.Stream is not null) VisitStream(proc, own, depth + 1);
        }
    }

    private (FontUse?, bool) Current(PdfDictionary? font)
    {
        var use = Register(font);
        return (use, use?.Cid == true);
    }

    private FontUse? Register(PdfDictionary? font)
    {
        if (font is null) return null;
        string subtype = font.Elements.GetName("/Subtype");
        if (subtype == "/TrueType") return Use(font, cid: false);
        if (subtype != "/Type0" || font.Elements.GetArray("/DescendantFonts") is not { Elements.Count: > 0 } kids) return null;
        if (Resolve(kids.Elements[0]) is not PdfDictionary descendant || descendant.Elements.GetName("/Subtype") != "/CIDFontType2") return null;
        var use = Use(descendant, cid: true);
        if (!use.Parents.Contains(font, ByReference.Instance)) use.Parents.Add(font);
        string encoding = font.Elements.GetName("/Encoding");
        if (encoding is not ("/Identity-H" or "/Identity-V")) use.Blocked = true;
        return use;
    }

    private FontUse Use(PdfDictionary font, bool cid)
    {
        if (!_uses.TryGetValue(font, out var use)) _uses[font] = use = new FontUse(font, cid);
        return use;
    }

    private static void Show((FontUse? Use, bool TwoByte) current, byte[]? text)
    {
        if (current.Use is null || text is null) return;
        if (current.TwoByte)
            for (int i = 0; i + 1 < text.Length; i += 2) current.Use.Codes.Add((text[i] << 8) | text[i + 1]);
        else
            foreach (byte b in text) current.Use.Codes.Add(b);
    }

    private void BlockAll(PdfDictionary resources)
    {
        foreach (var font in Values(resources.Elements.GetDictionary("/Font")))
            if (Register(font) is { } use) use.Blocked = true;
    }

    // ── codes to glyphs ───────────────────────────────────────────────────────

    private static bool CidToGid(PdfDictionary cidFont, out byte[]? map)
    {
        map = null;
        var value = Resolve(cidFont.Elements["/CIDToGIDMap"]);
        if (value is null || (value is PdfName n && n.Value == "/Identity")) return true;
        if (value is PdfDictionary stream && stream.Stream is not null)
        {
            bool complete = true;
            map = Decode(stream, ref complete);
            return complete;
        }
        return false;
    }

    private static bool SimpleEncoding(PdfDictionary font, out Dictionary<int, string> differences)
    {
        differences = new Dictionary<int, string>();
        var encoding = Resolve(font.Elements["/Encoding"]);
        if (encoding is null || encoding is PdfName { Value: "/WinAnsiEncoding" or "/MacRomanEncoding" }) return true;
        if (encoding is not PdfDictionary dict) return false;
        string baseEncoding = dict.Elements.GetName("/BaseEncoding");
        if (baseEncoding is not ("" or "/WinAnsiEncoding" or "/MacRomanEncoding")) return false;
        int code = 0;
        if (dict.Elements.GetArray("/Differences") is not { } list) return true;
        foreach (var item in list.Elements)
        {
            if (item is PdfInteger i) code = i.Value;
            else if (item is PdfName name) differences[code++] = name.Value.TrimStart('/');
        }
        return true;
    }

    /// <summary>
    /// Every glyph a viewer may pick for a one-byte code: symbolic (3,0) and Mac (1,0) cmaps by
    /// code, the Unicode cmaps through the encoding, and the post table by glyph name.
    /// </summary>
    private static HashSet<int> SimpleGlyphs(int code, Dictionary<int, string> differences,
        IReadOnlyList<(int Platform, int Encoding, Dictionary<int, int> Map)> cmaps, Func<Dictionary<string, int>> postNames)
    {
        var found = new HashSet<int>();
        differences.TryGetValue(code, out string? glyphName);
        var unicodes = new List<int>();
        if (glyphName is not null) { if (NameToUnicode(glyphName) is int u) unicodes.Add(u); }
        else
        {
            unicodes.Add(WinAnsi(code));
            if (code == 0xAD) unicodes.Add(0x2D);
            if (code == 0xA0) unicodes.Add(0x20);
        }
        foreach (var (platform, encoding, map) in cmaps)
        {
            if (platform == 3 && encoding == 0)
            {
                foreach (int c in new[] { code, 0xF000 + code, 0xF100 + code, 0xF200 + code })
                    if (map.TryGetValue(c, out int g)) found.Add(g);
            }
            else if (platform == 1 && encoding == 0)
            {
                if (map.TryGetValue(code, out int g)) found.Add(g);
            }
            else if (platform == 0 || (platform == 3 && encoding is 1 or 10))
            {
                foreach (int u in unicodes)
                    if (map.TryGetValue(u, out int g)) found.Add(g);
            }
        }
        if (glyphName is not null && postNames().TryGetValue(glyphName, out int named)) found.Add(named);
        return found;
    }

    private static int? NameToUnicode(string name)
    {
        if (name.Length == 7 && name.StartsWith("uni", StringComparison.Ordinal)
            && int.TryParse(name.Substring(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int uni)) return uni;
        if (name.Length is >= 5 and <= 7 && name[0] == 'u'
            && int.TryParse(name.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u)) return u;
        int index = MacGlyphNames.IndexOf(name);
        return index is >= 3 and <= 97 ? 0x20 + index - 3 : null;   // space .. asciitilde
    }

    private static readonly int[] WinAnsiHigh =
    {
        0x20AC, 0x2022, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x2022, 0x017D, 0x2022,
        0x2022, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x2022, 0x017E, 0x0178,
    };

    /// <summary>WinAnsiEncoding (ISO 32000-1, Annex D) code to Unicode; unassigned codes show a bullet.</summary>
    internal static int WinAnsi(int code)
        => code == 0x7F ? 0x2022 : code is >= 0x80 and <= 0x9F ? WinAnsiHigh[code - 0x80] : code;

    // ── helpers ───────────────────────────────────────────────────────────────

    private static PdfDictionary? PageResources(PdfPage page)
    {
        PdfDictionary? node = page;
        for (int i = 0; node is not null && i < 32; i++)
        {
            if (Resolve(node.Elements["/Resources"]) is PdfDictionary r) return r;
            node = Resolve(node.Elements["/Parent"]) as PdfDictionary;
        }
        return null;
    }

    private static byte[] PageContent(PdfPage page, ref bool complete)
    {
        var contents = Resolve(page.Elements["/Contents"]);
        var parts = new List<byte[]>();
        if (contents is PdfDictionary single) parts.Add(Decode(single, ref complete));
        else if (contents is PdfArray array)
            foreach (var item in array.Elements)
                if (Resolve(item) is PdfDictionary d) parts.Add(Decode(d, ref complete));
        var all = new List<byte>();
        foreach (var part in parts) { all.AddRange(part); all.Add((byte)'\n'); }
        return all.ToArray();
    }

    internal static byte[] Decode(PdfDictionary stream, ref bool complete)
    {
        if (stream.Stream is null) return Array.Empty<byte>();
        var filter = Resolve(stream.Elements["/Filter"]);
        var names = filter is PdfName one ? new[] { one.Value }
            : filter is PdfArray many ? many.Elements.Select(e => (Resolve(e) as PdfName)?.Value ?? "?").ToArray()
            : Array.Empty<string>();
        if (names.Any(n => !ReadableFilters.Contains(n))) { complete = false; return Array.Empty<byte>(); }
        try { return stream.Stream.UnfilteredValue; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.IO.IOException or ArgumentException)
        {
            complete = false;
            return Array.Empty<byte>();
        }
    }

    private static IEnumerable<PdfDictionary> Values(PdfDictionary? dict)
    {
        if (dict is null) yield break;
        foreach (var key in dict.Elements.Keys.ToList())
            if (Resolve(dict.Elements[key]) is PdfDictionary d) yield return d;
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;

    private sealed class ByReference : IEqualityComparer<PdfDictionary>
    {
        public static readonly ByReference Instance = new();
        public bool Equals(PdfDictionary? x, PdfDictionary? y) => ReferenceEquals(x, y);
        public int GetHashCode(PdfDictionary obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class PairByReference : IEqualityComparer<(PdfDictionary, PdfDictionary?)>
    {
        public static readonly PairByReference Instance = new();
        public bool Equals((PdfDictionary, PdfDictionary?) x, (PdfDictionary, PdfDictionary?) y)
            => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);
        public int GetHashCode((PdfDictionary, PdfDictionary?) obj)
            => RuntimeHelpers.GetHashCode(obj.Item1) * 31 + (obj.Item2 is null ? 0 : RuntimeHelpers.GetHashCode(obj.Item2));
    }
}

/// <summary>
/// A tokenizer for PDF content streams (ISO 32000-1, 7.2 and 7.8.2) that keeps string operands
/// as their exact bytes. Operands are double, a name with its slash, byte[] for strings,
/// List&lt;object&gt; for arrays and null for dictionaries, booleans and null. Inline image data
/// (BI ... ID ... EI) is skipped.
/// </summary>
internal static class ContentLexer
{
    public static void Scan(byte[] data, Action<string, List<object?>> onOperator)
    {
        int p = 0;
        var operands = new List<object?>();
        while (true)
        {
            var token = Next(data, ref p, out bool end);
            if (end) return;
            if (token is Op op)
            {
                if (op.Name == "BI")
                {
                    SkipInlineImage(data, ref p);
                    operands.Clear();
                    continue;
                }
                if (op.Name is "true" or "false" or "null") { operands.Add(null); continue; }
                onOperator(op.Name, operands);
                operands.Clear();
            }
            else if (token is Close) throw new FormatException("Unbalanced ']' or '>>' in a content stream.");
            else operands.Add(token);
        }
    }

    private sealed class Op { public Op(string name) { Name = name; } public string Name { get; } }

    private sealed class Close
    {
        public static readonly Close ArrayEnd = new();
        public static readonly Close DictionaryEnd = new();
    }

    private static object? Next(byte[] d, ref int p, out bool end)
    {
        end = false;
        while (true)
        {
            while (p < d.Length && IsWhite(d[p])) p++;
            if (p >= d.Length) { end = true; return null; }
            if (d[p] != '%') break;
            while (p < d.Length && d[p] != '\n' && d[p] != '\r') p++;
        }
        byte c = d[p];
        switch (c)
        {
            case (byte)'(': return LiteralString(d, ref p);
            case (byte)'<':
                if (p + 1 < d.Length && d[p + 1] == '<')
                {
                    p += 2;
                    while (true)
                    {
                        var inner = Next(d, ref p, out bool e);
                        if (e) throw new FormatException("Unterminated dictionary in a content stream.");
                        if (ReferenceEquals(inner, Close.DictionaryEnd)) return null;
                    }
                }
                return HexString(d, ref p);
            case (byte)'>':
                if (p + 1 < d.Length && d[p + 1] == '>') { p += 2; return Close.DictionaryEnd; }
                throw new FormatException("Stray '>' in a content stream.");
            case (byte)'[':
            {
                p++;
                var items = new List<object>();
                while (true)
                {
                    var inner = Next(d, ref p, out bool e);
                    if (e) throw new FormatException("Unterminated array in a content stream.");
                    if (ReferenceEquals(inner, Close.ArrayEnd)) return items;
                    if (inner is Op || inner is Close) throw new FormatException("Unexpected token in an array.");
                    if (inner is not null) items.Add(inner);
                }
            }
            case (byte)']': p++; return Close.ArrayEnd;
            case (byte)'{': case (byte)'}': p++; return null;
            case (byte)'/':
            {
                p++;
                var name = new StringBuilder("/");
                while (p < d.Length && !IsWhite(d[p]) && !IsDelimiter(d[p]))
                {
                    if (d[p] == '#' && p + 2 < d.Length && IsHex(d[p + 1]) && IsHex(d[p + 2]))
                    {
                        name.Append((char)(Hex(d[p + 1]) * 16 + Hex(d[p + 2])));
                        p += 3;
                    }
                    else name.Append((char)d[p++]);
                }
                return name.ToString();
            }
        }
        int begin = p;
        while (p < d.Length && !IsWhite(d[p]) && !IsDelimiter(d[p])) p++;
        if (p == begin) throw new FormatException($"Stray '{(char)c}' in a content stream.");
        string word = Encoding.ASCII.GetString(d, begin, p - begin);
        if (word.Length > 0 && (char.IsDigit(word[0]) || word[0] is '+' or '-' or '.'))
        {
            if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
            if (word is "-" or "+" or ".") return 0.0;
        }
        return new Op(word);
    }

    private static byte[] LiteralString(byte[] d, ref int p)
    {
        p++;
        var bytes = new List<byte>();
        int depth = 1;
        while (p < d.Length)
        {
            byte c = d[p++];
            if (c == '\\')
            {
                if (p >= d.Length) break;
                byte e = d[p++];
                switch (e)
                {
                    case (byte)'n': bytes.Add(10); break;
                    case (byte)'r': bytes.Add(13); break;
                    case (byte)'t': bytes.Add(9); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'\r': if (p < d.Length && d[p] == '\n') p++; break;
                    case (byte)'\n': break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int v = e - '0';
                            for (int i = 0; i < 2 && p < d.Length && d[p] >= '0' && d[p] <= '7'; i++) v = v * 8 + (d[p++] - '0');
                            bytes.Add((byte)v);
                        }
                        else bytes.Add(e);
                        break;
                }
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return bytes.ToArray();
            bytes.Add(c);
        }
        throw new FormatException("Unterminated string in a content stream.");
    }

    private static byte[] HexString(byte[] d, ref int p)
    {
        p++;
        var bytes = new List<byte>();
        int high = -1;
        while (p < d.Length && d[p] != '>')
        {
            byte c = d[p++];
            if (IsWhite(c)) continue;
            if (!IsHex(c)) throw new FormatException("Invalid character in a hexadecimal string.");
            if (high < 0) high = Hex(c);
            else { bytes.Add((byte)(high * 16 + Hex(c))); high = -1; }
        }
        if (p >= d.Length) throw new FormatException("Unterminated hexadecimal string.");
        p++;
        if (high >= 0) bytes.Add((byte)(high * 16));
        return bytes.ToArray();
    }

    /// <summary>Skips the key/value pairs and the binary data of an inline image up to its EI.</summary>
    private static void SkipInlineImage(byte[] d, ref int p)
    {
        while (true)
        {
            var token = Next(d, ref p, out bool end);
            if (end) throw new FormatException("Inline image without ID.");
            if (token is Op { Name: "ID" }) break;
        }
        p++;   // the single white-space byte after ID
        for (; p + 1 < d.Length; p++)
        {
            if (d[p] == 'E' && d[p + 1] == 'I' && p > 0 && IsWhite(d[p - 1])
                && (p + 2 == d.Length || IsWhite(d[p + 2]) || IsDelimiter(d[p + 2])))
            {
                p += 2;
                return;
            }
        }
        throw new FormatException("Inline image without EI.");
    }

    private static bool IsWhite(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte c) => c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
        or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool IsHex(byte c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int Hex(byte c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}

/// <summary>The 258 standard Macintosh glyph names a format 2 post table indexes into (Apple TrueType reference, 'post').</summary>
internal static class MacGlyphNames
{
    private static readonly string[] Names = (
        ".notdef .null nonmarkingreturn space exclam quotedbl numbersign dollar percent ampersand quotesingle parenleft " +
        "parenright asterisk plus comma hyphen period slash zero one two three four five six seven eight nine colon " +
        "semicolon less equal greater question at A B C D E F G H I J K L M N O P Q R S T U V W X Y Z bracketleft " +
        "backslash bracketright asciicircum underscore grave a b c d e f g h i j k l m n o p q r s t u v w x y z " +
        "braceleft bar braceright asciitilde Adieresis Aring Ccedilla Eacute Ntilde Odieresis Udieresis aacute agrave " +
        "acircumflex adieresis atilde aring ccedilla eacute egrave ecircumflex edieresis iacute igrave icircumflex " +
        "idieresis ntilde oacute ograve ocircumflex odieresis otilde uacute ugrave ucircumflex udieresis dagger degree " +
        "cent sterling section bullet paragraph germandbls registered copyright trademark acute dieresis notequal AE " +
        "Oslash infinity plusminus lessequal greaterequal yen mu partialdiff summation product pi integral ordfeminine " +
        "ordmasculine Omega ae oslash questiondown exclamdown logicalnot radical florin approxequal Delta guillemotleft " +
        "guillemotright ellipsis nonbreakingspace Agrave Atilde Otilde OE oe endash emdash quotedblleft quotedblright " +
        "quoteleft quoteright divide lozenge ydieresis Ydieresis fraction currency guilsinglleft guilsinglright fi fl " +
        "daggerdbl periodcentered quotesinglbase quotedblbase perthousand Acircumflex Ecircumflex Aacute Edieresis " +
        "Egrave Iacute Icircumflex Idieresis Igrave Oacute Ocircumflex apple Ograve Uacute Ucircumflex Ugrave dotlessi " +
        "circumflex tilde macron breve dotaccent ring cedilla hungarumlaut ogonek caron Lslash lslash Scaron scaron " +
        "Zcaron zcaron brokenbar Eth eth Yacute yacute Thorn thorn minus multiply onesuperior twosuperior " +
        "threesuperior onehalf onequarter threequarters franc Gbreve gbreve Idotaccent Scedilla scedilla Cacute " +
        "cacute Ccaron ccaron dcroat").Split(' ');

    public static int Count => Names.Length;

    public static string? Get(int index) => index >= 0 && index < Names.Length ? Names[index] : null;

    public static int IndexOf(string name) => Array.IndexOf(Names, name);
}
