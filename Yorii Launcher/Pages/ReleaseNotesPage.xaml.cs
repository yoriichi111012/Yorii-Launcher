using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Pages
{
    public sealed partial class ReleaseNotesPage : Page
    {
        private MinecraftReleaseNotesService? releaseNotesService;
        private bool isLoadingReleaseNotes;
        private CancellationTokenSource? loadCts;
        private MinecraftReleaseNote? lastSelectedReleaseNote;
        private string? requestedVersion;
        public ReleaseNotesPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            requestedVersion = e.Parameter as string;

            // reset progress state before reloading
            releaseNotesProgressRing.IsActive = false;
            releaseNotesProgressRing.Visibility = Visibility.Collapsed;
            releaseNotesErrorText.Visibility = Visibility.Collapsed;
            isLoadingReleaseNotes = false;

            _ = LoadReleaseNotesAsync();
        }

        // fetch release notes, match to selected version
        private async Task LoadReleaseNotesAsync()
        {
            if (isLoadingReleaseNotes)
                return;

            isLoadingReleaseNotes = true;
            releaseNotesErrorText.Visibility = Visibility.Collapsed;

            try
            {
                if (releaseNotesService == null)
                    releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);

                var entries = await releaseNotesService.GetReleaseNotesAsync();
                releaseNotesVersionComboBox.ItemsSource = entries;

                // preserve user's manual selection, otherwise default to latest
                MinecraftReleaseNote? match = null;

                if (!string.IsNullOrWhiteSpace(requestedVersion))
                    match = entries.FirstOrDefault(e =>
                        string.Equals(e.Version, requestedVersion, StringComparison.OrdinalIgnoreCase));

                if (match == null && lastSelectedReleaseNote != null)
                    match = entries.FirstOrDefault(e =>
                        string.Equals(e.Version, lastSelectedReleaseNote.Version, StringComparison.OrdinalIgnoreCase));

                match ??= entries.FirstOrDefault();

                if (match != null)
                {
                    lastSelectedReleaseNote = match;
                    releaseNotesVersionComboBox.SelectedItem = match;
                    await LoadReleaseNoteHtmlAsync(match, CancellationToken.None);
                }
            }
            catch
            {
                releaseNotesScrollView.Visibility = Visibility.Collapsed;
                releaseNotesErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                releaseNotesProgressRing.IsActive = false;
                releaseNotesProgressRing.Visibility = Visibility.Collapsed;
                isLoadingReleaseNotes = false;
            }
        }

        // cancel previous load if switching versions fast
        private async void ReleaseNotesVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingReleaseNotes)
                return;

            loadCts?.Cancel();
            loadCts = new CancellationTokenSource();

            if (releaseNotesVersionComboBox.SelectedItem is MinecraftReleaseNote note)
            {
                lastSelectedReleaseNote = note;

                try
                {
                    await LoadReleaseNoteHtmlAsync(note, loadCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        // render html then wait 1s and release memory
        private async Task LoadReleaseNoteHtmlAsync(MinecraftReleaseNote releaseNote, CancellationToken ct)
        {
            releaseNotesErrorText.Visibility = Visibility.Collapsed;

            if (releaseNotesService == null)
                releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);

            // only show progress ring when fetching from the internet
            var cached = await releaseNotesService.IsHtmlCached(releaseNote);
            if (!cached)
            {
                releaseNotesProgressRing.IsActive = true;
                releaseNotesProgressRing.Visibility = Visibility.Visible;
            }

            try
            {

                var html = await releaseNotesService.GetReleaseNoteHtmlAsync(releaseNote);
                ct.ThrowIfCancellationRequested();

                var rendered = ReleaseNotesRenderer.RenderCached(releaseNote.Version, html);
                html = null;

                releaseNotesContentControl.Content = rendered;
                releaseNotesScrollView.Visibility = Visibility.Visible;

                await Task.Delay(1000, ct);
                MemoryOptimizer.ReduceMemory();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                releaseNotesScrollView.Visibility = Visibility.Collapsed;
                releaseNotesErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                releaseNotesProgressRing.IsActive = false;
                releaseNotesProgressRing.Visibility = Visibility.Collapsed;
            }
        }
    }
}