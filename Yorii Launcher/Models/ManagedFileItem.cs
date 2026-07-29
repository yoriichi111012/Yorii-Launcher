using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yorii_Launcher.Models
{
    public partial class ManagedFileItem : INotifyPropertyChanged
    {
        private bool isEnabled;

        public string Name { get; set; } = "";

        public string Version { get; set; } = "";

        public string FilePath { get; set; } = "";

        public string ProjectType { get; set; } = "";

        public string ModrinthSlug { get; set; } = "";

        public ImageSource? Icon { get; set; }

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
