using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Il2CppDumper
{
    class Program
    {
        private const uint MetadataMagic = 0xFAB11BAF;
        private const uint ZipMagic = 0x04034B50;

        private const int ExitOk = 0;
        private const int ExitFailed = 1;
        private const int ExitUsage = 2;

        private static readonly string exeName = AppDomain.CurrentDomain.FriendlyName;

        [STAThread]
        static int Main(string[] args)
        {
            Logger.Writer = ConsoleLog.Write;
            // Zip entry names of some packages use the IBM437 code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            CliOptions options;
            try
            {
                options = CliOptions.Parse(args);
            }
            catch (CommandLineException ex)
            {
                return UsageError(ex.Message);
            }
            if (options.ShowHelp)
            {
                Console.WriteLine($"Il2CppDumper CLI v{AppVersion}");
                Console.WriteLine();
                Console.WriteLine(CliOptions.HelpText(exeName));
                return ExitOk;
            }
            if (options.ShowVersion)
            {
                Console.WriteLine($"Il2CppDumper CLI v{AppVersion}");
                return ExitOk;
            }

            var config = LoadConfig(options.ConfigPath);
            if (config == null)
            {
                return ExitUsage;
            }
            options.Dump.Config = config;
            var exitCode = Run(args, options, out var cancelled);
            if (!cancelled && !options.NoPause && config.RequireAnyKey && !Console.IsInputRedirected && OwnsConsoleWindow())
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
            return exitCode;
        }

        private static int Run(string[] args, CliOptions options, out bool cancelled)
        {
            cancelled = false;
            string il2cppPath = null;
            string metadataPath = null;
            string packagePath = null;
            var outputDir = options.OutputDir;
            try
            {
                foreach (var input in options.Inputs)
                {
                    if (File.Exists(input))
                    {
                        switch (ReadMagic(input))
                        {
                            case MetadataMagic:
                                SetOnce(ref metadataPath, input, "global-metadata.dat");
                                break;
                            case ZipMagic:
                                SetOnce(ref packagePath, input, "package");
                                break;
                            default:
                                if (il2cppPath != null)
                                {
                                    throw new CommandLineException($"Two executable files given ({il2cppPath}, {input}). " +
                                        "If one of them is global-metadata.dat, it is encrypted or not a metadata file.");
                                }
                                il2cppPath = input;
                                break;
                        }
                    }
                    else if (Directory.Exists(input))
                    {
                        SetOnce(ref outputDir, input, "output directory");
                    }
                    else
                    {
                        throw new CommandLineException($"File or directory not found: {input}");
                    }
                }
                if (packagePath != null && (il2cppPath != null || metadataPath != null))
                {
                    throw new CommandLineException("A package can't be combined with an executable or global-metadata.dat");
                }
            }
            catch (CommandLineException ex)
            {
                return UsageError(ex.Message);
            }

            // Like the original Il2CppDumper, ask for the files when started without arguments.
            if (args.Length == 0 && OperatingSystem.IsWindows() && Environment.UserInteractive)
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Select Il2Cpp binary file, APK or IPA",
                    Filter = "Il2Cpp binary file|*.*"
                };
                if (!ofd.ShowDialog())
                {
                    cancelled = true;
                    return ExitOk;
                }
                if (ReadMagic(ofd.FileName) == ZipMagic)
                {
                    packagePath = ofd.FileName;
                }
                else
                {
                    il2cppPath = ofd.FileName;
                    ofd.Title = "Select global-metadata.dat";
                    ofd.Filter = "global-metadata|global-metadata.dat";
                    if (!ofd.ShowDialog())
                    {
                        cancelled = true;
                        return ExitOk;
                    }
                    metadataPath = ofd.FileName;
                }
            }

            if (packagePath == null && il2cppPath == null)
            {
                if (metadataPath == null)
                {
                    Console.WriteLine(CliOptions.HelpText(exeName));
                    return ExitUsage;
                }
                return UsageError("The executable file is missing");
            }
            if (packagePath == null && metadataPath == null)
            {
                Logger.Error("ERROR: Metadata file not found or encrypted.");
                return ExitFailed;
            }

            Logger.Log($"Il2CppDumper CLI v{AppVersion}");
            ConsoleLog.StartTimer();
            var runner = new DumpRunner(options.Dump, new ConsolePrompts());
            bool ok;
            if (packagePath != null)
            {
                outputDir ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(packagePath)),
                    Path.GetFileNameWithoutExtension(packagePath) + "_dumped");
                ok = new PackageDumper(options.Dump, runner).Run(packagePath, outputDir);
            }
            else
            {
                // Not the current directory itself: that is the exe's folder when it is double-clicked.
                ok = runner.Run(il2cppPath, metadataPath, outputDir ?? Path.Combine(Directory.GetCurrentDirectory(), "output"));
            }
            if (ok)
            {
                Logger.Success("All done!");
            }
            return ok ? ExitOk : ExitFailed;
        }

        private static Config LoadConfig(string path)
        {
            if (path != null && !File.Exists(path))
            {
                UsageError($"Config file not found: {path}");
                return null;
            }
            var configPath = path ?? Path.Combine(AppContext.BaseDirectory, "config.json");
            try
            {
                return ConfigLoader.Load(configPath);
            }
            catch (JsonException ex)
            {
                Logger.Error($"ERROR: Can't read {configPath}: {ex.Message}");
                return null;
            }
        }

        private static void SetOnce(ref string target, string value, string what)
        {
            if (target != null)
            {
                throw new CommandLineException($"More than one {what} given ({target}, {value})");
            }
            target = value;
        }

        private static uint ReadMagic(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            return stream.ReadAtLeast(magic, 4, false) == 4 ? BitConverter.ToUInt32(magic) : 0;
        }

        private static int UsageError(string message)
        {
            Console.Error.WriteLine($"ERROR: {message}");
            Console.Error.WriteLine($"Run '{exeName} --help' for usage.");
            return ExitUsage;
        }

        private static string AppVersion =>
            string.Join(".", Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.').Take(3));

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        // True when the console was opened for this process alone, e.g. by
        // double-clicking the exe or dropping files on it, so it closes on exit.
        private static bool OwnsConsoleWindow()
        {
            if (!OperatingSystem.IsWindows())
                return false;
            var processes = new uint[2];
            return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
        }
    }
}
