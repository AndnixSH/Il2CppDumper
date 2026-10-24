using System.IO;
using System.Text.Json;

namespace Il2CppDumper
{
    public static class ConfigLoader
    {
        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Reads config.json. A missing file gives the defaults; invalid JSON
        /// throws a <see cref="JsonException"/>.
        /// </summary>
        public static Config Load(string path)
        {
            if (!File.Exists(path))
            {
                Logger.Warning("config.json file does not exist. Using defaults");
                return new Config();
            }
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), jsonOptions) ?? new Config();
        }
    }
}
