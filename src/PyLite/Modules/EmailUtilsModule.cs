using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // email.utils: the date helpers (formatdate, format_datetime, parsedate, parsedate_tz,
    // parsedate_to_datetime, mktime_tz), ported from CPython's Python source (Modules/Python/email_utils.py).
    internal static class EmailUtilsModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            return PythonSourceModules.Load(ctx, "Outbridge.PyLite.email_utils.py", "email.utils");
        }
    }
}
