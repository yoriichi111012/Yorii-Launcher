using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        public void ApplyFilters()
        {
            FilteredVersions.Clear();

            foreach (var version in AllVersions)
            {
                if (!ShowSnapshots && version.IsSnapshot)
                    continue;

                if (!ShowFabric && version.IsFabric)
                    continue;

                if (!ShowOld && version.IsOld)
                    continue;

                FilteredVersions.Add(version.Name);
            }

            MainWindow.Instance?.LoadSavedVersion();
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