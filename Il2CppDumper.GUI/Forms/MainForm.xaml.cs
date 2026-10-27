using Bluegrams.Application;
using Il2CppDumper.Forms;
using Il2CppDumper.Properties;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shell;

namespace Il2CppDumper
{
    public partial class MainForm : Window, IDumpPrompts
    {
        // Elapsed time of the running dump, shown as [mm:ss.fff] in front of every log line.
        private static readonly Stopwatch dumpTimer = new();
        string appVersion = null;

        string basePath = Path.GetDirectoryName(AppContext.BaseDirectory);

        //Nobody mod 32-bit iOS games so the switch has been removed, and forced 64-bit dump
        //I'll leave the codes here in case someone want to mod 32-bit games

        bool use64bitMach_O = true;

        public MainForm()
        {
            InitializeComponent();
            // The dumper core writes its messages through Logger.
            Logger.Writer = (text, level) => Log(text, level switch
            {
                LogLevel.Success => Brushes.Chartreuse,
                LogLevel.Highlight => Brushes.PaleTurquoise,
                LogLevel.Warning => Brushes.Yellow,
                LogLevel.Error => Brushes.Orange,
                _ => null,
            });

            // Register the code page provider to fix error
            // System.ArgumentException: ''IBM437' is not a supported encoding name. For information on defining a custom encoding, see the documentation for the Encoding.RegisterProvider method. Arg_ParamName_Name'
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            //Get version
            string[] versionArray = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');
            appVersion = string.Join(".", versionArray.Take(3));
            versionTxt.Text = "v" + appVersion;

            //Load configs
            //https://github.com/Bluegrams/SettingsProviders
            PortableSettingsProvider.SettingsFileName = "Il2CppDumper.config";
            PortableSettingsProviderBase.SettingsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            PortableSettingsProvider.ApplyProvider(Settings.Default);

            binFileTxtBox.Text = Settings.Default.BinaryFileTxtBox;
            datFileTxtBox.Text = Settings.Default.DatFileTxtBox;
            outputTxtBox.Text = Settings.Default.OutputTxtBox;
            androArch.SelectedIndex = Settings.Default.AndroArch;

            updateChkBox.IsChecked = Settings.Default.CheckForUpdate;
            autoSetDirCheck.IsChecked = Settings.Default.AutoSetDir;
            extBinaryChkBox.IsChecked = Settings.Default.ExtBinaryChkBox;
            extDatChkBox.IsChecked = Settings.Default.ExtDatChkBox;

            ghidraChkBox.IsChecked = Settings.Default.ghidra;
            ghidraWasmChkBox.IsChecked = Settings.Default.ghidra_wasm;
            ghidraWithStructChkBox.IsChecked = Settings.Default.ghidra_with_struct;
            hopperPy3ChkBox.IsChecked = Settings.Default.hopper;
            hopperPy3ChkBox.IsChecked = Settings.Default.hopper;
            idaChkBox.IsChecked = Settings.Default.ida;
            idaPy3ChkBox.IsChecked = Settings.Default.ida_py3;
            idaStructChkBox.IsChecked = Settings.Default.ida_with_struct;
            idaStructPy3ChkBox.IsChecked = Settings.Default.ida_with_struct_py3;
            headerToBinjaChkBox.IsChecked = Settings.Default.il2cpp_header_to_binja;
            headerToGhidraChkBox.IsChecked = Settings.Default.il2cpp_header_to_ghidra;

            genDumpCsChkBox.IsChecked = Settings.Default.GenDumpCs;
            genStructChkBox.IsChecked = Settings.Default.GenStructFiles;
            genDummyDllChkBox.IsChecked = Settings.Default.GenDummyDll;
            fastModeChkBox.IsChecked = Settings.Default.FastMode;
            threadsCombo.SelectedIndex = Settings.Default.WorkerThreads switch
            {
                1 => 1,
                2 => 2,
                4 => 3,
                8 => 4,
                _ => 0,
            };

            //Check admin
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (isAdmin)
            {
                Title += " - Administrator";
                Log("You are running as administrator. Drag and drop will not work\nIf this program is running as administrator by default, change your User Account Control back to default", Brushes.Yellow);
            }

            //Check update
            if (Settings.Default.CheckForUpdate)
                CheckUpdate();

            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                Log("Your system is 64-bit but this app is running 32-bit. Please use 64-bit for better experience to avoid out of memory issues", Brushes.Yellow);
        }

