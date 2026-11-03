using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public static class RamExclusionService
    {
        private static readonly string SettingsFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        static RamExclusionService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCareSuite");

            // 기본적으로 로컬 실행 디렉토리에 우선 생성 시도, 쓰기 권한이 없으면 LocalAppData로 fallback
            string candidatePath = Path.Combine(baseDir, "whitelist.json");
            try
            {
                if (!Directory.Exists(localAppDataDir))
                {
                    Directory.CreateDirectory(localAppDataDir);
                }

                // BaseDirectory 쓰기 테스트
                string testFile = Path.Combine(baseDir, $".perm_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                SettingsFilePath = candidatePath;
            }
            catch
            {
                SettingsFilePath = Path.Combine(localAppDataDir, "whitelist.json");
            }
        }

        public static List<RamExclusionItem> LoadExclusions()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize<List<RamExclusionItem>>(json, JsonOptions);
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

                string json = JsonSerializer.Serialize(items, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
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
