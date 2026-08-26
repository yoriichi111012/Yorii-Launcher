using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Yorii_Launcher.Helpers
{
    // one place for the shared content dialog look so every dialog in the
    // app has the same size instead of each one doing its own thing
    public static class DialogHelper
    {
        // tighter than the winui default of 548 so simple forms dont look huge
        // must be set through the contentdialogmaxwidth resource, not the
        // control's maxwidth property - the control also contains the fullscreen
        // dimming backdrop, so shrinking the control left aligns the whole
        // popup and the dialog ends up stuck on the left side of the window
        public const double MaxWidth = 440;

        public static void Apply(ContentDialog dialog)
        {
            dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        }

        // the dark acrylic brush reads wrong in light mode, so pick the
        // white-tinted variant there. resolved from settings rather than
        // theme dictionaries because the app switches themes through the
        // root element, which app-level indexer lookups don't follow
        public static Brush GetAcrylicBrush()
        {
            var key = ThemeHelper.GetCurrentTheme() == ElementTheme.Light
                ? "CustomAcrylicBrushLight"
                : "CustomAcrylicBrush";

            return (Brush)Application.Current.Resources[key];
        }
    }
}
