using System.Text.Json.Serialization;

using Yorii_Launcher.Helpers;

namespace Yorii_Launcher;

[JsonSerializable(typeof(LoginHelper.ElyAuthRequest))]
[JsonSerializable(typeof(LoginHelper.ElyAuthResponse))]
[JsonSerializable(typeof(LoginHelper.ElyProfile))]
internal partial class ElyJsonContext : JsonSerializerContext
{
}