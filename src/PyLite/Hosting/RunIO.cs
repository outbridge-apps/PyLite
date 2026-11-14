using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Hosting
{
    // The host contract of a run: how RunRequest inputs become globals and how declared outputs come back
    // as strings. A failure here is a HostMarshalFailure, which ExecuteRun turns
    // into a HostError result; name validation runs before the gate and reports through the out string.
    internal static class RunIO
    {
        internal static readonly IReadOnlyDictionary<string, string> EmptyStringMap =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        // Guard against quadratic BigInteger.Parse on adversarial input; +1 admits a leading sign.
        internal const int MaxNumericLiteralDigits = 4300;

        // --- names, checked before the gate ----

        internal static bool ValidateNames(RunRequest request, out string error)
        {
            if (!AllIdentifiers(request.ScalarsIn, out error))
                return false;
            if (!AllIdentifiers(request.JsonIn, out error))
                return false;
            if (!AllIdentifiers(request.ScalarOutputNames, out error))
                return false;
            if (!AllIdentifiers(request.JsonOutputNames, out error))
                return false;
            if (request.ScalarsIn != null && request.JsonIn != null)
            {
                foreach (string k in request.ScalarsIn.Keys)
                {
                    if (request.JsonIn.ContainsKey(k))
                    {
                        error = "input variable '" + k + "' is defined in both ScalarsIn and JsonIn";
                        return false;
                    }
                }
            }
            error = null;
            return true;
        }

        private static bool AllIdentifiers(IReadOnlyDictionary<string, string> map, out string error)
        {
            error = null;
            if (map == null)
                return true;
            foreach (string name in map.Keys)
            {
                if (!IsIdentifier(name))
                {
                    error = "invalid variable name '" + name + "'";
                    return false;
                }
            }
            return true;
        }

        private static bool AllIdentifiers(IReadOnlyList<string> names, out string error)
        {
            error = null;
            if (names == null)
                return true;
            for (int i = 0; i < names.Count; i++)
            {
                if (!IsIdentifier(names[i]))
                {
                    error = "invalid variable name '" + names[i] + "'";
                    return false;
                }
            }
            return true;
        }

        // A usable input/output variable name: an ASCII identifier that is NOT a reserved word (the
        // lexer's Keywords table). A builtin name is allowed (a global legally shadows a builtin).
        internal static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            char c0 = s[0];
            if (!(c0 == '_' || (c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z')))
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                    return false;
            }
            return !Keywords.TryGet(s, out _);
        }

        // --- inputs (scalars JSON) ----

        internal static void PopulateScalarInputs(Outbridge.PyLite.Runtime.Environment moduleEnv, RunRequest request, EvalContext ctx)
        {
            if (request.ScalarsIn == null)
                return;
            foreach (KeyValuePair<string, string> kv in request.ScalarsIn)
                moduleEnv.SetGlobal(kv.Key, CoerceScalar(kv.Value, ctx));
        }

        // Each JsonIn value is parsed by PyJson under the run budget; a decode error is a HostMarshalFailure
        // (a budget EngineAbort during parsing flies through as BudgetExceeded).
        internal static IReadOnlyList<KeyValuePair<string, ScriptValue>> ParseJsonInputs(RunRequest request, EvalContext ctx)
        {
            var parsed = new List<KeyValuePair<string, ScriptValue>>();
            if (request.JsonIn == null)
                return parsed;
            foreach (KeyValuePair<string, string> kv in request.JsonIn)
            {
                try
                {
                    parsed.Add(new KeyValuePair<string, ScriptValue>(kv.Key, PyJson.Parse(ctx, kv.Value)));
                }
                catch (ScriptException se)
                {
                    throw new HostMarshalFailure("invalid JSON in input variable '" + kv.Key + "': "
                        + se.Value.GetMessageText(ctx));
                }
            }
            return parsed;
        }

        // null -> None; "" -> str; BigInteger; double (not inf/nan); true/false; else str.
        private static bool AllAsciiDigits(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    return false;
            }
            return true;
        }

        internal static ScriptValue CoerceScalar(string raw, EvalContext ctx)
        {
            if (raw == null)
                return ctx.Values.None;
            if (raw.Length == 0)
                return ctx.Values.Str("");

            if (raw.Length <= MaxNumericLiteralDigits + 1)
            {
                // a sign and 1..4,300 ASCII digits, nothing else: BigInteger.TryParse took "1\0" as 1 and 4,301
                // unsigned digits
                int sign = raw[0] == '-' || raw[0] == '+' ? 1 : 0;
                int digits = raw.Length - sign;
                if (digits > 0 && digits <= MaxNumericLiteralDigits && AllAsciiDigits(raw, sign))
                {
                    BigInteger bi = Outbridge.PyLite.Syntax.Lexer.ParseDecimal(raw, sign, raw.Length);
                    return ctx.Values.Int(raw[0] == '-' ? -bi : bi);
                }
                double d;
                if (NearestDouble.TryParse(raw, out d) && !double.IsInfinity(d))
                    return ctx.Values.Float(d);
            }
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return ctx.Values.Bool(true);
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return ctx.Values.Bool(false);
            return ctx.Values.Str(raw);
        }

        // --- outputs (scalar blacklist + str() semantics JSON) ----

        // Names are deduplicated (first wins); an unassigned declared output is simply absent.
        internal static IReadOnlyDictionary<string, string> CollectScalarOutputs(DictValue globals,
            IReadOnlyList<string> names, EvalContext ctx)
        {
            return CollectOutputs(globals, names, ctx, (name, value, c) =>
            {
                if (!IsScalarSerializable(value))
                    throw new HostMarshalFailure("output variable '" + name + "' is not serializable");
                try
                {
                    return value.Str(c);
                }
                catch (ScriptException se)   // the script is over: its output failed, not a line of it
                {
                    throw new HostMarshalFailure("output variable '" + name + "' is not serializable: " + TextOf(se, c));
                }
            });
        }

        // Each declared JSON output is serialized by PyJson under the budget (compact, ensure_ascii=false,
        // allow_nan=false). A non-serializable value / NaN / cycle is a HostMarshalFailure.
        internal static IReadOnlyDictionary<string, string> CollectJsonOutputs(DictValue globals,
            IReadOnlyList<string> names, EvalContext ctx)
        {
            return CollectOutputs(globals, names, ctx, (name, value, c) =>
            {
                try
                {
                    return PyJson.Dump(c, value, JsonDumpOptions.MarshalDefault);
                }
                catch (ScriptException se)
                {
                    throw new HostMarshalFailure("output variable '" + name + "' is not serializable: " + TextOf(se, c));
                }
            });
        }

        // The failure's own text; one whose __str__ raises as well is named by its class.
        private static string TextOf(ScriptException se, EvalContext ctx)
        {
            try
            {
                return se.Value.GetMessageText(ctx);
            }
            catch (ScriptException)
            {
                return se.Value.ExcType.Name;
            }
        }

        private static IReadOnlyDictionary<string, string> CollectOutputs(DictValue globals,
            IReadOnlyList<string> names, EvalContext ctx, Func<string, ScriptValue, EvalContext, string> encode)
        {
            if (names == null || names.Count == 0)
                return EmptyStringMap;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (result.ContainsKey(name))
                    continue;
                ScriptValue value;
                if (globals.TryGet(ctx.Values.Str(name), ctx, out value))
                    result[name] = encode(name, value, ctx);
            }
            return result;
        }

        // A scalar output is str(value) for anything that is data; callables, types, modules, iterators,
        // views and slices are refused rather than stringified.
        internal static bool IsScalarSerializable(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ValueKind.Function:
                case ValueKind.BoundMethod:
                case ValueKind.BuiltinFunction:
                case ValueKind.Module:
                case ValueKind.Type:
                case ValueKind.RecordClass:
                case ValueKind.Iterator:
                case ValueKind.DictView:
                case ValueKind.Slice:
                    return false;
                default:
                    return true;
            }
        }
    }
}
