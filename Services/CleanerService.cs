using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public static class CleanerService
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        /// <summary>
        /// 스파스(Sparse) 파일이나 NTFS 압축 파일의 경우 논리적 크기(FileInfo.Length) 대신
        /// 실제 디스크(SSD)에 할당된 물리적 점유 크기를 반환합니다.
        /// </summary>
        private static long GetActualDiskSize(FileInfo fi)
        {
            try
            {
                uint low = GetCompressedFileSizeW(fi.FullName, out uint high);
                if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0)
                {
                    return fi.Length;
                }
                return ((long)high << 32) | low;
            }
            catch
            {
                return fi.Length;
            }
        }
        public static List<CleanerTargetItem> CreateDefaultTargets() =>
        [
            new CleanerTargetItem
            {
                Id = "user_temp",
                Category = "시스템",
                Title = "사용자 임시 파일 (%TEMP%)",
                Description = "앱 실행 중 생성된 임시 캐시 및 찌꺼기 파일",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "sys_temp",
                Category = "시스템",
                Title = "Windows 시스템 임시 파일",
                Description = @"C:\Windows\Temp 내부 불필요한 시스템 임시 데이터",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "prefetch",
                Category = "시스템",
                Title = "시스템 프리패치 캐시 (Prefetch)",
                Description = @"C:\Windows\Prefetch 내 오래된 실행 흔적 정리",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "wu_cache",
                Category = "업데이트",
                Title = "Windows 업데이트 다운로드 캐시",
                Description = @"SoftwareDistribution\Download (wuauserv 서비스 제어 포함)",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "do_cache",
                Category = "업데이트",
                Title = "배달 최적화 캐시 (Delivery Optimization)",
                Description = "Windows 업데이트 배포용 임시 캐시 데이터",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "discord",
                Category = "앱 캐시",
                Title = "Discord 캐시 (Cache / Code Cache / GPUCache)",
                Description = "디스코드 이미지·렌더링 누적 캐시 정리",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "browser_cache",
                Category = "앱 캐시",
                Title = "브라우저 캐시 (Chrome / Edge)",
                Description = "Chrome·Edge의 웹 캐시 및 GPU 캐시 정리 (로그인·북마크 유지)",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "wer_thumb",
                Category = "시스템",
                Title = "오류 보고서 & 썸네일 캐시",
                Description = "Windows 오류 보고서(WER), 크래시 덤프 및 탐색기 썸네일 캐시 정리",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "dev_projects",
                Category = "개발 환경",
                Title = "개발 프로젝트 빌드 캐시 (bin / obj / .vs)",
                Description = "지정된 개발 폴더 내 빌드 중간 결과물 정리 (Publish 폴더 및 현재 앱 보호)",
                IsSelected = true
            },
            new CleanerTargetItem
            {
                Id = "pkg_cache",
                Category = "개발 환경",
                Title = "패키지 관리자 캐시 (NuGet / npm)",
                Description = "로컬 NuGet(http-cache/v3-cache) 및 npm-cache 임시 패키지 캐시 정리",
                IsSelected = false
            },
            new CleanerTargetItem
            {
                Id = "recycle_bin",
                Category = "시스템",
                Title = "Windows 휴지통 비우기",
                Description = "휴지통에 보관된 삭제 대기 파일 일괄 영구 비우기",
                IsSelected = false
            }
        ];

        // =====================================================================
        // DETAILED FILE SCAN & SELECTIVE DELETE
        // =====================================================================

        public const int DefaultMaxFilesPerCategory = 2500;
        public const string RecycleBinVirtualPath = "SHELL:::{645FF040-5081-101B-9F08-00AA002F954E}";

        public static CleanerScanResult ScanDetailedFiles(
            IEnumerable<string> targetIds,
            string devRootPath,
            bool protectRecentFiles,
            IProgress<(double Percent, string? Log)>? progress = null,
            int maxPerCategory = DefaultMaxFilesPerCategory)
        {
            var idList = targetIds.ToList();
            var results = new List<CleanerFileItem>();
            var truncatedCategories = new List<string>();
            int skippedRecent = 0;
            DateTime cutoffUtc = DateTime.UtcNow.AddHours(-24);

            for (int i = 0; i < idList.Count; i++)
            {
                string tid = idList[i];
                int targetCount = 0;
                string? categoryLabel = null;

                switch (tid)
                {
                    case "user_temp":
                        categoryLabel = "사용자 임시";
                        progress?.Report((i * 100.0 / idList.Count, " • 사용자 임시 파일 목록 스캔 중..."));
                        CollectFilesRecursive(new DirectoryInfo(Path.GetTempPath()), categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        break;

                    case "sys_temp":
                        categoryLabel = "시스템 임시";
                        progress?.Report((i * 100.0 / idList.Count, " • Windows 시스템 임시 파일 목록 스캔 중..."));
                        CollectFilesRecursive(new DirectoryInfo(@"C:\Windows\Temp"), categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        break;

                    case "prefetch":
                        categoryLabel = "프리패치";
                        progress?.Report((i * 100.0 / idList.Count, " • 시스템 프리패치 목록 스캔 중..."));
                        CollectFilesRecursive(new DirectoryInfo(@"C:\Windows\Prefetch"), categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        break;

                    case "wu_cache":
                        categoryLabel = "업데이트 캐시";
                        progress?.Report((i * 100.0 / idList.Count, " • Windows 업데이트 캐시 목록 스캔 중..."));
                        CollectFilesRecursive(new DirectoryInfo(@"C:\Windows\SoftwareDistribution\Download"), categoryLabel, tid, results, maxPerCategory, ref targetCount, null, ref skippedRecent);
                        break;

                    case "do_cache":
                        categoryLabel = "배달 최적화";
                        progress?.Report((i * 100.0 / idList.Count, " • 배달 최적화 캐시 목록 스캔 중..."));
                        CollectFilesRecursive(new DirectoryInfo(@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), categoryLabel, tid, results, maxPerCategory, ref targetCount, null, ref skippedRecent);
                        break;

                    case "discord":
                        categoryLabel = "디스코드 캐시";
                        progress?.Report((i * 100.0 / idList.Count, " • Discord 캐시 파일 목록 스캔 중..."));
                        string discordBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
                        foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                        {
                            string full = Path.Combine(discordBase, sub);
                            if (Directory.Exists(full))
                                CollectFilesRecursive(new DirectoryInfo(full), categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        }
                        break;

                    case "browser_cache":
                        categoryLabel = "브라우저 캐시";
                        progress?.Report((i * 100.0 / idList.Count, " • Chrome / Edge 브라우저 캐시 스캔 중..."));
                        CollectBrowserCaches(categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        break;

                    case "wer_thumb":
                        categoryLabel = "오류·썸네일";
                        progress?.Report((i * 100.0 / idList.Count, " • 오류 보고서 및 썸네일 캐시 스캔 중..."));
                        CollectWerAndThumbnailCaches(categoryLabel, tid, results, maxPerCategory, ref targetCount, protectRecentFiles ? cutoffUtc : null, ref skippedRecent);
                        break;

                    case "dev_projects":
                        categoryLabel = "개발 캐시";
                        progress?.Report((i * 100.0 / idList.Count, " • 개발 프로젝트 빌드 파일 목록 스캔 중..."));
                        if (!string.IsNullOrWhiteSpace(devRootPath) && Directory.Exists(devRootPath))
                        {
                            CollectDevFilesRecursive(new DirectoryInfo(devRootPath), results, maxPerCategory, ref targetCount);
                        }
                        break;

                    case "pkg_cache":
                        categoryLabel = "패키지 캐시";
                        progress?.Report((i * 100.0 / idList.Count, " • NuGet / npm 패키지 캐시 스캔 중..."));
                        CollectPackageCaches(categoryLabel, tid, results, maxPerCategory, ref targetCount);
                        break;

                    case "recycle_bin":
                        categoryLabel = "휴지통";
                        progress?.Report((i * 100.0 / idList.Count, " • Windows 휴지통 상태 확인 중..."));
                        CollectRecycleBinItem(results);
                        break;
                }

                if (categoryLabel != null && targetCount >= maxPerCategory)
                {
                    truncatedCategories.Add(categoryLabel);
                }

                progress?.Report(((i + 1) * 100.0 / idList.Count, null));
            }

            return new CleanerScanResult
            {
                Files = results,
                TruncatedCategories = truncatedCategories,
                SkippedRecentCount = skippedRecent
            };
        }

        private static void CollectBrowserCaches(
            string category,
            string targetId,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount,
            DateTime? cutoffUtc,
            ref int skippedRecent)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] userDataRoots =
            [
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data")
            ];
            string[] cacheSubDirs = ["Cache", "Code Cache", "GPUCache"];

            foreach (string root in userDataRoots)
            {
                if (!Directory.Exists(root) || targetCount >= maxItems) continue;
                try
                {
                    foreach (var profileDir in new DirectoryInfo(root).GetDirectories())
                    {
                        if (!profileDir.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                            !profileDir.Name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var sub in cacheSubDirs)
                        {
                            string cachePath = Path.Combine(profileDir.FullName, sub);
                            if (Directory.Exists(cachePath))
                            {
                                CollectFilesRecursive(new DirectoryInfo(cachePath), category, targetId, results, maxItems, ref targetCount, cutoffUtc, ref skippedRecent);
                            }
                            if (targetCount >= maxItems) return;
                        }
                    }
                }
                catch { }
            }
        }

        private static void CollectWerAndThumbnailCaches(
            string category,
            string targetId,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount,
            DateTime? cutoffUtc,
            ref int skippedRecent)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string explorerDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");

            if (Directory.Exists(explorerDir) && targetCount < maxItems)
            {
                try
                {
                    foreach (var fi in new DirectoryInfo(explorerDir).GetFiles())
                    {
                        if (!fi.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                            !fi.Name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))
                            continue;

                        TryAddScannedFile(fi, category, targetId, results, maxItems, ref targetCount, cutoffUtc, ref skippedRecent);
                        if (targetCount >= maxItems) break;
                    }
                }
                catch { }
            }

            string[] werDirs =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
                Path.Combine(localAppData, "CrashDumps")
            ];

            foreach (string dir in werDirs)
            {
                if (Directory.Exists(dir) && targetCount < maxItems)
                {
                    CollectFilesRecursive(new DirectoryInfo(dir), category, targetId, results, maxItems, ref targetCount, cutoffUtc, ref skippedRecent);
                }
            }
        }

        private static void CollectPackageCaches(
            string category,
            string targetId,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] dirs =
            [
                Path.Combine(localAppData, "NuGet", "v3-cache"),
                Path.Combine(localAppData, "NuGet", "plugins-cache"),
                Path.Combine(localAppData, "npm-cache"),
                Path.Combine(appData, "npm-cache")
            ];

            int dummySkipped = 0;
            foreach (string dir in dirs)
            {
                if (Directory.Exists(dir) && targetCount < maxItems)
                {
                    CollectFilesRecursive(new DirectoryInfo(dir), category, targetId, results, maxItems, ref targetCount, null, ref dummySkipped);
                }
            }
        }

        private static bool IsReparsePoint(FileSystemInfo fsi)
        {
            try
            {
                return (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeDirPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HashSet<string> BuildProtectedRootSet(string? devRootPath = null)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRoot(string? path)
            {
                string norm = NormalizeDirPath(path ?? string.Empty);
                if (!string.IsNullOrEmpty(norm))
                {
                    roots.Add(norm);
                }
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            AddRoot(Path.GetTempPath());
            AddRoot(@"C:\Windows\Temp");
            AddRoot(@"C:\Windows\Prefetch");
            AddRoot(@"C:\Windows\SoftwareDistribution\Download");
            AddRoot(@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");

            string discordBase = Path.Combine(appData, "discord");
            AddRoot(discordBase);
            foreach (string sub in new[] { "Cache", "Code Cache", "GPUCache" })
            {
                AddRoot(Path.Combine(discordBase, sub));
            }

            string[] browserRoots =
            [
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data")
            ];
            foreach (string bRoot in browserRoots)
            {
                AddRoot(bRoot);
                if (!Directory.Exists(bRoot)) continue;
                try
                {
                    foreach (var profileDir in new DirectoryInfo(bRoot).GetDirectories())
                    {
                        if (!profileDir.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                            !profileDir.Name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        AddRoot(profileDir.FullName);
                        foreach (string sub in new[] { "Cache", "Code Cache", "GPUCache" })
                        {
                            AddRoot(Path.Combine(profileDir.FullName, sub));
                        }
                    }
                }
                catch { }
            }

            AddRoot(Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"));
            AddRoot(Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportQueue"));
            AddRoot(Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportArchive"));
            AddRoot(Path.Combine(localAppData, "CrashDumps"));
            AddRoot(Path.Combine(localAppData, "NuGet", "v3-cache"));
            AddRoot(Path.Combine(localAppData, "NuGet", "plugins-cache"));
            AddRoot(Path.Combine(localAppData, "npm-cache"));
            AddRoot(Path.Combine(appData, "npm-cache"));

            if (!string.IsNullOrWhiteSpace(devRootPath))
            {
                AddRoot(devRootPath);
            }

            return roots;
        }

        private static bool IsProtectedDirectory(string dirPath, HashSet<string> protectedRoots)
        {
            string norm = NormalizeDirPath(dirPath);
            if (string.IsNullOrEmpty(norm)) return true;

            string driveRoot = NormalizeDirPath(Path.GetPathRoot(norm) ?? string.Empty);
            if (!string.IsNullOrEmpty(driveRoot) && norm.Equals(driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return protectedRoots.Contains(norm);
        }

        private static void CollectRecycleBinItem(List<CleanerFileItem> results)
        {
            try
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                int hr = SHQueryRecycleBinW(null, ref info);
                if (hr == 0 && (info.i64NumItems > 0 || info.i64Size > 0))
                {
                    results.Add(new CleanerFileItem
                    {
                        Category = "휴지통",
                        TargetId = "recycle_bin",
                        FileName = $"휴지통 전체 항목 ({info.i64NumItems:N0}개)",
                        FullPath = RecycleBinVirtualPath,
                        FileSizeBytes = info.i64Size,
                        IsSelected = true
                    });
                }
            }
            catch { }
        }

        private static bool TryAddScannedFile(
            FileInfo fi,
            string category,
            string targetId,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount,
            DateTime? cutoffUtc,
            ref int skippedRecent)
        {
            if (targetCount >= maxItems) return false;

            try
            {
                if (cutoffUtc.HasValue && fi.LastWriteTimeUtc > cutoffUtc.Value)
                {
                    skippedRecent++;
                    return true;
                }

                results.Add(new CleanerFileItem
                {
                    Category = category,
                    TargetId = targetId,
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    FileSizeBytes = GetActualDiskSize(fi),
                    IsSelected = true
                });
                targetCount++;
            }
            catch { }

            return targetCount < maxItems;
        }

        private static void CollectFilesRecursive(
            DirectoryInfo di,
            string category,
            string targetId,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount,
            DateTime? cutoffUtc,
            ref int skippedRecent)
        {
            if (!di.Exists || IsReparsePoint(di) || targetCount >= maxItems) return;

            try
            {
                foreach (var fi in di.GetFiles())
                {
                    if (!TryAddScannedFile(fi, category, targetId, results, maxItems, ref targetCount, cutoffUtc, ref skippedRecent))
                        return;
                }
            }
            catch { return; }

            try
            {
                foreach (var sub in di.GetDirectories())
                {
                    if (IsReparsePoint(sub)) continue;
                    CollectFilesRecursive(sub, category, targetId, results, maxItems, ref targetCount, cutoffUtc, ref skippedRecent);
                    if (targetCount >= maxItems) return;
                }
            }
            catch { }
        }

        private static void CollectDevFilesRecursive(
            DirectoryInfo di,
            List<CleanerFileItem> results,
            int maxItems,
            ref int targetCount)
        {
            if (!di.Exists || IsReparsePoint(di) || targetCount >= maxItems) return;

            try
            {
                foreach (var sub in di.GetDirectories())
                {
                    if (IsReparsePoint(sub) || sub.FullName.Contains("SystemCareSuite", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = sub.Name.ToLowerInvariant();
                    int dummySkipped = 0;
                    if (name is "bin" or "obj" or ".vs")
                    {
                        CollectFilesRecursive(sub, "개발 캐시", "dev_projects", results, maxItems, ref targetCount, null, ref dummySkipped);
                    }
                    else if (name is not "publish" and not ".git" and not "node_modules")
                    {
                        CollectDevFilesRecursive(sub, results, maxItems, ref targetCount);
                    }

                    if (targetCount >= maxItems) return;
                }
            }
            catch { }
        }

        public static (long FreedBytes, int DeletedFiles, int FailedFiles, bool RecycleBinEmptied) DeleteSelectedFiles(
            IEnumerable<CleanerFileItem> filesToDelete,
            IProgress<(double Percent, string? Log)>? progress,
            string? devRootPath = null)
        {
            var fileList = filesToDelete.ToList();
            if (fileList.Count == 0) return (0, 0, 0, false);

            bool hasWuFiles = fileList.Any(x => x.TargetId == "wu_cache");
            ServiceController? sc = null;
            bool stoppedWuService = false;

            if (hasWuFiles)
            {
                try
                {
                    sc = new ServiceController("wuauserv");
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        progress?.Report((0, " -> [서비스] Windows Update(wuauserv) 일시 중지 중..."));
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        stoppedWuService = true;
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report((0, $" -> [주의] 업데이트 서비스 제어: {ex.Message}"));
                }
            }

            long freedBytes = 0;
            int deleted = 0;
            int failed = 0;
            bool recycleBinEmptied = false;
            var affectedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int reportStep = Math.Max(1, Math.Min(150, fileList.Count / 20));

            for (int i = 0; i < fileList.Count; i++)
            {
                var item = fileList[i];
                try
                {
                    if (item.TargetId == "recycle_bin" && item.FullPath == RecycleBinVirtualPath)
                    {
                        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                        if (hr == 0)
                        {
                            freedBytes += item.FileSizeBytes;
                            deleted++;
                            recycleBinEmptied = true;
                            progress?.Report(((i + 1) * 100.0 / fileList.Count, $" -> [휴지통] 휴지통 비우기 완료 ({CleanerTargetItem.FormatBytes(item.FileSizeBytes)} 확보)"));
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    else if (File.Exists(item.FullPath))
                    {
                        long len = item.FileSizeBytes;
                        File.Delete(item.FullPath);
                        freedBytes += len;
                        deleted++;

                        string? dir = Path.GetDirectoryName(item.FullPath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            affectedDirs.Add(dir);
                        }
                    }
                }
                catch
                {
                    failed++;
                }

                bool isLast = i == fileList.Count - 1;
                if ((i + 1) % reportStep == 0 || isLast)
                {
                    double pct = (i + 1) * 100.0 / fileList.Count;
                    string? msg = ((i + 1) % 150 == 0 || isLast)
                        ? $" -> [{i + 1}/{fileList.Count}] 파일 삭제 진행 중... ({CleanerTargetItem.FormatBytes(freedBytes)} 확보)"
                        : null;
                    progress?.Report((pct, msg));
                }
            }

            // 빈 폴더 정리 시도 (시스템/캐시 루트 폴더는 보호)
            var protectedRoots = BuildProtectedRootSet(devRootPath);
            foreach (var dir in affectedDirs.OrderByDescending(d => d.Length))
            {
                try
                {
                    if (IsProtectedDirectory(dir, protectedRoots)) continue;

                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, false);
                    }
                }
                catch { }
            }

            if (sc != null)
            {
                try
                {
                    if (stoppedWuService && sc.Status == ServiceControllerStatus.Stopped)
                    {
                        progress?.Report((100, " -> [서비스] Windows Update(wuauserv) 재시작 중..."));
                        sc.Start();
                    }
                }
                catch { }
                finally
                {
                    sc.Dispose();
                }
            }

            progress?.Report((100, $" -> 삭제 완료: {deleted:N0}개 삭제 ({CleanerTargetItem.FormatBytes(freedBytes)} 확보, 잠김/실패 {failed:N0}개)"));
            return (freedBytes, deleted, failed, recycleBinEmptied);
        }
    }
}