        #region Il2Cpp dumper
        // Collects the dump settings on the UI thread. Returns null after logging
        // why the dump can't start.
        private DumpOptions CreateDumpOptions()
        {
            var options = new DumpOptions
            {
                GenerateDumpCs = Settings.Default.GenDumpCs,
                GenerateStruct = Settings.Default.GenStructFiles,
                GenerateDummyDll = Settings.Default.GenDummyDll,
                FastMode = Settings.Default.FastMode,
                WorkerThreads = Settings.Default.WorkerThreads,
                MachO32 = !use64bitMach_O,
                Abi = androArch.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string abi } && abi != "default" ? abi : null,
                ExtractBinary = Settings.Default.ExtBinaryChkBox,
                ExtractMetadata = Settings.Default.ExtDatChkBox,
                Scripts = GetSelectedScripts(),
                ScriptsDirectory = basePath,
            };
            try
            {
                options.Config = ConfigLoader.Load(Path.Combine(basePath, "config.json"));
            }
            catch (JsonException ex)
            {
                Log("Can't read config.json: " + ex.Message, Brushes.Orange);
                return null;
            }

            // Used when the automatic search fails
            var codeRegistration = codeRegistrationTxtBox.Text.Trim();
            var metadataRegistration = metadataRegistrationTxtBox.Text.Trim();
            if (codeRegistration.Length > 0 || metadataRegistration.Length > 0)
            {
                if (!TryParseHex(codeRegistration, out var code) || !TryParseHex(metadataRegistration, out var metadata))
                {
                    Log("CodeRegistration and MetadataRegistration must both be hexadecimal addresses", Brushes.Orange);
                    return null;
                }
                options.CodeRegistration = code;
                options.MetadataRegistration = metadata;
            }
            return options;
        }

        private static bool TryParseHex(string text, out ulong value)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static List<string> GetSelectedScripts()
        {
            var scripts = new List<(bool Selected, string FileName)>
            {
                (Settings.Default.ghidra, "ghidra.py"),
                (Settings.Default.ghidra_wasm, "ghidra_wasm.py"),
                (Settings.Default.ghidra_with_struct, "ghidra_with_struct.py"),
                (Settings.Default.hopper, "hopper-py3.py"),
                (Settings.Default.ida, "ida.py"),
                (Settings.Default.ida_py3, "ida_py3.py"),
                (Settings.Default.ida_with_struct, "ida_with_struct.py"),
                (Settings.Default.ida_with_struct_py3, "ida_with_struct_py3.py"),
                (Settings.Default.il2cpp_header_to_binja, "il2cpp_header_to_binja.py"),
                (Settings.Default.il2cpp_header_to_ghidra, "il2cpp_header_to_ghidra.py")
            };
            return scripts.Where(x => x.Selected).Select(x => x.FileName).ToList();
        }

        // IDumpPrompts: called from the dump task
        public ulong AskDumpAddress()
        {
            ulong address = 0;
            Dispatcher.Invoke(() =>
            {
                var form = new InputOffsetForm();
                form.ShowDialog();
                if (!string.IsNullOrEmpty(form.ReturnedOffset) && !TryParseHex(form.ReturnedOffset, out address))
                {
                    Log($"{form.ReturnedOffset} is not a valid address, processing it as a normal file", Brushes.Yellow);
                    address = 0;
                }
            });
            return address;
        }

