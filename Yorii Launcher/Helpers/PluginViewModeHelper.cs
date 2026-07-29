using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Yorii_Launcher.Helpers
{
    public enum PluginViewMode
    {
        List,
        Grid
    }

    public static class PluginViewModeHelper
    {
        // swap the listview template between a grid layout and a list layout based on what the user picked
        public static void Apply(ListView listView, PluginViewMode mode)
        {
            var resources = Application.Current.Resources;

            listView.ItemsPanel = (ItemsPanelTemplate)resources[mode == PluginViewMode.Grid ? "PluginGridItemsPanel" : "PluginListItemsPanel"];
            listView.ItemContainerStyle = (Style)resources[mode == PluginViewMode.Grid ? "PluginGridViewItemContainerStyle" : "PluginListViewItemContainerStyle"];
            listView.Padding = new Thickness(0);
        }

        public static void ApplyFromSelectedIndex(ListView listView, int selectedIndex)
        {
            Apply(listView, selectedIndex == 1 ? PluginViewMode.Grid : PluginViewMode.List);
        }
    }
}
