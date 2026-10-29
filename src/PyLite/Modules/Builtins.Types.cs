using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterTypes(Dictionary<string, ScriptValue> d)
        {
            Add(d, "isinstance", 2, 2, (self, args, kw, c) => IsInstanceBuiltin(c, args));
            Add(d, "issubclass", 2, 2, (self, args, kw, c) => IsSubclassBuiltin(c, args));
        }

        private static ScriptValue IsInstanceBuiltin(EvalContext c, ScriptValue[] args)
        {
            return c.Values.Bool(MatchClassInfo(c, args[1], args[0], true,
                "isinstance() arg 2 must be a type or tuple of types"));
        }

        private static ScriptValue IsSubclassBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args[0].Kind != ValueKind.Type && args[0].Kind != ValueKind.RecordClass && !(args[0] is EnumClassValue))
                throw Raise.TypeError(c, "issubclass() arg 1 must be a class");
            return c.Values.Bool(MatchClassInfo(c, args[1], args[0], false,
                "issubclass() arg 2 must be a class or tuple of classes"));
        }

        private static bool MatchesType(ScriptValue obj, TypeValue type, bool instance)
        {
            TypeValue objType = obj as TypeValue;   // a record or enum class is never a subclass of a builtin type
            return instance ? IsInstanceOf(obj, type) : objType != null && IsSubclassOf(objType, type);
        }

        // classinfo may be a type or a (possibly nested) tuple of types; explicit stack, depth-limited.
        private static bool MatchClassInfo(EvalContext c, ScriptValue classinfo, ScriptValue obj, bool instance, string argErr)
        {
            if (classinfo.Kind == ValueKind.Type)   // the common single type: no stack (P8)
                return MatchesType(obj, (TypeValue)classinfo, instance);
            var stack = new Stack<ScriptValue>();
            stack.Push(classinfo);
            int depth = 0;
            while (stack.Count > 0)
            {
                ScriptValue ci = stack.Pop();
                if (ci.Kind == ValueKind.Type)
                {
                    if (MatchesType(obj, (TypeValue)ci, instance))
                        return true;
                }
                else if (ci is EnumClassValue)
                {
                    EnumClassValue ec = (EnumClassValue)ci;
                    if (instance)
                    {
                        EnumMemberValue em = EnumClassValue.MemberOf(obj, c);
                        if (em != null && em.Class.IsSubclassOf(ec))
                            return true;
                    }
                    else
                    {
                        EnumClassValue oc = obj as EnumClassValue;
                        if (oc != null && oc.IsSubclassOf(ec))
                            return true;
                    }
                }
                else if (ci.Kind == ValueKind.RecordClass)
                {
                    RecordClassValue rc = (RecordClassValue)ci;
                    if (instance)
                    {
                        RecordInstanceValue ri = obj as RecordInstanceValue;
                        if (ri != null && ri.Class.IsSubclassOf(rc))
                            return true;
                    }
                    else
                    {
                        RecordClassValue oc = obj as RecordClassValue;
                        if (oc != null && oc.IsSubclassOf(rc))
                            return true;
                    }
                }
                else if (ci.Kind == ValueKind.Tuple)
                {
                    if (++depth > 16)
                        throw Raise.RecursionError(c, "maximum recursion depth exceeded");
                    TupleValue t = (TupleValue)ci;
                    for (int i = 0; i < t.Items.Length; i++)
                        stack.Push(t.Items[i]);
                }
                else
                {
                    throw Raise.TypeError(c, argErr);
                }
            }
            return false;
        }

        internal static bool IsInstanceOf(ScriptValue obj, TypeValue tv)
        {
            if (tv.ExcType != null)
            {
                ExceptionValue ev = obj as ExceptionValue;
                return ev != null && ev.ExcType.IsSubtypeOf(tv.ExcType);
            }
            if (tv.Describes != null && tv.Describes.IsScriptDefined)
                return obj.TypeInfo.IsSubtypeOf(tv.Describes);
            return obj.TypeInfo.IsSubtypeOf(tv.Name);
        }

        private static bool IsSubclassOf(TypeValue cls, TypeValue tv)
        {
            if (tv.ExcType != null && cls.ExcType != null)
                return cls.ExcType.IsSubtypeOf(tv.ExcType);
            if (tv.ExcType != null || cls.ExcType != null)
                return tv.Name == "object";
            if (tv.Describes != null && tv.Describes.IsScriptDefined)
                return cls.Describes != null && cls.Describes.IsSubtypeOf(tv.Describes);
            if (cls.Describes != null)
                return cls.Describes.IsSubtypeOf(tv.Name);
            return ScriptTypeInfo.IsSubtypeByName(cls.Name, tv.Name);
        }
    }
}
