using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Il2CppDumper
{
    /// <summary>
    /// Finds the il2cpp binary and global-metadata.dat in an APK, a split APK
    /// bundle (APKS, XAPK, APKM, ZIP) or a decrypted IPA, extracts them to a temp
    /// folder and dumps them.
    /// </summary>
    public class PackageDumper
    {
        private const string ApkMetadataPath = "assets/bin/Data/Managed/Metadata/global-metadata.dat";
        // The ABI ends up in the output path, so only plain folder names are matched.
        private static readonly Regex apkLibPath = new(@"^lib/([A-Za-z0-9_-]+)/libil2cpp\.so$");
        private static readonly Regex ipaAppDir = new(@"^Payload/([^/]+)\.app/");

        private readonly DumpOptions options;
        private readonly DumpRunner runner;

        public PackageDumper(DumpOptions options, DumpRunner runner)
        {
            this.options = options;
            this.runner = runner;
        }

        /// <summary>
        /// Returns false if nothing could be dumped or a dump failed.
        /// </summary>
        public bool Run(string packagePath, string outputDir)
        {
            outputDir = DumpRunner.NormalizeDir(outputDir);
            var tempDir = Directory.CreateTempSubdirectory("Il2CppDumper-").FullName;
            try
            {
                Logger.Log($"Opening {packagePath}");
                using var archive = ZipFile.OpenRead(packagePath);
                if (archive.Entries.Any(e => ipaAppDir.IsMatch(e.FullName)))
                {
                    return DumpIpa(archive, outputDir, tempDir);
                }
                return DumpAndroid(archive, Path.GetFileName(packagePath), outputDir, tempDir);
            }
            catch (InvalidDataException ex)
            {
                Logger.Error($"ERROR: {packagePath} is not a valid zip archive: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.Error($"ERROR: Failed to extract files from {packagePath}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                return false;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (IOException)
                {
                    Logger.Warning($"Could not delete the temp folder {tempDir}");
                }
            }
        }

        private static bool IsApkEntry(ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlainFileName(string name)
        {
            return name.Length > 0 && name != "." && name != ".." &&
                name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOf('\\') < 0;
        }

        private bool AbiSelected(string abi)
        {
            return options.Abi == null || options.Abi == abi;
        }

        // Files found in an Android package, extracted to the temp folder.
        private class AndroidFiles
        {
            public string MetadataPath;
            public readonly Dictionary<string, string> Binaries = new();
            // Also set for libraries of ABIs that were not selected.
            public bool SawLibrary;
        }

        /// <summary>
        /// Handles APKs and split APK bundles alike. The package itself is searched
        /// first and the APKs inside it only for what is still missing, so an APK
        /// that embeds other APKs is not mistaken for a bundle.
        /// </summary>
        private bool DumpAndroid(ZipArchive archive, string packageName, string outputDir, string tempDir)
        {
            var found = new AndroidFiles();
            Collect(archive, packageName, found, outputDir, tempDir);
            if (found.MetadataPath == null || found.Binaries.Count == 0)
            {
                var apkPath = Path.Combine(tempDir, "split.apk");
                foreach (var apkEntry in archive.Entries.Where(IsApkEntry))
                {
                    Logger.Log($"Checking {apkEntry.FullName}");
                    apkEntry.ExtractToFile(apkPath, true);
                    using (var apk = ZipFile.OpenRead(apkPath))
                    {
                        Collect(apk, apkEntry.FullName, found, outputDir, tempDir);
                    }
                    File.Delete(apkPath);
                }
            }
            if (found.MetadataPath == null)
            {
                Logger.Warning(found.SawLibrary
                    ? "This package contains libil2cpp.so but no global-metadata.dat. It may be protected or the APK has been split"
                    : "This package does not contain an IL2CPP application");
                return false;
            }
            if (found.Binaries.Count == 0)
            {
                Logger.Warning(found.SawLibrary
                    ? $"No libil2cpp.so for {options.Abi} found"
                    : "This package does not contain libil2cpp.so. The APK has been split");
                return false;
            }
            return DumpAbis(found.Binaries, found.MetadataPath, outputDir);
        }

        private void Collect(ZipArchive apk, string apkName, AndroidFiles found, string outputDir, string tempDir)
        {
            if (found.MetadataPath == null)
            {
                var metadataEntry = apk.GetEntry(ApkMetadataPath);
                if (metadataEntry != null)
                {
                    Logger.Success($"Found global-metadata.dat in {apkName}");
                    found.MetadataPath = ExtractMetadata(metadataEntry, outputDir, tempDir);
                }
            }
            foreach (var entry in apk.Entries)
            {
                var match = apkLibPath.Match(entry.FullName);
                if (!match.Success)
                    continue;
                found.SawLibrary = true;
                var abi = match.Groups[1].Value;
                if (AbiSelected(abi) && !found.Binaries.ContainsKey(abi))
                {
                    Logger.Success($"Found {entry.FullName} in {apkName}");
                    found.Binaries[abi] = ExtractLib(entry, abi, tempDir);
                }
            }
        }

        private bool DumpIpa(ZipArchive archive, string outputDir, string tempDir)
        {
            var appName = archive.Entries.Select(e => ipaAppDir.Match(e.FullName)).First(m => m.Success).Groups[1].Value;
            var appDir = $"Payload/{appName}.app/";
            var metadataEntry = archive.GetEntry(appDir + "Data/Managed/Metadata/global-metadata.dat");
            if (metadataEntry == null)
            {
                Logger.Warning("This IPA does not contain an IL2CPP application");
                return false;
            }
            // Unity 2019.3+ builds keep the game code in UnityFramework.
            var binaryEntry = archive.GetEntry(appDir + "Frameworks/UnityFramework.framework/UnityFramework")
                ?? archive.GetEntry(appDir + appName);
            // The entry name is used as a file name in the temp and output folders.
            if (binaryEntry == null || !IsPlainFileName(binaryEntry.Name))
            {
                Logger.Warning("Failed to find the executable in this IPA. Please extract the files manually");
                return false;
            }
            var metadataPath = ExtractMetadata(metadataEntry, outputDir, tempDir);
            Logger.Highlight($"> Dumping {binaryEntry.Name}");
            var binaryPath = Path.Combine(tempDir, binaryEntry.Name);
            binaryEntry.ExtractToFile(binaryPath, true);
            if (options.ExtractBinary)
            {
                Directory.CreateDirectory(outputDir);
                File.Copy(binaryPath, outputDir + binaryEntry.Name, true);
            }
            return runner.Run(binaryPath, metadataPath, outputDir);
        }

        private string ExtractMetadata(ZipArchiveEntry entry, string outputDir, string tempDir)
        {
            var path = Path.Combine(tempDir, "global-metadata.dat");
            entry.ExtractToFile(path, true);
            if (options.ExtractMetadata)
            {
                Directory.CreateDirectory(outputDir);
                File.Copy(path, outputDir + "global-metadata.dat", true);
            }
            return path;
        }

        private static string ExtractLib(ZipArchiveEntry entry, string abi, string tempDir)
        {
            var dir = Path.Combine(tempDir, abi);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "libil2cpp.so");
            entry.ExtractToFile(path, true);
            return path;
        }

        private bool DumpAbis(Dictionary<string, string> binaries, string metadataPath, string outputDir)
        {
            var ok = true;
            foreach (var abi in DumpOptions.AndroidAbis.Where(binaries.ContainsKey).Concat(binaries.Keys.Except(DumpOptions.AndroidAbis)))
            {
                Logger.Highlight($"> Dumping {abi}");
                var abiDir = outputDir + abi + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(abiDir);
                if (options.ExtractBinary)
                {
                    File.Copy(binaries[abi], abiDir + "libil2cpp.so", true);
                }
                ok &= runner.Run(binaries[abi], metadataPath, abiDir);
            }
            return ok;
        }
    }
}
