using System;
using Windows.UI;

namespace Yorii_Launcher.Helpers
{
    public sealed class AccentPalette
    {
        public Color Base { get; init; }
        public Color Light1 { get; init; }
        public Color Light2 { get; init; }
        public Color Light3 { get; init; }
        public Color Dark1 { get; init; }
        public Color Dark2 { get; init; }
        public Color Dark3 { get; init; }
        // black or white, whichever reads better on base
        public Color TextOnBase { get; init; }
    }

    // generates the fluent accent palette from a single base color, mirroring the algorithm
    // extracted from the ms fluent xaml theme editor (colorpalette / colorscale / colorblending)
    // an rgb scale of [white .. base .. black] trimmed to [0.185 .. 0.84], an lch saturation
    // boost on the trimmed endpoints (only when the base is saturated enough), a 25% overlay
    // blend on the dark end, then 7 evenly spaced samples. the samples map to the windows
    // systemaccentcolor* shades: index 3 is the base, indices 0-2 are light3/2/1 and
    // indices 4-6 are dark1/2/3
    public static class AccentColorGenerator
    {
        private const double ClipLight = 0.185;
        private const double ClipDark = 0.160;
        private const double SaturationAdjustmentCutoff = 0.05;
        private const double SaturationLight = 0.35;
        private const double SaturationDark = 1.25;
        private const double OverlayDark = 0.25;
        private const double SaturationConstant = 18.0;

        public static AccentPalette Generate(Color baseColor)
        {
            var baseRgb = (baseColor.R, baseColor.G, baseColor.B);
            var white = ((byte)255, (byte)255, (byte)255);
            var black = ((byte)0, (byte)0, (byte)0);

            // trimmed endpoints of the [white .. base .. black] scale, interpolated in byte space
            var trimmedLight = LerpRgb(white, baseRgb, ClipLight / 0.5);
            var trimmedDark = LerpRgb(baseRgb, black, (1.0 - ClipDark - 0.5) / 0.5);

            var adjustedLight = Norm(trimmedLight);
            var adjustedDark = Norm(trimmedDark);

            // skip saturation boosting for near-gray base colors to avoid color noise
            if (RgbToSaturation(baseRgb) >= SaturationAdjustmentCutoff)
            {
                adjustedLight = SaturateLch(adjustedLight, SaturationLight);
                adjustedDark = SaturateLch(adjustedDark, SaturationDark);
            }

            // overlay blend between base and the dark end, mixed in at 25% (rgb space)
            var overlay = BlendOverlay(Norm(baseRgb), adjustedDark);
            adjustedDark = LerpRgbDouble(adjustedDark, overlay, OverlayDark);

            var lightEnd = Denorm(adjustedLight);
            var darkEnd = Denorm(adjustedDark);

            return new AccentPalette
            {
                Base = ToColor(baseRgb),
                Light3 = ToColor(lightEnd),
                Light2 = ToColor(LerpRgb(lightEnd, baseRgb, 1.0 / 3.0)),
                Light1 = ToColor(LerpRgb(lightEnd, baseRgb, 2.0 / 3.0)),
                Dark1 = ToColor(LerpRgb(baseRgb, darkEnd, 1.0 / 3.0)),
                Dark2 = ToColor(LerpRgb(baseRgb, darkEnd, 2.0 / 3.0)),
                Dark3 = ToColor(darkEnd),
                TextOnBase = ToColor(ChooseTextColor(baseRgb))
            };
        }

        // hsl saturation of the base color, used for the saturation-adjustment cutoff
        private static double RgbToSaturation((byte r, byte g, byte b) c)
        {
            double r = c.r / 255.0, g = c.g / 255.0, b = c.b / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            if (delta == 0)
                return 0;
            double lit = (max + min) / 2;
            return delta / (1 - Math.Abs(2 * lit - 1));
        }

        // lch chroma boost (saturatevialch): c += saturation * 18
        private static (double r, double g, double b) SaturateLch((double r, double g, double b) rgb, double saturation)
        {
            var (L, C, H) = RgbToLch(rgb);
            double c = C + saturation * SaturationConstant;
            if (c < 0)
                c = 0;
            return LchToRgb(L, c, H);
        }

        private static (double L, double C, double H) RgbToLch((double r, double g, double b) rgb)
        {
            var (L, A, B) = RgbToLab(rgb);
            // zero out rounded zeroes to keep atan2 from returning pi instead of 0
            double l = L == 0 ? 0 : L;
            double a = A == 0 ? 0 : A;
            double b = B == 0 ? 0 : B;
            double h = (Math.Atan2(b, a) * (180.0 / Math.PI) + 360.0) % 360.0;
            double c = Math.Sqrt(a * a + b * b);
            return (l, c, h);
        }

        private static (double L, double A, double B) RgbToLab((double r, double g, double b) rgb)
        {
            return XyzToLab(RgbToXyz(rgb), true);
        }

