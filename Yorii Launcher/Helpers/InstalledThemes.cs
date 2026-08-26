using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage;

namespace Yorii_Launcher.Helpers;

// single snapshot of installed themes so discover and my themes dont each rescan, watcher keeps everything in sync
public static class InstalledThemes
{
    private static readonly object gate = new();
    private static HashSet<string>? installed;
    private static FileSystemWatcher? watcher;
    private static int refreshPending;

    public static event Action? Changed;

    public static HashSet<string> GetNames()
    {
        lock (gate)
        {
            if (installed is null)
                Scan();
            return new HashSet<string>(installed!, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Invalidate()
    {
        lock (gate)
        {
            installed = null;
        }
    }

    public static void NotifyChanged()
    {
        Invalidate();
        Changed?.Invoke();
    }

    private static void Scan()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var themesDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Themes");
            if (Directory.Exists(themesDir))
            {
                foreach (var dir in Directory.GetDirectories(themesDir))
                {
                    if (File.Exists(Path.Combine(dir, "theme.yaml")))
                        set.Add(Path.GetFileName(dir));
                }
            }
        }
        catch
        {
        }
        installed = set;
        StartWatcher();
    }

    private static void StartWatcher()
    {
        if (watcher is not null) return;
        try
        {
            var themesDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Themes");
            Directory.CreateDirectory(themesDir);

            watcher = new FileSystemWatcher(themesDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                InternalBufferSize = 8192
            };
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // watcher is best effort scans still work without it
        }
    }

    private static void OnChanged(object sender, FileSystemEventArgs e)
    {
        // debounce burst from recursive deletes, coalesce overlapping events into one refresh
        if (System.Threading.Interlocked.Exchange(ref refreshPending, 1) == 1)
            return;

        System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
        {
            List<string> snapshot;
            lock (gate)
            {
                installed = null;
                Scan();
                snapshot = [.. installed!];
            }
            System.Threading.Volatile.Write(ref refreshPending, 0);
            Changed?.Invoke();
        }, System.Threading.Tasks.TaskScheduler.Default);
    }
}
