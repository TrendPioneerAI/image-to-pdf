using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalImageToPdf
{
    internal sealed class AppSettings
    {
        public string LastOutputDirectory { get; set; }
        public OutputTargetMode LastTargetMode { get; set; }

        public static AppSettings CreateDefault()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (String.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents))
                documents = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return new AppSettings
            {
                LastOutputDirectory = documents,
                LastTargetMode = OutputTargetMode.Folder
            };
        }
    }

    internal static class AppSettingsStore
    {
        private static readonly Regex DirectoryPattern = new Regex(
            "\\\"lastOutputDirectory\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ModePattern = new Regex(
            "\\\"lastTargetMode\\\"\\s*:\\s*\\\"(?<value>File|Folder)\\\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZenthZhang",
                    "ImageToPdf",
                    "settings.json");
            }
        }

        public static AppSettings Load()
        {
            AppSettings settings = AppSettings.CreateDefault();
            try
            {
                if (!File.Exists(SettingsPath)) return settings;
                string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                Match directoryMatch = DirectoryPattern.Match(json);
                if (directoryMatch.Success)
                {
                    string candidate = JsonUnescape(directoryMatch.Groups["value"].Value);
                    if (!String.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                        settings.LastOutputDirectory = candidate;
                }
                Match modeMatch = ModePattern.Match(json);
                if (modeMatch.Success)
                {
                    OutputTargetMode mode;
                    if (Enum.TryParse<OutputTargetMode>(modeMatch.Groups["value"].Value, true, out mode))
                        settings.LastTargetMode = mode;
                }
            }
            catch
            {
                return AppSettings.CreateDefault();
            }
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            string directory = Path.GetDirectoryName(SettingsPath);
            Directory.CreateDirectory(directory);
            string temporaryPath = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            string json = "{\r\n" +
                "  \"lastOutputDirectory\": \"" + JsonEscape(settings.LastOutputDirectory ?? String.Empty) + "\",\r\n" +
                "  \"lastTargetMode\": \"" + settings.LastTargetMode.ToString() + "\"\r\n" +
                "}\r\n";
            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(SettingsPath))
                    File.Replace(temporaryPath, SettingsPath, null);
                else
                    File.Move(temporaryPath, SettingsPath);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { }
            }
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return String.Empty;
            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32)
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string JsonUnescape(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character != '\\' || index + 1 >= value.Length)
                {
                    builder.Append(character);
                    continue;
                }
                char escaped = value[++index];
                switch (escaped)
                {
                    case '\\': builder.Append('\\'); break;
                    case '"': builder.Append('"'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 < value.Length)
                        {
                            int code;
                            if (Int32.TryParse(value.Substring(index + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                builder.Append((char)code);
                                index += 4;
                                break;
                            }
                        }
                        builder.Append('u');
                        break;
                    default: builder.Append(escaped); break;
                }
            }
            return builder.ToString();
        }
    }
}
