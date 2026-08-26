using System;
using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace Yorii_Launcher.Helpers
{
    public static class Logger
    {
        private static readonly object logLock = new();
        private const long MaxFileSize = 20 * 1024 * 1024;

        public static string LogsDir => Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            "Logs");

        public static string LogFilePath => Path.Combine(LogsDir, "logs.txt");

        static Logger()
        {
            try
            {
                Directory.CreateDirectory(LogsDir);

                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxFileSize)
                {
                    var lines = File.ReadAllLines(LogFilePath);
                    var keep = Math.Max(lines.Length / 5, 100);
                    File.WriteAllLines(LogFilePath, lines[^keep..]);
                    Debug.WriteLine($"[Logger] Log file exceeded {MaxFileSize / (1024 * 1024)}MB, trimmed to {keep} lines");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Logger] Failed to initialize log file: {ex.Message}");
            }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var fileLine = $"[{timestamp}] [{level}] {message}";
            var debugLine = $"[{level}] {message}";

            lock (logLock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, fileLine + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
                }
            }

            Debug.WriteLine(debugLine);
        }
    }
}
