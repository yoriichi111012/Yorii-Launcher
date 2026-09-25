namespace CmlLib.Core.Installer.NeoForge;

public class UnsupportedNeoForgeVersionException : Exception
{
    public UnsupportedNeoForgeVersionException() : base() { }

    public UnsupportedNeoForgeVersionException(string versionName) : 
        base($"The installer does not support this forge version: {versionName}")
    {

    }
}
