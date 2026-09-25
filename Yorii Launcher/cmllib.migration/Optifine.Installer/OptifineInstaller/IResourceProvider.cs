using System.IO;

namespace Optifine.Installer
{
    public interface IResourceProvider
    {
        Stream GetResourceStream(string path);
    }
}