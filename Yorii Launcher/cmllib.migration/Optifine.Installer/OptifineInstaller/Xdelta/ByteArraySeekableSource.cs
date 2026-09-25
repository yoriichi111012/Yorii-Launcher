using System;

namespace Optifine.Installer.Xdelta
{
    public class ByteArraySeekableSource : ISeekableSource
    {
        private readonly byte[] _source;
        private long _position = 0;

        public ByteArraySeekableSource(byte[] source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            _source = source;
        }

        public void Seek(long pos)
        {
            if (pos < 0 || pos > _source.Length)
                throw new ArgumentOutOfRangeException(nameof(pos), "Position must be within the source length.");
            _position = pos;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_source is null) throw new ObjectDisposedException(nameof(ByteArraySeekableSource));
            int available = (int)(_source.Length - _position);
            if (available <= 0)
                return -1;

            if (count > available)
                count = available;

            Array.Copy(_source, _position, buffer, offset, count);
            _position += count;
            return count;
        }

        public long Length
        {
            get
            {
                if (_source is null) throw new ObjectDisposedException(nameof(ByteArraySeekableSource));
                return _source.Length;
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}