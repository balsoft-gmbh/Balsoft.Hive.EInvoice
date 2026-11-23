using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfSharp.Pdf;

namespace Balsoft.Hive.EInvoice.Pdf;

/// <summary>
/// Reduces TrueType font programs to the glyphs a document shows. Glyph ids are kept: the
/// outlines of unused glyphs are emptied (fontTools calls this <c>--retain-gids</c>), so
/// character codes, CIDs, widths and every content stream stay valid without being touched.
/// Composite glyphs keep their components, the hinting tables stay, and only the tables a PDF
/// renderer reads from an embedded program are written (ISO 32000-1, 9.9).
/// </summary>
public static class FontSubsetter
{
    private static readonly string[] Kept = { "OS/2", "cmap", "cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "post", "prep" };

    /// <summary>
    /// The program with every glyph not in <paramref name="glyphIds"/> emptied (glyph 0 and
    /// composite components always stay), or null when it is no subsettable TrueType program
    /// (CFF outlines, a font collection, a damaged table directory).
    /// </summary>
    public static byte[]? Subset(byte[] trueTypeProgram, IEnumerable<int> glyphIds)
    {
        if (trueTypeProgram is null) throw new ArgumentNullException(nameof(trueTypeProgram));
        if (glyphIds is null) throw new ArgumentNullException(nameof(glyphIds));
        return TrueTypeFont.TryParse(trueTypeProgram, out var font) ? Subset(font, glyphIds, keepPost: true) : null;
    }

    /// <summary>
    /// Replaces every TrueType font program embedded in full (no subset tag in its name) by a
    /// subset of the glyphs the document shows with it, and tags the font names. Fonts the
    /// document may need more glyphs from later (form field defaults) or uses where the glyphs
    /// cannot be read are left as they are. Returns how many programs were replaced.
    /// </summary>
    public static int SubsetEmbeddedFonts(PdfDocument document, Action<string>? log = null)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var usage = GlyphUsage.Collect(document);
        int replaced = 0;
        foreach (var descriptor in usage.Descriptors())
        {
            if (descriptor.Elements.GetReference("/FontFile2")?.Value is not PdfDictionary file || file.Stream is null) continue;
            string name = descriptor.Elements.GetName("/FontName").TrimStart('/');
            if (HasSubsetTag(name)) continue;
            byte[] program;
            try { program = file.Stream.UnfilteredValue; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException) { continue; }
            if (!TrueTypeFont.TryParse(program, out var font)) continue;
            if (usage.Glyphs(descriptor, font) is not { } glyphs) continue;
            if (Subset(font, glyphs, keepPost: !usage.IsCidFont(descriptor)) is not { } subset || subset.Length >= program.Length) continue;

            file.Stream.Value = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(subset);
            file.Elements["/Filter"] = new PdfName("/FlateDecode");
            file.Elements.Remove("/DecodeParms");
            file.Elements["/Length1"] = new PdfInteger(subset.Length);
            usage.Tag(descriptor, Tag(glyphs, name));
            log?.Invoke($"Font '{name}' reduced to the {glyphs.Count} glyphs used ({program.Length:N0} to {subset.Length:N0} bytes).");
            replaced++;
        }
        return replaced;
    }

