using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Optifine.Installer
{
    public class Utils
    {
        public static string[] Tokenize(string str, string delim)
        {
            List<string> list = new List<string>();

            string[] tokens = str.Split(delim.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            list.AddRange(tokens);

            return list.ToArray();
        }

        public static ushort ReadUnsignedShort(BinaryReader reader)
        {
            int high = reader.ReadByte();
            int low = reader.ReadByte();
            return (ushort)(high << 8 | low);
        }

        public static int ReadInt(BinaryReader reader)
        {
            return reader.ReadByte() << 24
                         | reader.ReadByte() << 16
                         | reader.ReadByte() << 8
                         | reader.ReadByte();
        }

        public static long ReadLong(BinaryReader reader)
        {
            return (long)reader.ReadByte() << 56
                                | (long)reader.ReadByte() << 48
                                | (long)reader.ReadByte() << 40
                                | (long)reader.ReadByte() << 32
                                | (long)reader.ReadByte() << 24
                                | (long)reader.ReadByte() << 16
                                | (long)reader.ReadByte() << 8
                                | reader.ReadByte();
        }

        public static byte[] ReadAll(Stream inputStream)
        {
            MemoryStream baos = new MemoryStream();
            byte[] buf = new byte[1024];

            while (true)
            {
                int len = inputStream.Read(buf, 0, buf.Length);
                if (len <= 0)
                    break;
                baos.Write(buf, 0, len);
            }

            inputStream.Close();

            byte[] bytes = baos.ToArray();

            return bytes;
        }

        public static string RemovePrefix(string str, string prefix)
        {
            if (str == null || prefix == null)
            {
                return str;
            }

            if (str.StartsWith(prefix))
            {
                str = str.Substring(prefix.Length);
            }

            return str;
        }

        public static string ArrayToCommaSeparatedString(object[] arr)
        {
            if (arr == null)
                return "";

            StringBuilder buf = new StringBuilder();

            for (int i = 0; i < arr.Length; i++)
            {
                object val = arr[i];

                if (i > 0)
                    buf.Append(", ");

                if (val == null)
                {
                    buf.Append("null");
                }
                else if (val.GetType().IsArray)
                {
                    buf.Append('[');

                    if (val is object[] v)
                    {
                        buf.Append(ArrayToCommaSeparatedString(v));
                    }
                    else
                    {
                        int len = ((Array)val).Length;
                        for (int ai = 0; ai < len; ai++)
                        {
                            if (ai > 0)
                                buf.Append(", ");
                            buf.Append(((Array)val).GetValue(ai));
                        }
                    }

                    buf.Append(']');
                }
                else
                {
                    buf.Append(val);
                }
            }

            return buf.ToString();
        }

        public static bool IsLegacyVersion(OptifineVersion version)
        {
            int cmp = NormalizeVersion(version.MinecraftVersion)
                .CompareTo(NormalizeVersion("1.8.9"));
            if (cmp > 0) return false;
            if (cmp < 0) return true;
            char c = version.OptifineEdition.Split('_')[2][0];
            return c != 'L' && c != 'M';
        }

        public static bool IsNewVersion(OptifineVersion version)
        {
            int cmp = NormalizeVersion(version.MinecraftVersion)
                .CompareTo(NormalizeVersion("1.17.1"));
            return cmp > 0 || cmp == 0 && version.OptifineEdition != "HD_U_G9";
        }

        public static string GetLaunchwrapperVersionLegacy(OptifineVersion version)
        {
            var v = NormalizeVersion(version.MinecraftVersion);
            var baseV = NormalizeVersion("1.7.10");
            if (v.CompareTo(baseV) > 0 || v.CompareTo(baseV) == 0 && version.OptifineEdition.Split('_')[2].StartsWith("E"))
                return "1.12";
            return "1.7";
        }

        private static Version NormalizeVersion(string versionStr)
        {
            var parts = versionStr.Split('.').Select(int.Parse).ToList();
            while (parts.Count < 4) parts.Add(0);
            return new Version(parts[0], parts[1], parts[2], parts[3]);
        }
    }
}