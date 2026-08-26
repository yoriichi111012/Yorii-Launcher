using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher.Models;

public enum DownloadStatus
{
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public enum DownloadKind
{
    Minecraft,
    Mod,
    ResourcePack,
    Modpack,
    Update,
    Skin,
    Theme
}

public sealed class DownloadItem : INotifyPropertyChanged
{
    // ui property raises are throttled; progress data still tracked every call
    private static readonly TimeSpan RaiseInterval = TimeSpan.FromMilliseconds(120);

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Name { get; }
    public ImageSource? Icon { get; }
    public DownloadKind Kind { get; }
    public bool IsCancellable { get; }

    public CancellationTokenSource Cts { get; } = new();
    public CancellationToken Token => Cts.Token;

    // fired once when the item leaves the downloading state
    public event Action<DownloadItem>? Finished;

    private DownloadStatus status = DownloadStatus.Downloading;
    private bool isIndeterminate = true;
    private double progress;
    private string statusText = "";
    private string failMessage = "";
    private long lastProgressedBytes;
    private long lastTotalBytes;
    private long lastSpeedBytes;
    private long lastSpeedTicks = Environment.TickCount64;
    private double smoothedSpeed;
    private long lastRaiseTicks;
    private bool firstRaise = true;

    public DownloadItem(string name, DownloadKind kind, ImageSource? icon = null, bool cancellable = true)
    {
        Name = name;
        Kind = kind;
        Icon = icon;
        IsCancellable = cancellable;
    }

    public DownloadStatus Status
    {
        get => status;
        private set
        {
            if (status == value)
                return;

            status = value;
            OnUiPropertyChanged(
                nameof(Status),
                nameof(CancelVisibility),
                nameof(ProgressBarVisibility),
                nameof(StatusGlyphVisibility),
                nameof(StatusGlyph));
            UpdateStatusText();

            if (status != DownloadStatus.Downloading)
                Finished?.Invoke(this);
        }
    }

    public bool IsIndeterminate
    {
        get => isIndeterminate;
        private set
        {
            if (SetUiField(ref isIndeterminate, value))
                UpdateStatusText();
        }
    }