    internal static byte[]? Subset(TrueTypeFont font, IEnumerable<int> glyphIds, bool keepPost)
    {
        int count = font.GlyphCount;
        var keep = new HashSet<int> { 0 };
        foreach (int g in glyphIds)
            if (g >= 0 && g < count) keep.Add(g);
        var pending = new Stack<int>(keep);
        while (pending.Count > 0)
        {
            foreach (int component in font.Components(pending.Pop()))
                if (component < count && keep.Add(component)) pending.Push(component);
        }

        byte[] glyf = font.Table("glyf");
        using var newGlyf = new MemoryStream();
        var loca = new byte[4 * (count + 1)];
        for (int g = 0; g < count; g++)
        {
            Put32(loca, 4 * g, (uint)newGlyf.Length);
            if (!keep.Contains(g)) continue;
            int start = font.Loca[g], end = font.Loca[g + 1];
            newGlyf.Write(glyf, start, end - start);
            while (newGlyf.Length % 4 != 0) newGlyf.WriteByte(0);
        }
        Put32(loca, 4 * count, (uint)newGlyf.Length);

        // Widths of unused glyphs become zero (they compress away). The last long metric stays:
        // its advance applies to every glyph after it.
        byte[] hmtx = (byte[])font.Table("hmtx").Clone();
        int metrics = font.HorizontalMetricCount;
        for (int g = 0; g < count; g++)
        {
            if (keep.Contains(g)) continue;
            if (g < metrics)
            {
                if (g == metrics - 1 || 4 * g + 4 > hmtx.Length) continue;
                hmtx[4 * g] = hmtx[4 * g + 1] = hmtx[4 * g + 2] = hmtx[4 * g + 3] = 0;
            }
            else
            {
                int at = 4 * metrics + 2 * (g - metrics);
                if (at + 2 > hmtx.Length) break;
                hmtx[at] = hmtx[at + 1] = 0;
            }
        }

        byte[] head = (byte[])font.Table("head").Clone();
        head[8] = head[9] = head[10] = head[11] = 0;   // checkSumAdjustment, set below
        head[50] = 0; head[51] = 1;                     // indexToLocFormat: long offsets

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string tag in Kept)
        {
            if (!font.Has(tag) || (tag == "post" && !keepPost)) continue;
            tables[tag] = tag switch
            {
                "glyf" => newGlyf.ToArray(),
                "loca" => loca,
                "hmtx" => hmtx,
                "head" => head,
                _ => font.Table(tag),
            };
        }
        byte[] result = Write(tables);
        int headAt = HeadOffset(result);
        Put32(result, headAt + 8, unchecked(0xB1B0AFBA - Checksum(result, 0, result.Length)));
        return result;
    }

    /// <summary>Six capital letters from the glyph set and the name, the same for the same input (ISO 32000-1, 9.6.4).</summary>
    internal static string Tag(IEnumerable<int> glyphIds, string name)
    {
        string key = name + "|" + string.Join(",", glyphIds.OrderBy(g => g));
        ulong hash = 14695981039346656037;   // FNV-1a: stable across runs and platforms
        foreach (byte b in Encoding.UTF8.GetBytes(key)) hash = unchecked((hash ^ b) * 1099511628211);
        var tag = new char[6];
        for (int i = 0; i < 6; i++, hash /= 26) tag[i] = (char)('A' + (int)(hash % 26));
        return new string(tag);
    }

    internal static bool HasSubsetTag(string name)
        => name.Length > 7 && name[6] == '+' && name.Take(6).All(c => c >= 'A' && c <= 'Z');

    internal static string WithTag(string name, string tag)
    {
        string bare = name.TrimStart('/');
        if (HasSubsetTag(bare)) bare = bare.Substring(7);
        return "/" + tag + "+" + bare;
    }

    private static byte[] Write(SortedDictionary<string, byte[]> tables)
    {
        int n = tables.Count;
        int power = 1, log = 0;
        while (power * 2 <= n) { power *= 2; log++; }
        int offset = 12 + 16 * n;
        int total = offset + tables.Values.Sum(t => (t.Length + 3) & ~3);
        var output = new byte[total];
        Put32(output, 0, 0x00010000);
        Put16(output, 4, n);
        Put16(output, 6, power * 16);
        Put16(output, 8, log);
        Put16(output, 10, n * 16 - power * 16);
        int record = 12;
        foreach (var pair in tables)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(pair.Key);
            Buffer.BlockCopy(tagBytes, 0, output, record, 4);
            Buffer.BlockCopy(pair.Value, 0, output, offset, pair.Value.Length);
            Put32(output, record + 4, Checksum(output, offset, (pair.Value.Length + 3) & ~3));
            Put32(output, record + 8, (uint)offset);
            Put32(output, record + 12, (uint)pair.Value.Length);
            record += 16;
            offset += (pair.Value.Length + 3) & ~3;
        }
        return output;
    }

    private static int HeadOffset(byte[] font)
    {
        int n = (font[4] << 8) | font[5];
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            if (font[r] == 'h' && font[r + 1] == 'e' && font[r + 2] == 'a' && font[r + 3] == 'd')
                return (int)TrueTypeFont.U32(font, r + 8);
        }
        throw new InvalidOperationException("The subset has no head table.");
    }

    private static uint Checksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        for (int i = offset; i < offset + length; i += 4)
        {
            uint word = 0;
            for (int b = 0; b < 4; b++) word = (word << 8) | (i + b < data.Length ? data[i + b] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }

    private static void Put16(byte[] d, int at, int v) { d[at] = (byte)(v >> 8); d[at + 1] = (byte)v; }

    private static void Put32(byte[] d, int at, uint v)
    {
        d[at] = (byte)(v >> 24); d[at + 1] = (byte)(v >> 16); d[at + 2] = (byte)(v >> 8); d[at + 3] = (byte)v;
    }
}

