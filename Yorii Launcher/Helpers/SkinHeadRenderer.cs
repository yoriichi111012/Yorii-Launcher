using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace Yorii_Launcher.Helpers;

// renders the front face of a minecraft skin as a crisp 2d player head. the
// head face lives at (8,8)-(16,16) in a 64-wide skin and the second "hat"
// layer at (40,8)-(48,16); we composite the hat over the face using source-over
// alpha and only where the hat actually has pixels — matching the classic mc
// layering quirk where a transparent hat region lets the base face show through
public static class SkinHeadRenderer
{
    private const int FaceSize = 8;
    private const int Scale = 4;
    private const int OutputSize = FaceSize * Scale; // 32x32, nearest-neighbor upscale

    public static async Task<WriteableBitmap?> RenderHeadAsync(byte[] pngBytes)
    {
        try
        {
            using var stream = new MemoryStream(pngBytes);
            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            if (decoder.PixelWidth < 48 || decoder.PixelHeight < 16)
                return null;

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var src = pixelData.DetachPixelData();
            int width = (int)decoder.PixelWidth;
            bool hasHat = RegionHasPixels(src, width, 40, 8, FaceSize, FaceSize);

            var output = new byte[OutputSize * OutputSize * 4];

            for (int y = 0; y < FaceSize; y++)
            {
                for (int x = 0; x < FaceSize; x++)
                {
                    int baseIndex = ((8 + y) * width + (8 + x)) * 4;
                    byte b = src[baseIndex];
                    byte g = src[baseIndex + 1];
                    byte r = src[baseIndex + 2];
                    byte a = src[baseIndex + 3];

                    if (hasHat)
                    {
                        int hatIndex = ((8 + y) * width + (40 + x)) * 4;
                        byte hb = src[hatIndex];
                        byte hg = src[hatIndex + 1];
                        byte hr = src[hatIndex + 2];
                        byte ha = src[hatIndex + 3];
                        // premultiplied source-over: hat over face
                        float oneMinus = 1f - (ha / 255f);
                        r = (byte)(hr + r * oneMinus);
                        g = (byte)(hg + g * oneMinus);
                        b = (byte)(hb + b * oneMinus);
                        a = (byte)(ha + a * oneMinus);
                    }

                    for (int dy = 0; dy < Scale; dy++)
                    {
                        int rowOffset = ((y * Scale + dy) * OutputSize + x * Scale) * 4;
                        for (int dx = 0; dx < Scale; dx++)
                        {
                            int dst = rowOffset + dx * 4;
                            output[dst] = b;
                            output[dst + 1] = g;
                            output[dst + 2] = r;
                            output[dst + 3] = a;
                        }
                    }
                }
            }

            var bitmap = new WriteableBitmap(OutputSize, OutputSize);
            using (var pixelStream = bitmap.PixelBuffer.AsStream())
            {
                await pixelStream.WriteAsync(output);
            }

            bitmap.Invalidate();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static bool RegionHasPixels(byte[] src, int width, int x0, int y0, int xCount, int yCount)
    {
        for (int y = y0; y < y0 + yCount; y++)
        {
            for (int x = x0; x < x0 + xCount; x++)
            {
                if (src[(y * width + x) * 4 + 3] > 0)
                    return true;
            }
        }

        return false;
    }
}