    public double Progress
    {
        get => progress;
        private set => SetUiField(ref progress, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetUiField(ref statusText, value);
    }

    public string IconGlyph => GetGlyph(Kind);

    public string StatusGlyph => status switch
    {
        DownloadStatus.Completed => "\uE73E",
        DownloadStatus.Failed => "\uE7BA",
        DownloadStatus.Cancelled => "\uE711",
        _ => "\uE896"
    };

    public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CancelVisibility => Status == DownloadStatus.Downloading && IsCancellable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProgressBarVisibility => Status == DownloadStatus.Downloading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatusGlyphVisibility => Status == DownloadStatus.Downloading ? Visibility.Collapsed : Visibility.Visible;

    public void SetByteProgress(long progressedBytes, long totalBytes)
    {
        DownloadManager.EnqueueUi(() =>
        {
            if (status != DownloadStatus.Downloading)
                return;

if (totalBytes <= 0)
        {
            // no content-length: stay indeterminate but reflect the growing size
            lastProgressedBytes = progressedBytes;
            if (!isIndeterminate)
                IsIndeterminate = true;

            var nowTicks = Environment.TickCount64;
            if (nowTicks - lastRaiseTicks >= RaiseInterval.TotalMilliseconds)
            {
                lastRaiseTicks = nowTicks;
                UpdateStatusText();
            }
            return;
        }

            if (isIndeterminate)
                IsIndeterminate = false;

            lastTotalBytes = totalBytes;

            // measure bytes and time over the same window so the reading is
            // the true transfer rate; lastprogressedbytes tracks the latest
            // chunk for the size text, but speed uses its own baseline
            var now = Environment.TickCount64;
            var deltaBytes = progressedBytes - lastSpeedBytes;
            var deltaMs = now - lastSpeedTicks;

            if (deltaMs >= 400 && deltaBytes >= 0)
            {
                var instant = deltaBytes * 1000.0 / deltaMs;
                smoothedSpeed = smoothedSpeed <= 0 ? instant : smoothedSpeed * 0.7 + instant * 0.3;
                lastSpeedBytes = progressedBytes;
                lastSpeedTicks = now;
            }

            lastProgressedBytes = progressedBytes;

            var percent = Math.Clamp(progressedBytes / (double)Math.Max(totalBytes, 1) * 100.0, 0, 100);
            var progressChanged = Math.Abs(percent - progress) >= 0.15 || firstRaise;
            var timeChanged = now - lastRaiseTicks >= RaiseInterval.TotalMilliseconds;

            if (progressChanged || timeChanged)
            {
                lastRaiseTicks = now;
                firstRaise = false;
                Progress = percent;
                UpdateStatusText();
            }
        });
    }

    public void SetIndeterminate()
    {
        DownloadManager.EnqueueUi(() =>
        {
            if (status != DownloadStatus.Downloading)
                return;
            IsIndeterminate = true;
        });
    }

    public void Complete()
    {
        DownloadManager.EnqueueUi(() =>
        {
            if (status != DownloadStatus.Downloading)
                return;

            IsIndeterminate = false;
            Progress = 100;
            Status = DownloadStatus.Completed;
        });
    }

    public void Fail(string message)
    {
        DownloadManager.EnqueueUi(() =>
        {
            if (status != DownloadStatus.Downloading)
                return;

            failMessage = message ?? "";
            Status = DownloadStatus.Failed;
        });
    }

    public void Cancel()
    {
        DownloadManager.EnqueueUi(() =>
        {
            if (status != DownloadStatus.Downloading)
                return;

            Status = DownloadStatus.Cancelled;
        });
        Cts.Cancel();
    }

    private void UpdateStatusText()
    {
        var text = status switch
        {
            DownloadStatus.Completed => $"Completed · {FormatBytes(Math.Max(lastProgressedBytes, lastTotalBytes))}",
            DownloadStatus.Failed when !string.IsNullOrWhiteSpace(failMessage)
                => $"Failed · {Truncate(failMessage, 48)}",
            DownloadStatus.Failed => "Failed",
            DownloadStatus.Cancelled => "Cancelled",
            _ when isIndeterminate => lastProgressedBytes > 0
                ? $"{FormatBytes(lastProgressedBytes)}…"
                : "Downloading…",
            _ => BuildProgressText()
        };

        StatusText = text;
    }

    private string BuildProgressText()
    {
        var parts = new List<string>
        {
            $"{FormatBytes(lastProgressedBytes)} / {FormatBytes(lastTotalBytes)}"
        };

        if (smoothedSpeed > 0)
            parts.Add($"{FormatBytes((long)smoothedSpeed)}/s");

        var remaining = lastTotalBytes - lastProgressedBytes;
        if (remaining > 0 && smoothedSpeed > 0)
            parts.Add($"ETA {FormatEta(remaining / smoothedSpeed)}");

        return string.Join(" · ", parts);
    }

    private static string FormatEta(double seconds)
    {
        if (seconds < 1)
            return "<1s";

        if (seconds < 60)
            return $"{Math.Ceiling(seconds):0}s";

        var minutes = (int)(seconds / 60);
        var secs = (int)(seconds % 60);

        if (minutes < 60)
            return $"{minutes}m {secs}s";

        return $"{minutes / 60}h {minutes % 60}m";
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < 4)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} B" : $"{value:0.0} {GetUnit(unit)}";
    }

    private static string GetUnit(int unit) => unit switch
    {
        1 => "KB",
        2 => "MB",
        3 => "GB",
        _ => "TB"
    };

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string GetGlyph(DownloadKind kind) => kind switch
    {
        DownloadKind.Minecraft => "\uE7FC",
        DownloadKind.Mod => "\uE97A",
        DownloadKind.ResourcePack => "\uEB9F",
        DownloadKind.Modpack => "\uE8D7",
        DownloadKind.Update => "\uE895",
        DownloadKind.Skin => "\uE8B7",
        DownloadKind.Theme => "\uE790",
        _ => "\uE896"
    };

    private void OnUiPropertyChanged(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            OnPropertyChanged(propertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetUiField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}