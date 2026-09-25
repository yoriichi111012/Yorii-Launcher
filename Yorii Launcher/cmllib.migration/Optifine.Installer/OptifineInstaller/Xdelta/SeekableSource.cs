using System;

namespace Optifine.Installer.Xdelta
{
    public interface ISeekableSource : IDisposable
    {
        void Seek(long pos);

        int Read(byte[] buffer, int offset, int count);

        long Length { get; }
    }
}