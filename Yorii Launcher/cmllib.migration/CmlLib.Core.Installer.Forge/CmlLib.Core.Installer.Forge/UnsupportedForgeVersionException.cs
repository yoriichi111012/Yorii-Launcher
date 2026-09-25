namespace CmlLib.Core.Installer.Forge;

public class UnsupportedForgeVersionException : Exception
{
    public UnsupportedForgeVersionException() : base() { }

    public UnsupportedForgeVersionException(string versionName) : 
        base($"The installer does not support this forge version: {versionName}")
    {

    }
}
