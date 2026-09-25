using System.IO;
using System.IO.Compression;

namespace Optifine.Installer
{
    public class ZipResourceProvider : IResourceProvider
    {
        private readonly ZipArchive zipArchive;

        public ZipResourceProvider(ZipArchive zipArchive)
        {
            this.zipArchive = zipArchive;
        }

        public Stream GetResourceStream(string path)
        {
            path = Utils.RemovePrefix(path, "/");

            ZipArchiveEntry entry = zipArchive.GetEntry(path);

            if (entry == null)
                return null;

            return entry.Open();
        }
    }
}