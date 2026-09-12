using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Errors
{
    // A script exception as a value. str/repr follow CPython
    // Objects/exceptions.c (BaseException_str / BaseException_repr / KeyError_str); repr uses the
    // modern (3.7+) no-trailing-comma form.
    public sealed class ExceptionValue : ScriptValue
    {
        public PyExceptionType ExcType { get; }
        public ScriptValue[] Args { get; }
        public ExceptionValue Context { get; internal set; }
        public ExceptionValue Cause { get; internal set; }
        internal bool SuppressContext;   // set by `raise ... from X` (CPython __suppress_context__)

        // Traceback frames, innermost-first: RunBody appends one frame per function
        // the exception exits, RunModule appends "<module>"; capacity 8 up to the 32-frame cap.
        internal List<TracebackFrame> TracebackFrames;
        internal bool TracebackTruncated;
        internal int TracebackDropped;

        // Line of the (re-)raise statement, recorded by Raise.Make / ExecRaise. Used for the FIRST
        // traceback frame only, so an exception caught and bare-re-raised in the same function still
        // reports the original raise line (CPython keeps the original traceback across a bare raise).
        internal int RaiseLine;

        // Set when an except handler stamped the raise-site frame for an exception the engine raised
        // and that never left its frame. The frame-exit append then skips ONE turn, so a bare re-raise
        // out of that same handler does not report the function twice. Cleared by an explicit raise.
        internal bool FrameStampedAtCatch;

        // add_note() payload (3.11); null until the first note, so __notes__ raises AttributeError before that.
        internal List<string> Notes;

        // Named data attributes beyond args, for the exception types CPython gives some (JSONDecodeError's
        // msg/doc/pos/lineno/colno); null for the rest. Read-only from the script, like args.
        internal Dictionary<string, ScriptValue> Data;

        private static readonly IDictionary<string, SlotDescriptor> Slots = BuildSlots();
        private readonly ScriptTypeInfo _type;

        internal ExceptionValue(PyExceptionType excType, ScriptValue[] args)
        {
            ExcType = excType;
            Args = args ?? Array.Empty<ScriptValue>();
            _type = new ScriptTypeInfo(excType.Name, Slots);
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["add_note"] = SlotDescriptor.MakeMethod("add_note", (self, a, kw, c) => ((ExceptionValue)self).AddNote(c, a));
            return d;
        }

        private ScriptValue AddNote(EvalContext ctx, ScriptValue[] a)
        {
            Modules.Support.Args.Exactly(ctx, a, "BaseException.add_note", 1);
            StrValue s = a[0] as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "note must be a str, not '" + a[0].PyTypeName + "'");
            if (Notes == null)
                Notes = new List<string>(2);
            ctx.Values.PreCharge(32 + s.Value.Length);
            Notes.Add(s.Value);
            return ctx.Values.None;
        }

        public override ScriptTypeInfo TypeInfo { get { return _type; } }
        internal override ValueKind Kind { get { return ValueKind.Exception; } }

        // str(e): 0 args -> ""; 1 arg -> str(args[0]); n args -> repr(tuple(args)).
        // KeyError special-cases 1 arg -> repr(args[0]).
        // KeyError message is repr(key) capped at 512 chars with a "..." suffix.
        private const int KeyErrorReprCap = 512;

        public string GetMessageText(EvalContext ctx)
        {
            if (ReferenceEquals(ExcType, PyExceptionTypes.KeyError) && Args.Length == 1)
            {
                string r = Args[0].Repr(ctx);
                return r.Length > KeyErrorReprCap ? r.Substring(0, KeyErrorReprCap) + "..." : r;
            }
            if (Args.Length == 5 && ExcType.IsUnicodeCodecError)
            {
                string codec = CodecMessage(ctx);
                if (codec != null)
                    return codec;
            }
            if (Args.Length == 0)
                return "";
            if (Args.Length == 1)
                return Args[0].Str(ctx);
            return ArgsTuple(ctx);
        }

        // "'utf-8' codec can't decode byte 0xff in position 0: invalid start byte" (single unit) or
        // "... can't decode bytes in position 0-1: ..." (a range); null when the args are not the 5-tuple shape.
        private string CodecMessage(EvalContext ctx)
        {
            StrValue enc = Args[0] as StrValue;
            StrValue reason = Args[4] as StrValue;
            IntValue start = Args[2] as IntValue;
            IntValue end = Args[3] as IntValue;
            if (enc == null || reason == null || start == null || end == null)
                return null;
            bool decode = ReferenceEquals(ExcType, PyExceptionTypes.UnicodeDecodeError);
            string verb = decode ? "decode" : "encode";
            string head = "'" + enc.Value + "' codec can't " + verb + " ";
            if (end.Value == start.Value + 1 && start.Value >= 0)
            {
                string unit = null;
                BytesValue bv = Args[1] as BytesValue;
                StrValue sv = Args[1] as StrValue;
                if (decode && bv != null && start.Value < bv.Data.Length)
                    unit = "byte 0x" + bv.Data[(int)start.Value].ToString("x2");
                else if (!decode && sv != null && start.Value < sv.Value.Length)
                    unit = "character " + CharEscape(sv.Value[(int)start.Value]);
                if (unit != null)
                    return head + unit + " in position " + start.Value + ": " + reason.Value;
            }
            return head + (decode ? "bytes" : "characters") + " in position " + start.Value + "-" + (end.Value - 1) + ": " + reason.Value;
        }

        private static string CharEscape(char c)
        {
            return c < 0x100 ? "'\\x" + ((int)c).ToString("x2") + "'" : "'\\u" + ((int)c).ToString("x4") + "'";
        }

        private string ArgsTuple(EvalContext ctx)
        {
            var sb = new StringBuilder("(");
            for (int i = 0; i < Args.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(Args[i].Repr(ctx));
            }
            sb.Append(')');
            return sb.ToString();
        }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return true; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(GetMessageText(ctx));
        }

        // repr(e): Name(args...) — modern form, no trailing comma for a single argument (3.7+ dropped the
        // 3.4-era `ValueError('x',)`).
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(ExcType.Name);
            sb.Append('(');
            for (int i = 0; i < Args.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(Args[i].Repr(ctx, depth + 1));
            }
            sb.Append(')');
        }
    }
}