        public bool TryAskRegistrations(out ulong codeRegistration, out ulong metadataRegistration)
        {
            Log("Enter CodeRegistration and MetadataRegistration and press Start again", Brushes.Orange);
            codeRegistration = metadataRegistration = 0;
            return false;
        }
        #endregion

        #region Check for update
        private async void CheckUpdate()
        {
            await Task.Factory.StartNew(() =>
            {
                if (NetworkInterface.GetIsNetworkAvailable() == true)
                {
                    try
                    {
                        WebClient w = new WebClient();
                        string remoteVersion = w.DownloadString("https://repo.andnixsh.com/tools/il2cppdumper/version");

                        if (!String.IsNullOrEmpty(remoteVersion))
                        {
                            Version currentVersion = Version.Parse(appVersion);
                            Version latestVersion = Version.Parse(remoteVersion);

                            if (latestVersion > currentVersion)
                            {
                                Log("A new version is available: " + remoteVersion, Brushes.Lime);
                                Log("https://github.com/AndnixSH/Il2CppDumper/releases", Brushes.Lime);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("An error checking for update: " + ex.Message, Brushes.Yellow);
                    }
                }
            });
        }

        private void richTextBoxLogs_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
        #endregion

        #region Logging
        //Color list /*http://www.flounder.com/csharp_color_table.htm*/
        public void Log(string text, SolidColorBrush color = null)
        {
            if (!String.IsNullOrEmpty(text))
            {
                if (dumpTimer.IsRunning)
                {
                    var elapsed = dumpTimer.Elapsed;
                    text = $"[{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}] {text}";
                }
                Debug.WriteLine(text);

                if (!Dispatcher.CheckAccess())
                {
                    LogBox.Dispatcher.Invoke(() =>
                    {
                        PrintLog(text, color);
                    });
                }
                else
                {
                    PrintLog(text, color);
                }
            }
        }

        void PrintLog(string text, SolidColorBrush color = null)
        {
            if (color != null)
            {
                Paragraph paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(text) { Foreground = color });
                LogBox.Document.Blocks.Add(paragraph);
            }
            else
            {
                Paragraph paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(text));
                LogBox.Document.Blocks.Add(paragraph);
            }
        }
        #endregion

