namespace Outbridge.PyLite.Runtime.Values
{
    // Fixed. MoveNext returns false once exhausted (never revives).
    public interface IScriptIterator
    {
        bool MoveNext(EvalContext ctx, out ScriptValue value);
    }

    // Every MoveNext charges one step. The only Budget.Step site
    // for iteration.
    internal abstract class ScriptIteratorBase : IScriptIterator
    {
        public bool MoveNext(EvalContext ctx, out ScriptValue value)
        {
            ctx.Budget.Step();
            // A single pull through nested lazy iterators (map/filter/genexp/islice/chain/...) recurses one
            // C# frame per nesting level; without a probe a deeply nested chain overflows the stack (fatal,
            // uncatchable). Probe the real stack every 16 MoveNext calls -> EngineAbort(Recursion) instead.
            if ((++ctx.IterProbeCounter & 15) == 0)
                StackGuard.Probe(ctx);
            return MoveNextCore(ctx, out value);
        }

        protected abstract bool MoveNextCore(EvalContext ctx, out ScriptValue value);
    }
}
