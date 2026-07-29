using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace Yorii_Launcher.Models
{
    public class WorldItem
    {
        public string Id { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public string IconData { get; set; } = "";
        public DateTime LastWriteTimeUtc { get; set; }
        public ImageSource? Icon { get; private set; }

        public void LoadIcon(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return;

            try
            {
                var clean = base64;
                var commaIdx = clean.IndexOf(',');
                if (commaIdx >= 0)
                    clean = clean[(commaIdx + 1)..];

                var bytes = Convert.FromBase64String(clean);
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.SetSource(ms.AsRandomAccessStream());
                IconData = base64;
                Icon = bmp;
            }
            catch
            {
            }
        }

        public void LoadIconFromFile(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                return;

            try
            {
                using var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bmp = new BitmapImage();
                bmp.SetSource(fs.AsRandomAccessStream());
                Icon = bmp;
            }
            catch
            {
            }
        }
    }
}
