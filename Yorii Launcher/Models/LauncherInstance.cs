using Microsoft.UI.Xaml.Media.Imaging;

namespace Yorii_Launcher.Models
{
    public class LauncherInstance
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? IconPath { get; set; }
        public string MinecraftPath { get; set; } = "";
        public string InstancePath { get; set; } = "";
        public string? MinecraftVersion { get; set; }
        public string? CreatedAt { get; set; }
        public string? LastPlayedAt { get; set; }
        public BitmapImage? Icon { get; set; }
    }
}
