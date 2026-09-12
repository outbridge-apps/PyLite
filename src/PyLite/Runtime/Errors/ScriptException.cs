using System;

namespace Outbridge.PyLite.Runtime.Errors
{
    // The C# carrier of an ExceptionValue through the evaluator stack. Its Message contains ONLY the
    // type name — computing str(args) could be expensive or throwing.
    public class ScriptException : Exception
    {
        public ExceptionValue Value { get; }

        public ScriptException(ExceptionValue value) : base(SafeBrief(value))
        {
            Value = value;
        }

        private static string SafeBrief(ExceptionValue value)
        {
            return "<" + (value != null ? value.ExcType.Name : "?") + ">";
        }
    }
}
