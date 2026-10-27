using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Il2CppDumper
{
    class CommandLineException : Exception
    {
        public CommandLineException(string message) : base(message) { }
    }

    /// <summary>
    /// Options of the CLI. The dump settings are in <see cref="Dump"/>.
    /// </summary>
    class CliOptions
    {
        public DumpOptions Dump { get; } = new();
        public List<string> Inputs { get; } = new();
        public string OutputDir { get; set; }
        public string ConfigPath { get; set; }
        public bool NoPause { get; set; }
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "/?" || arg == "/h")
                {
                    options.ShowHelp = true;
                    continue;
                }
                if (arg.Length < 2 || arg[0] != '-')
                {
                    options.Inputs.Add(arg);
                    continue;
                }

                // --name=value is accepted as well as --name value
                string inlineValue = null;
                var name = arg;
                var equals = arg.IndexOf('=');
                if (arg.StartsWith("--") && equals > 0)
                {
                    name = arg[..equals];
                    inlineValue = arg[(equals + 1)..];
                }
                string Value()
                {
                    if (inlineValue != null)
                        return inlineValue;
                    if (i + 1 >= args.Length)
                        throw new CommandLineException($"Missing value for {name}");
                    return args[++i];
                }
                void NoValue()
                {
                    if (inlineValue != null)
                        throw new CommandLineException($"{name} does not take a value");
                }

                switch (name)
                {
                    case "-h":
                    case "--help":
                        NoValue();
                        options.ShowHelp = true;
                        break;
                    case "-v":
                    case "--version":
                        NoValue();
                        options.ShowVersion = true;
                        break;
                    case "-o":
                    case "--output":
                        options.OutputDir = Value();
                        break;
                    case "-c":
                    case "--config":
                        options.ConfigPath = Value();
                        break;
                    case "--arch":
                        options.Dump.Abi = ParseAbi(Value());
                        break;
                    case "--macho":
                        options.Dump.MachO32 = Value() switch
                        {
                            "64" => false,
                            "32" => true,
                            var v => throw new CommandLineException($"--macho must be 64 or 32, not '{v}'"),
                        };
                        break;
                    case "--code-registration":
                        options.Dump.CodeRegistration = ParseHex(name, Value());
                        break;
                    case "--metadata-registration":
                        options.Dump.MetadataRegistration = ParseHex(name, Value());
                        break;
                    case "--dump-address":
                        options.Dump.DumpAddress = ParseHex(name, Value());
                        break;
                    case "--no-dump-cs":
                        NoValue();
                        options.Dump.GenerateDumpCs = false;
                        break;
                    case "--no-struct":
                        NoValue();
                        options.Dump.GenerateStruct = false;
                        break;
                    case "--no-dummy-dll":
                        NoValue();
                        options.Dump.GenerateDummyDll = false;
                        break;
                    case "--fast":
                        NoValue();
                        options.Dump.FastMode = true;
                        break;
                    case "-j":
                    case "--threads":
                        var threads = Value();
                        if (!int.TryParse(threads, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                            throw new CommandLineException($"--threads must be a number, not '{threads}'");
                        options.Dump.WorkerThreads = n;
                        break;
                    case "--scripts":
                        options.Dump.Scripts = ParseScripts(Value());
                        break;
                    case "--extract":
                        ParseExtract(Value(), options.Dump);
                        break;
                    case "--no-pause":
                        NoValue();
                        options.NoPause = true;
                        break;
                    default:
                        throw new CommandLineException($"Unknown option {name}");
                }
            }
            if (options.Dump.CodeRegistration.HasValue != options.Dump.MetadataRegistration.HasValue)
                throw new CommandLineException("--code-registration and --metadata-registration must be given together");
            return options;
        }

        private static ulong ParseHex(string name, string value)
        {
            try
            {
                // Accepts an optional 0x prefix
                return Convert.ToUInt64(value, 16);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                throw new CommandLineException($"{name} must be a hexadecimal address, not '{value}'");
            }
        }

        private static string ParseAbi(string value)
        {
            return value.ToLowerInvariant().Replace('_', '-') switch
            {
                "all" => null,
                "arm64-v8a" or "arm64" => "arm64-v8a",
                "armeabi-v7a" or "armv7" => "armeabi-v7a",
                "x86" => "x86",
                "x86-64" or "x64" => "x86_64",
                _ => throw new CommandLineException($"Unknown ABI '{value}'. Use all, arm64-v8a, armeabi-v7a, x86 or x86_64"),
            };
        }

        private static List<string> ParseScripts(string value)
        {
            var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 1 && names[0].Equals("none", StringComparison.OrdinalIgnoreCase))
                return new List<string>();
            if (names.Length == 1 && names[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                return DumpOptions.ScriptFiles.Values.ToList();
            var files = new List<string>();
            foreach (var name in names)
            {
                var key = name.ToLowerInvariant();
                if (key.EndsWith(".py"))
                    key = key[..^3];
                if (key == "hopper")
                    key = "hopper-py3";
                if (!DumpOptions.ScriptFiles.TryGetValue(key, out var file))
                    throw new CommandLineException($"Unknown script '{name}'. Available: {string.Join(", ", DumpOptions.ScriptFiles.Keys)}");
                if (!files.Contains(file))
                    files.Add(file);
            }
            return files;
        }

        private static void ParseExtract(string value, DumpOptions options)
        {
            options.ExtractBinary = false;
            options.ExtractMetadata = false;
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "none":
                        break;
                    case "binary":
                        options.ExtractBinary = true;
                        break;
                    case "metadata":
                        options.ExtractMetadata = true;
                        break;
                    case "all":
                        options.ExtractBinary = true;
                        options.ExtractMetadata = true;
                        break;
                    default:
                        throw new CommandLineException($"Unknown --extract value '{part}'. Use binary, metadata, all or none");
                }
            }
        }

        private static string WrapList(string label, IEnumerable<string> items, int indent, int width)
        {
            var lines = new List<string>();
            var line = new string(' ', indent) + label;
            var first = true;
            foreach (var item in items)
            {
                var text = first ? item : ", " + item;
                if (!first && line.Length + text.Length + 1 > width)
                {
                    lines.Add(line + ",");
                    line = new string(' ', indent + label.Length) + item;
                }
                else
                {
                    line += text;
                }
                first = false;
            }
            lines.Add(line);
            return string.Join(Environment.NewLine, lines);
        }

        public static string HelpText(string exeName)
        {
            return $@"usage: {exeName} <executable-file> <global-metadata> [output-directory] [options]
       {exeName} <apk|apks|xapk|apkm|zip|ipa> [output-directory] [options]

The executable and global-metadata.dat can be given in any order; the metadata
is recognized by its header. Without an output directory the dump is written to
an output folder in the current directory, or to <package name>_dumped next to
a package.

Options:
  -o, --output <dir>               Output directory (created if missing)
  -c, --config <file>              config.json to use (default: the one next to {exeName})
      --arch <abi>                 ABI to dump from Android packages: all (default),
                                   arm64-v8a, armeabi-v7a, x86 or x86_64
      --macho <64|32>              Slice to dump from a fat Mach-O (default: 64)
      --code-registration <hex>    CodeRegistration address, used when auto search fails
      --metadata-registration <hex>
                                   MetadataRegistration address, used when auto search fails
      --dump-address <hex>         Base address of a libil2cpp.so dumped from memory
                                   (0 = treat it as a normal file)
      --no-dump-cs                 Don't generate dump.cs
      --no-struct                  Don't generate script.json, stringliteral.json and il2cpp.h
      --no-dummy-dll               Don't generate the DummyDll folder
      --fast                       Fast mode: skip the slow metadata-usage binary scan
  -j, --threads <n>                Worker threads for struct and DummyDll generation
                                   (default: 0 = all cores)
      --scripts <names>            Scripts to copy into the output: a comma-separated list,
                                   all or none (default: ida_with_struct,ida_with_struct_py3)
{WrapList("Available: ", DumpOptions.ScriptFiles.Keys, 35, 92)}
      --extract <what>             Files to copy out of packages into the output: binary,
                                   metadata, all or none (default: binary)
      --no-pause                   Never wait for a key press before exiting
  -h, --help                       Show this help
  -v, --version                    Show the version

Dump options without a command-line switch (DumpProperty, DumpAttribute,
ForceIl2CppVersion, ...) are read from config.json. RequireAnyKey only applies
when the console window was opened for {exeName}, e.g. by double-clicking it.
When started without arguments on Windows, {exeName} asks for the files with
file pickers, like the original Il2CppDumper.";
        }
    }
}
