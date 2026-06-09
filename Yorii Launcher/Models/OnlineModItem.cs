using Microsoft.UI.Xaml.Media.Imaging;

namespace Yorii_Launcher.Models
{
    public class OnlineModItem
    {
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public string Slug { get; set; } = "";

        public string DownloadUrl { get; set; } = "";

        public string VersionName { get; set; } = "";

        public string VersionId { get; set; } = "";

        public string FileName { get; set; } = "";

        public BitmapImage? Icon { get; set; }
    }
}