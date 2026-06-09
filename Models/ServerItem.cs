using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Text;

namespace Yorii_Launcher.Models
{
    public class ServerItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
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
                Icon = bmp;
            }
            catch
            {
            }
        }
    }
}
