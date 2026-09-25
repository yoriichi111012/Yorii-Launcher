using CmlLib.Utils;
using ICSharpCode.SharpZipLib.Zip;

namespace CmlLib.Core.Installer.Forge.Installers;

public class JarPatcher : IDisposable
{
    public static JarPatcher Extract(string jarPath)
    {
        var installDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()); //create folder in temp
        var installerPath = Path.Combine(installDir, jarPath);

        var zip = new FastZip();
        zip.ExtractZip(installerPath, installDir, null);
        return new JarPatcher(installDir);
    }

    public JarPatcher(string path)
    {
        ExtractedPath = path;
    }

    private bool disposedValue;
    public string ExtractedPath { get; }

    public void DeleteMetaInf()
    {
        IOUtil.DeleteDirectory(Path.Combine(ExtractedPath, "META-INF"));
    }

    public void CompressToJar(string jarPath)
    {
        var zip = new FastZip();
        zip.CreateZip(jarPath, ExtractedPath, true, null);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            if (Directory.Exists(ExtractedPath))
                IOUtil.DeleteDirectory(ExtractedPath);
            disposedValue = true;
        }
    }

    ~JarPatcher()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
