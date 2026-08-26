using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Yorii_Launcher.Models
{
    // one entry in the home-page player picker: a real account, or the special
    // "add player" / "manage players" rows. the 16x16 skin head is loaded
    // asynchronously (local skin first, no network) and pops in via inpc
    public sealed class AccountComboItem : INotifyPropertyChanged
    {
        public PlayerAccount? Account { get; init; }
        public bool IsAddNew { get; init; }
        public bool IsManagePlayers { get; init; }

        public string DisplayName =>
            IsAddNew ? "Add player" : IsManagePlayers ? "Manage players" : Account?.Username ?? "";

        // segoe mdl2: e710 = add (+), e70f = edit (pencil)
        public string IconGlyph => IsAddNew ? "\uE710" : IsManagePlayers ? "\uE70F" : "";

        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                if (ReferenceEquals(_previewImage, value)) return;
                _previewImage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static AccountComboItem AddNew { get; } = new() { IsAddNew = true };
        public static AccountComboItem ManagePlayers { get; } = new() { IsManagePlayers = true };

        public static AccountComboItem ForAccount(PlayerAccount account) => new() { Account = account };

        // x:bind helpers for the player-picker item template
        public static Visibility HeadVisibility(ImageSource? source) =>
            source is null ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility GlyphVisibility(string glyph) =>
            string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;

        public override string ToString() => DisplayName;
    }
}
