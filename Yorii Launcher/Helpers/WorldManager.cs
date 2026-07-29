using Yorii_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Yorii_Launcher.Helpers
{
    public static class WorldManager
    {
        public static List<WorldItem> LoadWorldsFromMinecraftPath(string minecraftPath)
        {
            if (string.IsNullOrWhiteSpace(minecraftPath))
                return [];

            var savesPath = Path.Combine(minecraftPath, "saves");
            if (!Directory.Exists(savesPath))
                return [];

            try
            {
                return Directory.EnumerateDirectories(savesPath)
                    .Select(CreateWorldItem)
                    .OrderByDescending(world => world.LastWriteTimeUtc)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] Failed to load worlds: {ex.Message}");
                return [];
            }
        }

        public static string? GetSelectedWorldId()
        {
            var id = SettingsManager.Current.SelectedWorldId;
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public static void SetSelectedWorldId(string? worldId)
        {
            SettingsManager.Current.SelectedWorldId = worldId ?? "";
            SettingsManager.SaveSettings();
        }

        public static WorldItem? CreateWorld(string minecraftPath, string name)
        {
            if (string.IsNullOrWhiteSpace(minecraftPath))
                return null;

            var cleanName = SanitizeFolderName(name);
            if (string.IsNullOrWhiteSpace(cleanName))
                return null;

            var savesPath = Path.Combine(minecraftPath, "saves");
            Directory.CreateDirectory(savesPath);

            var worldPath = GetUniqueWorldPath(savesPath, cleanName);
            if (!IsInsideSavesFolder(savesPath, worldPath))
                return null;

            Directory.CreateDirectory(worldPath);

            return CreateWorldItem(worldPath);
        }

        public static WorldItem? RenameWorld(string minecraftPath, WorldItem world, string newName)
        {
            if (string.IsNullOrWhiteSpace(minecraftPath) || world == null)
                return null;

            var cleanName = SanitizeFolderName(newName);
            if (string.IsNullOrWhiteSpace(cleanName))
                return null;

            var savesPath = Path.Combine(minecraftPath, "saves");
            var sourcePath = string.IsNullOrWhiteSpace(world.FolderPath)
                ? Path.Combine(savesPath, world.FolderName)
                : world.FolderPath;

            if (!IsInsideSavesFolder(savesPath, sourcePath) || !Directory.Exists(sourcePath))
                return null;

            var targetPath = Path.Combine(savesPath, cleanName);
            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Directory.Exists(targetPath) ? GetUniqueWorldPath(savesPath, cleanName) : targetPath;
                if (!IsInsideSavesFolder(savesPath, targetPath))
                    return null;

                Directory.Move(sourcePath, targetPath);
            }

            if (GetSelectedWorldId() == world.Id)
                SetSelectedWorldId(Path.GetFileName(targetPath));

            return CreateWorldItem(targetPath);
        }

        public static void DeleteWorld(string minecraftPath, WorldItem world)
        {
            if (string.IsNullOrWhiteSpace(minecraftPath) || world == null)
                return;

            var savesPath = Path.Combine(minecraftPath, "saves");
            var worldPath = string.IsNullOrWhiteSpace(world.FolderPath)
                ? Path.Combine(savesPath, world.FolderName)
                : world.FolderPath;

            if (!IsInsideSavesFolder(savesPath, worldPath) || !Directory.Exists(worldPath))
                return;

            Directory.Delete(worldPath, recursive: true);

            if (GetSelectedWorldId() == world.Id)
                SetSelectedWorldId(null);
        }

        private static WorldItem CreateWorldItem(string worldPath)
        {
            var directory = new DirectoryInfo(worldPath);
            var id = directory.Name;
            var levelName = TryReadLevelName(Path.Combine(worldPath, "level.dat"));
            var iconPath = Path.Combine(worldPath, "icon.png");

            var item = new WorldItem
            {
                Id = id,
                FolderName = id,
                FolderPath = worldPath,
                Name = string.IsNullOrWhiteSpace(levelName) ? id : levelName,
                Address = $"Folder: {id}",
                LastWriteTimeUtc = directory.LastWriteTimeUtc
            };

            item.LoadIconFromFile(iconPath);
            return item;
        }

        private static string GetUniqueWorldPath(string savesPath, string folderName)
        {
            var worldPath = Path.Combine(savesPath, folderName);
            if (!Directory.Exists(worldPath))
                return worldPath;

            for (var i = 2; ; i++)
            {
                var candidate = Path.Combine(savesPath, $"{folderName} ({i})");
                if (!Directory.Exists(candidate))
                    return candidate;
            }
        }

        // path traversal check so user cant delete or rename folders outside the saves dir
        private static bool IsInsideSavesFolder(string savesPath, string worldPath)
        {
            var savesFullPath = Path.GetFullPath(savesPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var worldFullPath = Path.GetFullPath(worldPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return worldFullPath.StartsWith(savesFullPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            sanitized = sanitized.Trim();

            return sanitized is "." or ".." ? "" : sanitized;
        }

        private static string TryReadLevelName(string levelDatPath)
        {
            if (!File.Exists(levelDatPath))
                return "";

            try
            {
                using var file = File.OpenRead(levelDatPath);
                Stream stream = file;

                if (file.Length >= 2 && file.ReadByte() == 0x1F && file.ReadByte() == 0x8B)
                {
                    file.Position = 0;
                    using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    return ReadLevelNameFromNbt(gzip);
                }

                file.Position = 0;
                return ReadLevelNameFromNbt(stream);
            }
            catch
            {
                return "";
            }
        }

        private static string ReadLevelNameFromNbt(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            return ReadNamedTag(reader, null) ?? "";
        }

        private static string? ReadNamedTag(BinaryReader reader, string? expectedName)
        {
            var type = reader.ReadByte();
            if (type == 0)
                return null;

            var name = ReadString(reader);
            return ReadPayload(reader, type, expectedName ?? name);
        }

        private static string? ReadPayload(BinaryReader reader, byte type, string tagName)
        {
            switch (type)
            {
                case 10:
                    return ReadCompound(reader);
                case 9:
                    return SkipList(reader);
                case 8:
                    var value = ReadString(reader);
                    return tagName == "LevelName" ? value : null;
                case 1:
                    reader.ReadByte();
                    break;
                case 2:
                    ReadInt16BE(reader);
                    break;
                case 3:
                    ReadInt32BE(reader);
                    break;
                case 4:
                    ReadInt64BE(reader);
                    break;
                case 5:
                    ReadInt32BE(reader);
                    break;
                case 6:
                    ReadInt64BE(reader);
                    break;
                case 7:
                    reader.ReadBytes(ReadInt32BE(reader));
                    break;
                case 11:
                    var intCount = ReadInt32BE(reader);
                    for (var i = 0; i < intCount; i++)
                        ReadInt32BE(reader);
                    break;
                case 12:
                    var longCount = ReadInt32BE(reader);
                    for (var i = 0; i < longCount; i++)
                        ReadInt64BE(reader);
                    break;
            }

            return null;
        }

        private static string? ReadCompound(BinaryReader reader)
        {
            while (true)
            {
                var type = reader.ReadByte();
                if (type == 0)
                    return null;

                var name = ReadString(reader);
                var value = ReadPayload(reader, type, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        private static string? SkipList(BinaryReader reader)
        {
            var elementType = reader.ReadByte();
            var count = ReadInt32BE(reader);
            for (var i = 0; i < count; i++)
            {
                var value = ReadPayload(reader, elementType, "");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static short ReadInt16BE(BinaryReader reader)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            return (short)((b0 << 8) | b1);
        }

        private static int ReadInt32BE(BinaryReader reader)
        {
            var b0 = reader.ReadByte();
            var b1 = reader.ReadByte();
            var b2 = reader.ReadByte();
            var b3 = reader.ReadByte();
            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        private static long ReadInt64BE(BinaryReader reader)
        {
            var b0 = (long)reader.ReadByte();
            var b1 = (long)reader.ReadByte();
            var b2 = (long)reader.ReadByte();
            var b3 = (long)reader.ReadByte();
            var b4 = (long)reader.ReadByte();
            var b5 = (long)reader.ReadByte();
            var b6 = (long)reader.ReadByte();
            var b7 = (long)reader.ReadByte();
            return (b0 << 56) | (b1 << 48) | (b2 << 40) | (b3 << 32) | (b4 << 24) | (b5 << 16) | (b6 << 8) | b7;
        }

        private static string ReadString(BinaryReader reader)
        {
            var len = (ushort)ReadInt16BE(reader);
            return Encoding.UTF8.GetString(reader.ReadBytes(len));
        }
    }
}
