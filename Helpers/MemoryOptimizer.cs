using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Yorii_Launcher.Helpers
{
    // calls windows API to trim the working set
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public static void ReduceMemory()
        {
            try
            {
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to trim memory: {ex.Message}");
            }
        }
    }
}
