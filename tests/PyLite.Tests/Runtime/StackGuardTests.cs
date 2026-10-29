using System.Runtime.InteropServices;
using System.Threading;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // StackGuard.Probe must convert an imminent C# stack overflow into a catchable
    // EngineAbort BEFORE the real (uncatchable, process-killing) StackOverflowException.
    //
    // WHY THE FRAMES ARE PADDED: EnsureSufficientExecutionStack fires when the remaining stack drops
    // below a FIXED reserve (~64 KB) — independent of the thread's total stack size. If the recursion
    // frame is tiny, thousands of frames fit before the guard fires, and unwinding the EngineAbort back
    // through those thousands of frames re-exhausts the small reserve → a real SOE DURING UNWIND, after
    // which net472 spins a core instead of exiting (this test used to hang the whole test host, and
    // bumping the thread stack did NOT help — the reserve is fixed). A large frame makes the guard fire
    // after a few dozen frames, so the unwind crosses few frames and completes cleanly. This also mirrors
    // production, where real Call/EvalExpr frames are large and the guard fires early by design.
    [Trait("Category", "ResourceLimits")]
    public class StackGuardTests
    {
        // Explicit-size struct: the JIT reserves Size bytes of stack per frame; touching a field keeps
        // the local live so it is not elided. ~4 KB/frame => the guard fires within tens of frames.
        [StructLayout(LayoutKind.Explicit, Size = 4096)]
        private struct FramePad { [FieldOffset(0)] public byte Touch; }

        private static long _stackSink;   // keeps FramePad live across the recursion

        // 256 KB thread + ~4 KB frames => the guard fires in well under a hundred frames, fast and stable.
        private const int ProbeThreadStackBytes = 256 * 1024;

        [Fact]
        public void ProbeOnAmpleStackDoesNotThrow()
        {
            StackGuard.Probe(Rt.Ctx());
        }

        [Fact]
        public void DeepRecursionThrowsEngineAbortNotOverflow()
        {
            EngineAbort caught = null;
            EvalContext ctx = null;
            var t = new Thread(() =>
            {
                ctx = Rt.Ctx();
                try
                {
                    Recurse(ctx, 0);
                }
                catch (EngineAbort ea)
                {
                    caught = ea;
                }
            }, ProbeThreadStackBytes);
            t.Start();
            Assert.True(t.Join(15000), "recursion guard did not resolve within 15 s (possible SOE-during-unwind spin)");

            Assert.NotNull(caught);
            Assert.Equal(EngineAbortKind.Recursion, caught.Kind);
            Assert.Equal("CSharpStack", caught.LimitName);
            Assert.True(ctx.Budget.IsTerminating);   // routed through CreateAbort
        }

        private static void Recurse(EvalContext ctx, int depth)
        {
            FramePad pad = default;
            pad.Touch = (byte)depth;
            _stackSink += pad.Touch;
            StackGuard.Probe(ctx);
            Recurse(ctx, depth + 1);
        }
    }
}
