using System;
using System.IO;

namespace Optifine.Installer.Xdelta
{
    public class GDiffPatcher
    {
        public static void Patch(ISeekableSource source, Stream patch, Stream @out)
        {
            var outOS = new BinaryWriter(@out);
            var patchIS = new BinaryReader(patch);

            try
            {
                if (patchIS.ReadByte() != 0xD1 ||
                    patchIS.ReadByte() != 0xFF ||
                    patchIS.ReadByte() != 0xD1 ||
                    patchIS.ReadByte() != 0xFF ||
                    patchIS.ReadByte() != 0x04)
                {
                    Console.Error.WriteLine("magic string not found, aborting!");
                    return;
                }

                while (patch.CanSeek && patch.Position < patch.Length)
                {
                    long loffset;
                    int command = patchIS.ReadByte();
                    int length, offset;
                    switch (command)
                    {
                        case 0:
                            continue;
                        case 1:
                            Append(1, patchIS, outOS);
                            continue;
                        case 2:
                            Append(2, patchIS, outOS);
                            continue;
                        case 246:
                            Append(246, patchIS, outOS);
                            continue;
                        case 247:
                            length = Utils.ReadUnsignedShort(patchIS);
                            Append(length, patchIS, outOS);
                            continue;
                        case 248:
                            length = Utils.ReadInt(patchIS);
                            Append(length, patchIS, outOS);
                            continue;
                        case 249:
                            offset = Utils.ReadUnsignedShort(patchIS);
                            length = patchIS.ReadByte();
                            Copy(offset, length, source, outOS);
                            continue;
                        case 250:
                            offset = Utils.ReadUnsignedShort(patchIS);
                            length = Utils.ReadUnsignedShort(patchIS);
                            Copy(offset, length, source, outOS);
                            continue;
                        case 251:
                            offset = Utils.ReadUnsignedShort(patchIS);
                            length = Utils.ReadInt(patchIS);
                            Copy(offset, length, source, outOS);
                            continue;
                        case 252:
                            offset = Utils.ReadInt(patchIS);
                            length = patchIS.ReadByte();
                            Copy(offset, length, source, outOS);
                            continue;
                        case 253:
                            offset = Utils.ReadInt(patchIS);
                            length = Utils.ReadUnsignedShort(patchIS);
                            Copy(offset, length, source, outOS);
                            continue;
                        case 254:
                            offset = Utils.ReadInt(patchIS);
                            length = Utils.ReadInt(patchIS);
                            Copy(offset, length, source, outOS);
                            continue;
                        case 255:
                            loffset = Utils.ReadLong(patchIS);
                            length = Utils.ReadInt(patchIS);
                            Copy(loffset, length, source, outOS);
                            continue;
                    }
                    Append(command, patchIS, outOS);
                }
            }
            finally
            {
                outOS.Flush();
            }
        }

        private static void Copy(long offset, int length, ISeekableSource source, BinaryWriter outOS)
        {
            if (offset + length > source.Length)
            {
                throw new InvalidDataException("truncated source file, aborting");
            }
            byte[] buf = new byte[256];
            source.Seek(offset);
            while (length > 0)
            {
                int len = Math.Min(256, length);
                int res = source.Read(buf, 0, len);
                if (res <= 0) break;
                outOS.Write(buf, 0, res);
                length -= res;
            }
        }

        private static void Append(int length, BinaryReader patchIS, BinaryWriter outOS)
        {
            byte[] buf = new byte[256];
            while (length > 0)
            {
                int len = Math.Min(256, length);
                int res = patchIS.Read(buf, 0, len);
                if (res <= 0) break;
                outOS.Write(buf, 0, res);
                length -= res;
            }
        }
    }
}