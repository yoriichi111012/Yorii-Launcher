using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Yorii_Launcher.Pages;

namespace Yorii_Launcher;

public sealed partial class ModsPage : Page
{
    public ModsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ModsFrame.Navigate(typeof(Pages.InstalledModsPage), null, new SuppressNavigationTransitionInfo());
        ModsNavigation.SelectedItem = ModsNavigation.MenuItems[0];
    }

    private void ModsNavigation_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer == null)
            return;

        var tag =
            args.SelectedItemContainer.Tag?.ToString();

        switch (tag)
        {
            case "installedmods":
                if (ModsFrame.CurrentSourcePageType != typeof(InstalledModsPage))
                {
                    ModsFrame.Navigate(typeof(Pages.InstalledModsPage), null, new SuppressNavigationTransitionInfo());
                }

                break;

            case "downloadmods":
                if (ModsFrame.CurrentSourcePageType != typeof(DownloadModsPage))
                {
                    ModsFrame.Navigate(typeof(Pages.DownloadModsPage), null, new SuppressNavigationTransitionInfo());
                }
                break;

            case "installedmodpacks":
                if (ModsFrame.CurrentSourcePageType != typeof(InstalledModpacksPage))
                {
                    ModsFrame.Navigate(typeof(Pages.InstalledModpacksPage), null, new SuppressNavigationTransitionInfo());
                }

                break;

            case "installedrspacks":
                if (ModsFrame.CurrentSourcePageType != typeof(InstalledResourcePacksPage))
                {
                    ModsFrame.Navigate(typeof(Pages.InstalledResourcePacksPage), null, new SuppressNavigationTransitionInfo());
                }

                break;

            case "downloadrspacks":
                if (ModsFrame.CurrentSourcePageType != typeof(DownloadResourcePacksPage))
                {
                    ModsFrame.Navigate(typeof(Pages.DownloadResourcePacksPage), null, new SuppressNavigationTransitionInfo());
                }
                break;

            case "downloadmodpacks":
                if (ModsFrame.CurrentSourcePageType != typeof(DownloadModpacksPage))
                {
                    ModsFrame.Navigate(typeof(Pages.DownloadModpacksPage), null, new SuppressNavigationTransitionInfo());
                }
                break;
        }
    }
}
