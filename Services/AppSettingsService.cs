using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    /// <summary>
    /// 앱 설정(settings.json) 및 백업/저장 경로를 관리합니다.
    /// </summary>
    public static class AppSettingsService
    {
        public static readonly string StorageDirectory;
        private static readonly string SettingsFilePath;
        public static readonly string BackupsDirectory;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        static AppSettingsService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SystemCareSuite");

            try
            {
                string testFile = Path.Combine(baseDir, $".perm_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                StorageDirectory = baseDir;
            }
            catch
            {
                try
                {
                    if (!Directory.Exists(localAppDataDir))
                    {
                        Directory.CreateDirectory(localAppDataDir);
                    }
                }
                catch { }
                StorageDirectory = localAppDataDir;
            }

            SettingsFilePath = Path.Combine(StorageDirectory, "settings.json");
            BackupsDirectory = Path.Combine(StorageDirectory, "Backups");
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.CleanerTargetStates = new Dictionary<string, bool>(
                            loaded.CleanerTargetStates ?? [],
                            StringComparer.OrdinalIgnoreCase);
                        return loaded;
                    }
                }
            }
            catch { }

            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }
}
