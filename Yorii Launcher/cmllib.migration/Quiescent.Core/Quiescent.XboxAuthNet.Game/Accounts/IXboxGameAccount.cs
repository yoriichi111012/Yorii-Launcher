using Quiescent.XboxAuthNet.Game.SessionStorages;

namespace Quiescent.XboxAuthNet.Game.Accounts;

public interface IXboxGameAccount : IComparable
{
    string? Identifier { get; }
    ISessionStorage SessionStorage { get; }
}