        private static (double X, double Y, double Z) RgbToXyz((double r, double g, double b) rgb)
        {
            static double Linearize(double i) => i <= 0.04045 ? i / 12.92 : Math.Pow((i + 0.055) / 1.055, 2.4);

            double r = Linearize(rgb.r);
            double g = Linearize(rgb.g);
            double b = Linearize(rgb.b);

            // d65 2 degree constants
            return (
                0.4124564 * r + 0.3575761 * g + 0.1804375 * b,
                0.2126729 * r + 0.7151522 * g + 0.0721750 * b,
                0.0193339 * r + 0.1191920 * g + 0.9503041 * b);
        }

        private static (double L, double A, double B) XyzToLab((double X, double Y, double Z) xyz, bool round)
        {
            static double F(double i) => i > 0.008856452 ? Math.Pow(i, 1.0 / 3.0) : i / 0.12841855 + 0.137931034;

            double x = F(xyz.X / 0.95047);
            double y = F(xyz.Y);
            double z = F(xyz.Z / 1.08883);

            double l = 116.0 * y - 16.0;
            double a = 500.0 * (x - y);
            double b = 200.0 * (y - z);

            if (round)
                return (Math.Round(l, 4), Math.Round(a, 4), Math.Round(b, 4));
            return (l, a, b);
        }

        private static (double r, double g, double b) LchToRgb(double l, double c, double h)
        {
            double a = h != 0 ? Math.Cos(h * (Math.PI / 180.0)) * c : 0;
            double b = h != 0 ? Math.Sin(h * (Math.PI / 180.0)) * c : 0;

            // lab -> xyz
            double y = (l + 16.0) / 116.0;
            double x = y + a / 500.0;
            double z = y - b / 200.0;

            double Finv(double i) => i > 0.206896552 ? i * i * i : 0.12841855 * (i - 0.137931034);

            double X = 0.95047 * Finv(x);
            double Y = Finv(y);
            double Z = 1.08883 * Finv(z);

            // xyz -> rgb
            double G(double i) => i <= 0.0031308 ? i * 12.92 : 1.055 * Math.Pow(i, 1.0 / 2.4) - 0.055;

            double r = G(3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z);
            double g = G(-0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z);
            double blue = G(0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z);

            return (ClampUnit(r), ClampUnit(g), ClampUnit(blue));
        }

        private static (double r, double g, double b) BlendOverlay((double r, double g, double b) bottom, (double r, double g, double b) top)
        {
            static double Channel(double b, double t) =>
                b < 0.5 ? ClampUnit(2.0 * t * b) : ClampUnit(1.0 - 2.0 * (1.0 - t) * (1.0 - b));

            return (Channel(bottom.r, top.r), Channel(bottom.g, top.g), Channel(bottom.b, top.b));
        }

        private static (byte r, byte g, byte b) LerpRgb((byte r, byte g, byte b) left, (byte r, byte g, byte b) right, double t)
        {
            if (t <= 0)
                return left;
            if (t >= 1)
                return right;

            byte Channel(byte l, byte r) => (byte)Math.Round(l + t * (r - l));

            return (Channel(left.r, right.r), Channel(left.g, right.g), Channel(left.b, right.b));
        }

        private static (double r, double g, double b) LerpRgbDouble((double r, double g, double b) left, (double r, double g, double b) right, double t)
        {
            if (t <= 0)
                return left;
            if (t >= 1)
                return right;

            return (left.r + t * (right.r - left.r), left.g + t * (right.g - left.g), left.b + t * (right.b - left.b));
        }

        private static (byte r, byte g, byte b) ChooseTextColor((byte r, byte g, byte b) background)
        {
            double luma = Luminance(background);
            double contrastWhite = 1.05 / (luma + 0.05);
            double contrastBlack = (luma + 0.05) / 0.05;

            return contrastWhite >= contrastBlack
                ? ((byte)255, (byte)255, (byte)255)
                : ((byte)0, (byte)0, (byte)0);
        }

        private static double Luminance((byte r, byte g, byte b) c)
        {
            static double L(double i)
            {
                i /= 255.0;
                return i <= 0.03928 ? i / 12.92 : Math.Pow((i + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * L(c.r) + 0.7152 * L(c.g) + 0.0722 * L(c.b);
        }

        private static (double r, double g, double b) Norm((byte r, byte g, byte b) c) =>
            (c.r / 255.0, c.g / 255.0, c.b / 255.0);

        private static (byte r, byte g, byte b) Denorm((double r, double g, double b) c) =>
            (ClampByte(c.r * 255.0), ClampByte(c.g * 255.0), ClampByte(c.b * 255.0));

        private static byte ClampByte(double c)
        {
            if (double.IsNaN(c))
                return 0;
            c = Math.Round(c);
            if (c <= 0)
                return 0;
            if (c >= 255)
                return 255;
            return (byte)c;
        }

        private static double ClampUnit(double c)
        {
            if (double.IsNaN(c))
                return 0;
            if (c <= 0)
                return 0;
            if (c >= 1)
                return 1;
            return c;
        }

        private static Color ToColor((byte r, byte g, byte b) c) => Color.FromArgb(255, c.r, c.g, c.b);
    }
}