/// <summary>The parts of a TrueType program the subsetter and the glyph mapping read.</summary>
internal sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables;

    private TrueTypeFont(byte[] data, Dictionary<string, (int, int)> tables, int glyphCount, int[] loca, int metrics)
    {
        _data = data;
        _tables = tables;
        GlyphCount = glyphCount;
        Loca = loca;
        HorizontalMetricCount = metrics;
    }

    public int GlyphCount { get; }

    /// <summary>Glyph data offsets into the glyf table, GlyphCount + 1 entries.</summary>
    public int[] Loca { get; }

    public int HorizontalMetricCount { get; }

    public static bool TryParse(byte[] data, out TrueTypeFont font)
    {
        font = null!;
        if (data is null || data.Length < 12) return false;
        uint version = U32(data, 0);
        if (version != 0x00010000 && version != 0x74727565) return false;   // 1.0 or 'true'; not 'OTTO' (CFF), not 'ttcf'
        int n = U16(data, 4);
        if (12 + 16 * n > data.Length) return false;
        var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            string tag = Encoding.ASCII.GetString(data, r, 4);
            long offset = U32(data, r + 8), length = U32(data, r + 12);
            if (offset + length > data.Length) return false;
            tables[tag] = ((int)offset, (int)length);
        }
        foreach (string required in new[] { "head", "hhea", "maxp", "loca", "glyf", "hmtx" })
            if (!tables.ContainsKey(required)) return false;

        var (headAt, headLength) = tables["head"];
        var (maxpAt, maxpLength) = tables["maxp"];
        var (hheaAt, hheaLength) = tables["hhea"];
        if (headLength < 54 || maxpLength < 6 || hheaLength < 36) return false;
        bool longLoca = U16(data, headAt + 50) == 1;
        int count = U16(data, maxpAt + 4);
        int metrics = U16(data, hheaAt + 34);
        var (locaAt, locaLength) = tables["loca"];
        var (_, glyfLength) = tables["glyf"];
        if (locaLength < (longLoca ? 4 : 2) * (count + 1) || metrics == 0 || metrics > count) return false;
        var loca = new int[count + 1];
        for (int g = 0; g <= count; g++)
        {
            long v = longLoca ? U32(data, locaAt + 4 * g) : 2L * U16(data, locaAt + 2 * g);
            if (v > glyfLength || (g > 0 && v < loca[g - 1])) return false;
            loca[g] = (int)v;
        }
        font = new TrueTypeFont(data, tables, count, loca, metrics);
        return true;
    }

    public bool Has(string tag) => _tables.ContainsKey(tag);

    public byte[] Table(string tag)
    {
        var (offset, length) = _tables[tag];
        var copy = new byte[length];
        Buffer.BlockCopy(_data, offset, copy, 0, length);
        return copy;
    }

    /// <summary>The outline bytes of one glyph (empty for a glyph without outline).</summary>
    public byte[] Glyph(int glyph)
    {
        if (glyph < 0 || glyph >= GlyphCount) return Array.Empty<byte>();
        int start = _tables["glyf"].Offset + Loca[glyph], length = Loca[glyph + 1] - Loca[glyph];
        var bytes = new byte[length];
        Buffer.BlockCopy(_data, start, bytes, 0, length);
        return bytes;
    }

    /// <summary>The glyphs a composite glyph is built from (none for a simple glyph).</summary>
    public IEnumerable<int> Components(int glyph)
    {
        if (glyph < 0 || glyph >= GlyphCount) yield break;
        int start = _tables["glyf"].Offset + Loca[glyph], end = _tables["glyf"].Offset + Loca[glyph + 1];
        if (end - start < 10 || (short)U16(_data, start) >= 0) yield break;
        int p = start + 10;
        while (p + 4 <= end)
        {
            int flags = U16(_data, p);
            yield return U16(_data, p + 2);
            p += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
            if ((flags & 0x0080) != 0) p += 8;
            else if ((flags & 0x0040) != 0) p += 4;
            else if ((flags & 0x0008) != 0) p += 2;
            if ((flags & 0x0020) == 0) yield break;
        }
    }

    /// <summary>Character code to glyph id for each cmap subtable the font has, by (platform, encoding).</summary>
    public IReadOnlyList<(int Platform, int Encoding, Dictionary<int, int> Map)> Cmaps()
    {
        var result = new List<(int, int, Dictionary<int, int>)>();
        if (!_tables.TryGetValue("cmap", out var cmap)) return result;
        int at = cmap.Offset, n = U16(_data, at + 2);
        for (int i = 0; i < n && at + 4 + 8 * i + 8 <= _data.Length; i++)
        {
            int platform = U16(_data, at + 4 + 8 * i), encoding = U16(_data, at + 6 + 8 * i);
            long sub = at + U32(_data, at + 8 + 8 * i);
            if (sub + 4 > cmap.Offset + cmap.Length) continue;
            var map = ReadSubtable((int)sub, cmap.Offset + cmap.Length);
            if (map is not null) result.Add((platform, encoding, map));
        }
        return result;
    }

    /// <summary>Glyph names from a format 2 post table, name to glyph id; empty for other formats.</summary>
    public Dictionary<string, int> PostNames()
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!_tables.TryGetValue("post", out var post) || post.Length < 34 || U32(_data, post.Offset) != 0x00020000) return names;
        int count = U16(_data, post.Offset + 32);
        int indexAt = post.Offset + 34, stringsAt = indexAt + 2 * count, end = post.Offset + post.Length;
        if (stringsAt > end) return names;
        var custom = new List<string>();
        for (int p = stringsAt; p < end;)
        {
            int length = _data[p];
            if (p + 1 + length > end) break;
            custom.Add(Encoding.ASCII.GetString(_data, p + 1, length));
            p += 1 + length;
        }
        for (int g = 0; g < count; g++)
        {
            int index = U16(_data, indexAt + 2 * g);
            string? name = index < 258 ? MacGlyphNames.Get(index) : (index - 258 < custom.Count ? custom[index - 258] : null);
            if (name is not null && !names.ContainsKey(name)) names[name] = g;
        }
        return names;
    }

    // A real cmap maps at most every Unicode code point once; a damaged or crafted one with overlapping
    // or huge ranges is not read rather than walked for billions of steps.
    private const int MaxCmapSteps = 0x120000;

    private Dictionary<int, int>? ReadSubtable(int at, int end)
    {
        int format = U16(_data, at);
        var map = new Dictionary<int, int>();
        long steps = 0;
        switch (format)
        {
            case 0:
                if (at + 262 > end) return null;
                for (int c = 0; c < 256; c++) if (_data[at + 6 + c] != 0) map[c] = _data[at + 6 + c];
                return map;
            case 4:
            {
                int segments = U16(_data, at + 6) / 2;
                int ends = at + 14, starts = ends + 2 * segments + 2, deltas = starts + 2 * segments, ranges = deltas + 2 * segments;
                if (ranges + 2 * segments > end) return null;
                for (int s = 0; s < segments; s++)
                {
                    int last = U16(_data, ends + 2 * s), first = U16(_data, starts + 2 * s);
                    int delta = (short)U16(_data, deltas + 2 * s), range = U16(_data, ranges + 2 * s);
                    for (int c = first; c <= last && c != 0xFFFF; c++)
                    {
                        if (++steps > MaxCmapSteps) return null;
                        int g;
                        if (range == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            int p = ranges + 2 * s + range + 2 * (c - first);
                            if (p + 2 > end) break;
                            g = U16(_data, p);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0) map[c] = g;
                    }
                }
                return map;
            }
            case 6:
            {
                int first = U16(_data, at + 6), count = U16(_data, at + 8);
                if (at + 10 + 2 * count > end) return null;
                for (int i = 0; i < count; i++)
                {
                    int g = U16(_data, at + 10 + 2 * i);
                    if (g != 0) map[first + i] = g;
                }
                return map;
            }
            case 12:
            {
                if (at + 16 > end) return null;
                long groups = U32(_data, at + 12);
                if (at + 16 + 12 * groups > end) return null;
                for (long i = 0; i < groups; i++)
                {
                    int p = (int)(at + 16 + 12 * i);
                    long first = U32(_data, p), last = U32(_data, p + 4), glyph = U32(_data, p + 8);
                    if (last < first || (steps += last - first + 1) > MaxCmapSteps) return null;
                    for (long c = first; c <= last; c++) if (glyph + c - first != 0) map[(int)c] = (int)(glyph + c - first);
                }
                return map;
            }
            default:
                return null;
        }
    }

    internal static int U16(byte[] d, int at) => at + 2 <= d.Length ? (d[at] << 8) | d[at + 1] : 0;

    internal static uint U32(byte[] d, int at)
        => at + 4 <= d.Length ? ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3] : 0;
}
