using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    // A host-supplied C#-implemented module. The engine registers it into the shared
    // ModuleRegistry; CreateInstance runs once per run on first `import Name`, charged to that run's budget.
    // Native capabilities live behind this interface (reviewed as engine code), which keeps Python
    // preludes (PythonModules) strictly sandbox-equal and native modules exclusively here.
    public interface IModuleProvider
    {
        string Name { get; }
        ModuleValue CreateInstance(EvalContext ctx);
    }
}
