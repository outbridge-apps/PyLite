using System;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime
{
    // C# stack guard. #1: TryEnsureSufficientExecutionStack does NOT exist in
    // net472, so we use the throwing EnsureSufficientExecutionStack + catch. An uncaught
    // StackOverflowException would kill the host process, so every recursive descent must Probe.
    //
    // Call cadence (normative for): Evaluator.Call prologue — always; EvalExpr — on every 16th
    // ctx.NodeCounter value (parser bounds expr nesting to 64, so at most 16 small frames grow between
    // probes); every recursive descent of the parser; every re-entry of a data walker (ProbeData).
    // Explicit-stack walkers need no probe (no C# recursion).
    internal static class StackGuard
    {
        public static void Probe(EvalContext ctx)
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                // Routed through CreateAbort so the budget enters Terminating correctly.
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "CSharpStack", 0, ctx.CallDepth);
            }
        }

        // For the data walkers (repr, hash, ==, ordering, asdict) that re-enter through a leaf: MaxDataDepth
        // bounds them, but a host may raise it past what the stack holds.
        public static void ProbeData(EvalContext ctx)
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "StackDepth", 0, 0);
            }
        }
    }
}
