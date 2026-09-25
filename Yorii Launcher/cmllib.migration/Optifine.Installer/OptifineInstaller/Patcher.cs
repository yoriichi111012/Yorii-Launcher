using Optifine.Installer.Xdelta;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Optifine.Installer
{
    public static class Patcher
    {
        public static void Process(FileInfo baseFile, FileInfo diffFile, FileInfo modFile, bool isOver1_17)
        {
            var diffStream = diffFile.OpenRead();
            var diffZip = new ZipArchive(diffStream, ZipArchiveMode.Read);

            var modStream = modFile.Open(FileMode.Create, FileAccess.Write);
            var zipOut = new ZipArchive(modStream, ZipArchiveMode.Create);

            var baseStream = baseFile.OpenRead();
            var baseZip = new ZipArchive(baseStream, ZipArchiveMode.Read);

            bool isForgeSupportedVersion = diffZip.GetEntry("patch2.cfg") != null;
            Debug.WriteLine(isForgeSupportedVersion);
            Dictionary<string, string> cfgMap;
            if (isForgeSupportedVersion)
                cfgMap = GetConfigurationMap(diffZip);
            else
                cfgMap = GetConfigurationMap(diffZip, "patch.cfg");
            Regex[] patterns = GetConfigurationPatterns(cfgMap);

            var zrp = new ZipResourceProvider(baseZip);

            foreach (var diffEntry in diffZip.Entries)
            {
                var entryStream = diffEntry.Open();
                byte[] bytes = Utils.ReadAll(entryStream);
                string name = diffEntry.FullName;

                if (name.StartsWith("patch/") && name.EndsWith(".xdelta"))
                {
                    string trimmedName = name
                        .Substring("patch/".Length, name.Length - "patch/".Length - ".xdelta".Length);

                    byte[] bytesMod = ApplyPatch(trimmedName, bytes, patterns, cfgMap, zrp, isOver1_17);

                    string nameMd5 = "patch/" + trimmedName + ".md5";
                    var md5Entry = diffZip.GetEntry(nameMd5);
                    if (md5Entry != null)
                    {
                        byte[] md5Bytes = Utils.ReadAll(md5Entry.Open());
                        string md5Str = Encoding.ASCII.GetString(md5Bytes);

                        byte[] md5ModBytes = HashUtils.GetHashMd5(bytesMod);
                        string md5ModStr = HashUtils.ToHexString(md5ModBytes);

                        if (!md5Str.Equals(md5ModStr, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"MD5 not matching, name: {trimmedName}, saved: {md5Str}, patched: {md5ModStr}"
                            );
                        }
                    }

                    var modEntry = zipOut.CreateEntry(trimmedName);
                    using (var modEntryStream = modEntry.Open())
                    {
                        modEntryStream.Write(bytesMod, 0, bytesMod.Length);
                    }
                    Debug.WriteLine("Mod: " + trimmedName);
                }
                else if (!name.StartsWith("patch/") || !name.EndsWith(".md5"))
                {
                    var sameEntry = zipOut.CreateEntry(name);
                    using (var sameStream = sameEntry.Open())
                    {
                        sameStream.Write(bytes, 0, bytes.Length);
                    }
                    Debug.WriteLine("Same: " + name);
                }
            }
            diffZip.Dispose();
            diffStream.Dispose();

            zipOut.Dispose();
            modStream.Dispose();

            baseZip.Dispose();
            baseStream.Dispose();
        }

        public static Dictionary<string, string> GetConfigurationMap(ZipArchive modZip)
        {
            Dictionary<string, string> cfgMap = GetConfigurationMap(modZip, "patch.cfg");
            Dictionary<string, string> cfgMap2 = GetConfigurationMap(modZip, "patch2.cfg");
            Dictionary<string, string> cfgMap3 = GetConfigurationMap(modZip, "patch3.cfg");
            foreach (var kv in cfgMap2)
                cfgMap[kv.Key] = kv.Value;

            foreach (var kv in cfgMap3)
                cfgMap[kv.Key] = kv.Value;
            Debug.WriteLine(string.Join(", ", cfgMap.Select(kv => $"{kv.Key}: {kv.Value}")));

            return cfgMap;
        }

        private static Dictionary<string, string> GetConfigurationMap(ZipArchive modZip, string pathConfig)
        {
            var cfgMap = new Dictionary<string, string>();
            if (modZip == null) return cfgMap;
            var entry = modZip.GetEntry(pathConfig);
            if (entry == null) return cfgMap;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                    var parts = trimmed.Split(new char[] { '=' }, 2);
                    if (parts.Length != 2) throw new FormatException($"Invalid patch configuration: {trimmed}");
                    var key = parts[0].Trim();
                    var val = parts[1].Trim();
                    cfgMap[key] = val;
                }
            }
            return cfgMap;
        }

        private static Regex[] GetConfigurationPatterns(IDictionary<string, string> cfgMap)
        {
            var patterns = new Regex[cfgMap.Count];
            int index = 0;
            foreach (var key in cfgMap.Keys)
            {
                patterns[index++] = new Regex(key);
            }
            return patterns;
        }

        public static byte[] StreamToByteArray(Stream input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            using (var memoryStream = new MemoryStream())
            {
                input.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        public static byte[] ApplyPatch(string name, byte[] bytesDiff, Regex[] patterns, IDictionary<string, string> cfgMap, IResourceProvider resourceProvider, bool isOver1_17)
        {
            name = Utils.RemovePrefix(name, "/");
            string baseName = GetPatchBase(name, patterns, cfgMap, isOver1_17);
            Debug.WriteLine("bs name:" + baseName);
            if (baseName == null)
                throw new FileNotFoundException("No patch base, name: " + name + ", patterns: " + Utils.ArrayToCommaSeparatedString(patterns));

            Stream baseIn = resourceProvider.GetResourceStream(baseName) ?? throw new IOException("Base resource not found: " + baseName);
            byte[] baseBytes = Utils.ReadAll(baseIn);
            Stream patchStream = new MemoryStream(bytesDiff);
            MemoryStream outputStream = new MemoryStream();
            var source = new ByteArraySeekableSource(baseBytes);
            GDiffPatcher.Patch(source, patchStream, outputStream);
            outputStream.Close();
            return outputStream.ToArray();
        }

        private static string GetPatchBase(string name, Regex[] patterns, IDictionary<string, string> cfgMap, bool isOver1_17)
        {
            name = Utils.RemovePrefix(name, "/");

            foreach (var pattern in patterns)
            {
                var match = pattern.Match(name);
                if (!match.Success || match.Value != name)
                    continue;

                cfgMap.TryGetValue(pattern.ToString(), out var baseVal);

                if (baseVal != null && baseVal.Trim() == "*")
                    return name;

                if (isOver1_17)
                {
                    int groupCount = match.Groups.Count - 1;
                    for (int g = 1; g <= groupCount; ++g)
                    {
                        baseVal = baseVal.Replace("$" + g, match.Groups[g].Value);
                    }
                }

                return baseVal;
            }

            return null;
        }

    }
}