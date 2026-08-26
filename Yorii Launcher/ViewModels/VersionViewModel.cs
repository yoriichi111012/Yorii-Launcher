using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Yorii_Launcher.ViewModels
{
    public partial class VersionViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<VersionItem> AllVersions { get; }
            = [];

        public ObservableCollection<string> FilteredVersions { get; }
            = [];

        private bool showSnapshots = true;
        public bool ShowSnapshots
        {
            get => showSnapshots;
            set
            {
                showSnapshots = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool showFabric = true;
        public bool ShowFabric
        {
            get => showFabric;
            set
            {
                showFabric = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool showForge = true;
        public bool ShowForge
        {
            get => showForge;
            set
            {
                showForge = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool showNeoForge = true;
        public bool ShowNeoForge
        {
            get => showNeoForge;
            set
            {
                showNeoForge = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // private bool showoptifine = true
        // public bool showoptifine
        // {
        // get => showoptifine
        // set
        // {
        // showoptifine = value
        // onpropertychanged()
        // applyfilters()
        // }
        // }

        private bool showOld = false;
        public bool ShowOld
        {
            get => showOld;
            set
            {
                showOld = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // filter the version list based on user's toggle state in settings
        public void ApplyFilters()
        {
            FilteredVersions.Clear();

            foreach (var version in AllVersions
                .OrderByDescending(x => x.IsInstalled)
                .ThenByDescending(GetVersionSortKey)
                .ThenBy(GetLoaderTier)
                .ThenBy(x => x.Name))
            {
                if (!ShowSnapshots && version.IsSnapshot)
                    continue;

                if (!ShowFabric && version.IsFabric)
                    continue;

                if (!ShowForge && version.IsForge)
                    continue;

                if (!ShowNeoForge && version.IsNeoForge)
                    continue;

                // if (!showoptifine && version.isoptifine)
                // continue

                if (!ShowOld && version.IsOld)
                    continue;

                FilteredVersions.Add(version.Name);
            }

            // re-select saved version after filtering so it doesnt jump around
            MainWindow.Instance?.LoadSavedVersion();
        }

        // base minecraft version so the list groups like
        // fabric 26.2 / neoforge 26.2 / forge 26.2 / 26.2-snapshot / 26.2
        // fabric 26.1 /
        // returns a zero-padded comparable string so "26.2" beats "1.21.11"
        private static string GetVersionSortKey(VersionItem v)
        {
            string name = v.Name;
            string versionPart = name;

            if (name.StartsWith("Fabric ", StringComparison.Ordinal))
                versionPart = name[7..];
            else if (name.StartsWith("NeoForge ", StringComparison.Ordinal))
                versionPart = name[9..];
            else             if (name.StartsWith("Forge ", StringComparison.Ordinal))
                versionPart = name[6..];
            // else if (name.startswith("optifine ", stringcomparison.ordinal))
            // versionpart = name[9..]

            // strip pre-release suffixes (e.g. "26.2-snapshot-3", "26.2-pre1")
            // so they sort with their base release, and split into numeric parts
            int[] baseParts = GetBaseVersionParts(versionPart);

            // fixed-width zero-padded parts keep numeric ordering correct
            // ("26.2" vs "1.21.11": 00000026... > 00000001...)
            return string.Join(".", baseParts.Select(p => p.ToString("D8")));
        }

        // loader tier so the group shows loaders above the plain release
        // with forge above snapshots: fabric < neoforge < forge < snapshot < vanilla
        private static int GetLoaderTier(VersionItem v)
        {
            if (v.IsFabric) return 0;
            if (v.IsNeoForge) return 1;
            if (v.IsForge) return 2;
            // if (v.isoptifine) return 3
            if (v.IsSnapshot) return 4;
            return 5;
        }

        private static int[] GetBaseVersionParts(string version)
        {
            var match = Regex.Match(version, @"^\d+(\.\d+)*");
            if (!match.Success)
                return [];

            return match.Value
                .Split('.')
                .Select(int.Parse)
                .ToArray();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }
}