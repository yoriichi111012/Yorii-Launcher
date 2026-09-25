using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.OAuth;

namespace Quiescent.XboxAuthNet.Game.OAuth;

public class MicrosoftOAuthSessionSource : SessionFromStorage<MicrosoftOAuthResponse>
{
    private static MicrosoftOAuthSessionSource? _default;
    public static MicrosoftOAuthSessionSource Default => _default ??= new();

    public static string KeyName { get; } = "MicrosoftOAuth";
    public MicrosoftOAuthSessionSource()
     : base(KeyName)
    {

    }
}