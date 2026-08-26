using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher.Pages;

public sealed partial class ThemesPage : Page
{
    public ThemesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ThemesFrame.Navigate(typeof(MyThemesPage), null, new SuppressNavigationTransitionInfo());
        ThemesNavigation.SelectedItem = ThemesNavigation.MenuItems[0];
        MemoryOptimizer.ReduceMemory();
    }

    private void ThemesNavigation_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
    {
        MemoryOptimizer.ReduceMemory();
        if (args.SelectedItemContainer == null)
            return;

        var tag = args.SelectedItemContainer.Tag?.ToString();

        switch (tag)
        {
            case "mythemes":
                if (ThemesFrame.CurrentSourcePageType != typeof(MyThemesPage))
                {
                    ThemesFrame.Navigate(typeof(MyThemesPage), null, new SuppressNavigationTransitionInfo());
                }
                break;

            case "discoverthemes":
                if (ThemesFrame.CurrentSourcePageType != typeof(DiscoverThemesPage))
                {
                    ThemesFrame.Navigate(typeof(DiscoverThemesPage), null, new SuppressNavigationTransitionInfo());
                }
                break;
        }
    }
}