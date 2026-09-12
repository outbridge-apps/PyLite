using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime
{
    // Factory registration + lazy per-run Import. Frozen at engine build time and
    // shared read-only across all runs; the per-run instance cache lives in EvalContext, not here.
    public sealed class ModuleRegistry
    {
        private readonly Dictionary<string, Func<EvalContext, ModuleValue>> _factories =
            new Dictionary<string, Func<EvalContext, ModuleValue>>(StringComparer.Ordinal);
        private bool _frozen;

        public void Register(string name, Func<EvalContext, ModuleValue> factory)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("module registry is frozen");
            }
            if (_factories.ContainsKey(name))
            {
                throw new InvalidOperationException("module already registered: " + name);
            }
            _factories.Add(name, factory);
        }

        // Pre-freeze overwrite: host-source last-wins replacement and ModuleOverrides
        // wrapping. The accidental-duplicate protection of Register applies only
        // within one registration source; ScriptEngine enforces that per source.
        internal void Replace(string name, Func<EvalContext, ModuleValue> factory)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("module registry is frozen");
            }
            _factories[name] = factory;
        }

        internal Func<EvalContext, ModuleValue> GetFactory(string name)
        {
            Func<EvalContext, ModuleValue> f;
            _factories.TryGetValue(name, out f);
            return f;
        }

        public void Freeze()
        {
            _frozen = true;
            _packages = BuildPackageIndex();
        }

        // Registering "xml.etree.ElementTree" implies the packages "xml" and "xml.etree". They have no
        // factory of their own; this index maps each implied package to its IMMEDIATE child segments so
        // Import can synthesize it. Built at Freeze; the lazy fallback is idempotent (same content) for
        // a registry that is used without freezing.
        private Dictionary<string, SortedSet<string>> _packages;

        private Dictionary<string, SortedSet<string>> Packages
        {
            get { return _packages ?? (_packages = BuildPackageIndex()); }
        }

        private Dictionary<string, SortedSet<string>> BuildPackageIndex()
        {
            var pkgs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (string full in _factories.Keys)
            {
                for (int at = 0; ; )
                {
                    int dot = full.IndexOf('.', at);
                    if (dot < 0)
                        break;
                    string prefix = full.Substring(0, dot);
                    int next = full.IndexOf('.', dot + 1);
                    string child = next < 0 ? full.Substring(dot + 1) : full.Substring(dot + 1, next - dot - 1);
                    SortedSet<string> kids;
                    if (!pkgs.TryGetValue(prefix, out kids))
                        pkgs[prefix] = kids = new SortedSet<string>(StringComparer.Ordinal);
                    kids.Add(child);
                    at = dot + 1;
                }
            }
            return pkgs;
        }

        public bool IsRegistered(string name)
        {
            return _factories.ContainsKey(name);
        }

        public ModuleValue Import(string name, EvalContext ctx)
        {
            ModuleValue cached;
            if (ctx.ModuleInstances.TryGetValue(name, out cached))
            {
                ctx.Budget.Step(1);
                return cached;
            }
            ctx.Budget.Step(BudgetCost.ImportModule);

            Func<EvalContext, ModuleValue> factory;
            if (!_factories.TryGetValue(name, out factory))
            {
                ModuleValue pkg = TryImportPackage(name, ctx);
                if (pkg != null)
                {
                    return pkg;
                }
                throw Raise.Import(ctx, "No module named '" + name + "'");
            }
            if (!ctx.ModulesInitializing.Add(name))
            {
                throw Raise.Import(ctx, "circular module initialization: '" + name + "'");
            }

            ModuleValue m;
            try
            {
                m = factory(ctx);   // allocates via ctx.Values -> charged
            }
            finally
            {
                ctx.ModulesInitializing.Remove(name);
            }
            ctx.ModuleInstances[name] = m;   // only successful inits are cached
            return m;
        }

        // An implied package: a module whose members are its immediate children, each imported through
        // the same path (so a nested package is synthesized too and the leaves are the real modules).
        // Unlike CPython, importing a package materializes its children eagerly — the module set is tiny
        // and it keeps `import xml.etree.ElementTree` working without an import-system rewrite.
        private ModuleValue TryImportPackage(string name, EvalContext ctx)
        {
            SortedSet<string> children;
            if (!Packages.TryGetValue(name, out children))
            {
                return null;
            }
            if (!ctx.ModulesInitializing.Add(name))
            {
                throw Raise.Import(ctx, "circular module initialization: '" + name + "'");
            }
            ModuleValue pkg;
            try
            {
                var members = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
                foreach (string child in children)
                {
                    members[child] = Import(name + "." + child, ctx);
                }
                pkg = ctx.Values.Module(name, members);
            }
            finally
            {
                ctx.ModulesInitializing.Remove(name);
            }
            ctx.ModuleInstances[name] = pkg;
            return pkg;
        }
    }
}
