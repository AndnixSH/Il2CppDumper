using System;

namespace Il2CppDumper
{
    public enum LogLevel
    {
        Info,
        Success,
        Highlight,
        Warning,
        Error,
    }

    /// <summary>
    /// Log output of the dumper core. The core is shared by the GUI and the CLI,
    /// so it never writes to a window or the console itself; each front end
    /// installs its own <see cref="Writer"/> at startup.
    /// </summary>
    public static class Logger
    {
        public static Action<string, LogLevel> Writer { get; set; } = (text, level) => Console.WriteLine(text);

        // Messages ported from the original Il2CppDumper carry their level in an
        // ERROR or WARNING prefix.
        public static void Log(string text)
        {
            if (text.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                Error(text);
            else if (text.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                Warning(text);
            else
                Writer?.Invoke(text, LogLevel.Info);
        }

        public static void Log(string format, object arg)
        {
            Log(string.Format(format, arg));
        }

        public static void Success(string text)
        {
            Writer?.Invoke(text, LogLevel.Success);
        }

        public static void Highlight(string text)
        {
            Writer?.Invoke(text, LogLevel.Highlight);
        }

        public static void Warning(string text)
        {
            Writer?.Invoke(text, LogLevel.Warning);
        }

        public static void Error(string text)
        {
            Writer?.Invoke(text, LogLevel.Error);
        }
    }
}
