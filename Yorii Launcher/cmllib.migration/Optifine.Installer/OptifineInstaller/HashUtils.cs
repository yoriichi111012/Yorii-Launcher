using System.Security.Cryptography;
using System.Text;

namespace Optifine.Installer
{
    public static class HashUtils
    {
        public static string ToHexString(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public static byte[] GetHashMd5(byte[] data)
        {
            using (var hasher = MD5.Create())
            {
                return hasher.ComputeHash(data);
            }
        }
    }
}