using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // def / lambda construction + import. Defaults are evaluated once, when the def
    // executes, in the current environment. The closure captures Cell REFERENCES (late binding).
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecImport(ImportNode n, Environment env, EvalContext ctx)
        {
            for (int i = 0; i < n.Names.Count; i++)
            {
                ImportAlias alias = n.Names[i];
                ModuleValue m = ctx.Modules.Import(alias.DottedName, ctx);
                if (alias.AsName != null)
                {
                    env.BindScopeName(alias.AsName, m);
                }
                else if (alias.DottedName.IndexOf('.') >= 0)
                {
                    // CPython: `import a.b.c` binds the TOP package `a`; the submodules are reachable
                    // through it (a.b.c) because a package's members are its children. The leaf was
                    // imported above, so it is initialized and cached either way.
                    string top = alias.DottedName.Substring(0, alias.DottedName.IndexOf('.'));
                    env.BindScopeName(top, ctx.Modules.Import(top, ctx));
                }
                else
                {
                    env.BindScopeName(alias.DottedName, m);
                }
            }
            return ExecSignal.Normal;
        }

        private ExecSignal ExecFromImport(FromImportNode n, Environment env, EvalContext ctx)
        {
            ModuleValue m = ctx.Modules.Import(n.Module, ctx);
            for (int i = 0; i < n.Names.Count; i++)
            {
                ImportAlias alias = n.Names[i];
                ScriptValue v;
                if (!m.Members.TryGetValue(alias.DottedName, out v))
                {
                    throw Raise.Import(ctx, "cannot import name '" + alias.DottedName + "'");
                }
                env.BindScopeName(alias.AsName ?? alias.DottedName, v);
            }
            return ExecSignal.Normal;
        }

        private ExecSignal ExecFuncDef(FuncDefNode n, Environment env, EvalContext ctx)
        {
            FunctionInfo info = _program.GetFunctionInfo(n);
            if (n.Decorators.Count == 0)
            {
                FunctionValue fn = BuildFunction(n.Name, info, n.Params, n.Body, null, env, ctx, n.ReturnAnnotationName);
                env.BindScopeName(n.Name, fn);
                return ExecSignal.Normal;
            }
            // PEP 614: decorator expressions evaluate top-to-bottom, then the calls apply bottom-up.
            var decs = new ScriptValue[n.Decorators.Count];
            for (int i = 0; i < decs.Length; i++)
                decs[i] = EvalExpr(n.Decorators[i], env, ctx);
            ScriptValue val = BuildFunction(n.Name, info, n.Params, n.Body, null, env, ctx, n.ReturnAnnotationName);
            for (int i = decs.Length - 1; i >= 0; i--)
                val = Call(decs[i], new[] { val }, KwArgs.Empty, ctx);
            env.BindScopeName(n.Name, val);
            return ExecSignal.Normal;
        }

        private ScriptValue EvalLambda(LambdaNode n, Environment env, EvalContext ctx)
        {
            FunctionInfo info = _program.GetFunctionInfo(n);
            return BuildFunction("<lambda>", info, n.Params, null, n.Body, env, ctx);
        }

        private FunctionValue BuildFunction(string name, FunctionInfo info, ParamList prms,
            IReadOnlyList<StmtNode> body, ExprNode lambdaBody, Environment env, EvalContext ctx, string returnAnn = null)
        {
            // (a) positional defaults, left to right, evaluated NOW (def-time)
            var defaults = new List<ScriptValue>();
            for (int i = 0; i < prms.Positional.Count; i++)
            {
                Param p = prms.Positional[i];
                if (p.Default != null)
                {
                    defaults.Add(EvalExpr(p.Default, env, ctx));
                }
            }

            // (b) keyword-only defaults, by name
            var kwDefaults = new List<KeyValuePair<string, ScriptValue>>();
            for (int i = 0; i < prms.KwOnly.Count; i++)
            {
                Param p = prms.KwOnly[i];
                if (p.Default != null)
                {
                    kwDefaults.Add(new KeyValuePair<string, ScriptValue>(p.Name, EvalExpr(p.Default, env, ctx)));
                }
            }

            // (c) closure: capture Cell references for the function's free variables from the current frame
            var closure = new Cell[info.FreeVars.Count];
            for (int i = 0; i < closure.Length; i++)
            {
                closure[i] = env.GetClosureCell(info.FreeVars[i]);
            }

            // (d) annotations: the plain-name ones, in parameter order, then the return one
            // (typing.get_type_hints answers with these names)
            List<KeyValuePair<string, string>> ann = null;
            foreach (Param p in prms.Positional)
                AddAnnotation(ref ann, p.Name, p.AnnotationName);
            foreach (Param p in prms.KwOnly)
                AddAnnotation(ref ann, p.Name, p.AnnotationName);
            AddAnnotation(ref ann, "return", returnAnn);

            return ctx.Values.Function(name, info, defaults.ToArray(), kwDefaults.ToArray(),
                closure, env.ModuleEnv, _program, body, lambdaBody, ann == null ? null : ann.ToArray());
        }

        private static void AddAnnotation(ref List<KeyValuePair<string, string>> ann, string name, string type)
        {
            if (type == null)
                return;
            (ann ?? (ann = new List<KeyValuePair<string, string>>())).Add(new KeyValuePair<string, string>(name, type));
        }
    }
}
