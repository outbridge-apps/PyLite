using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// A module implemented by the host in C#. The engine registers it beside the built-in ones;
    /// <see cref="CreateInstance"/> runs once per run, on the first <c>import</c> of <see cref="Name"/>, charged
    /// to that run's budget. Native capabilities belong here, reviewed as engine code, so the Python modules
    /// of <see cref="EngineOptions.PythonModules"/> stay exactly as sandboxed as scripts.
    /// </summary>
    public interface IModuleProvider
    {
        /// <summary>The name scripts import.</summary>
        string Name { get; }

        /// <summary>Builds the module for one run.</summary>
        /// <param name="ctx">The importing run's context: its value factory and budget.</param>
        ModuleValue CreateInstance(EvalContext ctx);
    }
}
