using YamlDotNet.Serialization;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers;

[YamlStaticContext]
[YamlSerializable(typeof(UserSettings))]
[YamlSerializable(typeof(ThemeSettings))]
[YamlSerializable(typeof(PlayerAccount))]
[YamlSerializable(typeof(PlayerAccountType))]
[YamlSerializable(typeof(InstanceMetadata))]
[YamlSerializable(typeof(ThemeDefinition))]
[YamlSerializable(typeof(ThemeDetails))]
[YamlSerializable(typeof(ThemeCatalog))]
[YamlSerializable(typeof(ThemeCatalogEntry))]
public sealed partial class LauncherYamlContext : StaticContext
{
}
