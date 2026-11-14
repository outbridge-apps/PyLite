namespace Outbridge.PyLite.Runtime.Values
{
    // Fixed. MoveNext returns false once exhausted (never revives).
    public interface IScriptIterator
    {
        bool MoveNext(EvalContext ctx, out ScriptValue value);
    }

    // An iterator whose whole state is its position over a shared sequence: copy.copy makes a second one there.
    internal interface ICopyableIterator
    {
        IScriptIterator CopyPosition();
    }

    // Every MoveNext charges one step. The only Budget.Step site
    // for iteration.
    internal abstract class ScriptIteratorBase : IScriptIterator
    {
        // A StopIteration out of a callback ends an iterator that calls one (map, filter, iter(f, s), the
        // itertools ones): CPython's consumers read it as the end, whoever raised it.
        private readonly bool _callsScript;

        protected ScriptIteratorBase()
        {
        }

        protected ScriptIteratorBase(bool callsScript)
        {
            _callsScript = callsScript;
        }

        public bool MoveNext(EvalContext ctx, out ScriptValue value)
        {
            ctx.Budget.Step();
            // A single pull through nested lazy iterators (map/filter/genexp/islice/chain/...) recurses one
            // C# frame per nesting level; without a probe a deeply nested chain overflows the stack (fatal,
            // uncatchable). Probe the real stack every 16 MoveNext calls -> EngineAbort(Recursion) instead.
            if ((++ctx.IterProbeCounter & 15) == 0)
                StackGuard.Probe(ctx);
            return _callsScript ? MoveNextCalling(ctx, out value) : MoveNextCore(ctx, out value);
        }

        private bool MoveNextCalling(EvalContext ctx, out ScriptValue value)
        {
            try
            {
                return MoveNextCore(ctx, out value);
            }
            catch (Errors.ScriptException e) when (IsStop(e))
            {
                return EndOnStop(ctx, e, out value);
            }
        }

        // The same catch for an iterator that keeps it in its own MoveNextCore (map, filter, the itertools ones):
        // nested ones then take one C# frame a level, not two, so a chain goes as deep as it did without the catch.
        protected static bool IsStop(Errors.ScriptException e)
        {
            return e.Value.ExcType.IsSubtypeOf(Errors.PyExceptionTypes.StopIteration);
        }

        protected static bool EndOnStop(EvalContext ctx, Errors.ScriptException e, out ScriptValue value)
        {
            ctx.CaughtStop = e.Value;   // next() raises this one: it has the callback's arguments
            ctx.CaughtStopDepth = ctx.CallDepth;
            value = null;
            return false;
        }

        // item's truth; false when its __bool__ raised StopIteration, which ends the iterator that asked. The
        // handler is here, off the frame that nests.
        protected static bool TruthOrStop(EvalContext ctx, ScriptValue item, out bool truthy)
        {
            bool tested;
            try
            {
                truthy = item.IsTruthy(ctx);
                tested = true;
            }
            catch (Errors.ScriptException e) when (IsStop(e))
            {
                ScriptValue none;
                truthy = false;
                tested = EndOnStop(ctx, e, out none);
            }
            return tested;
        }

        // 1 when a == b, 0 when not, -1 when an __eq__ raised StopIteration, which ends the iterator that asked.
        // One number, so the frame that nests takes no local by address for it.
        protected static int EqualOrStop(EvalContext ctx, ScriptValue a, ScriptValue b)
        {
            int answer;
            try
            {
                answer = PyOps.Equals(a, b, ctx, 0) ? 1 : 0;
            }
            catch (Errors.ScriptException e) when (IsStop(e))
            {
                ScriptValue none;
                EndOnStop(ctx, e, out none);
                answer = -1;
            }
            return answer;
        }

        protected abstract bool MoveNextCore(EvalContext ctx, out ScriptValue value);
    }
}
