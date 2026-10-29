using System.Collections.Generic;

namespace Outbridge.PyLite.PerfBench
{
    internal static partial class Program
    {
        // ---------- the bench corpus ----------

        private sealed class Bench
        {
            public string Name;
            public string Kind;   // micro | macro
            public string Source;
        }

        private static IEnumerable<Bench> Benches(int scale)
        {
            // MICRO (impl/12 T-TST-18 list; sizes / scale). All scripts are 3.4-syntax so IronPython runs them.
            yield return new Bench
            {
                Name = "micro_arith_loop",
                Kind = "micro",
                Source = "s = 0\nfor i in range(" + 1000000 / scale + "):\n    s = s + i*2 - 1\nresult = s\n",
            };
            yield return new Bench
            {
                Name = "micro_func_calls",
                Kind = "micro",
                Source = "def f(x):\n    return x + 1\ns = 0\nfor i in range(" + 500000 / scale + "):\n    s = f(i)\nresult = s\n",
            };
            yield return new Bench
            {
                Name = "micro_dict_ops",
                Kind = "micro",
                Source = "d = {}\nfor i in range(" + 200000 / scale + "):\n    d[i] = i*i\nresult = sum(d[k] for k in d)\n",
            };
            yield return new Bench
            {
                Name = "micro_str_join",
                Kind = "micro",
                Source = "parts = []\nfor i in range(" + 100000 / scale + "):\n    parts.append(str(i))\nresult = len(''.join(parts))\n",
            };
            yield return new Bench
            {
                Name = "micro_list_comp",
                Kind = "micro",
                Source = "r = [i*i for i in range(" + 500000 / scale + ") if i % 2 == 0]\nresult = len(r)\n",
            };
            yield return new Bench
            {
                Name = "micro_sorted_key",
                Kind = "micro",
                Source = "r = sorted(range(" + 100000 / scale + "), key=lambda x: -x)\nresult = r[0]\n",
            };
            // Money-shaped decimal work: a division that must round to the context precision, a product,
            // a quantize to cents and a running sum - the four operations an integration script lives on.
            yield return new Bench
            {
                Name = "micro_decimal_money",
                Kind = "micro",
                Source =
                    "from decimal import Decimal, ROUND_HALF_UP\n" +
                    "cent = Decimal('0.01')\n" +
                    "rate = Decimal('1.2')\n" +
                    "seven = Decimal(7)\n" +
                    "total = Decimal(0)\n" +
                    "for i in range(" + 5000 / scale + "):\n" +
                    "    price = Decimal(i % 1000) / seven\n" +
                    "    total = total + (price * rate).quantize(cent, rounding=ROUND_HALF_UP)\n" +
                    "result = int(total)\n",
            };

            // MACRO — corpus-shaped end-to-end scripts (json/text/regex/datetime/csv).
            yield return new Bench
            {
                Name = "macro_json_orders",
                Kind = "macro",
                Source =
                    "import json\n" +
                    "data = json.dumps([{\"id\": i, \"qty\": i % 7, \"price\": (i % 13) * 2.5} for i in range(" + 10000 / scale + ")])\n" +
                    "orders = json.loads(data)\n" +
                    "total = 0.0\n" +
                    "for o in orders:\n" +
                    "    if o[\"qty\"] > 2:\n" +
                    "        total += o[\"qty\"] * o[\"price\"]\n" +
                    "result = round(total, 2)\n",
            };
            yield return new Bench
            {
                Name = "macro_text_report",
                Kind = "macro",
                Source =
                    "lines = []\n" +
                    "for i in range(" + 15000 / scale + "):\n" +
                    "    lines.append(\"user%d|%d|%s\" % (i % 50, i, \"ok\" if i % 3 else \"err\"))\n" +
                    "counts = {}\n" +
                    "total = 0\n" +
                    "for line in lines:\n" +
                    "    parts = line.split(\"|\")\n" +
                    "    if parts[2] == \"err\":\n" +
                    "        counts[parts[0]] = counts.get(parts[0], 0) + 1\n" +
                    "    total += int(parts[1])\n" +
                    "result = \"%d:%d\" % (total, len(counts))\n",
            };
            yield return new Bench
            {
                Name = "macro_regex_log",
                Kind = "macro",
                Source =
                    "import re\n" +
                    "lines = []\n" +
                    "for i in range(" + 10000 / scale + "):\n" +
                    "    lines.append(\"2026-07-%02d level=%s code=%d\" % (i % 28 + 1, \"ERROR\" if i % 4 == 0 else \"INFO\", i % 9))\n" +
                    "text = \"\\n\".join(lines)\n" +
                    "pat = re.compile(r\"level=ERROR code=(\\d)\")\n" +
                    "counts = {}\n" +
                    "for m in pat.finditer(text):\n" +
                    "    c = m.group(1)\n" +
                    "    counts[c] = counts.get(c, 0) + 1\n" +
                    "result = sum(counts.values())\n",
            };
            yield return new Bench
            {
                Name = "macro_datetime_calc",
                Kind = "macro",
                Source =
                    "import datetime\n" +
                    "base = datetime.datetime(2026, 1, 1)\n" +
                    "total = 0\n" +
                    "for i in range(" + 25000 / scale + "):\n" +
                    "    d = base + datetime.timedelta(days=i % 365, hours=i % 24)\n" +
                    "    if d.weekday() < 5:\n" +
                    "        total += d.day\n" +
                    "result = total\n",
            };
            yield return new Bench
            {
                Name = "macro_csv_roundtrip",
                Kind = "macro",
                Source =
                    "import csv\n" +
                    "from io import StringIO\n" +
                    "buf = StringIO()\n" +
                    "w = csv.writer(buf)\n" +
                    "for i in range(" + 7500 / scale + "):\n" +
                    "    w.writerow([i, \"name%d\" % i, i * 1.5])\n" +
                    "data = buf.getvalue()\n" +
                    "total = 0.0\n" +
                    "for row in csv.reader(StringIO(data)):\n" +
                    "    total += float(row[2])\n" +
                    "result = int(total)\n",
            };
        }
    }
}
