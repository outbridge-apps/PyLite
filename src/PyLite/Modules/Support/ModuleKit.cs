using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    internal static class ModuleKit
    {
        private static readonly object ClassLock = new object();

        // The class object of a module's exception (re.error, json.JSONDecodeError, ...): made once for the
        // engine's lifetime, as the builtin exception classes are, and recorded on the exception type, so
        // `type(e) is re.error` holds and every import hands out the same class. Calling it constructs the
        // exception (a class in the builtins tree, JSONDecodeError among them, was made by BuiltinsTable).
        internal static TypeValue ExceptionClass(PyExceptionType pt)
        {
            TypeValue cls = pt.ClassValue;
            if (cls != null)
                return cls;
            BuiltinsTable.EnsureBuilt();   // a builtins-tree type gets its class (and its constructor) there
            if (pt.ClassValue != null)
                return pt.ClassValue;
            lock (ClassLock)
            {
                if (pt.ClassValue == null)
                    pt.ClassValue = TypeValue.Make(pt.Name, (self, args, kw, c) => c.Values.Exception(pt, args), null, null, pt);
                return pt.ClassValue;
            }
        }
    }
}
