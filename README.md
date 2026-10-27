# Il2CppDumper GUI

![Screenshot](Screenshot.png)

[![](https://img.shields.io/github/downloads/AndnixSH/Il2CppDumper-GUI/total?style=for-the-badge)](https://github.com/AndnixSH/Il2CppDumper-GUI/releases) [![](https://img.shields.io/github/v/release/andnixsh/Il2CppDumper-GUI?style=for-the-badge)](https://github.com/AndnixSH/APKToolGUI/releases)

This is the simple GUI version of Perfare's Il2CppDumper. The GUI is based on Bunifu Framework because I like dark theme. Support drag and drop binary files and global-metadata.dat for file selection. APK, APKS, XAPK, ZIP and decrypted IPA file for auto dump

# Note

Due to the variety and complexity of protection and encryption methods utilized by many games, I cannot offer support or assistance for protected games. Consequently, the Issues section is closed. Only Pull Requests are being accepted.

# Features
- Complete DLL restore (except code), can be used to extract MonoBehaviour and MonoScript
- Supports ELF, ELF64, Mach-O, PE, NSO and WASM format
- Supports Unity 5.3 - 6000.7
- Supports Metadata 16 - 39 and 104 - 111 (Unity 6000.5 - 6000.7)
- Supports generate IDA, Ghidra and Binary Ninja scripts to help them better analyze il2cpp files
- Supports generate structures header file
- Supports Android memory dumped libil2cpp.so file to bypass protection
- Support bypassing simple PE protection
- Set output directory
- Set registration offsets
- Support drag and drop
- Performance settings
- Fast mode (skip the slow metadata-usage binary scan)
- Support APK and IPA dump automations
- Command-line version for scripts, batch dumps, Linux and macOS

# Requirements
- Windows 7 and above
- .NET 10.0 Desktop Runtime (Windows): https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Command-line version: .NET 10.0 Runtime on Windows, Linux or macOS

# Download links

Note: Antivirus may flag this tool as malcious, it is false positive and you should not worry about it. They flag all modding tools you need as malicious, this is their business, this is their way to make money.

Il2CppDumper GUI: https://github.com/AndnixSH/Il2CppDumper-GUI/releases

# How to use

Drop APK, APKS, XAPK, ZIP or decrypted IPA file on the Start button to dump

To manually select files, drop binary file and global-metadata.dat on the textboxes or the Select button, or click Select and choose a file. After that, press the start button to dump

To obtain CodeRegistration and MetadataRegistration, read the following tutorials:
- https://tomorrowisnew.com/posts/Finding-CodeRegistration-and-MetadataRegistration/
- https://il2cppdumper.com/reverse/examining-the-binary

# Command-line version

`Il2CppDumper` (in the `Il2CppDumper.CLI` project) is the command-line version. It uses the same dumper as the GUI, so its output is identical, and it accepts the arguments of the original Il2CppDumper:

```
Il2CppDumper <executable-file> <global-metadata> [output-directory] [options]
Il2CppDumper <apk|apks|xapk|apkm|zip|ipa> [output-directory] [options]
```

The executable and global-metadata.dat can be given in any order. Without an output directory, the dump is written to an `output` folder in the current directory, or to `<package name>_dumped` next to a package. Started without arguments on Windows, it asks for the files with file pickers like the original.

Examples:

```
Il2CppDumper libil2cpp.so global-metadata.dat out
Il2CppDumper game.xapk --arch arm64-v8a --scripts ghidra_with_struct,il2cpp_header_to_ghidra
Il2CppDumper GameAssembly.dll global-metadata.dat -o out --fast --no-dummy-dll
Il2CppDumper libil2cpp.so global-metadata.dat --code-registration 0x33beed8 --metadata-registration 0x33e58f0
```

Main options (run `Il2CppDumper --help` for all of them):
- `-o, --output <dir>`: output directory, created if missing
- `--arch <abi>`: ABI to dump from Android packages: `all` (default), `arm64-v8a`, `armeabi-v7a`, `x86` or `x86_64`
- `--code-registration <hex>`, `--metadata-registration <hex>`: addresses used when auto search fails. Without them, the CLI asks for them on the console
- `--dump-address <hex>`: base address of a libil2cpp.so dumped from memory
- `--no-dump-cs`, `--no-struct`, `--no-dummy-dll`, `--fast`, `-j, --threads <n>`: the GUI's output and performance settings
- `--scripts <names>`: scripts to copy into the output (default: `ida_with_struct,ida_with_struct_py3`), `all` or `none`
- `--extract <what>`: files to copy out of packages: `binary` (default), `metadata`, `all` or `none`

The other dump settings come from `config.json` next to the executable, the same file the GUI uses. The exit code is 0 on success, 1 when a dump failed and 2 for invalid arguments.

# Building

`build.ps1` builds the GUI and the CLI for 32-bit and 64-bit Windows:

```
.\build.ps1
```

Each architecture gets one folder, `publish\Il2CppDumper-<version>-<win-x86|win-x64>`, with both single-file exes (`Il2CppDumper GUI.exe` and `Il2CppDumper.exe`) sharing `config.json` and the scripts. Use the x86 build for 32-bit `GameAssembly.dll` files that need the custom PE loader. The builds need the .NET 10 runtime; `.\build.ps1 -SelfContained` bundles it into the exe instead.

For Linux or macOS, publish the CLI for that platform, for example:

```
dotnet publish Il2CppDumper.CLI -c Release -r linux-x64 -p:PublishSingleFile=true -o publish/cli-linux-x64
```

# Project structure

- `Il2CppDumper.Core`: the dumper (executable formats, metadata, dump.cs / struct / DummyDll output, APK and IPA handling), plus `config.json` and the scripts
- `Il2CppDumper.GUI`: the GUI (WPF, Windows only)
- `Il2CppDumper.CLI`: the command-line version (Windows, Linux and macOS)

# Outputs

#### DummyDll

Folder, containing all restored dll files

Use [dnSpy](https://github.com/0xd4d/dnSpy), [ILSpy](https://github.com/icsharpcode/ILSpy) or other .Net decompiler tools to view

Can be used to extract Unity `MonoBehaviour` and `MonoScript`, for [UtinyRipper](https://github.com/mafaca/UtinyRipper), [UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor)

#### ida.py

For IDA (Python 2 / IDAPython 2, older IDA versions)

#### ida_py3.py

For IDA (Python 3 / IDAPython 3, IDA 7.4 and newer). Same as `ida.py` but updated for Python 3 syntax

#### ida_with_struct.py

For IDA, read il2cpp.h file and apply structure information in IDA (Python 2 version)

#### ida_with_struct_py3.py

For IDA, read il2cpp.h file and apply structure information in IDA (Python 3 version, IDA 7.4 and newer)

#### il2cpp.h

Structure information header file

#### ghidra.py

For Ghidra

#### ghidra_with_struct.py

For Ghidra, read il2cpp.h file and apply structure/function signature information in Ghidra

#### ghidra_wasm.py

For Ghidra, work with [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin)

#### il2cpp_header_to_ghidra.py

For Ghidra, run inside Ghidra's Script Manager to parse `il2cpp.h` and import all il2cpp structures into Ghidra's Data Type Manager

#### Il2CppBinaryNinja

For Binary Ninja

#### il2cpp_header_to_binja.py

For Binary Ninja, converts `il2cpp.h` into a Binary Ninja-compatible header so its structures can be imported

#### hopper-py3.py

For [Hopper Disassembler](https://www.hopperapp.com/) (Python 3). Reads `script.json` and renames the addresses/methods in Hopper

#### script.json

For ida.py, ghidra.py and Il2CppBinaryNinja

#### stringliteral.json

Contains all stringLiteral information

# Common errors

#### `ERROR: Metadata file supplied is not valid metadata file.`  

Make sure you choose the correct file. Sometimes games may obfuscate this file for content protection purposes and so on. Deobfuscating of such files is beyond the scope of this program, so please **DO NOT** file an issue regarding to deobfuscating.

#### `ERROR: Can't use auto mode to process file, try manual mode.`

Please note that the executable file for the PC platform is `GameAssembly.dll` or `*Assembly.dll`

#### `ERROR: This file may be protected.`

Il2CppDumper detected that the executable file has been protected, use `GameGuardian` to dump `libil2cpp.so` from the game memory, then use Il2CppDumper to load and follow the prompts, can bypass most protections.

# Credits

- AndnixSH (GUI design)

- [Mika Cybertron](https://github.com/MikaCybertron) (Unity 6000.5 / Metadata v104 - v110 support)

- [Axey](https://platinmods.com/members/axey.63365/) (Unity 6 / Metadata v39 upgrade, performance options)

- Perfare [Il2CppDumper](https://github.com/Perfare/Il2CppDumper)

- djkaty ([Il2CppInspector](https://github.com/djkaty/Il2CppInspector/ + Helped me fixing a bug in GUI)

- T5ive [Il2CppDumper-GUI alternative](https://github.com/T5ive/Il2CppDumper-GUI)
