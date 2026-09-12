using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // xml.etree.ElementTree.ElementTree: a root holder. find/findall/findtext/iter/iterfind delegate to
    // the root as in CPython; write() takes a text sink with .write() (io.StringIO) and encoding='unicode',
    // since there are no files in this dialect.
    internal sealed class ElementTreeValue : ScriptValue
    {
        internal static readonly ScriptTypeInfo TreeType = new ScriptTypeInfo("xml.etree.ElementTree.ElementTree", BuildSlots());

        internal ElementValue Root;   // null => an empty tree

        internal ElementTreeValue(ElementValue root) { Root = root; }

        public override ScriptTypeInfo TypeInfo { get { return TreeType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal ElementValue RequireRootFor(EvalContext ctx, string method) { return RequireRoot(ctx, method); }

        private ElementValue RequireRoot(EvalContext ctx, string method)
        {
            if (Root == null)
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "'NoneType' object has no attribute '" + method + "'");
            return Root;
        }

        private ScriptValue Write(EvalContext ctx, ScriptValue[] a, KwArgs kw)
        {
            if (a.Length < 1)
                throw Raise.TypeError(ctx, "write() missing required argument 'file_or_filename'");
            string enc = a.Length >= 2 && a[1].Kind != ValueKind.None ? EncArg(ctx, a[1]) : "us-ascii";
            ScriptValue v;
            if (kw.TryGet("encoding", out v) && v.Kind != ValueKind.None)
                enc = EncArg(ctx, v);
            bool? declare = null;
            if (kw.TryGet("xml_declaration", out v) && v.Kind != ValueKind.None)
                declare = v.IsTruthy(ctx);
            bool shortEmpty = true;
            if (kw.TryGet("short_empty_elements", out v) && v.Kind != ValueKind.None)
                shortEmpty = v.IsTruthy(ctx);
            if (!string.Equals(enc, "unicode", StringComparison.Ordinal))
                throw Raise.TypeError(ctx, "write() supports only encoding='unicode' into a text sink in this dialect");
            ScriptValue text = Modules.XmlModule.Serialize(ctx, RequireRoot(ctx, "write"), enc, declare,
                Modules.XmlModule.Method(ctx, kw), shortEmpty);
            ScriptValue sink = PyOps.GetAttr(a[0], "write", ctx);
            ctx.CallHook(sink, new[] { text }, KwArgs.Empty);
            return ctx.Values.None;
        }

        private static string EncArg(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "write() argument 'encoding' must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static Dictionary<string, SlotDescriptor> BuildSlots()
        {
            return new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["getroot"] = SlotDescriptor.MakeMethod("getroot", (self, a, kw, c) =>
                {
                    ElementValue r = ((ElementTreeValue)self).Root;
                    return r == null ? (ScriptValue)c.Values.None : r;
                }),
                ["_setroot"] = SlotDescriptor.MakeMethod("_setroot", (self, a, kw, c) =>
                {
                    ((ElementTreeValue)self).Root = (ElementValue)ElementValue.RequireElement(c, a.Length > 0 ? a[0] : c.Values.None);
                    return c.Values.None;
                }),
                ["find"] = SlotDescriptor.MakeMethod("find", (self, a, kw, c) => ElementXPath.TreeFind(((ElementTreeValue)self).RequireRoot(c, "find"), a, kw, c)),
                ["findall"] = SlotDescriptor.MakeMethod("findall", (self, a, kw, c) => ElementXPath.TreeFindAll(((ElementTreeValue)self).RequireRoot(c, "findall"), a, kw, c)),
                ["findtext"] = SlotDescriptor.MakeMethod("findtext", (self, a, kw, c) => ElementXPath.TreeFindText(((ElementTreeValue)self).RequireRoot(c, "findtext"), a, kw, c)),
                ["iter"] = SlotDescriptor.MakeMethod("iter", (self, a, kw, c) => ElementXPath.TreeIter(((ElementTreeValue)self).RequireRoot(c, "iter"), a, kw, c)),
                ["iterfind"] = SlotDescriptor.MakeMethod("iterfind", (self, a, kw, c) => ElementXPath.TreeIterFind(((ElementTreeValue)self).RequireRoot(c, "iterfind"), a, kw, c)),
                ["write"] = SlotDescriptor.MakeMethod("write", (self, a, kw, c) => ((ElementTreeValue)self).Write(c, a, kw)),
                ["parse"] = SlotDescriptor.MakeMethod("parse", (self, a, kw, c) => Modules.XmlModule.TreeParse(c, (ElementTreeValue)self, a, kw)),
            };
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<xml.etree.ElementTree.ElementTree object>");
        }
    }
}
