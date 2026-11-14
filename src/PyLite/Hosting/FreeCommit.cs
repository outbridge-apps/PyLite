using System;
using System.Runtime.InteropServices;

namespace Outbridge.PyLite.Hosting
{
    // What Windows can still promise: physical memory plus page file. .NET Framework takes a thread's whole stack from
    // it at start, so it runs out long before physical memory does. Not MemoryFailPoint: it can grow the page file.
    internal static class FreeCommit
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        // An unknown amount is not a shortage: the check must never be what fails a run. Does not see a job object's cap.
        public static bool IsBelow(long bytes)
        {
            long available = AvailableBytes();
            return available >= 0 && available < bytes;
        }

        // -1 when Windows does not answer.
        internal static long AvailableBytes()
        {
            try
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (!GlobalMemoryStatusEx(ref status))
                    return -1;
                return status.AvailablePageFile > long.MaxValue ? long.MaxValue : (long)status.AvailablePageFile;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
