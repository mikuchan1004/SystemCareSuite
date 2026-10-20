using System.IO;
using Microsoft.Win32;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public static class RegistryScannerService
    {
        private static readonly string[] SafeWhiteList =
        [
            "Microsoft", "Windows", "Classes", "Clients", "Policies",
            "RegisteredApplications", "WOW6432Node", "Intel", "AMD", "NVIDIA", "Realtek", "Google"
        ];

        public static List<RegistryItem> ScanAllBrokenItems()
        {
            var results = new List<RegistryItem>();
            results.AddRange(ScanStartupItems(onlyBroken: true));
            results.AddRange(ScanUnusedSoftware());
            results.AddRange(ScanMuiCache());
            results.AddRange(ScanFileExtensions());
            return results;
        }

        public static List<RegistryItem> ScanStartupItems(bool onlyBroken)
        {
            var results = new List<RegistryItem>();
            string[] locations =
            [
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
            ];

            foreach (var loc in locations)
            {
                CheckStartupRegistry(Registry.CurrentUser, loc, onlyBroken, results);
                CheckStartupRegistry(Registry.LocalMachine, loc, onlyBroken, results);
            }
            return results;
        }

        public static List<RegistryItem> ScanUnusedSoftware()
        {
            var results = new List<RegistryItem>();
            CheckSoftwareRegistry(Registry.CurrentUser, "Software", results);
            CheckSoftwareRegistry(Registry.LocalMachine, "SOFTWARE", results);
            return results;
        }

        public static List<RegistryItem> ScanMuiCache()
        {
            var results = new List<RegistryItem>();
            string muiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(muiPath);
                if (key == null) return results;

                foreach (string valueName in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(valueName) || valueName == "LangID") continue;

                    string potentialPath = valueName;
                    if (valueName.Contains(".ApplicationCompany") || valueName.Contains(".FriendlyAppName"))
                    {
                        int exeIndex = valueName.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (exeIndex != -1) potentialPath = valueName[..(exeIndex + 4)];
                    }

                    if (!string.IsNullOrEmpty(potentialPath) && potentialPath.Contains('\\') && !File.Exists(potentialPath))
                    {
                        results.Add(new RegistryItem
                        {
                            IsChecked = true,
                            Category = "MUI 캐시",
                            Name = Path.GetFileName(potentialPath),
                            Data = potentialPath,
                            KeyPath = $@"HKCU\{muiPath}",
                            Reason = "실행 파일이 삭제된 과거 캐시 기록"
                        });
                    }
                }
            }
            catch { }

            return results;
        }

        public static List<RegistryItem> ScanFileExtensions()
        {
            var results = new List<RegistryItem>();
            try
            {
                using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
                if (root == null) return results;

                foreach (string extName in root.GetSubKeyNames())
                {
                    if (!extName.StartsWith('.')) continue;

                    using RegistryKey? extKey = root.OpenSubKey($@"{extName}\shell\open\command");
                    if (extKey == null) continue;

                    object? val = extKey.GetValue("");
                    if (val == null) continue;

                    string commandPath = ParseExecutablePath(val.ToString() ?? "");
                    if (!string.IsNullOrEmpty(commandPath) && commandPath.Contains('\\') && !File.Exists(commandPath))
                    {
                        results.Add(new RegistryItem
                        {
                            IsChecked = true,
                            Category = "확장자 연결",
                            Name = extName,
                            Data = commandPath,
                            KeyPath = $@"HKCU\Software\Classes\{extName}",
                            Reason = "연결된 실행 프로그램이 존재하지 않음"
                        });
                    }
                }
            }
            catch { }

            return results;
        }

        private static void CheckStartupRegistry(RegistryKey root, string subKey, bool onlyBroken, List<RegistryItem> results)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(subKey);
                if (key == null) return;

                string rootShort = root == Registry.LocalMachine ? "HKLM" : "HKCU";

                foreach (string valueName in key.GetValueNames())
                {
                    object? val = key.GetValue(valueName);
                    if (val == null) continue;

                    string rawData = val.ToString() ?? string.Empty;
                    string cleanPath = ParseExecutablePath(rawData);
                    bool exists = !string.IsNullOrEmpty(cleanPath) && File.Exists(cleanPath);

                    if (onlyBroken && !exists && !string.IsNullOrEmpty(cleanPath))
                    {
                        results.Add(new RegistryItem
                        {
                            IsChecked = true,
                            Category = "시작 프로그램",
                            Name = valueName,
                            Data = rawData,
                            KeyPath = $@"{rootShort}\{subKey}",
                            Reason = "원본 실행 파일이 존재하지 않음"
                        });
                    }
                    else if (!onlyBroken && exists)
                    {
                        results.Add(new RegistryItem
                        {
                            IsChecked = false,
                            Category = "정상 시작앱",
                            Name = valueName,
                            Data = cleanPath,
                            KeyPath = $@"{rootShort}\{subKey}",
                            Reason = "정상 등록된 윈도우 시작프로그램"
                        });
                    }
                }
            }
            catch { }
        }

        private static void CheckSoftwareRegistry(RegistryKey root, string subKey, List<RegistryItem> results)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(subKey);
                if (key == null) return;

                string rootShort = root == Registry.LocalMachine ? "HKLM" : "HKCU";

                foreach (string subkeyName in key.GetSubKeyNames())
                {
                    if (SafeWhiteList.Any(w => subkeyName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    using RegistryKey? softwareKey = key.OpenSubKey(subkeyName);
                    if (softwareKey == null) continue;

                    if (softwareKey.SubKeyCount == 0 && softwareKey.ValueCount == 0)
                    {
                        results.Add(new RegistryItem
                        {
                            IsChecked = true,
                            Category = "소프트웨어 잔상",
                            Name = subkeyName,
                            Data = "(비어 있는 레지스트리 키)",
                            KeyPath = $@"{rootShort}\{subKey}\{subkeyName}",
                            Reason = "프로그램 삭제 후 남은 빈 레지스트리 키"
                        });
                    }
                }
            }
            catch { }
        }

        public static bool DeleteRegistryItem(RegistryItem item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.KeyPath)) return false;

                RegistryKey root = item.KeyPath.StartsWith("HKEY_LOCAL_MACHINE") || item.KeyPath.StartsWith("HKLM")
                    ? Registry.LocalMachine
                    : Registry.CurrentUser;

                int slashIndex = item.KeyPath.IndexOf('\\');
                string subKeyPath = slashIndex != -1 ? item.KeyPath[(slashIndex + 1)..] : item.KeyPath;

                if (item.Category is "소프트웨어 잔상" or "확장자 연결")
                {
                    root.DeleteSubKeyTree(subKeyPath, false);
                    return true;
                }

                using RegistryKey? key = root.OpenSubKey(subKeyPath, writable: true);
                if (key == null) return false;

                if (item.Category == "MUI 캐시")
                {
                    string target = item.Data;
                    foreach (string valName in key.GetValueNames())
                    {
                        if (!string.IsNullOrEmpty(target) && valName.Contains(target, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue(valName, false);
                            return true;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(item.Name))
                {
                    key.DeleteValue(item.Name, false);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string ParseExecutablePath(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(rawCommand).Trim();

            if (expanded.StartsWith('\"'))
            {
                int nextQuote = expanded.IndexOf('\"', 1);
                if (nextQuote > 1)
                    return expanded[1..nextQuote];
            }

            int exeIdx = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx != -1)
                return expanded[..(exeIdx + 4)].Trim('\"');

            return expanded.Split(' ')[0].Trim('\"');
        }
    }
}
