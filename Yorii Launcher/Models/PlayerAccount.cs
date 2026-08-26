using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Yorii_Launcher.Models
{
    public sealed class PlayerAccount
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Password { get; set; }
        public PlayerAccountType AccountType { get; set; } = PlayerAccountType.YoriiSkins;
        public string? MojangIdentifier { get; set; }
        public string? CustomUUID { get; set; }
        public string? SkinUrl { get; set; }
        public string? GitHubOwner { get; set; }

        [JsonIgnore]
        [YamlIgnore]
        public bool IsOffline => AccountType == PlayerAccountType.Offline;

        [JsonIgnore]
        [YamlIgnore]
        public string DisplayName => $"{Username} ({GetAccountTypeLabel(AccountType)})";

        public static string GetAccountTypeLabel(PlayerAccountType accountType)
        {
            return accountType switch
            {
                PlayerAccountType.Mojang => "Mojang",
                PlayerAccountType.Offline => "Offline",
                PlayerAccountType.YoriiSkins => "YoriiSkins",
                _ => accountType.ToString()
            };
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<PlayerAccountType>))]
    public enum PlayerAccountType
    {
        Mojang,
        Offline,
        YoriiSkins
    }
}
