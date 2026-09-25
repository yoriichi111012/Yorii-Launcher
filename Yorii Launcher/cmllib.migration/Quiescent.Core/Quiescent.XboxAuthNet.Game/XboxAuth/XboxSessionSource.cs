using Quiescent.XboxAuthNet.Game.SessionStorages;

namespace Quiescent.XboxAuthNet.Game.XboxAuth;

public class XboxSessionSource : SessionFromStorage<XboxAuthTokens>
{
    private static XboxSessionSource? _default;
    public static XboxSessionSource Default => _default ??= new();

    public static string KeyName { get; } = "XboxTokens";

    public XboxSessionSource()
     : base(KeyName)
    {

    }
}
