using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // Comments and processing instructions are Elements whose TAG is the factory itself, which is how
    // CPython marks them: `elem.tag is ET.Comment` is the test, they never match a tag filter, and the
    // serializer writes their text verbatim. The factories are static so that identity holds across
    // module instances.
    public static partial class XmlModule
    {
        internal static readonly BuiltinFunctionValue CommentFn =
            BuiltinFunctionValue.Make("Comment", (s, a, kw, c) => MakeNode(c, a, kw, "Comment", CommentFn, false));

        internal static readonly BuiltinFunctionValue ProcessingInstructionFn =
            BuiltinFunctionValue.Make("ProcessingInstruction", (s, a, kw, c) => MakeNode(c, a, kw, "ProcessingInstruction", ProcessingInstructionFn, true));

        private static ScriptValue MakeNode(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn,
            BuiltinFunctionValue tag, bool instruction)
        {
            ScriptValue first = a.Length > 0 ? a[0] : null;
            ScriptValue second = a.Length > 1 ? a[1] : null;
            ScriptValue v;
            if (kw.TryGet(instruction ? "target" : "text", out v))
                first = v;
            if (kw.TryGet("text", out v) && instruction)
                second = v;
            if (instruction && first == null)
                throw Raise.TypeError(ctx, fn + "() missing required argument 'target'");

            var e = new ElementValue(tag, ctx.Values.Dict(0), ctx);
            string text = first == null || first.Kind == ValueKind.None ? null : StrOf(ctx, first, fn);
            if (instruction && second != null && second.Kind != ValueKind.None)
                text = text + " " + StrOf(ctx, second, fn);
            if (text != null)
                e.Text = ctx.Values.Str(text);
            return e;
        }

        private static string StrOf(EvalContext ctx, ScriptValue v, string fn)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, fn + "() argument must be str, not " + v.PyTypeName);
            return s.Value;
        }

        // dump(elem): the element (or a whole tree) written to the print sink with a trailing newline,
        // as CPython's debugging helper does.
        private static ScriptValue Dump(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "dump", 1);
            ElementValue root = RootOf(ctx, a[0], "dump");
            string text = ((StrValue)Serialize(ctx, root, "unicode", false)).Value;
            if (!text.EndsWith("\n", StringComparison.Ordinal))
                text += "\n";
            if (ctx.Out != null)
            {
                ctx.Out.Write(text);
            }
            else
            {
                ctx.Budget.ChargeOutput(2L * text.Length);
                if (ctx.PrintBuffer == null)
                    ctx.PrintBuffer = new System.Text.StringBuilder();
                ctx.PrintBuffer.Append(text);
            }
            return ctx.Values.None;
        }

        // tostringlist(): CPython may split the document into several chunks; one whole chunk is a legal
        // answer and ''.join() of it is the contract callers rely on.
        private static ScriptValue ToStringList(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ScriptValue one = ToStringFn(self, a, kw, ctx);
            ListValue list = ctx.Values.List(1);
            list.Add(one, ctx);
            return list;
        }

        private static ElementValue RootOf(EvalContext ctx, ScriptValue v, string fn)
        {
            ElementValue e = v as ElementValue;
            if (e != null)
                return e;
            ElementTreeValue t = v as ElementTreeValue;
            if (t != null)
                return t.RequireRootFor(ctx, fn);
            throw Raise.TypeError(ctx, fn + "() argument must be an Element, not " + v.PyTypeName);
        }
    }
}
