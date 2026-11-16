using System.Globalization;
using System.IO;
using System.Text;
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

            CheckStartupRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", onlyBroken, results);

            if (!onlyBroken)
            {
                CheckStartupFolder(Registry.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup), results);
                CheckStartupFolder(Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), results);
            }

            return results;
        }

        private static void CheckStartupFolder(RegistryKey root, string folderPath, List<RegistryItem> results)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

            string rootShort = root == Registry.LocalMachine ? "HKLM" : "HKCU";
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(folderPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    bool isDisabled = IsStartupDisabled(root, "StartupFolder", fileName);
                    results.Add(new RegistryItem
                    {
                        IsChecked = false,
                        Category = isDisabled ? "중지된 시작앱" : "정상 시작앱",
                        Name = fileName,
                        Data = filePath,
                        KeyPath = $@"{rootShort}\StartupFolder",
                        Reason = isDisabled ? "일시 중지된 시작 폴더 앱 (재사용 가능)" : "시작프로그램 폴더에 등록된 앱"
                    });
                }
            }
            catch { }
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
                        bool isDisabled = IsStartupDisabled(root, subKey, valueName);
                        results.Add(new RegistryItem
                        {
                            IsChecked = false,
                            Category = isDisabled ? "중지된 시작앱" : "정상 시작앱",
                            Name = valueName,
                            Data = cleanPath,
                            KeyPath = $@"{rootShort}\{subKey}",
                            Reason = isDisabled ? "일시 중지된 시작프로그램 (재사용 가능)" : "정상 등록된 윈도우 시작프로그램"
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

        private static string? GetStartupApprovedSubKey(string runSubKey)
        {
            if (runSubKey.Equals("StartupFolder", StringComparison.OrdinalIgnoreCase))
                return @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
            if (runSubKey.EndsWith(@"WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase))
                return @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
            if (runSubKey.EndsWith(@"Microsoft\Windows\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase))
                return @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            return null;
        }

        private static bool IsStartupDisabled(RegistryKey root, string runSubKey, string valueName)
        {
            try
            {
                string? approvedSubKey = GetStartupApprovedSubKey(runSubKey);
                if (approvedSubKey == null) return false;

                using RegistryKey? key = root.OpenSubKey(approvedSubKey);
                if (key?.GetValue(valueName) is byte[] bytes && bytes.Length > 0)
                {
                    return (bytes[0] & 0x01) == 1;
                }
            }
            catch { }
            return false;
        }

        public static bool ToggleStartupEnabled(RegistryItem item, bool enable, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                var (root, subKeyPath) = ParseRootAndSubKey(item.KeyPath);
                string? approvedSubKey = GetStartupApprovedSubKey(subKeyPath);
                if (approvedSubKey == null)
                {
                    errorMessage = subKeyPath.Contains("RunOnce", StringComparison.OrdinalIgnoreCase)
                        ? $"'{item.Name}'은(는) 1회성(RunOnce) 항목이라 일시 중지를 지원하지 않습니다."
                        : $"'{item.KeyPath}' 경로는 사용/중지 전환을 지원하지 않습니다.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    errorMessage = "항목 이름이 비어 있습니다.";
                    return false;
                }

                using RegistryKey? approvedKey = root.OpenSubKey(approvedSubKey, writable: true)
                    ?? root.CreateSubKey(approvedSubKey, writable: true);
                if (approvedKey == null)
                {
                    errorMessage = $"레지스트리 키를 열 수 없습니다: {approvedSubKey}";
                    return false;
                }

                byte[] payload = new byte[12];
                if (enable)
                {
                    payload[0] = 0x02;
                }
                else
                {
                    payload[0] = 0x03;
                    byte[] ft = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                    Array.Copy(ft, 0, payload, 4, 8);
                }

                approvedKey.SetValue(item.Name, payload, RegistryValueKind.Binary);
                approvedKey.Flush();

                bool nowDisabled = IsStartupDisabled(root, subKeyPath, item.Name);
                if (nowDisabled != !enable)
                {
                    errorMessage = $"'{item.Name}' 상태 변경이 레지스트리에 반영되지 않았습니다.";
                    return false;
                }

                bool isFolder = subKeyPath.Equals("StartupFolder", StringComparison.OrdinalIgnoreCase);
                item.Category = enable ? "정상 시작앱" : "중지된 시작앱";
                item.Reason = enable
                    ? (isFolder ? "시작프로그램 폴더에 등록된 앱" : "정상 등록된 윈도우 시작프로그램")
                    : (isFolder ? "일시 중지된 시작 폴더 앱 (재사용 가능)" : "일시 중지된 시작프로그램 (재사용 가능)");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static string? BackupRegistryItems(IEnumerable<RegistryItem> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return null;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Windows Registry Editor Version 5.00");
                sb.AppendLine();

                bool hasContent = false;
                var exportedSubTrees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var group in list.GroupBy(x => x.KeyPath, StringComparer.OrdinalIgnoreCase))
                {
                    var (root, subKeyPath) = ParseRootAndSubKey(group.Key);
                    if (subKeyPath.Equals("StartupFolder", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fullHiveName = root == Registry.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
                    string fullRegPath = $@"{fullHiveName}\{subKeyPath}";

                    bool isSubTreeGroup = group.Any(x => x.Category is "소프트웨어 잔상" or "확장자 연결");
                    if (isSubTreeGroup)
                    {
                        if (!exportedSubTrees.Add(fullRegPath)) continue;
                        using RegistryKey? subTreeKey = root.OpenSubKey(subKeyPath);
                        if (subTreeKey != null)
                        {
                            AppendSubKeyTreeReg(sb, fullRegPath, subTreeKey);
                            hasContent = true;
                        }
                        continue;
                    }

                    using RegistryKey? key = root.OpenSubKey(subKeyPath);
                    if (key == null) continue;

                    var valueNamesToExport = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in group)
                    {
                        if (item.Category == "MUI 캐시")
                        {
                            foreach (string valName in key.GetValueNames())
                            {
                                if (!string.IsNullOrEmpty(item.Data) && valName.Contains(item.Data, StringComparison.OrdinalIgnoreCase))
                                {
                                    valueNamesToExport.Add(valName);
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(item.Name))
                        {
                            valueNamesToExport.Add(item.Name);
                        }
                    }

                    if (valueNamesToExport.Count == 0) continue;

                    sb.AppendLine($"[{fullRegPath}]");
                    foreach (string valName in valueNamesToExport)
                    {
                        object? val = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (val == null) continue;
                        AppendRegValue(sb, valName, val, key.GetValueKind(valName));
                        hasContent = true;
                    }
                    sb.AppendLine();
                }

                if (!hasContent) return null;

                if (!Directory.Exists(AppSettingsService.BackupsDirectory))
                {
                    Directory.CreateDirectory(AppSettingsService.BackupsDirectory);
                }

                string filePath = Path.Combine(
                    AppSettingsService.BackupsDirectory,
                    $"RegistryBackup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
                File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
                return filePath;
            }
            catch
            {
                return null;
            }
        }

        private static void AppendSubKeyTreeReg(StringBuilder sb, string fullRegPath, RegistryKey key)
        {
            sb.AppendLine($"[{fullRegPath}]");
            foreach (string valName in key.GetValueNames())
            {
                object? val = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (val == null) continue;
                AppendRegValue(sb, valName, val, key.GetValueKind(valName));
            }
            sb.AppendLine();

            foreach (string childName in key.GetSubKeyNames())
            {
                using RegistryKey? child = key.OpenSubKey(childName);
                if (child != null)
                {
                    AppendSubKeyTreeReg(sb, $@"{fullRegPath}\{childName}", child);
                }
            }
        }

        private static void AppendRegValue(StringBuilder sb, string valueName, object value, RegistryValueKind kind)
        {
            string formattedName = string.IsNullOrEmpty(valueName)
                ? "@"
                : $"\"{EscapeRegString(valueName)}\"";

            switch (kind)
            {
                case RegistryValueKind.String:
                    sb.AppendLine($"{formattedName}=\"{EscapeRegString(value.ToString() ?? string.Empty)}\"");
                    break;

                case RegistryValueKind.DWord:
                    uint dw = unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    sb.AppendLine($"{formattedName}=dword:{dw:x8}");
                    break;

                case RegistryValueKind.QWord:
                    ulong qw = unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    sb.AppendLine($"{formattedName}=hex(b):{FormatHexBytes(BitConverter.GetBytes(qw))}");
                    break;

                case RegistryValueKind.Binary:
                    if (value is byte[] bytes)
                        sb.AppendLine($"{formattedName}=hex:{FormatHexBytes(bytes)}");
                    break;

                case RegistryValueKind.ExpandString:
                    byte[] expBytes = Encoding.Unicode.GetBytes((value.ToString() ?? string.Empty) + "\0");
                    sb.AppendLine($"{formattedName}=hex(2):{FormatHexBytes(expBytes)}");
                    break;

                case RegistryValueKind.MultiString:
                    string joined = value is string[] arr ? string.Join("\0", arr) + "\0\0" : "\0\0";
                    byte[] multiBytes = Encoding.Unicode.GetBytes(joined);
                    sb.AppendLine($"{formattedName}=hex(7):{FormatHexBytes(multiBytes)}");
                    break;
            }
        }

        private static string EscapeRegString(string input) =>
            input.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string FormatHexBytes(byte[] bytes) =>
            string.Join(",", bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

        private static (RegistryKey Root, string SubKeyPath) ParseRootAndSubKey(string keyPath)
        {
            RegistryKey root = keyPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                               keyPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine
                : Registry.CurrentUser;

            int slashIndex = keyPath.IndexOf('\\');
            string subKeyPath = slashIndex != -1 ? keyPath[(slashIndex + 1)..] : keyPath;
            return (root, subKeyPath);
        }

        public static bool DeleteRegistryItem(RegistryItem item)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.KeyPath)) return false;

                var (root, subKeyPath) = ParseRootAndSubKey(item.KeyPath);

                if (subKeyPath.Equals("StartupFolder", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(item.Data) && File.Exists(item.Data))
                    {
                        File.Delete(item.Data);
                        using RegistryKey? approvedKey = root.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
                            writable: true);
                        approvedKey?.DeleteValue(item.Name, false);
                        return true;
                    }
                    return false;
                }

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
                    bool deletedAny = false;
                    foreach (string valName in key.GetValueNames())
                    {
                        if (!string.IsNullOrEmpty(target) && valName.Contains(target, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue(valName, false);
                            deletedAny = true;
                        }
                    }
                    return deletedAny;
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
