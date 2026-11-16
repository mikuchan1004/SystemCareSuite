using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public static class RamExclusionService
    {
        private static readonly string SettingsFilePath =
            Path.Combine(AppSettingsService.StorageDirectory, "whitelist.json");

        public static List<RamExclusionItem> LoadExclusions()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<List<RamExclusionItem>>(json, AppSettingsService.JsonOptions);
                    if (loaded != null && loaded.Count > 0)
                    {
                        return loaded;
                    }
                }
            }
            catch { }

            // 기본 기본값 (사용자가 자주 보호하고 싶어하는 주요 프로그램 예시)
            return
            [
                new RamExclusionItem { ProcessName = "devenv", DisplayName = "Visual Studio", IsEnabled = true },
                new RamExclusionItem { ProcessName = "Code", DisplayName = "Visual Studio Code", IsEnabled = true },
                new RamExclusionItem { ProcessName = "Discord", DisplayName = "Discord", IsEnabled = true },
                new RamExclusionItem { ProcessName = "obs64", DisplayName = "OBS Studio", IsEnabled = true },
                new RamExclusionItem { ProcessName = "steam", DisplayName = "Steam", IsEnabled = true }
            ];
        }

        public static void SaveExclusions(IEnumerable<RamExclusionItem> items)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(items, AppSettingsService.JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }

        public static bool AddExclusionIfNotExists(string processName, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            string cleanName = processName.Trim();
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName = Path.GetFileNameWithoutExtension(cleanName);

            var list = LoadExclusions();
            if (list.Any(x => x.ProcessName.Equals(cleanName, StringComparison.OrdinalIgnoreCase)))
                return false;

            list.Add(new RamExclusionItem
            {
                ProcessName = cleanName,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? cleanName : displayName.Trim(),
                IsEnabled = true
            });
            SaveExclusions(list);
            return true;
        }

        public static List<string> GetRunningProcessNames()
        {
            try
            {
                return Process.GetProcesses()
                    .Select(p =>
                    {
                        try { return p.ProcessName; } catch { return null; }
                        finally { p.Dispose(); }
                    })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return [];
            }
        }
    }
}
