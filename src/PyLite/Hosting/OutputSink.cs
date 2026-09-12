using System.Text;
using Outbridge.PyLite.Runtime;

namespace Outbridge.PyLite.Hosting
{
    // The print sink under the output cap. print hands us already-
    // formatted text (sep/end applied); we charge it, split on '\n' iteratively, and flush each complete line
    // to the host callback (WITHOUT the trailing newline) or accumulate it into the internal buffer. The
    // incomplete remainder waits in _pending until the next Write or the final flush.
    internal sealed class OutputSink
    {
        private readonly PrintDelegate _sink;     // null => buffered mode
        private readonly StringBuilder _buffer;   // non-null only in buffered mode
        private readonly StringBuilder _pending;  // the incomplete line (text after the last '\n')
        private readonly EvalContext _ctx;

        public OutputSink(PrintDelegate sink, EvalContext ctx)
        {
            _sink = sink;
            _ctx = ctx;
            _pending = new StringBuilder();
            if (sink == null)
            {
                _buffer = new StringBuilder();
            }
        }

        public void Write(string text)
        {
            _ctx.Budget.ChargeOutput(2L * text.Length + 2);   // charge BEFORE processing (may throw)
            int start = 0;
            while (true)
            {
                int nl = text.IndexOf('\n', start);
                if (nl < 0)
                {
                    _pending.Append(text, start, text.Length - start);
                    return;
                }
                _pending.Append(text, start, nl - start);
                EmitLine(_pending.ToString());
                _pending.Clear();
                start = nl + 1;
            }
        }

        // The final flush of the incomplete remainder (called when the run finishes, including on error).
        public void FlushFinal()
        {
            if (_pending.Length == 0)
            {
                return;
            }
            string rem = _pending.ToString();
            _pending.Clear();
            if (_sink != null)
            {
                HostCallGuard.InvokePrint(_sink, rem, _ctx);   // no trailing newline on the last line
            }
            else
            {
                _buffer.Append(rem);
            }
        }

        // Buffered mode => the accumulated string; callback mode => null (the host already received the lines).
        public string GetBufferedOrNull()
        {
            return _buffer == null ? null : _buffer.ToString();
        }

        private void EmitLine(string line)
        {
            if (_sink != null)
            {
                HostCallGuard.InvokePrint(_sink, line, _ctx);
            }
            else
            {
                _buffer.Append(line).Append('\n');
            }
        }
    }
}