        #region Save Config
        private int GetSelectedWorkerThreads()
        {
            if (threadsCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var n))
            {
                return n;
            }
            return 0;
        }

        private void SaveConfig()
        {
            Settings.Default.BinaryFileTxtBox = binFileTxtBox.Text;
            Settings.Default.DatFileTxtBox = datFileTxtBox.Text;
            Settings.Default.OutputTxtBox = outputTxtBox.Text;
            Settings.Default.AndroArch = androArch.SelectedIndex;

            Settings.Default.CheckForUpdate = (bool)updateChkBox.IsChecked;
            Settings.Default.AutoSetDir = (bool)autoSetDirCheck.IsChecked;
            Settings.Default.ExtBinaryChkBox = (bool)extBinaryChkBox.IsChecked;
            Settings.Default.ExtDatChkBox = (bool)extDatChkBox.IsChecked;

            Settings.Default.ghidra = (bool)ghidraChkBox.IsChecked;
            Settings.Default.ghidra_wasm = (bool)ghidraWasmChkBox.IsChecked;
            Settings.Default.ghidra_with_struct = (bool)ghidraWithStructChkBox.IsChecked;
            Settings.Default.hopper = (bool)hopperPy3ChkBox.IsChecked;
            Settings.Default.hopper = (bool)hopperPy3ChkBox.IsChecked;
            Settings.Default.ida = (bool)idaChkBox.IsChecked;
            Settings.Default.ida_py3 = (bool)idaPy3ChkBox.IsChecked;
            Settings.Default.ida_with_struct = (bool)idaStructChkBox.IsChecked;
            Settings.Default.ida_with_struct_py3 = (bool)idaStructPy3ChkBox.IsChecked;
            Settings.Default.il2cpp_header_to_binja = (bool)headerToBinjaChkBox.IsChecked;
            Settings.Default.il2cpp_header_to_ghidra = (bool)headerToGhidraChkBox.IsChecked;

            Settings.Default.GenDumpCs = (bool)genDumpCsChkBox.IsChecked;
            Settings.Default.GenStructFiles = (bool)genStructChkBox.IsChecked;
            Settings.Default.GenDummyDll = (bool)genDummyDllChkBox.IsChecked;
            Settings.Default.FastMode = (bool)fastModeChkBox.IsChecked;
            Settings.Default.WorkerThreads = GetSelectedWorkerThreads();

            Settings.Default.Save();
        }
        #endregion

        #region Window handlers
        private bool _actionButtonsEnabled;
        internal bool ActionButtonsEnabled
        {
            set
            {
                startBtn.Content = value ? "Press start or drop APK, APKS, XAPK, ZIP, decrypted IPA file to dump" : "Dumping...";
                startBtn.IsEnabled = value;
                selBinFile.IsEnabled = value;
                selDatFile.IsEnabled = value;
                selOutDir.IsEnabled = value;
                binFileTxtBox.IsEnabled = value;
                datFileTxtBox.IsEnabled = value;
                outputTxtBox.IsEnabled = value;
                androArch.IsEnabled = value;
                codeRegistrationTxtBox.IsEnabled = value;
                metadataRegistrationTxtBox.IsEnabled = value;
            }
            get
            {
                return _actionButtonsEnabled;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //Auto scroll
            outputTxtBox.ScrollToHorizontalOffset(double.MaxValue);
            datFileTxtBox.ScrollToHorizontalOffset(double.MaxValue);
            binFileTxtBox.ScrollToHorizontalOffset(double.MaxValue);
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveConfig();
        }

        private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogBox.ScrollToVerticalOffset(double.MaxValue);
        }

        private void binFileTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            binFileTxtBox.ScrollToHorizontalOffset(double.MaxValue);
        }

        private void datFileTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            datFileTxtBox.ScrollToHorizontalOffset(double.MaxValue);
        }

        private void outputTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            outputTxtBox.ScrollToHorizontalOffset(double.MaxValue);
        }
        #endregion

        #region Button click handlers
        private async void startBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            LogBox.Document.Blocks.Clear();
            dumpTimer.Restart();
            if (!Directory.Exists(outputTxtBox.Text))
            {
                Log("Output directory does not exist", Brushes.Orange);
                return;
            }

            //Check of bin or dat file exists
            if (binFileTxtBox.Text == "")
            {
                Log("Executable file is not selected", Brushes.Orange);
                return;
            }

            if (datFileTxtBox.Text == "")
            {
                Log("Metadata-global.dat file is not selected", Brushes.Orange);
                return;
            }

            var options = CreateDumpOptions();
            if (options == null)
                return;

            ActionButtonsEnabled = false;

            string binFile = binFileTxtBox.Text;
            string datFile = datFileTxtBox.Text;
            string outputPath = outputTxtBox.Text;

            await Task.Run(() => new DumpRunner(options, this).Run(binFile, datFile, outputPath));

            dumpTimer.Stop();
            ActionButtonsEnabled = true;
        }

        private void openFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", outputTxtBox.Text);
        }

        private void selBinFile_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Il2Cpp binary file|*.*";
            ofd.Title = "Select Il2Cpp binary file";

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                binFileTxtBox.Text = ofd.FileName;
                codeRegistrationTxtBox.Text = "";
                metadataRegistrationTxtBox.Text = "";
                if (Settings.Default.AutoSetDir)
                {
                    outputTxtBox.Text = Path.GetDirectoryName(binFileTxtBox.Text) + "\\";
                }
            }
        }

        private void selDatFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "global-metadata|global-metadata.dat";
            ofd.Title = "Select global-metadata.dat";
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                datFileTxtBox.Text = ofd.FileName;
                codeRegistrationTxtBox.Text = "";
                metadataRegistrationTxtBox.Text = "";
            }
        }

        private void selOutDir_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new VistaFolderBrowserDialog();
            if (fbd.ShowDialog() ?? true)
            {
                outputTxtBox.Text = fbd.SelectedPath + "\\"; //Show the path in label
            }
        }
        #endregion

        #region Drag & Drop handlers
        private void OnDragLeave(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.TextBox control)
            {
                control.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#999999"));
            }
            else if (sender is System.Windows.Controls.Button button)
            {
                button.BorderBrush = null;
            }
        }

        private void OnDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.Control control)
            {
                bool isValid = false;
                if (control.Tag is string tag)
                {
                    switch (tag)
                    {
                        case "bin":
                            isValid = e.CheckDragOver();
                            break;
                        case "dat":
                            isValid = e.CheckDragOver(".dat");
                            break;
                        case "folder":
                            isValid = e.CheckDragOverFolder();
                            break;
                        case "start":
                            isValid = e.CheckManyDragOver(".apk", ".xapk", ".zip", ".apks", ".apkm", ".ipa");
                            break;
                    }
                }

                if (isValid)
                {
                    control.BorderThickness = new Thickness(1, 1, 1, 1);
                    control.BorderBrush = Brushes.PaleTurquoise;
                }
            }
        }

        private void OnDrop(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.Control control)
            {
                string[] files = e.GetFilesDrop();
                if (files.Length > 0)
                {
                    if (control.Tag is string tag)
                    {
                        switch (tag)
                        {
                            case "bin":
                                if (File.Exists(files[0]))
                                {
                                    binFileTxtBox.Text = files[0];

                                    if (Settings.Default.AutoSetDir)
                                    {
                                        outputTxtBox.Text = Path.GetDirectoryName(binFileTxtBox.Text) + "\\";
                                    }
                                }
                                break;
                            case "dat":
                                if (File.Exists(files[0]))
                                    datFileTxtBox.Text = files[0];
                                break;
                            case "folder":
                                if (Directory.Exists(files[0]))
                                    outputTxtBox.Text = files[0];
                                break;
                        }
                    }
                }
            }

            if (sender is System.Windows.Controls.TextBox textbox)
            {
                textbox.BorderBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#999999"));
            }
            else if (sender is System.Windows.Controls.Button button)
            {
                button.BorderBrush = null;
            }
        }

        private async void startBtn_Drop(object sender, System.Windows.DragEventArgs e)
        {
            startBtn.BorderBrush = null;
            ActionButtonsEnabled = false;
            LogBox.Document.Blocks.Clear();
            dumpTimer.Restart();
            SaveConfig();
            var options = CreateDumpOptions();
            string[] files = options == null ? Array.Empty<string>() : e.GetFilesDrop();
            foreach (var file in files)
            {
                string outputPath;
                if (Settings.Default.AutoSetDir)
                {
                    outputPath = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + "_dumped");
                }
                else
                {
                    outputPath = Path.Combine(outputTxtBox.Text, Path.GetFileNameWithoutExtension(file) + "_dumped");
                }

                switch (Path.GetExtension(file).ToLowerInvariant())
                {
                    case ".apk":
                    case ".apks":
                    case ".xapk":
                    case ".apkm":
                    case ".zip":
                    case ".ipa":
                        await Task.Run(() => new PackageDumper(options, new DumpRunner(options, this)).Run(file, outputPath));
                        break;
                    default:
                        Log($"{Path.GetFileName(file)} is not an APK, APKS, XAPK, APKM, ZIP or IPA file", Brushes.Yellow);
                        continue;
                }

                if (Settings.Default.AutoSetDir)
                    outputTxtBox.Text = Path.GetDirectoryName(file) + "\\";
            }
            dumpTimer.Stop();
            ActionButtonsEnabled = true;
        }
        #endregion
    }
}
