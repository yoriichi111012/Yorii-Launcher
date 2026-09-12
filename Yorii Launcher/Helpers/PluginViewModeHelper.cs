using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Yorii_Launcher.Helpers
{
    public enum PluginViewMode
    {
        List,
        Grid
    }

    public static class PluginViewModeHelper
    {
        // Dual-ListView toggle (NativeAOT-safe): each page declares a list
        // ListView (ItemsStackPanel + list container style) and a grid ListView
        // (ItemsWrapGrid + grid container style) sharing one ItemsSource; code
        // only flips Visibility — a plain bool DP on strongly-typed references.
        // no VisualStates (self-targets never resolve, Page-level states proved
        // unreliable here) and no resource-dictionary casts (fail over the WinRT
        // RCW under NativeAOT). renders exactly the pre-AOT look: same panels,
        // styles and templates Apply() used to install at runtime.
        // TEMP-DIAG(viewmode): info log per switch. remove after.
        public static void ApplyDualView(ListView listView, ListView gridView, PluginViewMode mode)
        {
            var grid = mode == PluginViewMode.Grid;
            try
            {
                if (listView is not null)
                    listView.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
                if (gridView is not null)
                    gridView.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                try { Logger.Error($"PluginViewModeHelper.ApplyDualView failed ({mode}): {ex.Message}"); } catch { }
            }
            try { Logger.Info($"PluginViewModeHelper: dual view -> {mode} (list={(listView is not null ? listView.Visibility.ToString() : "null")}, grid={(gridView is not null ? gridView.Visibility.ToString() : "null")})"); } catch { }
        }

        public static void ApplyDualViewFromSelectedIndex(ListView listView, ListView gridView, int selectedIndex)
        {
            ApplyDualView(listView, gridView, selectedIndex == 1 ? PluginViewMode.Grid : PluginViewMode.List);
        }
    }
}
