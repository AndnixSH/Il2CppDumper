using System;

namespace Il2CppDumper
{
    /// <summary>
    /// Asks on the console like the original Il2CppDumper. With redirected
    /// input there is nobody to ask, so the command-line options are the only way.
    /// </summary>
    class ConsolePrompts : IDumpPrompts
    {
        public ulong AskDumpAddress()
        {
            if (Console.IsInputRedirected)
            {
                Logger.Warning("No --dump-address given, processing it as a normal file");
                return 0;
            }
            return AskHex("Input il2cpp dump address or input 0 to force continue: ");
        }

        public bool TryAskRegistrations(out ulong codeRegistration, out ulong metadataRegistration)
        {
            if (Console.IsInputRedirected)
            {
                Logger.Error("Pass the addresses with --code-registration and --metadata-registration");
                codeRegistration = metadataRegistration = 0;
                return false;
            }
            codeRegistration = AskHex("Input CodeRegistration: ");
            metadataRegistration = AskHex("Input MetadataRegistration: ");
            return true;
        }

        private static ulong AskHex(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                var line = Console.ReadLine();
                if (line == null)
                    throw new InvalidOperationException("No input");
                try
                {
                    return Convert.ToUInt64(line.Trim(), 16);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
                {
                    Logger.Warning($"'{line.Trim()}' is not a hexadecimal address");
                }
            }
        }
    }
}
