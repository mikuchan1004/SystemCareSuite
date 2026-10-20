using System.IO;
using System.ServiceProcess;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public static class CleanerService
    {
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
                Id = "dev_projects",
                Category = "개발 환경",
                Title = "개발 프로젝트 빌드 캐시 (bin / obj / .vs)",
                Description = "지정된 개발 폴더 내 빌드 중간 결과물 정리 (Publish 폴더 및 현재 앱 보호)",
                IsSelected = true
            }
        ];

        public static long ScanTargetBytes(string targetId, string devRootPath)
        {
            return targetId switch
            {
                "user_temp" => GetDirSizeSafe(Path.GetTempPath()),
                "sys_temp" => GetDirSizeSafe(@"C:\Windows\Temp"),
                "prefetch" => GetDirSizeSafe(@"C:\Windows\Prefetch"),
                "wu_cache" => GetDirSizeSafe(@"C:\Windows\SoftwareDistribution\Download"),
                "do_cache" => GetDirSizeSafe(@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                "discord" => GetDiscordCacheSize(),
                "dev_projects" => GetDevProjectsSize(devRootPath),
                _ => 0
            };
        }

        public static long CleanTarget(string targetId, string devRootPath, IProgress<string> log)
        {
            return targetId switch
            {
                "user_temp" => CleanDirectory(Path.GetTempPath(), "사용자 임시 파일", log),
                "sys_temp" => CleanDirectory(@"C:\Windows\Temp", "Windows 시스템 임시 파일", log),
                "prefetch" => CleanDirectory(@"C:\Windows\Prefetch", "시스템 프리패치", log),
                "wu_cache" => CleanWindowsUpdateCache(log),
                "do_cache" => CleanDirectory(@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", "배달 최적화 캐시", log),
                "discord" => CleanDiscordCache(log),
                "dev_projects" => CleanDevProjects(devRootPath, log),
                _ => 0
            };
        }

        private static long GetDiscordCacheSize()
        {
            long total = 0;
            string discordBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            string[] subDirs = ["Cache", "Code Cache", "GPUCache"];
            foreach (var sub in subDirs)
            {
                string full = Path.Combine(discordBase, sub);
                if (Directory.Exists(full))
                    total += GetDirSizeSafe(full);
            }
            return total;
        }

        private static long CleanDiscordCache(IProgress<string> log)
        {
            long freed = 0;
            string discordBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            string[] subDirs = ["Cache", "Code Cache", "GPUCache"];
            foreach (var sub in subDirs)
            {
                string full = Path.Combine(discordBase, sub);
                if (Directory.Exists(full))
                    freed += CleanDirectory(full, $"Discord {sub}", log);
            }
            return freed;
        }

        private static long CleanWindowsUpdateCache(IProgress<string> log)
        {
            string path = @"C:\Windows\SoftwareDistribution\Download";
            if (!Directory.Exists(path)) return 0;

            ServiceController? sc = null;
            long freed = 0;
            try
            {
                sc = new ServiceController("wuauserv");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    log.Report(" -> [서비스] Windows Update(wuauserv) 일시 중지 중...");
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }

                freed = CleanDirectory(path, "Windows 업데이트 캐시", log);

                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    log.Report(" -> [서비스] Windows Update(wuauserv) 재시작 중...");
                    sc.Start();
                }
            }
            catch (Exception ex)
            {
                log.Report($" -> [주의] 업데이트 서비스 제어 중 메시지: {ex.Message}");
                freed += CleanDirectory(path, "Windows 업데이트 캐시(일반)", log);
            }
            finally
            {
                sc?.Dispose();
            }
            return freed;
        }

        private static long GetDevProjectsSize(string devRootPath)
        {
            if (string.IsNullOrWhiteSpace(devRootPath) || !Directory.Exists(devRootPath))
                return 0;

            long total = 0;
            ScanDevDirsRecursive(new DirectoryInfo(devRootPath), ref total);
            return total;
        }

        private static void ScanDevDirsRecursive(DirectoryInfo di, ref long total)
        {
            try
            {
                foreach (var sub in di.GetDirectories())
                {
                    if (sub.FullName.Contains("SystemCareSuite", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = sub.Name.ToLowerInvariant();
                    if (name is "bin" or "obj" or ".vs")
                    {
                        total += GetDirSizeSafe(sub.FullName);
                    }
                    else if (name is not "publish" and not ".git" and not "node_modules")
                    {
                        ScanDevDirsRecursive(sub, ref total);
                    }
                }
            }
            catch { }
        }

        private static long CleanDevProjects(string devRootPath, IProgress<string> log)
        {
            if (string.IsNullOrWhiteSpace(devRootPath) || !Directory.Exists(devRootPath))
                return 0;

            long freed = 0;
            int folderCount = 0;
            CleanDevDirsRecursive(new DirectoryInfo(devRootPath), ref freed, ref folderCount, log);
            log.Report($" -> 개발 빌드 캐시 폴더 {folderCount}개 정리 완료 ({CleanerTargetItem.FormatBytes(freed)})");
            return freed;
        }

        private static void CleanDevDirsRecursive(DirectoryInfo di, ref long freed, ref int folderCount, IProgress<string> log)
        {
            try
            {
                foreach (var sub in di.GetDirectories())
                {
                    if (sub.FullName.Contains("SystemCareSuite", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string name = sub.Name.ToLowerInvariant();
                    if (name is "bin" or "obj" or ".vs")
                    {
                        int deletedFiles = 0;
                        SafeCleanDirectoryRecursive(sub, ref freed, ref deletedFiles);
                        try
                        {
                            if (sub.Exists) sub.Delete(true);
                        }
                        catch { }
                        folderCount++;
                    }
                    else if (name is not "publish" and not ".git" and not "node_modules")
                    {
                        CleanDevDirsRecursive(sub, ref freed, ref folderCount, log);
                    }
                }
            }
            catch { }
        }

        private static long CleanDirectory(string path, string label, IProgress<string> log)
        {
            if (!Directory.Exists(path))
            {
                log.Report($"[건너뜀] {label} 경로가 존재하지 않습니다.");
                return 0;
            }

            long freed = 0;
            int count = 0;
            log.Report($"[정리 중] {label} ({path})");
            SafeCleanDirectoryRecursive(new DirectoryInfo(path), ref freed, ref count);
            log.Report($" -> {count:N0}개 파일 삭제 완료 ({CleanerTargetItem.FormatBytes(freed)} 확보)");
            return freed;
        }

        private static void SafeCleanDirectoryRecursive(DirectoryInfo di, ref long freedBytes, ref int count)
        {
            try
            {
                foreach (FileInfo file in di.GetFiles())
                {
                    try
                    {
                        long len = file.Length;
                        file.Delete();
                        freedBytes += len;
                        count++;
                    }
                    catch { }
                }
            }
            catch { return; }

            try
            {
                foreach (DirectoryInfo subDir in di.GetDirectories())
                {
                    SafeCleanDirectoryRecursive(subDir, ref freedBytes, ref count);
                    try
                    {
                        if (subDir.Exists) subDir.Delete(false);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static long GetDirSizeSafe(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            AccumulateDirSize(new DirectoryInfo(path), ref size);
            return size;
        }

        private static void AccumulateDirSize(DirectoryInfo di, ref long size)
        {
            try
            {
                foreach (FileInfo fi in di.GetFiles())
                {
                    try { size += fi.Length; } catch { }
                }
            }
            catch { return; }

            try
            {
                foreach (DirectoryInfo sub in di.GetDirectories())
                {
                    AccumulateDirSize(sub, ref size);
                }
            }
            catch { }
        }
    }
}
