using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Outbridge.PyLite.UnicodeGen
{
    // Generates src/PyLite/Runtime/Values/Text/PyUnicodeCase.Tables.g.cs from the Unicode Character Database.
    // Run by hand when bumping the Unicode version; the OUTPUT is what ships, so the build never needs
    // the UCD files or a network. See README.md for the exact invocation.
    //
    // Why generate at all: .NET Framework's own casing disagrees with Unicode on 427 of the 2373 cased
    // BMP characters (Georgian, Cherokee, the Cyrillic supplement, Latin Ext-D/E), it has no full
    // (length-changing) mappings at all, and it is not pinned to the version CPython uses.
    internal static class Program
    {
        private const int PageSize = 256;
        private const int BmpPages = 256;

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("usage: Outbridge.PyLite.UnicodeGen <ucd-directory> <src/PyLite>");
                Console.Error.WriteLine("  the input directory must hold UnicodeData.txt, SpecialCasing.txt,");
                Console.Error.WriteLine("  CaseFolding.txt, DerivedCoreProperties.txt, NameAliases.txt,");
                Console.Error.WriteLine("  NamedSequences.txt and EastAsianWidth.txt; the three generated files");
                Console.Error.WriteLine("  go to the layers that own them, Runtime/Values and Syntax");
                return 2;
            }
            string ucd = args[0], root = args[1];
            string outPath = Path.Combine(root, "Runtime", "Values", "Text", "PyUnicodeCase.Tables.g.cs");
            var db = new Ucd(ucd);
            string version = db.Version;
            Console.WriteLine("Unicode " + version);

            var sb = new StringBuilder();
            Header(sb, version);
            EmitMap(sb, "Upper", db.SimpleUpper);
            EmitMap(sb, "Lower", db.SimpleLower);
            EmitMap(sb, "Title", db.SimpleTitle);
            EmitMap(sb, "Fold", db.SimpleFold);
            EmitFull(sb, db);
            EmitFlags(sb, db);
            EmitAstral(sb, db);
            EmitXid(sb, "XidStart", db.XidStart);
            EmitXid(sb, "XidContinue", db.XidContinue);
            Footer(sb);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length / 1024 + " KB)");
            NamesGen.Write(ucd, Path.Combine(root, "Syntax", "PyUnicodeNames.Tables.g.cs"));
            PropsGen.Write(ucd, Path.Combine(root, "Runtime", "Values", "Text", "PyUnicodeProps.Tables.g.cs"), version);
            string zones = Path.Combine(ucd, "windowsZones.xml");
            if (File.Exists(zones))
                ZonesGen.Write(zones, Path.Combine(root, "Modules", "Support", "Time", "WindowsZones.g.cs"));
            else
                Console.WriteLine("windowsZones.xml not present: the time zone map is left as is");
            return 0;
        }

        // ---- table emission ----

        // Two levels: a 256-entry page index (0 = the page maps nothing, so the character is its own
        // image) plus the non-identity pages packed into one string, indexed directly as chars.
        private static void EmitMap(StringBuilder sb, string name, IDictionary<int, int> map)
        {
            var pageId = new byte[BmpPages];
            var data = new StringBuilder();
            int next = 1;
            for (int page = 0; page < BmpPages; page++)
            {
                bool any = false;
                for (int k = 0; k < PageSize && !any; k++)
                    any = map.ContainsKey((page << 8) | k);
                if (!any)
                    continue;
                pageId[page] = checked((byte)next++);
                for (int k = 0; k < PageSize; k++)
                {
                    int c = (page << 8) | k;
                    int to;
                    data.Append((char)(map.TryGetValue(c, out to) ? to : c));
                }
            }
            Console.WriteLine("  " + name + ": " + map.Count + " mappings in " + (next - 1) + " pages");
            sb.AppendLine("        private static readonly byte[] " + name + "Page =");
            sb.AppendLine("        {");
            AppendBytes(sb, pageId);
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        private const string " + name + "Data =");
            AppendStringLiteral(sb, data.ToString());
            sb.AppendLine();
        }

        // Per-character flags, same page scheme. Cased / Lowercase / Uppercase come from
        // DerivedCoreProperties (what CPython's makeunicodedata.py uses), NOT from the general category,
        // so islower/isupper/title agree with CPython on the Other_Lowercase characters too.
        private static void EmitFlags(StringBuilder sb, Ucd db)
        {
            var flags = new Dictionary<int, int>();
            void Set(IEnumerable<int> src, int bit)
            {
                foreach (int c in src)
                {
                    if (c > 0xFFFF)
                        continue;
                    int v;
                    flags.TryGetValue(c, out v);
                    flags[c] = v | bit;
                }
            }
            Set(db.Cased, 1);
            Set(db.Uppercase, 2);
            Set(db.Lowercase, 4);
            Set(db.TitlecaseCategory, 8);
            Set(db.CaseIgnorable, 16);
            // bit 5 rides along so "does this character have a length-changing mapping" is answered by
            // the lookup the walker already does, instead of by a second table
            Set(db.FullUpper.Keys, 32);
            Set(db.FullLower.Keys, 32);
            Set(db.FullTitle.Keys, 32);
            Set(db.FullFold.Keys, 32);

            var pageId = new byte[BmpPages];
            var data = new StringBuilder();
            int next = 1;
            for (int page = 0; page < BmpPages; page++)
            {
                bool any = false;
                for (int k = 0; k < PageSize && !any; k++)
                    any = flags.ContainsKey((page << 8) | k);
                if (!any)
                    continue;
                pageId[page] = checked((byte)next++);
                for (int k = 0; k < PageSize; k++)
                {
                    int v;
                    flags.TryGetValue((page << 8) | k, out v);
                    data.Append((char)v);
                }
            }
            Console.WriteLine("  Flags: " + flags.Count + " chars in " + (next - 1) + " pages");
            sb.AppendLine("        private static readonly byte[] FlagPage =");
            sb.AppendLine("        {");
            AppendBytes(sb, pageId);
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        private const string FlagData =");
            AppendStringLiteral(sb, data.ToString());
            sb.AppendLine();
        }

        // The length-changing mappings: SpecialCasing's unconditional rows plus CaseFolding's F rows.
        // Sorted by key, and the flag table's bit 5 says whether a character is in here at all.
        private static void EmitFull(StringBuilder sb, Ucd db)
        {
            var keys = new SortedSet<int>();
            foreach (int k in db.FullUpper.Keys) keys.Add(k);
            foreach (int k in db.FullLower.Keys) keys.Add(k);
            foreach (int k in db.FullTitle.Keys) keys.Add(k);
            foreach (int k in db.FullFold.Keys) keys.Add(k);
            int[] ks = keys.Where(k => k <= 0xFFFF).ToArray();
            Console.WriteLine("  Full: " + ks.Length + " characters");

            sb.AppendLine("        private const string FullKeys =");
            AppendStringLiteral(sb, new string(ks.Select(k => (char)k).ToArray()));
            sb.AppendLine();
            EmitFullValues(sb, "FullUpperV", ks, db.FullUpper, db.SimpleUpper);
            EmitFullValues(sb, "FullLowerV", ks, db.FullLower, db.SimpleLower);
            EmitFullValues(sb, "FullTitleV", ks, db.FullTitle, db.SimpleTitle);
            EmitFullValues(sb, "FullFoldV", ks, db.FullFold, db.SimpleFold);
        }

        // A character may have a full mapping in one direction only (0130 uppercases to itself), so the
        // slot falls back to the simple mapping rather than to identity.
        private static void EmitFullValues(StringBuilder sb, string name, int[] keys,
            IDictionary<int, string> full, IDictionary<int, int> simple)
        {
            sb.AppendLine("        private static readonly string[] " + name + " =");
            sb.AppendLine("        {");
            var line = new StringBuilder("            ");
            foreach (int k in keys)
            {
                string v;
                if (!full.TryGetValue(k, out v))
                {
                    int s;
                    v = simple.TryGetValue(k, out s) ? char.ConvertFromUtf32(s) : char.ConvertFromUtf32(k);
                }
                string entry = Quote(v) + ", ";
                if (line.Length + entry.Length > 116)
                {
                    sb.AppendLine(line.ToString().TrimEnd());
                    line = new StringBuilder("            ");
                }
                line.Append(entry);
            }
            sb.AppendLine(line.ToString().TrimEnd());
            sb.AppendLine("        };");
            sb.AppendLine();
        }

        // Case pairs above the BMP (Deseret, Adlam, Vithkuqi, ...). Rare enough for a binary search over
        // a sorted key array, reached only when the walker meets a surrogate pair.
        private static void EmitAstral(StringBuilder sb, Ucd db)
        {
            var keys = new SortedSet<int>();
            foreach (var m in new[] { db.SimpleUpper, db.SimpleLower, db.SimpleTitle, db.SimpleFold })
                foreach (int k in m.Keys)
                    if (k > 0xFFFF)
                        keys.Add(k);
            int[] ks = keys.ToArray();
            Console.WriteLine("  Astral: " + ks.Length + " characters");
            EmitIntArray(sb, "AstralKeys", ks);
            EmitIntArray(sb, "AstralUpper", ks.Select(k => Get(db.SimpleUpper, k)).ToArray());
            EmitIntArray(sb, "AstralLower", ks.Select(k => Get(db.SimpleLower, k)).ToArray());
            EmitIntArray(sb, "AstralTitle", ks.Select(k => Get(db.SimpleTitle, k)).ToArray());
            EmitIntArray(sb, "AstralFold", ks.Select(k => Get(db.SimpleFold, k)).ToArray());
        }

        // The identifier properties are dense across whole scripts, so the BMP half becomes a flat bitmap
        // (16 bits per emitted char, O(1) lookup) and the astral half a sorted lo/hi range array.
        private static void EmitXid(StringBuilder sb, string name, List<int[]> ranges)
        {
            var bits = new int[0x10000 / 16];
            var astral = new List<int>();
            foreach (int[] r in ranges.OrderBy(r => r[0]))
            {
                for (int c = r[0]; c <= r[1] && c <= 0xFFFF; c++)
                    bits[c >> 4] |= 1 << (c & 15);
                if (r[1] > 0xFFFF)
                {
                    astral.Add(Math.Max(r[0], 0x10000));
                    astral.Add(r[1]);
                }
            }
            Console.WriteLine("  " + name + ": " + ranges.Count + " ranges, " + (astral.Count / 2) + " astral");
            sb.AppendLine("        private const string " + name + "Bits =");
            AppendStringLiteral(sb, new string(bits.Select(b => (char)b).ToArray()));
            sb.AppendLine();
            EmitIntArray(sb, name + "Astral", astral.ToArray());
        }

        private static int Get(IDictionary<int, int> map, int k)
        {
            int v;
            return map.TryGetValue(k, out v) ? v : k;
        }

        internal static void EmitIntArray(StringBuilder sb, string name, int[] values)
        {
            sb.AppendLine("        private static readonly int[] " + name + " =");
            sb.AppendLine("        {");
            var line = new StringBuilder("            ");
            foreach (int v in values)
            {
                string entry = "0x" + v.ToString("X") + ", ";
                if (line.Length + entry.Length > 116)
                {
                    sb.AppendLine(line.ToString().TrimEnd());
                    line = new StringBuilder("            ");
                }
                line.Append(entry);
            }
            sb.AppendLine(line.ToString().TrimEnd());
            sb.AppendLine("        };");
            sb.AppendLine();
        }

        private static void AppendBytes(StringBuilder sb, byte[] values)
        {
            var line = new StringBuilder("            ");
            foreach (byte v in values)
            {
                string entry = v + ", ";
                if (line.Length + entry.Length > 116)
                {
                    sb.AppendLine(line.ToString().TrimEnd());
                    line = new StringBuilder("            ");
                }
                line.Append(entry);
            }
            sb.AppendLine(line.ToString().TrimEnd());
        }

        // Every character escaped, so the generated file stays pure ASCII whatever the editor does.
        internal static void AppendStringLiteral(StringBuilder sb, string s)
        {
            const int PerLine = 16;
            for (int i = 0; i < s.Length; i += PerLine)
            {
                var line = new StringBuilder("            \"");
                for (int k = i; k < s.Length && k < i + PerLine; k++)
                    line.Append("\\u").Append(((int)s[k]).ToString("X4"));
                line.Append('"');
                line.Append(i + PerLine >= s.Length ? ";" : " +");
                sb.AppendLine(line.ToString());
            }
        }

        internal static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
                sb.Append("\\u").Append(((int)c).ToString("X4"));
            return sb.Append('"').ToString();
        }

        private static void Header(StringBuilder sb, string version)
        {
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     Generated by tools/Outbridge.PyLite.UnicodeGen from the Unicode Character Database " + version + ".");
            sb.AppendLine("//     Do not edit by hand: re-run the generator instead (see its README).");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace Outbridge.PyLite.Runtime.Values");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class PyUnicodeCase");
            sb.AppendLine("    {");
            sb.AppendLine("        internal const string UnicodeVersion = \"" + version + "\";");
            sb.AppendLine();
        }

        private static void Footer(StringBuilder sb)
        {
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
    }

    // ---- the UCD files ----

    internal sealed class Ucd
    {
        internal readonly Dictionary<int, int> SimpleUpper = new Dictionary<int, int>();
        internal readonly Dictionary<int, int> SimpleLower = new Dictionary<int, int>();
        internal readonly Dictionary<int, int> SimpleTitle = new Dictionary<int, int>();
        internal readonly Dictionary<int, int> SimpleFold = new Dictionary<int, int>();
        internal readonly Dictionary<int, string> FullUpper = new Dictionary<int, string>();
        internal readonly Dictionary<int, string> FullLower = new Dictionary<int, string>();
        internal readonly Dictionary<int, string> FullTitle = new Dictionary<int, string>();
        internal readonly Dictionary<int, string> FullFold = new Dictionary<int, string>();
        internal readonly List<int> Cased = new List<int>();
        internal readonly List<int> Uppercase = new List<int>();
        internal readonly List<int> Lowercase = new List<int>();
        internal readonly List<int> CaseIgnorable = new List<int>();
        internal readonly List<int> TitlecaseCategory = new List<int>();
        internal readonly List<int[]> XidStart = new List<int[]>();
        internal readonly List<int[]> XidContinue = new List<int[]>();
        internal readonly string Version;

        internal Ucd(string dir)
        {
            Version = ReadVersion(Path.Combine(dir, "CaseFolding.txt"));
            ReadUnicodeData(Path.Combine(dir, "UnicodeData.txt"));
            ReadSpecialCasing(Path.Combine(dir, "SpecialCasing.txt"));
            ReadCaseFolding(Path.Combine(dir, "CaseFolding.txt"));
            ReadDerived(Path.Combine(dir, "DerivedCoreProperties.txt"));
        }

        private static string ReadVersion(string path)
        {
            string first = File.ReadLines(path).First();
            int dash = first.LastIndexOf('-');
            int dot = first.LastIndexOf(".txt", StringComparison.Ordinal);
            return dash > 0 && dot > dash ? first.Substring(dash + 1, dot - dash - 1) : "unknown";
        }

        private static int Hex(string s)
        {
            return int.Parse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private void ReadUnicodeData(string path)
        {
            foreach (string raw in File.ReadLines(path))
            {
                string[] f = raw.Split(';');
                if (f.Length < 15)
                    continue;
                int c = Hex(f[0]);
                if (f[12].Length > 0)
                    SimpleUpper[c] = Hex(f[12]);
                if (f[13].Length > 0)
                    SimpleLower[c] = Hex(f[13]);
                if (f[14].Length > 0)
                    SimpleTitle[c] = Hex(f[14]);
                if (f[2] == "Lt")
                    TitlecaseCategory.Add(c);
            }
        }

        // Only the unconditional rows are taken. Final_Sigma is applied by the runtime as a context rule;
        // the lt / tr / az rows are locale-dependent and CPython does not apply them either, so skipping
        // them IS the parity behaviour.
        private void ReadSpecialCasing(string path)
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = Strip(raw);
                if (line.Length == 0)
                    continue;
                string[] f = line.Split(';');
                if (f.Length < 4)
                    continue;
                bool conditional = f.Length > 4 && f[4].Trim().Length > 0;
                if (conditional)
                    continue;
                int c = Hex(f[0]);
                Put(FullLower, c, f[1]);
                Put(FullTitle, c, f[2]);
                Put(FullUpper, c, f[3]);
            }
        }

        private void Put(IDictionary<int, string> map, int c, string codes)
        {
            string[] parts = codes.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (string p in parts)
                sb.Append(char.ConvertFromUtf32(Hex(p)));
            string v = sb.ToString();
            if (v.Length != 1 || v[0] != c)     // an identity row carries no information
                map[c] = v;
        }

        // C = the common single-character fold, F = the full one that expands. T (Turkic) is skipped for
        // the same reason as SpecialCasing's tr rows; S is the simple subset of F and would contradict it.
        private void ReadCaseFolding(string path)
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = Strip(raw);
                if (line.Length == 0)
                    continue;
                string[] f = line.Split(';');
                if (f.Length < 3)
                    continue;
                int c = Hex(f[0]);
                string status = f[1].Trim();
                if (status == "C")
                    SimpleFold[c] = Hex(f[2]);
                else if (status == "F")
                    Put(FullFold, c, f[2]);
            }
        }

        private void ReadDerived(string path)
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = Strip(raw);
                if (line.Length == 0)
                    continue;
                string[] f = line.Split(';');
                if (f.Length < 2)
                    continue;
                string prop = f[1].Trim();
                string range = f[0].Trim();
                int dots = range.IndexOf("..", StringComparison.Ordinal);
                int lo = Hex(dots < 0 ? range : range.Substring(0, dots));
                int hi = dots < 0 ? lo : Hex(range.Substring(dots + 2));

                // XID_Start / XID_Continue cover most letters in every script, so they are kept as
                // ranges (a BMP bitmap plus astral ranges) rather than expanded per character.
                if (prop == "XID_Start")
                {
                    XidStart.Add(new[] { lo, hi });
                    continue;
                }
                if (prop == "XID_Continue")
                {
                    XidContinue.Add(new[] { lo, hi });
                    continue;
                }

                List<int> target;
                switch (prop)
                {
                    case "Cased": target = Cased; break;
                    case "Uppercase": target = Uppercase; break;
                    case "Lowercase": target = Lowercase; break;
                    case "Case_Ignorable": target = CaseIgnorable; break;
                    default: continue;
                }
                for (int c = lo; c <= hi && c <= 0xFFFF; c++)
                    target.Add(c);
            }
        }

        private static string Strip(string raw)
        {
            int hash = raw.IndexOf('#');
            return (hash < 0 ? raw : raw.Substring(0, hash)).Trim();
        }
    }
}
