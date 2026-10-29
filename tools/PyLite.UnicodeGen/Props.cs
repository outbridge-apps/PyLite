using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Outbridge.PyLite.UnicodeGen
{
    // Generates Runtime/Values/PyUnicodeProps.Tables.g.cs: the East Asian Width and canonical combining
    // class tables behind unicodedata.east_asian_width/combining. Width is run-length coded over the
    // whole code space (default N); combining classes are the sparse non-zero runs (default 0).
    internal static class PropsGen
    {
        // EastAsianWidth.txt codes in the order the value table uses; PyUnicodeProps.WidthCodes matches.
        private static readonly string[] Widths = { "N", "A", "H", "W", "F", "Na" };

        internal static void Write(string ucdDir, string outPath, string version)
        {
            var eaw = new byte[0x110000];
            foreach (string raw in File.ReadLines(Path.Combine(ucdDir, "EastAsianWidth.txt")))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                string[] parts = line.Split(';');
                int lo, hi;
                ParseRange(parts[0].Trim(), out lo, out hi);
                int v = Array.IndexOf(Widths, parts[1].Trim());
                if (v < 0)
                    throw new InvalidDataException("unknown East Asian Width code: " + parts[1]);
                for (int c = lo; c <= hi; c++)
                    eaw[c] = (byte)v;
            }
            var eawStart = new List<int>();
            var eawValue = new List<int>();
            for (int c = 0; c < eaw.Length; c++)
            {
                if (c == 0 || eaw[c] != eaw[c - 1])
                {
                    eawStart.Add(c);
                    eawValue.Add(eaw[c]);
                }
            }

            var cccStart = new List<int>();
            var cccEnd = new List<int>();
            var cccValue = new List<int>();
            foreach (string line in File.ReadLines(Path.Combine(ucdDir, "UnicodeData.txt")))
            {
                string[] f = line.Split(';');
                if (f.Length < 4)
                    continue;
                int ccc = int.Parse(f[3], CultureInfo.InvariantCulture);
                if (ccc == 0)
                    continue;   // the First>/Last> range rows are all class 0, so no range handling is needed
                int cp = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int last = cccStart.Count - 1;
                if (last >= 0 && cccEnd[last] == cp - 1 && cccValue[last] == ccc)
                    cccEnd[last] = cp;
                else
                {
                    cccStart.Add(cp);
                    cccEnd.Add(cp);
                    cccValue.Add(ccc);
                }
            }
            // bidi class (field 4) and mirrored (field 9) over every assigned code point, the First>/Last>
            // range rows expanded; an unassigned code point has bidi class "" (index 0), as in CPython.
            var bidiCodes = new List<string> { "" };
            var decompCp = new List<int>();
            var decompOffset = new List<int>();
            var decompText = new StringBuilder();   // field 5 verbatim ("<fraction> 0031 2044 0032"), concatenated
            var bidi = new byte[0x110000];
            var mirrored = new bool[0x110000];
            int rangeStart = -1;
            foreach (string line in File.ReadLines(Path.Combine(ucdDir, "UnicodeData.txt")))
            {
                string[] f = line.Split(';');
                if (f.Length < 10)
                    continue;
                int cp = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int code = bidiCodes.IndexOf(f[4]);
                if (code < 0)
                {
                    code = bidiCodes.Count;
                    bidiCodes.Add(f[4]);
                }
                bool mir = f[9] == "Y";
                int from = cp;
                if (f[1].EndsWith(", First>", StringComparison.Ordinal))
                {
                    rangeStart = cp;
                    continue;
                }
                if (f[1].EndsWith(", Last>", StringComparison.Ordinal))
                    from = rangeStart;
                else if (f[5].Length > 0)
                {
                    decompCp.Add(cp);   // rows come in ascending order, so this stays sorted
                    decompOffset.Add(decompText.Length);
                    decompText.Append(f[5]);
                }
                for (int c = from; c <= cp; c++)
                {
                    bidi[c] = (byte)code;
                    mirrored[c] = mir;
                }
            }
            var bidiStart = new List<int>();
            var bidiValue = new List<int>();
            var mirStart = new List<int>();
            var mirEnd = new List<int>();
            for (int c = 0; c < 0x110000; c++)
            {
                if (c == 0 || bidi[c] != bidi[c - 1])
                {
                    bidiStart.Add(c);
                    bidiValue.Add(bidi[c]);
                }
                if (mirrored[c])
                {
                    if (mirEnd.Count > 0 && mirEnd[mirEnd.Count - 1] == c - 1)
                        mirEnd[mirEnd.Count - 1] = c;
                    else
                    {
                        mirStart.Add(c);
                        mirEnd.Add(c);
                    }
                }
            }
            Console.WriteLine("  Width: " + eawStart.Count + " runs; combining: " + cccStart.Count + " runs; bidi: "
                + bidiStart.Count + " runs over " + bidiCodes.Count + " classes; mirrored: " + mirStart.Count + " runs");

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     Generated by tools/Outbridge.PyLite.UnicodeGen from the Unicode Character Database " + version + ".");
            sb.AppendLine("//     Do not edit by hand: re-run the generator instead (see its README).");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace Outbridge.PyLite.Runtime.Values");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class PyUnicodeProps");
            sb.AppendLine("    {");
            sb.AppendLine("        internal const string UnicodeVersion = \"" + version + "\";");
            sb.AppendLine();
            sb.AppendLine("        // East Asian Width as runs: EawStart[i] opens a run whose code is WidthCodes[EawValue[i]].");
            Program.EmitIntArray(sb, "EawStart", eawStart.ToArray());
            Program.EmitIntArray(sb, "EawValue", eawValue.ToArray());
            sb.AppendLine("        // Canonical combining class: the non-zero runs [CccStart[i], CccEnd[i]] with class CccValue[i].");
            Program.EmitIntArray(sb, "CccStart", cccStart.ToArray());
            Program.EmitIntArray(sb, "CccEnd", cccEnd.ToArray());
            Program.EmitIntArray(sb, "CccValue", cccValue.ToArray());
            sb.AppendLine("        // Bidi class as runs: BidiStart[i] opens a run of class BidiCodes[BidiValue[i]] (\"\" = unassigned).");
            sb.AppendLine("        private static readonly string[] BidiCodes =");
            sb.AppendLine("        {");
            var codesLine = new StringBuilder("            ");
            foreach (string code in bidiCodes)
                codesLine.Append(Program.Quote(code)).Append(", ");
            sb.AppendLine(codesLine.ToString().TrimEnd());
            sb.AppendLine("        };");
            sb.AppendLine();
            Program.EmitIntArray(sb, "BidiStart", bidiStart.ToArray());
            Program.EmitIntArray(sb, "BidiValue", bidiValue.ToArray());
            sb.AppendLine("        // Bidi_Mirrored=Y as the closed ranges [MirrorStart[i], MirrorEnd[i]].");
            Program.EmitIntArray(sb, "MirrorStart", mirStart.ToArray());
            Program.EmitIntArray(sb, "MirrorEnd", mirEnd.ToArray());
            decompOffset.Add(decompText.Length);
            Console.WriteLine("  Decomposition: " + decompCp.Count + " entries, " + decompText.Length + " chars");
            sb.AppendLine("        // Decomposition mappings: DecompCp[i] maps to DecompText[DecompOffset[i] .. DecompOffset[i + 1]).");
            Program.EmitIntArray(sb, "DecompCp", decompCp.ToArray());
            Program.EmitIntArray(sb, "DecompOffset", decompOffset.ToArray());
            sb.AppendLine("        private const string DecompText =");
            string text = decompText.ToString();   // pure ASCII (hex digits, spaces, <tags>), safe to emit verbatim
            for (int i = 0; i < text.Length; i += 100)
            {
                int n = Math.Min(100, text.Length - i);
                sb.AppendLine("            \"" + text.Substring(i, n) + "\"" + (i + n >= text.Length ? ";" : " +"));
            }
            sb.AppendLine();
            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length / 1024 + " KB)");
        }

        private static void ParseRange(string s, out int lo, out int hi)
        {
            int dots = s.IndexOf("..", StringComparison.Ordinal);
            if (dots < 0)
            {
                lo = hi = int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return;
            }
            lo = int.Parse(s.Substring(0, dots), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            hi = int.Parse(s.Substring(dots + 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
