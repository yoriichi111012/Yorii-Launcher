using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    // tracks every download in the launcher - minecraft versions modrinth files updates skins themes, titlebar flyout binds to this
    public static class DownloadManager
    {
        private static DispatcherQueue? uiDispatcher;

        public static ObservableCollection<DownloadItem> Items { get; } = [];

        // fires when something gets added removed or changes
        public static event Action? ActivityChanged;

        public static void Initialize(DispatcherQueue dispatcherQueue) => uiDispatcher = dispatcherQueue;

        public static bool HasActiveDownloads => Items.Any(i => i.Status == DownloadStatus.Downloading);

        public static DownloadItem Add(string name, DownloadKind kind, ImageSource? icon = null, bool cancellable = true)
        {
            var item = new DownloadItem(name, kind, icon, cancellable);
            item.Finished += OnItemFinished;

            EnqueueUi(() =>
            {
                Items.Add(item);
                ActivityChanged?.Invoke();
            });

            return item;
        }

        public static void CancelAll()
        {
            foreach (var item in Items.Where(i => i.Status == DownloadStatus.Downloading).ToArray())
                item.Cancel();
        }

        private static void OnItemFinished(DownloadItem item)
        {
            // stop the titlebar spinner right away even though item stays in list a bit showing final state
            EnqueueUi(() => ActivityChanged?.Invoke());

            var delay = item.Status == DownloadStatus.Failed
                ? TimeSpan.FromSeconds(60)
                : TimeSpan.FromSeconds(45);

            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                // if launcher is closing dont touch dispatcher or it crashes with 0xc0000005
                if (App.IsShuttingDown)
                    return;
                EnqueueUi(() =>
                {
                    Items.Remove(item);
                    ActivityChanged?.Invoke();
                });
            }, TaskScheduler.Default);
        }

        internal static void EnqueueUi(Action action)
        {
            if (uiDispatcher is null || App.IsShuttingDown)
                return;
            if (uiDispatcher.HasThreadAccess)
                action();
            else
                uiDispatcher.TryEnqueue(() => action());
        }
    }
}