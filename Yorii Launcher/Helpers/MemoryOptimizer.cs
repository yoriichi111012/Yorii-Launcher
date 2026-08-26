using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Yorii_Launcher.Helpers
{
    // free heap then trim working set
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static Timer? idleTimer;
        private static int trimming;
        // keep task manager low when user stops, trim soon after idle
        private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

        public static void StartIdleTrimming()
        {
            if (idleTimer is not null) return;

            // startup touches a ton of pages so one trim isnt enough, trim a few times while things settle then go idle mode
            _ = Task.Run(async () =>
            {
                foreach (var delay in new[] { 5000, 15000, 30000, 45000 })
                {
                    await Task.Delay(delay);
                    if (App.IsShuttingDown) return;
                    ReduceMemory();
                }
            });

            idleTimer = new Timer(_ => TrimIfIdle(), null, TimeSpan.FromMinutes(1), CheckInterval);
        }

        // full cleanup - compact heap including loh then ask windows to page out leftovers
        public static void ReduceMemory()
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();

                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to trim memory: {ex.Message}");
            }
        }

        private static void TrimIfIdle()
        {
            // dont fight active work
            if (Interlocked.Exchange(ref trimming, 1) == 1) return;
            try
            {
                if (!IsUserIdle()) return;
                if (DownloadManager.HasActiveDownloads) return;
                if (App.IsShuttingDown) return;

                ReduceMemory();
            }
            finally
            {
                Interlocked.Exchange(ref trimming, 0);
            }
        }

        private static bool IsUserIdle()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return false;

            var uptime = (uint)Environment.TickCount;
            var lastInput = info.dwTime;
            var idleMs = uptime >= lastInput ? uptime - lastInput : 0;
            return idleMs >= (uint)IdleThreshold.TotalMilliseconds;
        }
    }
}