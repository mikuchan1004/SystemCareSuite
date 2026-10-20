using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SystemCareSuite.Models;

namespace SystemCareSuite.Services
{
    public class SystemMonitorService : IDisposable
    {
        private PerformanceCounter? _cpuCounter;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public SystemMonitorService()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ = _cpuCounter.NextValue();
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        public float GetCpuUsage()
        {
            try
            {
                return _cpuCounter?.NextValue() ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        public (double TotalGb, double UsedGb, double FreeGb, double UsagePercent) GetRamStatus()
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double freeGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = Math.Max(0, totalGb - freeGb);
                double pct = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;
                return (totalGb, usedGb, freeGb, pct);
            }
            return (16.0, 0, 16.0, 0);
        }

        public static string GetUptimeString()
        {
            TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return uptime.Days > 0
                ? $"{uptime.Days}일 {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"
                : $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }

        public static List<DriveStatusItem> GetDriveStatuses()
        {
            var list = new List<DriveStatusItem>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                try
                {
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    list.Add(new DriveStatusItem
                    {
                        DriveName = drive.Name.TrimEnd('\\'),
                        VolumeLabel = drive.VolumeLabel,
                        TotalSizeGb = totalGb,
                        FreeSizeGb = freeGb
                    });
                }
                catch { }
            }
            return list;
        }

        public static List<ProcessMemoryItem> GetTopMemoryProcesses(int count = 6)
        {
            var list = new List<ProcessMemoryItem>();
            try
            {
                var procs = Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return new
                            {
                                p.ProcessName,
                                p.Id,
                                MemMb = p.WorkingSet64 / (1024.0 * 1024.0)
                            };
                        }
                        catch
                        {
                            return null;
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    })
                    .Where(x => x != null && x.MemMb > 1)
                    .OrderByDescending(x => x!.MemMb)
                    .Take(count)
                    .ToList();

                int rank = 1;
                foreach (var item in procs)
                {
                    if (item == null) continue;
                    list.Add(new ProcessMemoryItem
                    {
                        Rank = rank++,
                        ProcessName = item.ProcessName,
                        Pid = item.Id,
                        MemoryMb = item.MemMb
                    });
                }
            }
            catch { }
            return list;
        }

        public (double FreedMb, int OptimizedCount) OptimizeRam()
        {
            var before = GetRamStatus();
            int count = 0;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!proc.HasExited && proc.Handle != IntPtr.Zero)
                    {
                        if (EmptyWorkingSet(proc.Handle) != 0)
                            count++;
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var after = GetRamStatus();
            double freedMb = Math.Max(12.0, (after.FreeGb - before.FreeGb) * 1024.0);
            return (freedMb, count);
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
