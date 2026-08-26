using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class ServerManager
    {
        public static List<ServerItem> LoadServersFromMinecraftPath(string minecraftPath)
        {
            var serversDat = Path.Combine(minecraftPath, "servers.dat");
            if (!File.Exists(serversDat))
                return [];

            try
            {
                return ParseServersDat(serversDat);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to parse servers.dat: {ex.Message}");
                return [];
            }
        }

        public static string? GetSelectedServerAddress()
        {
            var addr = SettingsManager.Current.SelectedServerAddress;
            return string.IsNullOrEmpty(addr) ? null : addr;
        }

        public static void SetSelectedServerAddress(string? address)
        {
            SettingsManager.Current.SelectedServerAddress = address ?? "";
            SettingsManager.SaveSettings();
        }

        public static void AddServer(string minecraftPath, ServerItem server)
        {
            var servers = LoadServersFromMinecraftPath(minecraftPath);
            servers.Add(server);
            SaveServersToMinecraftPath(minecraftPath, servers);
        }

        public static void UpdateServer(string minecraftPath, string oldAddress, ServerItem server)
        {
            var servers = LoadServersFromMinecraftPath(minecraftPath);
            var index = servers.FindIndex(s => s.Address == oldAddress);

            if (index < 0)
                return;

            if (string.IsNullOrWhiteSpace(server.IconData))
                server.IconData = servers[index].IconData;

            servers[index] = server;
            SaveServersToMinecraftPath(minecraftPath, servers);

            if (GetSelectedServerAddress() == oldAddress)
                SetSelectedServerAddress(server.Address);
        }

        public static void DeleteServer(string minecraftPath, string address)
        {
            var servers = LoadServersFromMinecraftPath(minecraftPath);
            servers.RemoveAll(s => s.Address == address);
            SaveServersToMinecraftPath(minecraftPath, servers);

            if (GetSelectedServerAddress() == address)
                SetSelectedServerAddress(null);
        }

        public static void SaveServersToMinecraftPath(string minecraftPath, List<ServerItem> servers)
        {
            Directory.CreateDirectory(minecraftPath);
            var serversDat = Path.Combine(minecraftPath, "servers.dat");

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // root compound named ""
            writer.Write((byte)10);
            WriteString(writer, "");

            // servers list
            writer.Write((byte)9);
            WriteString(writer, "servers");
            writer.Write((byte)10);
            WriteInt32BE(writer, servers.Count);

            foreach (var server in servers)
            {
                WriteStringTag(writer, "name", server.Name);
                WriteStringTag(writer, "ip", server.Address);

                if (!string.IsNullOrWhiteSpace(server.IconData))
                    WriteStringTag(writer, "icon", server.IconData);

                writer.Write((byte)0);
            }

            writer.Write((byte)0);
            File.WriteAllBytes(serversDat, ms.ToArray());
        }

        // 0x1f 0x8b = gzip magic bytes
        private static List<ServerItem> ParseServersDat(string path)
        {
            byte[] data = File.ReadAllBytes(path);

            var servers = new List<ServerItem>();

            if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
            {
                using var ms = new MemoryStream(data);
                using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
                ReadTagPayload(reader, null, servers);
            }
            else
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
                ReadTagPayload(reader, null, servers);
            }

            return servers;
        }

        // read next nbt payload based on type byte
        private static void ReadTagPayload(BinaryReader reader, string? tagName, List<ServerItem> servers)
        {
            var type = reader.ReadByte();
            if (type == 0)
                return;

            if (tagName == null)
                tagName = ReadString(reader);

            switch (type)
            {
                case 10: // tag_compound
                    ReadCompound(reader, servers);
                    break;
                case 9: // tag_list
                    ReadList(reader, servers);
                    break;
                case 8: // tag_string
                    ReadString(reader);
                    break;
                case 3: // tag_int
                    ReadInt32BE(reader);
                    break;
                case 2: // tag_short
                    reader.ReadInt16();
                    break;
                case 5: // tag_double
                    reader.ReadDouble();
                    break;
                case 6: // tag_float
                    reader.ReadSingle();
                    break;
                case 7: // tag_byte_array
                    var arrLen = ReadInt32BE(reader);
                    reader.ReadBytes(arrLen);
                    break;
                case 11: // tag_int_array
                    var intLen = ReadInt32BE(reader);
                    for (int i = 0; i < intLen; i++) ReadInt32BE(reader);
                    break;
                case 12: // tag_long_array
                    var longLen = ReadInt32BE(reader);
                    for (int i = 0; i < longLen; i++) reader.ReadInt64();
                    break;
                case 1: // tag_byte
                    reader.ReadByte();
                    break;
                case 4: // tag_long
                    reader.ReadInt64();
                    break;
            }
        }

        // grab name, ip, port, icon from compound
        // minecraft stores server data as nbt in a gzipped file
        private static void ReadCompound(BinaryReader reader, List<ServerItem> servers)
        {
            string? name = null;
            string? ip = null;
            string? icon = null;
            int port = 25565;

            while (true)
            {
                var type = reader.ReadByte();
                if (type == 0) break;

                var key = ReadString(reader);

                switch (type)
                {
                    case 8: // tag_string
                        var val = ReadString(reader);
                        if (key == "name") name = val;
                        else if (key == "ip") ip = val;
                        else if (key == "icon") icon = val;
                        break;
                    case 3: // tag_int
                        var intVal = ReadInt32BE(reader);
                        if (key == "port") port = intVal;
                        break;
                    case 10: // nested compound
                        SkipCompound(reader);
                        break;
                    case 9: // tag_list
                        if (key == "servers")
                            ReadList(reader, servers);
                        else
                            SkipList(reader);
                        break;
                    case 7: // tag_byte_array
                        var arrLen = ReadInt32BE(reader);
                        reader.ReadBytes(arrLen);
                        break;
                    case 11: // tag_int_array
                        var intLen = ReadInt32BE(reader);
                        for (int i = 0; i < intLen; i++) ReadInt32BE(reader);
                        break;
                    case 12: // tag_long_array
                        var longLen = ReadInt32BE(reader);
                        for (int i = 0; i < longLen; i++) reader.ReadInt64();
                        break;
                    case 1: reader.ReadByte(); break;
                    case 2: reader.ReadInt16(); break;
                    case 4: reader.ReadInt64(); break;
                    case 5: reader.ReadDouble(); break;
                    case 6: reader.ReadSingle(); break;
                }
            }

            if (!string.IsNullOrEmpty(ip))
            {
                var address = port == 25565 ? ip : $"{ip}:{port}";
                var item = new ServerItem
                {
                    Id = address,
                    Name = name ?? ip,
                    Address = address
                };
                if (!string.IsNullOrEmpty(icon))
                    item.LoadIcon(icon);
                servers.Add(item);
            }
        }

        private static void WriteStringTag(BinaryWriter writer, string name, string value)
        {
            writer.Write((byte)8);
            WriteString(writer, name);
            WriteString(writer, value);
        }

        private static void WriteInt32BE(BinaryWriter writer, int value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((byte)((bytes.Length >> 8) & 0xFF));
            writer.Write((byte)(bytes.Length & 0xFF));
            writer.Write(bytes);
        }

        // skip nested structures we dont need
        private static void SkipCompound(BinaryReader reader)
        {
            while (true)
            {
                var type = reader.ReadByte();
                if (type == 0) break;
                ReadString(reader);
                SkipPayload(reader, type);
            }
        }

        // read element type + count, then read or skip
        private static void ReadList(BinaryReader reader, List<ServerItem> servers)
        {
            var elemType = reader.ReadByte();
            var count = ReadInt32BE(reader);

            if (elemType == 10) // list of compounds
            {
                for (int i = 0; i < count; i++)
                    ReadCompound(reader, servers);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    SkipPayload(reader, elemType);
            }
        }

        private static void SkipList(BinaryReader reader)
        {
            var elemType = reader.ReadByte();
            var count = ReadInt32BE(reader);
            for (int i = 0; i < count; i++)
                SkipPayload(reader, elemType);
        }

        private static void SkipPayload(BinaryReader reader, byte type)
        {
            switch (type)
            {
                case 1: reader.ReadByte(); break;
                case 2: reader.ReadInt16(); break;
                case 3: ReadInt32BE(reader); break;
                case 4: reader.ReadInt64(); break;
                case 5: reader.ReadDouble(); break;
                case 6: reader.ReadSingle(); break;
                case 7:
                    var len = ReadInt32BE(reader);
                    reader.ReadBytes(len);
                    break;
                case 8: ReadString(reader); break;
                case 9: SkipList(reader); break;
                case 10: SkipCompound(reader); break;
                case 11:
                    var intLen = ReadInt32BE(reader);
                    for (int i = 0; i < intLen; i++) ReadInt32BE(reader);
                    break;
                case 12:
                    var longLen = ReadInt32BE(reader);
                    for (int i = 0; i < longLen; i++) reader.ReadInt64();
                    break;
            }
        }

        // nbt uses big endian, .net doesnt. manually swap bytes
        private static int ReadInt32BE(BinaryReader reader)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();
            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        // 2 byte length prefix then utf-8 string, standard in nbt
        private static string ReadString(BinaryReader reader)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            var len = (ushort)((b0 << 8) | b1);
            var bytes = reader.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
