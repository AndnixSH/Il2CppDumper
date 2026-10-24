using System;
using System.Diagnostics;

namespace Il2CppDumper
{
    /// <summary>
    /// Console writer for <see cref="Logger"/>. Lines are prefixed with the
    /// elapsed time like in the GUI log, and colored by level when the output is
    /// a terminal.
    /// </summary>
    static class ConsoleLog
    {
        private static readonly object sync = new();
        private static readonly Stopwatch timer = new();
        private static readonly bool useColor = !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        public static void StartTimer()
        {
            timer.Restart();
        }

        public static void Write(string text, LogLevel level)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (timer.IsRunning)
            {
                var elapsed = timer.Elapsed;
                text = $"[{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}] {text}";
            }
            ConsoleColor? color = level switch
            {
                LogLevel.Success => ConsoleColor.Green,
                LogLevel.Highlight => ConsoleColor.Cyan,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => null,
            };
            // The core logs from parallel workers; keep color and text together.
            lock (sync)
            {
                if (useColor && color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(text);
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(text);
                }
            }
        }
    }
}
