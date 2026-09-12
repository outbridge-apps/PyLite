using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterPrint(Dictionary<string, ScriptValue> d)
        {
            Add(d, "print", (self, args, kw, c) => PrintBuiltin(c, args, kw));
        }

        private static ScriptValue PrintBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            string sep = " ", end = "\n";
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                ScriptValue val = kw.ValueAt(j);
                switch (name)
                {
                    case "sep":
                        sep = SepEnd(c, val, "sep", " ");
                        break;
                    case "end":
                        end = SepEnd(c, val, "end", "\n");
                        break;
                    case "file":
                        if (val.Kind != ValueKind.None)
                            throw Raise.TypeError(c, "print() 'file' argument is not supported in this environment");
                        break;
                    case "flush":
                        break;   // accepted and ignored
                    default:
                        throw KwReader.InvalidError(c, "print", name);
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0)
                    sb.Append(sep);
                sb.Append(args[i].Str(c));   // Str charges through the value model
            }
            sb.Append(end);
            string text = sb.ToString();
            if (c.Out != null)
            {
                c.Out.Write(text);   // host sink / buffered mode + line splitting + output cap
                return c.Values.None;
            }
            c.Budget.ChargeOutput(2L * text.Length);
            if (c.PrintBuffer == null)
                c.PrintBuffer = new StringBuilder();
            c.PrintBuffer.Append(text);
            return c.Values.None;
        }

        private static string SepEnd(EvalContext c, ScriptValue val, string which, string dflt)
        {
            if (val.Kind == ValueKind.None)
                return dflt;
            StrValue s = val as StrValue;
            if (s == null)
                throw Raise.TypeError(c, which + " must be None or a string, not " + val.PyTypeName);
            return s.Value;
        }
    }
}
