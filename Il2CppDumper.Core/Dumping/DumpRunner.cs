using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Il2CppDumper
{
    /// <summary>
    /// Dumps one executable / global-metadata.dat pair: the steps of the original
    /// Il2CppDumper, shared by the GUI and the CLI. Values the original asked on
    /// the console come from <see cref="DumpOptions"/> or <see cref="IDumpPrompts"/>.
    /// </summary>
    public class DumpRunner
    {
        private readonly DumpOptions options;
        private readonly IDumpPrompts prompts;

        public DumpRunner(DumpOptions options, IDumpPrompts prompts)
        {
            this.options = options;
            this.prompts = prompts;
        }

        private Config config => options.Config;

        /// <summary>
        /// Returns false if the dump or one of its outputs failed.
        /// </summary>
        public bool Run(string il2cppPath, string metadataPath, string outputDir)
        {
            outputDir = NormalizeDir(outputDir);
            try
            {
                Directory.CreateDirectory(outputDir);
                if (!Init(il2cppPath, metadataPath, out var metadata, out var il2Cpp))
                    return false;
                var ok = Dump(metadata, il2Cpp, outputDir);
                CopyScripts(outputDir);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Full path with a trailing separator; the core appends file names to it.
        /// </summary>
        public static string NormalizeDir(string dir)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar;
        }

        private bool Init(string il2cppPath, string metadataPath, out Metadata metadata, out Il2Cpp il2Cpp)
        {
            Logger.Log("Initializing metadata...");
            var metadataBytes = File.ReadAllBytes(metadataPath);
            metadata = new Metadata(new MemoryStream(metadataBytes));
            Logger.Log($"Metadata Version: {metadata.Version}");
            Logger.Success($"Detected Unity Version: {UnityVersionMap.GetUnityVersionRange(metadata.Version)}");
            if (metadata.Version >= 38)
            {
                Logger.Success("Unity 6 metadata detected - using variable-width index mode");
            }

            Logger.Log("Initializing il2cpp file...");
            var il2cppBytes = File.ReadAllBytes(il2cppPath);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);
            switch (il2cppMagic)
            {
                default:
                    throw new NotSupportedException("ERROR: il2cpp file not supported.");
                case 0x6D736100:
                    var web = new WebAssembly(il2CppMemory);
                    il2Cpp = web.CreateMemory();
                    break;
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case 0x905A4D: //PE
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464c457f: //ELF
                    if (il2cppBytes[4] == 2) //ELF64
                    {
                        il2Cpp = new Elf64(il2CppMemory);
                    }
                    else
                    {
                        il2Cpp = new Elf(il2CppMemory);
                    }
                    break;
                case 0xCAFEBABE: //FAT Mach-O
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    var wanted = options.MachO32 ? 0xFEEDFACEu : 0xFEEDFACFu;
                    var index = Array.FindIndex(machofat.fats, x => x.magic == wanted);
                    if (index < 0)
                    {
                        throw new NotSupportedException($"ERROR: This fat Mach-O has no {(options.MachO32 ? "32" : "64")}-bit slice.");
                    }
                    Logger.Log($"Fat Mach-O: using the {(options.MachO32 ? "32" : "64")}-bit slice");
                    var magic = machofat.fats[index].magic;
                    il2cppBytes = machofat.GetMacho(index);
                    il2CppMemory = new MemoryStream(il2cppBytes);
                    if (magic == 0xFEEDFACF)
                        goto case 0xFEEDFACF;
                    else
                        goto case 0xFEEDFACE;
                case 0xFEEDFACF: // 64bit Mach-O
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE: // 32bit Mach-O
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }
            var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount, metadata);
            Logger.Log($"Il2Cpp Version: {il2Cpp.Version}");
            if (config.ForceDump || il2Cpp.CheckDump())
            {
                if (il2Cpp is ElfBase elf)
                {
                    Logger.Warning("Detected this may be a dump file.");
                    var dumpAddr = options.DumpAddress ?? prompts?.AskDumpAddress() ?? 0;
                    if (dumpAddr != 0)
                    {
                        Logger.Log($"Dump address: {dumpAddr:X}");
                        il2Cpp.ImageBase = dumpAddr;
                        il2Cpp.IsDumped = true;
                        if (!config.NoRedirectedPointer)
                        {
                            elf.Reload();
                        }
                    }
                }
                else
                {
                    il2Cpp.IsDumped = true;
                }
            }

            Logger.Log("Searching...");
            try
            {
                var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!flag && il2Cpp is PE)
                    {
                        Logger.Log("Use custom PE loader");
                        il2Cpp = PELoader.Load(il2cppPath);
                        il2Cpp.SetProperties(version, metadata.metadataUsagesCount, metadata);
                        flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                    }
                }
                if (!flag)
                {
                    flag = il2Cpp.Search();
                }
                if (!flag)
                {
                    flag = il2Cpp.SymbolSearch();
                }
                if (!flag)
                {
                    Logger.Error("ERROR: Can't use auto mode to process file, try manual mode.");
                    ulong codeRegistration;
                    ulong metadataRegistration;
                    if (options.CodeRegistration.HasValue && options.MetadataRegistration.HasValue)
                    {
                        codeRegistration = options.CodeRegistration.Value;
                        metadataRegistration = options.MetadataRegistration.Value;
                    }
                    else if (prompts == null || !prompts.TryAskRegistrations(out codeRegistration, out metadataRegistration))
                    {
                        return false;
                    }
                    Logger.Log($"Using CodeRegistration {codeRegistration:x} and MetadataRegistration {metadataRegistration:x}");
                    il2Cpp.Init(codeRegistration, metadataRegistration);
                }
                if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                {
                    var typeDef = metadata.typeDefs[0];
                    var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                    var typeDefinitionsOffset = metadata.Version >= 38
                        ? (ulong)metadata.header.typeDefinitions.offset
                        : metadata.header.typeDefinitionsOffset;
                    metadata.ImageBase = il2CppType.data.typeHandle - typeDefinitionsOffset;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
                Logger.Error("ERROR: An error occurred while processing.");
                return false;
            }
            return true;
        }

        private bool Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            var ok = true;
            Logger.Log("Output path: " + outputDir);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            if (options.GenerateDumpCs)
            {
                Logger.Log("Dumping...");
                var decompiler = new Il2CppDecompiler(executor);
                decompiler.Decompile(config, outputDir);
                Logger.Success("Done!");
            }
            else
            {
                Logger.Log("Skipping dump.cs");
            }
            if (config.GenerateStruct && options.GenerateStruct)
            {
                Logger.Log("Generate struct...");
                try
                {
                    var scriptGenerator = new StructGenerator(executor);
                    scriptGenerator.WriteScript(outputDir, options.FastMode, options.WorkerThreads);
                    Logger.Success("Done!");
                }
                catch (Exception ex)
                {
                    Logger.Error("ERROR: There was an error trying to generate struct: " + ex.Message);
                    Logger.Error(ex.ToString());
                    ok = false;
                }
            }
            if (config.GenerateDummyDll && options.GenerateDummyDll)
            {
                // The exporter changes the current directory to the DummyDll folder.
                var currentDir = Directory.GetCurrentDirectory();
                try
                {
                    Logger.Log("Generating dummy dll...");
                    DummyAssemblyExporter.Export(executor, outputDir, config.DummyDllAddToken, options.WorkerThreads);
                    Logger.Success("Done!");
                }
                catch (Exception ex)
                {
                    Logger.Error("ERROR: There was an error trying to generate dummy dll: " + ex.Message);
                    Logger.Error(ex.ToString());
                    ok = false;
                }
                finally
                {
                    Directory.SetCurrentDirectory(currentDir);
                }
            }
            return ok;
        }

        private void CopyScripts(string outputDir)
        {
            foreach (var fileName in options.Scripts)
            {
                var source = Path.Combine(options.ScriptsDirectory, fileName);
                if (File.Exists(source))
                {
                    File.Copy(source, outputDir + fileName, true);
                    Logger.Log($"Copied {fileName}");
                }
                else
                {
                    Logger.Warning($"{fileName} does not exist");
                }
            }
        }
    }
}
