using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yorii_Launcher.Models
{
    public partial class ModItem : INotifyPropertyChanged
    {
        private bool isEnabled;

        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? FilePath { get; set; }

        public BitmapImage? Icon { get; set; }

        public string? ModId { get; set; }

        public string? CachedModrinthSlug { get; set; }

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

        protected void OnPropertyChanged(
            [CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }
}