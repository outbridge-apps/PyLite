using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // __future__: the feature records, ported from CPython's Python source (Modules/Python/future.py). The parser
    // checks the future statements themselves.
    internal static class FutureModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            return PythonSourceModules.Load(ctx, "Outbridge.PyLite.future.py", "__future__");
        }
    }
}
