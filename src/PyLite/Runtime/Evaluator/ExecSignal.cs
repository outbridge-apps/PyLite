using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Control-flow signals returned by ExecStmt/ExecBlock. break/continue/
    // return AND the raise statement are signals, NOT C# exceptions: cheap on the hot path (a .NET
    // throw costs tens of microseconds on net472) and they never collide with the except dispatcher.
    // Only expression-level raises (engine C# code) and EngineAbort travel as C# exceptions; a Raised
    // signal is converted back to a ScriptException carrier at the function/module boundary.
    public enum ExecSignalKind
    {
        Normal = 0,
        Break,
        Continue,
        Return,
        Raised,
    }

    public struct ExecSignal
    {
        public ExecSignalKind Kind;
        public ScriptValue ReturnValue;   // Return: the value; Raised: the ExceptionValue

        public bool IsNormal { get { return Kind == ExecSignalKind.Normal; } }

        public ExceptionValue Raised { get { return (ExceptionValue)ReturnValue; } }   // valid only when Kind == Raised

        public static ExecSignal Normal { get { return default(ExecSignal); } }   // Kind = 0
        public static readonly ExecSignal Break = new ExecSignal { Kind = ExecSignalKind.Break };
        public static readonly ExecSignal Continue = new ExecSignal { Kind = ExecSignalKind.Continue };

        public static ExecSignal Return(ScriptValue v)
        {
            return new ExecSignal { Kind = ExecSignalKind.Return, ReturnValue = v };
        }

        public static ExecSignal Raise(ExceptionValue ev)
        {
            return new ExecSignal { Kind = ExecSignalKind.Raised, ReturnValue = ev };
        }
    }
}
