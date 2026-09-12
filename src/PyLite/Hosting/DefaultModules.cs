using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Runtime;

namespace Outbridge.PyLite.Hosting
{
    // The canonical set of C#-implemented modules an engine exposes by default. This is the production
    // counterpart of the test harness's ad-hoc registration (tests/Evaluator/Ev.cs): the ScriptEngine builds
    // its shared, frozen ModuleRegistry from here plus host ExtraModules (C#) and PythonModules.
    internal static class DefaultModules
    {
        public static void RegisterBuiltins(ModuleRegistry registry)
        {
            registry.Register("json", JsonModule.Create);
            registry.Register("re", ReModule.Create);
            registry.Register("datetime", DateTimeModule.Create);
            registry.Register("time", TimeModule.Create);
            registry.Register("itertools", ItertoolsModule.Create);
            registry.Register("collections", CollectionsModule.Create);
            registry.Register("copy", CopyModule.Create);
            registry.Register("html", HtmlModule.Create);
            registry.Register("uuid", UuidModule.Create);
            registry.Register("xml.etree.ElementTree", XmlModule.Create);
            registry.Register("copyreg", CopyregModule.Create);
            registry.Register("typing", TypingModule.Create);
            registry.Register("binascii", BinasciiModule.Create);
            registry.Register("struct", StructModule.Create);
            registry.Register("io", IoModule.Create);
            registry.Register("csv", CsvModule.Create);
            registry.Register("math", MathModule.Create);
            registry.Register("fractions", FractionsModule.Create);
            registry.Register("decimal", DecimalModule.Create);
            registry.Register("statistics", StatisticsModule.Create);
            registry.Register("random", RandomModule.Create);
            registry.Register("functools", FunctoolsModule.Create);
            registry.Register("dataclasses", DataclassesModule.Create);
            registry.Register("base64", Base64Module.Create);
            registry.Register("hashlib", HashlibModule.Create);
            registry.Register("hmac", HmacModule.Create);
            registry.Register("zlib", ZlibModule.Create);
            registry.Register("gzip", GzipModule.Create);
            registry.Register("bisect", BisectModule.Create);
            registry.Register("string", StringModule.Create);
            registry.Register("heapq", HeapqModule.Create);
            registry.Register("operator", OperatorModule.Create);
            registry.Register("unicodedata", UnicodedataModule.Create);
            registry.Register("zoneinfo", ZoneinfoModule.Create);
            registry.Register("calendar", CalendarModule.Create);
            registry.Register("urllib.parse", UrllibParseModule.Create);
            registry.Register("traceback", TracebackModule.Create);
            registry.Register("textwrap", TextwrapModule.Create);
            registry.Register("secrets", SecretsModule.Create);
        }
    }
}
