using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher.Models;

public sealed class ThemeMarketplaceItem : INotifyPropertyChanged
{
    private string themeName = "";
    private string author = "";
    private string description = "";
    private bool isInstalled;
    private bool isActive;
    private bool isOwnTheme;
    private BitmapImage? previewImage;

    public string ThemeName
    {
        get => themeName;
        set => SetField(ref themeName, value);
    }

    public string Author
    {
        get => author;
        set => SetField(ref author, value);
    }

    public string Description
    {
        get => description;
        set => SetField(ref description, value);
    }

    public bool IsInstalled
    {
        get => isInstalled;
        set
        {
            if (SetField(ref isInstalled, value))
            {
                OnPropertyChanged(nameof(InstallButtonVisibility));
                OnPropertyChanged(nameof(ApplyButtonVisibility));
                OnPropertyChanged(nameof(ActiveIndicatorVisibility));
                OnPropertyChanged(nameof(UninstallButtonVisibility));
            }
        }
    }

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (SetField(ref isActive, value))
            {
                OnPropertyChanged(nameof(InstallButtonVisibility));
                OnPropertyChanged(nameof(ApplyButtonVisibility));
                OnPropertyChanged(nameof(ActiveIndicatorVisibility));
            }
        }
    }

    public bool IsOwnTheme
    {
        get => isOwnTheme;
        set
        {
            if (SetField(ref isOwnTheme, value))
            {
                OnPropertyChanged(nameof(CatalogDeleteButtonVisibility));
            }
        }
    }

    public BitmapImage? PreviewImage
    {
        get => previewImage;
        set
        {
            if (SetField(ref previewImage, value))
                OnPropertyChanged(nameof(PreviewPlaceholderVisibility));
        }
    }

    public Visibility PreviewPlaceholderVisibility => previewImage is null ? Visibility.Visible : Visibility.Collapsed;

    // guards against duplicate lazy preview requests for the same item
    internal bool PreviewRequested { get; set; }

    public Visibility InstallButtonVisibility => isInstalled ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ApplyButtonVisibility => isInstalled && !isActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActiveIndicatorVisibility => isInstalled && isActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UninstallButtonVisibility => isInstalled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CatalogDeleteButtonVisibility => isOwnTheme ? Visibility.Visible : Visibility.Collapsed;

    public ThemeCatalogEntry Entry { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
