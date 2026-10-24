using System;
using System.Collections.Generic;

namespace Il2CppDumper
{
    /// <summary>
    /// Settings of a dump run. The defaults match the defaults of the GUI settings.
    /// </summary>
    public class DumpOptions
    {
        // Helper scripts that can be copied next to the output, by short name.
        public static readonly IReadOnlyDictionary<string, string> ScriptFiles = new Dictionary<string, string>
        {
            ["ghidra"] = "ghidra.py",
            ["ghidra_wasm"] = "ghidra_wasm.py",
            ["ghidra_with_struct"] = "ghidra_with_struct.py",
            ["hopper-py3"] = "hopper-py3.py",
            ["ida"] = "ida.py",
            ["ida_py3"] = "ida_py3.py",
            ["ida_with_struct"] = "ida_with_struct.py",
            ["ida_with_struct_py3"] = "ida_with_struct_py3.py",
            ["il2cpp_header_to_binja"] = "il2cpp_header_to_binja.py",
            ["il2cpp_header_to_ghidra"] = "il2cpp_header_to_ghidra.py",
        };

        // Android ABIs in the order they are dumped from a package.
        public static readonly string[] AndroidAbis = { "armeabi-v7a", "arm64-v8a", "x86", "x86_64" };

        public Config Config { get; set; } = new();
        public bool GenerateDumpCs { get; set; } = true;
        public bool GenerateStruct { get; set; } = true;
        public bool GenerateDummyDll { get; set; } = true;
        /// <summary>Skips the slow metadata-usage scan of the struct generator.</summary>
        public bool FastMode { get; set; }
        /// <summary>Worker threads for struct and DummyDll generation, 0 for all cores.</summary>
        public int WorkerThreads { get; set; }
        /// <summary>Dumps the 32-bit slice of a fat Mach-O instead of the 64-bit one.</summary>
        public bool MachO32 { get; set; }
        /// <summary>Base address of an ELF dumped from memory; asked through <see cref="IDumpPrompts"/> when null.</summary>
        public ulong? DumpAddress { get; set; }
        /// <summary>Used when the automatic search fails; asked through <see cref="IDumpPrompts"/> when null.</summary>
        public ulong? CodeRegistration { get; set; }
        public ulong? MetadataRegistration { get; set; }
        /// <summary>File names of the scripts copied into the output.</summary>
        public List<string> Scripts { get; set; } = new() { "ida_with_struct.py", "ida_with_struct_py3.py" };
        public string ScriptsDirectory { get; set; } = AppContext.BaseDirectory;
        /// <summary>Android ABI to dump from packages, null for all.</summary>
        public string Abi { get; set; }
        /// <summary>Copy the executable out of packages into the output.</summary>
        public bool ExtractBinary { get; set; } = true;
        /// <summary>Copy global-metadata.dat out of packages into the output.</summary>
        public bool ExtractMetadata { get; set; }
    }

    /// <summary>
    /// Values the dump needs from the user while it runs.
    /// </summary>
    public interface IDumpPrompts
    {
        /// <summary>
        /// Base address of an ELF that looks like a memory dump; 0 processes it
        /// as a normal file.
        /// </summary>
        ulong AskDumpAddress();

        /// <summary>
        /// CodeRegistration and MetadataRegistration when the automatic search
        /// fails. Returns false to give up.
        /// </summary>
        bool TryAskRegistrations(out ulong codeRegistration, out ulong metadataRegistration);
    }
}
