using System;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime
{
    // C# stack guard. #1: TryEnsureSufficientExecutionStack does NOT exist in
    // net472, so we use the throwing EnsureSufficientExecutionStack + catch. An uncaught
    // StackOverflowException would kill the host process, so every recursive descent must Probe.
    //
    // Call cadence (normative for): Evaluator.Call prologue — always; EvalExpr — when
    // (++ctx.ExprProbeCounter & 15) == 0 (parser bounds expr nesting to 64, so at most 16 small
    // frames grow between probes); every recursive descent of the parser. Explicit-stack walkers
    // need no probe (no C# recursion).
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

        // Parser variant (no EvalContext). The Compile path converts this into a SyntaxError
        // "expression too deeply nested".
        public static void ProbeParser()
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw new EngineAbort(EngineAbortKind.Recursion, "CSharpStack", 0, 0);
            }
        }
    }
}
