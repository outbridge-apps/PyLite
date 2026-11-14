using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Spec
{
    // The CPython oracle as a gate (review 3, T3). Oracle/*.py are scripts whose *.out is what CPython 3.14 printed
    // for them (checked against 3.14.2 when added; Oracle/README.md says how to add one). Each runs as a host
    // runs it, then under a hostile caller culture, sixteen at a time, and in an ASP.NET host's .NET 4.5 mode.
    [Trait("Category", "Compatibility")]
    public class OracleCorpusTests
    {
        private static string Root()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oracle");
        }

        public static IEnumerable<object[]> Scripts()
        {
            return Directory.GetFiles(Root(), "*.py").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });
        }

        private static string Source(string name)
        {
            return File.ReadAllText(Path.Combine(Root(), name + ".py"));
        }

        private static string Expected(string name)
        {
            return File.ReadAllText(Path.Combine(Root(), name + ".out")).Replace("\r\n", "\n");
        }

        internal static string RunScript(ScriptEngine engine, string source)
        {
            RunResult r = engine.Run(source, new RunRequest { Limits = TestEngines.Unhurried });
            return r.Succeeded ? r.PrintOutput : r.PrintOutput + "ERROR: " + r.Error.PythonType + ": " + r.Error.Message + "\n";
        }

        // The first differing line, so a failure reads as a diff and not as two 25 KB strings.
        private static void AssertSameOutput(string name, string expected, string actual)
        {
            if (expected == actual)
                return;
            string[] e = expected.Split('\n'), a = actual.Split('\n');
            int i = 0;
            while (i < e.Length && i < a.Length && e[i] == a[i])
                i++;
            Assert.True(false, name + " line " + (i + 1) + ":\n  CPython " + (i < e.Length ? e[i] : "<end>")
                + "\n  PyLite  " + (i < a.Length ? a[i] : "<end>"));
        }

        [Theory]
        [MemberData(nameof(Scripts))]
        public void PrintsWhatCPythonPrints(string name)
        {
            AssertSameOutput(name, Expected(name), RunScript(TestEngines.WithCeiling(TestEngines.Unhurried), Source(name)));
        }

        // A culture whose minus is U+2212, whose decimal point is a comma and whose texts would be Russian: the
        // caller's culture reaches neither the run nor the compile.
        [Theory]
        [MemberData(nameof(Scripts))]
        public void TheCallersCultureChangesNothing(string name)
        {
            var odd = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
            odd.NumberFormat.NegativeSign = "−";
            odd.NumberFormat.NumberDecimalSeparator = ",";
            string output = null;
            Exception failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = odd;
                    Thread.CurrentThread.CurrentUICulture = odd;
                    output = RunScript(TestEngines.WithCeiling(TestEngines.Unhurried), Source(name));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            t.Start();
            Assert.True(t.Join(180000));
            Assert.Null(failure);
            AssertSameOutput(name, Expected(name), output);
        }

        // Every script sixteen times at once on one engine prints what one run prints: no run reaches another.
        [Fact]
        public void ParallelRunsPrintWhatOneRunPrints()
        {
            string[] names = Scripts().Select(o => (string)o[0]).ToArray();
            var engine = TestEngines.WithCeiling(TestEngines.Unhurried);
            var outputs = new string[names.Length * 16];
            var threads = new Thread[outputs.Length];
            for (int i = 0; i < outputs.Length; i++)
            {
                int k = i;
                threads[i] = new Thread(() => outputs[k] = RunScript(engine, Source(names[k % names.Length]))) { IsBackground = true };
                threads[i].Start();
            }
            foreach (Thread t in threads)
                Assert.True(t.Join(300000));
            for (int i = 0; i < outputs.Length; i++)
                AssertSameOutput(names[i % names.Length] + " (parallel run " + i + ")", Expected(names[i % names.Length]), outputs[i]);
        }

        // An ASP.NET host runs .NET in its 4.5 compatibility mode (httpRuntime targetFramework="4.5"): legacy culture
        // flow, no async-local culture, the old XML defaults. A domain with that target framework is the same mode.
        [Fact]
        public void TheAosCompatibilityModePrintsTheSame()
        {
            string[] names = Scripts().Select(o => (string)o[0]).ToArray();
            string[] outputs = null;
            string mode = null;
            Exception failure = null;
            var t = new Thread(() =>
            {
                var setup = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                    ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
                    TargetFrameworkName = ".NETFramework,Version=v4.5",
                };
                AppDomain domain = AppDomain.CreateDomain("aos-4.5-mode", null, setup);
                try
                {
                    var runner = (LegacyModeRunner)domain.CreateInstanceAndUnwrap(
                        typeof(LegacyModeRunner).Assembly.FullName, typeof(LegacyModeRunner).FullName);
                    mode = runner.Mode();
                    outputs = runner.RunAll(names.Select(Source).ToArray());
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            });
            t.Start();
            Assert.True(t.Join(300000));
            Assert.Null(failure);
            Assert.Equal(".NETFramework,Version=v4.5 NoAsyncCurrentCulture=True", mode);
            for (int i = 0; i < names.Length; i++)
                AssertSameOutput(names[i] + " (4.5 mode)", Expected(names[i]), outputs[i]);
        }
    }

    // Runs the corpus inside the 4.5-mode domain, under a hostile caller culture as a web host's request threads have.
    public sealed class LegacyModeRunner : MarshalByRefObject
    {
        public string Mode()
        {
            bool legacy;
            AppContext.TryGetSwitch("Switch.System.Globalization.NoAsyncCurrentCulture", out legacy);
            return AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName + " NoAsyncCurrentCulture=" + legacy;
        }

        public string[] RunAll(string[] sources)
        {
            var odd = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
            odd.NumberFormat.NegativeSign = "−";
            Thread.CurrentThread.CurrentCulture = odd;
            Thread.CurrentThread.CurrentUICulture = odd;
            var engine = TestEngines.WithCeiling(TestEngines.Unhurried);
            return sources.Select(s => OracleCorpusTests.RunScript(engine, s)).ToArray();
        }
    }
}
