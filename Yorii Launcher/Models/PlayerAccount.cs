using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models
{
    public sealed class PlayerAccount
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Password { get; set; }
        public PlayerAccountType AccountType { get; set; } = PlayerAccountType.ElyBy;
        public string? MojangIdentifier { get; set; }

        [JsonIgnore]
        public bool IsOffline => AccountType != PlayerAccountType.Mojang && string.IsNullOrWhiteSpace(Password);

        [JsonIgnore]
        public string DisplayName => AccountType == PlayerAccountType.Mojang
            ? $"{Username} (Microsoft)"
            : IsOffline
                ? $"{Username} (Offline)"
                : $"{Username} ({GetAccountTypeLabel(AccountType)})";

        public static string GetAccountTypeLabel(PlayerAccountType accountType)
        {
            return accountType switch
            {
                PlayerAccountType.ElyBy => "Ely.by",
                PlayerAccountType.Mojang => "Mojang",
                _ => accountType.ToString()
            };
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<PlayerAccountType>))]
    public enum PlayerAccountType
    {
        ElyBy,
        Mojang
    }
}
