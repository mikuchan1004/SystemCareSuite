using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SystemCareSuite.Models;
using SystemCareSuite.Services;

namespace SystemCareSuite
{
    public partial class MainWindow : Window
    {
        private readonly SystemMonitorService _monitorService = new();
        private DispatcherTimer? _dashboardTimer;
        private int _tickCounter;
        private AppSettings _settings = new();
        private bool _isRamBoostRunning;
        private bool _isProcContextMenuOpen;
        private DateTime _lastAutoBoostUtc = DateTime.MinValue;

        // Module 2: Cleaner
        private readonly ObservableCollection<CleanerTargetItem> _cleanerTargets = [];
        private readonly List<CleanerFileItem> _cleanerMasterFiles = [];

        // Module 3: Registry
        private List<RegistryItem> _registryMasterList = [];

        // Module 4: WinSxS DISM / System Repair
        private Process? _activeDismProcess;
        private CancellationTokenSource? _activeDismWaitCts;
        private bool _isSxsJobRunning;
        private string _lastDismMode = string.Empty;
        private double? _lastActualSizeMb; // 마지막 분석 시 "실제 크기"(MB) — 정리 전/후 비교용
        private readonly StringBuilder _dismLogBuffer = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeModules();
            Loaded += MainWindow_Loaded;
        }

        private void InitializeModules()
        {
            _settings = AppSettingsService.Load();

            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null)
            {
                TxtSidebarVersion.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build} · .NET 8.0 Portable";
            }

            TxtCoreCount.Text = $"{Environment.ProcessorCount} Threads";

            if (!string.IsNullOrWhiteSpace(_settings.DevRootPath))
            {
                TxtDevRootPath.Text = _settings.DevRootPath;
            }
            ChkProtectRecentFiles.IsChecked = _settings.ProtectRecentCleanerFiles;
            ChkAutoRamBoost.IsChecked = _settings.AutoRamBoostEnabled;

            foreach (var target in CleanerService.CreateDefaultTargets())
            {
                if (_settings.CleanerTargetStates.TryGetValue(target.Id, out bool savedSelected))
                {
                    target.IsSelected = savedSelected;
                }
                _cleanerTargets.Add(target);
            }
            IcCleanerTargets.ItemsSource = _cleanerTargets;

            // 마지막 WinSxS 분석 결과 복원
            if (!string.IsNullOrWhiteSpace(_settings.LastSxsActual))
            {
                TxtSxsActual.Text = _settings.LastSxsActual;
                TxtSxsShared.Text = _settings.LastSxsShared ?? "미측정";
                TxtSxsExpected.Text = _settings.LastSxsExpected ?? "미측정";
                _lastActualSizeMb = _settings.LastSxsActualMb;

                if (_settings.LastSxsRecommended.HasValue)
                {
                    TxtSxsStatusCard.Text = _settings.LastSxsStatus ?? (_settings.LastSxsRecommended.Value ? "📢 정리 권장됨" : "✅ 아주 깨끗한 상태");
                    TxtSxsStatusCard.Foreground = _settings.LastSxsRecommended.Value ? AppBrushes.NeonCrimson : AppBrushes.NeonMint;
                }

                if (!string.IsNullOrWhiteSpace(_settings.LastSxsAnalyzedAt))
                {
                    TxtSxsLastAnalyzed.Text = $"최근 분석: {_settings.LastSxsAnalyzedAt}";
                }
            }
        }

        private void SaveCurrentSettings()
        {
            _settings.DevRootPath = TxtDevRootPath.Text.Trim();
            _settings.ProtectRecentCleanerFiles = ChkProtectRecentFiles.IsChecked == true;
            _settings.AutoRamBoostEnabled = ChkAutoRamBoost.IsChecked == true;
            foreach (var t in _cleanerTargets)
            {
                _settings.CleanerTargetStates[t.Id] = t.IsSelected;
            }
            AppSettingsService.Save(_settings);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDashboardMetrics();
            RefreshDrives();
            RefreshTopProcesses();

            _dashboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _dashboardTimer.Tick += DashboardTimer_Tick;
            _dashboardTimer.Start();

            AppendSxsLog("💡 [저장소 분석 (Analyze)] 버튼을 눌러 현재 WinSxS 구성 요소 저장소 상태를 진단하세요.", Brushes.Cyan);
        }

        // =====================================================================
        // NAVIGATION
        // =====================================================================
        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (PageDashboard == null) return;

            PageDashboard.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageCleaner.Visibility = NavCleaner.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageRegistry.Visibility = NavRegistry.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageWinSxS.Visibility = NavWinSxS.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (PageWinSxS.Visibility == Visibility.Visible)
            {
                _ = RefreshSxsIdleStatusAsync();
            }
        }

        // =====================================================================
        // MODULE 1: DASHBOARD & RAM BOOST
        // =====================================================================
        private void DashboardTimer_Tick(object? sender, EventArgs e)
        {
            UpdateDashboardMetrics();

            _tickCounter++;
            if (_tickCounter % 5 == 0)
            {
                RefreshDrives();
            }
            if (_tickCounter % 4 == 0 && PageDashboard.Visibility == Visibility.Visible && !_isProcContextMenuOpen)
            {
                RefreshTopProcesses();
            }
        }

        private void UpdateDashboardMetrics()
        {
            string uptime = SystemMonitorService.GetUptimeString();
            TxtUptimeMain.Text = uptime;
            TxtSidebarUptime.Text = $"Uptime: {uptime}";

            float cpu = _monitorService.GetCpuUsage();
            TxtCpuPercent.Text = $"{cpu:F1}%";
            PbCpu.Value = Math.Clamp(cpu, 0, 100);

            var ram = _monitorService.GetRamStatus();
            TxtRamPercent.Text = $"{ram.UsagePercent:F1}%";
            PbRam.Value = Math.Clamp(ram.UsagePercent, 0, 100);
            TxtRamDetail.Text = $"{ram.UsedGb:F1} GB 사용 중 / 총 {ram.TotalGb:F1} GB (여유 {ram.FreeGb:F1} GB)";

            if (ram.UsagePercent >= 88.0)
            {
                TxtRamPercent.Foreground = AppBrushes.NeonCrimson;
                PbRam.Foreground = AppBrushes.NeonCrimson;
                TxtRamBadge.Text = "⚠️ 부족 주의";
                TxtRamBadge.Foreground = AppBrushes.NeonCrimson;

                if (_settings.AutoRamBoostEnabled &&
                    !_isRamBoostRunning &&
                    (DateTime.UtcNow - _lastAutoBoostUtc).TotalSeconds >= 60)
                {
                    _lastAutoBoostUtc = DateTime.UtcNow;
                    _ = RunRamBoostCoreAsync(isAuto: true);
                }
            }
            else
            {
                TxtRamPercent.Foreground = AppBrushes.NeonMint;
                PbRam.Foreground = AppBrushes.NeonMint;
                TxtRamBadge.Text = "정상";
                TxtRamBadge.Foreground = AppBrushes.NeonMint;
            }
        }

        private void ChkAutoRamBoost_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            SaveCurrentSettings();
        }

        private void ProcContextMenu_Opened(object sender, RoutedEventArgs e) => _isProcContextMenuOpen = true;

        private void ProcContextMenu_Closed(object sender, RoutedEventArgs e) => _isProcContextMenuOpen = false;

        private static ProcessMemoryItem? GetProcessFromMenuItem(object sender)
        {
            if (sender is MenuItem mi &&
                mi.Parent is ContextMenu cm &&
                cm.PlacementTarget is FrameworkElement fe)
            {
                return fe.Tag as ProcessMemoryItem ?? fe.DataContext as ProcessMemoryItem;
            }
            return null;
        }

        private void ProcMenuExclude_Click(object sender, RoutedEventArgs e)
        {
            var proc = GetProcessFromMenuItem(sender);
            if (proc == null) return;

            bool added = RamExclusionService.AddExclusionIfNotExists(proc.ProcessName, proc.ProcessName);
            TxtRamBoostResult.Text = added
                ? $"🛡️ '{proc.ProcessName}'을(를) RAM 부스트 제외 목록에 추가했습니다."
                : $"ℹ️ '{proc.ProcessName}'은(는) 이미 제외 목록에 등록되어 있습니다.";
        }

        private void ProcMenuOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            var proc = GetProcessFromMenuItem(sender);
            if (proc == null) return;

            string? exePath = SystemMonitorService.GetProcessExecutablePath(proc.Pid);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                MessageBox.Show($"'{proc.ProcessName}'(PID: {proc.Pid})의 실행 파일 경로에 접근할 수 없습니다.", "경로 확인 불가", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{exePath}\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void ProcMenuKill_Click(object sender, RoutedEventArgs e)
        {
            var proc = GetProcessFromMenuItem(sender);
            if (proc == null) return;

            var confirm = MessageBox.Show(
                $"'{proc.ProcessName}' (PID: {proc.Pid}, 메모리 {proc.MemoryText}) 프로세스를 종료하시겠습니까?\n저장하지 않은 작업 내용은 손실될 수 있습니다.",
                "프로세스 종료 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            if (SystemMonitorService.TryTerminateProcess(proc.Pid, out string err))
            {
                TxtRamBoostResult.Text = $"🛑 '{proc.ProcessName}'(PID: {proc.Pid}) 프로세스를 종료했습니다.";
                RefreshTopProcesses();
                UpdateDashboardMetrics();
            }
            else
            {
                MessageBox.Show($"프로세스를 종료하지 못했습니다.\n사유: {err}", "종료 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshDrives()
        {
            IcDrives.ItemsSource = SystemMonitorService.GetDriveStatuses();
        }

        private void RefreshTopProcesses()
        {
            IcTopProcesses.ItemsSource = SystemMonitorService.GetTopMemoryProcesses(6);
        }

        private void BtnRefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDrives();

        private void DriveCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DriveStatusItem drive && !string.IsNullOrWhiteSpace(drive.DriveName))
            {
                try
                {
                    Process.Start(new ProcessStartInfo($"{drive.DriveName}\\") { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void BtnRefreshProcs_Click(object sender, RoutedEventArgs e) => RefreshTopProcesses();

        private void BtnRamExclusionSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new RamExclusionWindow
            {
                Owner = this
            };
            win.ShowDialog();
        }

        private async void BtnRamBoost_Click(object sender, RoutedEventArgs e)
        {
            await RunRamBoostCoreAsync(isAuto: false);
        }

        private async Task RunRamBoostCoreAsync(bool isAuto)
        {
            if (_isRamBoostRunning) return;
            _isRamBoostRunning = true;

            BtnQuickRamBoost.IsEnabled = false;
            BtnQuickRamBoost.Content = isAuto ? "⚡ 자동 메모리 최적화 중..." : "⚡ 메모리 최적화 중...";

            try
            {
                // 제외(화이트리스트) 목록 중 활성화된 항목 로드
                var exclusions = RamExclusionService.LoadExclusions()
                    .Where(x => x.IsEnabled)
                    .Select(x => x.ProcessName)
                    .ToList();

                if (_isSxsJobRunning)
                {
                    exclusions.AddRange(["TiWorker", "TrustedInstaller", "dism", "DismHost", "sfc"]);
                }

                var (freedMb, count, skipped) = await Task.Run(() => _monitorService.OptimizeRam(exclusions));

                UpdateDashboardMetrics();
                RefreshTopProcesses();

                string skippedMsg = skipped > 0 ? $", 제외 {skipped}개" : string.Empty;
                string prefix = isAuto ? "⚡ 자동 부스트:" : "✅";
                TxtRamBoostResult.Text = $"{prefix} {count}개 프로세스 정리 (약 {freedMb:F0} MB 확보{skippedMsg})";
            }
            finally
            {
                BtnQuickRamBoost.Content = "⚡ 원클릭 RAM 메모리 부스트";
                BtnQuickRamBoost.IsEnabled = true;
                _isRamBoostRunning = false;
            }
        }

        // =====================================================================
        // MODULE 2: SYSTEM & DEV CLEANER
        // =====================================================================
        private void CleanerSubTab_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewCleanerFiles == null || ViewCleanerTargets == null || ViewCleanerLog == null) return;

            ViewCleanerFiles.Visibility = RbCleanerTabFiles.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewCleanerTargets.Visibility = RbCleanerTabTargets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewCleanerLog.Visibility = RbCleanerTabLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnBrowseDevRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "개발 프로젝트 루트 폴더 선택 (bin/obj/.vs 스캔 대상)"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtDevRootPath.Text = dlg.FolderName;
                SaveCurrentSettings();
            }
        }

        private void TxtDevRootPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) SaveCurrentSettings();
        }

        private void ChkProtectRecentFiles_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) SaveCurrentSettings();
        }

        private void CleanerTargetCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) SaveCurrentSettings();
        }

        private void CbCleanerFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCleanerFilter();
        }

        private void TxtCleanerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyCleanerFilter();
        }

        private void ApplyCleanerFilter()
        {
            if (DgCleanerFiles == null) return;

            string selectedCat = (CbCleanerFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체 분류";
            string search = TxtCleanerSearch?.Text?.Trim() ?? string.Empty;

            var filtered = _cleanerMasterFiles.AsEnumerable();

            if (selectedCat != "전체 분류")
            {
                filtered = filtered.Where(x => x.Category.Equals(selectedCat, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(x => x.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                               x.FullPath.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var list = filtered.ToList();
            DgCleanerFiles.ItemsSource = list;

            if (TxtCleanerFilesEmptyGuide != null)
            {
                TxtCleanerFilesEmptyGuide.Visibility = _cleanerMasterFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateCleanerStats();
        }

        private void CleanerFileCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateCleanerStats();
        }

        private void BtnSelectAllCleanerFiles_Click(object sender, RoutedEventArgs e)
        {
            if (DgCleanerFiles.ItemsSource is IEnumerable<CleanerFileItem> currentList)
            {
                foreach (var item in currentList) item.IsSelected = true;
            }
            UpdateCleanerStats();
        }

        private void BtnDeselectAllCleanerFiles_Click(object sender, RoutedEventArgs e)
        {
            if (DgCleanerFiles.ItemsSource is IEnumerable<CleanerFileItem> currentList)
            {
                foreach (var item in currentList) item.IsSelected = false;
            }
            UpdateCleanerStats();
        }

        private void UpdateCleanerStats()
        {
            if (TxtCleanerTotalCount == null) return;

            var visibleList = (DgCleanerFiles.ItemsSource as IEnumerable<CleanerFileItem> ?? _cleanerMasterFiles).ToList();
            int total = visibleList.Count;
            int selected = visibleList.Count(x => x.IsSelected);
            long selectedBytes = visibleList.Where(x => x.IsSelected).Sum(x => x.FileSizeBytes);

            TxtCleanerTotalCount.Text = $"{total:N0}개";
            TxtCleanerSelectedCount.Text = $"{selected:N0}개";
            TxtCleanerEstimatedTotal.Text = CleanerTargetItem.FormatBytes(selectedBytes);
        }

        private async void BtnScanCleaner_Click(object sender, RoutedEventArgs e)
        {
            string devRoot = TxtDevRootPath.Text.Trim();
            bool protectRecent = ChkProtectRecentFiles.IsChecked == true;
            var activeTargetIds = _cleanerTargets.Where(x => x.IsSelected).Select(x => x.Id).ToList();

            if (activeTargetIds.Count == 0)
            {
                MessageBox.Show("스캔할 정리 대상을 1개 이상 선택해 주세요.\n[스캔 대상 범주] 탭에서 원하는 항목을 체크할 수 있습니다.", "스캔 대상 없음", MessageBoxButton.OK, MessageBoxImage.Information);
                RbCleanerTabTargets.IsChecked = true;
                return;
            }

            BtnScanCleaner.IsEnabled = false;
            BtnRunCleaner.IsEnabled = false;
            PbCleaner.Value = 0;
            TxtCleanerStatus.Text = "파일 정밀 스캔 중...";
            TxtCleanerLogStatus.Text = "스캔 중...";
            TxtCleanerLog.Text = $"[{DateTime.Now:HH:mm:ss}] 파일 정밀 스캔을 시작합니다...\n";

            SaveCurrentSettings();

            try
            {
                var progress = new Progress<(double Percent, string? Log)>(p =>
                {
                    PbCleaner.Value = Math.Clamp(p.Percent, 0, 100);
                    if (!string.IsNullOrEmpty(p.Log))
                    {
                        TxtCleanerLog.Text += $"{p.Log}\n";
                        SvCleanerLog.ScrollToBottom();
                    }
                });

                var scanResult = await Task.Run(() => CleanerService.ScanDetailedFiles(activeTargetIds, devRoot, protectRecent, progress));
                var scannedFiles = scanResult.Files;

                // 카테고리별 요약 크기 업데이트
                foreach (var t in _cleanerTargets)
                {
                    if (!t.IsSelected)
                    {
                        t.EstimatedBytes = 0;
                        t.StatusText = "스캔 제외";
                        continue;
                    }

                    int targetCount = scannedFiles.Count(x => x.TargetId == t.Id);
                    long targetBytes = scannedFiles.Where(x => x.TargetId == t.Id).Sum(x => x.FileSizeBytes);
                    t.EstimatedBytes = targetBytes;
                    t.StatusText = targetCount > 0 ? $"{targetCount:N0}개 파일" : "깨끗함";
                }

                _cleanerMasterFiles.Clear();
                _cleanerMasterFiles.AddRange(scannedFiles);

                ApplyCleanerFilter();

                // 스캔 완료 후 파일 목록 탭으로 자동 전환하여 보여주기
                RbCleanerTabFiles.IsChecked = true;
                PbCleaner.Value = 100;

                long totalBytes = _cleanerMasterFiles.Sum(x => x.FileSizeBytes);
                string totalSizeText = CleanerTargetItem.FormatBytes(totalBytes);

                string statusExtra = scanResult.TruncatedCategories.Count > 0 ? " · 일부 상한 초과" : string.Empty;
                TxtCleanerStatus.Text = $"스캔 완료 ({_cleanerMasterFiles.Count:N0}개 발견{statusExtra})";
                TxtCleanerLogStatus.Text = "스캔 완료";
                TxtCleanerLog.Text += $"\n✅ 정밀 스캔 완료! 총 {_cleanerMasterFiles.Count:N0}개 파일 ({totalSizeText})\n";
                if (scanResult.SkippedRecentCount > 0)
                {
                    TxtCleanerLog.Text += $"🛡️ 최근 24시간 이내 수정 파일 {scanResult.SkippedRecentCount:N0}개는 안전을 위해 건너뛰었습니다.\n";
                }
                if (scanResult.TruncatedCategories.Count > 0)
                {
                    TxtCleanerLog.Text += $"ℹ️ 항목당 최대 {CleanerService.DefaultMaxFilesPerCategory:N0}개 제한으로 일부 파일이 생략된 분류: {string.Join(", ", scanResult.TruncatedCategories)}\n";
                }
                SvCleanerLog.ScrollToBottom();
            }
            finally
            {
                BtnScanCleaner.IsEnabled = true;
                BtnRunCleaner.IsEnabled = true;
            }
        }

        private async void BtnRunCleaner_Click(object sender, RoutedEventArgs e)
        {
            var visibleFiles = DgCleanerFiles.ItemsSource as IEnumerable<CleanerFileItem> ?? _cleanerMasterFiles;
            var selectedFiles = visibleFiles.Where(x => x.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("삭제할 파일이 선택되지 않았습니다.\n현재 표시된 목록에서 지우고 싶은 파일의 체크박스를 확인해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            long selectedTotalBytes = selectedFiles.Sum(x => x.FileSizeBytes);
            string selectedSizeText = CleanerTargetItem.FormatBytes(selectedTotalBytes);

            var confirm = MessageBox.Show(
                $"선택한 {selectedFiles.Count:N0}개의 항목(약 {selectedSizeText})을 영구 삭제하시겠습니까?\n\n※ 임시/캐시 및 휴지통 파일은 삭제 후 복구할 수 없습니다.",
                "클리너 삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            BtnScanCleaner.IsEnabled = false;
            BtnRunCleaner.IsEnabled = false;
            PbCleaner.Value = 0;
            TxtCleanerStatus.Text = "파일 삭제 진행 중...";
            TxtCleanerLogStatus.Text = "정리 중...";

            // 실시간 상황을 볼 수 있도록 로그 탭으로 전환
            RbCleanerTabLog.IsChecked = true;
            TxtCleanerLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] 선택된 {selectedFiles.Count:N0}개 항목 삭제를 시작합니다...\n";

            try
            {
                var progress = new Progress<(double Percent, string? Log)>(p =>
                {
                    PbCleaner.Value = Math.Clamp(p.Percent, 0, 100);
                    if (!string.IsNullOrEmpty(p.Log))
                    {
                        TxtCleanerLog.Text += $"{p.Log}\n";
                        SvCleanerLog.ScrollToBottom();
                    }
                });

                string devRoot = TxtDevRootPath.Text.Trim();
                var (freedBytes, deletedCount, failedCount, recycleBinEmptied) =
                    await Task.Run(() => CleanerService.DeleteSelectedFiles(selectedFiles, progress, devRoot));

                // 삭제된 파일들을 마스터 목록에서 제거 (휴지통은 비우기 성공 여부로 판단)
                var deletedPathSet = new HashSet<string>(selectedFiles.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
                _cleanerMasterFiles.RemoveAll(x => deletedPathSet.Contains(x.FullPath) &&
                    (x.TargetId == "recycle_bin" ? recycleBinEmptied : !File.Exists(x.FullPath)));

                // 카테고리별 요약도 갱신
                foreach (var t in _cleanerTargets)
                {
                    if (!t.IsSelected)
                    {
                        t.EstimatedBytes = 0;
                        t.StatusText = "스캔 제외";
                        continue;
                    }

                    int remainCount = _cleanerMasterFiles.Count(x => x.TargetId == t.Id);
                    long remainBytes = _cleanerMasterFiles.Where(x => x.TargetId == t.Id).Sum(x => x.FileSizeBytes);
                    t.EstimatedBytes = remainBytes;
                    t.StatusText = remainCount > 0 ? $"{remainCount:N0}개 파일" : "정리됨";
                }

                ApplyCleanerFilter();
                PbCleaner.Value = 100;

                string finalSize = CleanerTargetItem.FormatBytes(freedBytes);
                TxtCleanerStatus.Text = $"정리 완료 ({finalSize} 확보)";
                TxtCleanerLogStatus.Text = "정리 완료";
                TxtCleanerLog.Text += "\n============================================\n";
                TxtCleanerLog.Text += $"✨ 최종 정리 완료: {deletedCount:N0}개 삭제, {finalSize} 디스크 공간 확보\n";
                if (failedCount > 0)
                {
                    TxtCleanerLog.Text += $"ℹ️ {failedCount:N0}개 파일은 다른 앱에서 사용 중(잠김)이어서 보존되었습니다.\n";
                }
                TxtCleanerLog.Text += "============================================\n\n";
                SvCleanerLog.ScrollToBottom();

                RefreshDrives();

                MessageBox.Show(
                    $"총 {deletedCount:N0}개의 파일이 성공적으로 삭제되었습니다.\n확보된 디스크 용량: {finalSize}",
                    "정리 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                BtnScanCleaner.IsEnabled = true;
                BtnRunCleaner.IsEnabled = true;
            }
        }

        // =====================================================================
        // MODULE 3: REGISTRY & STARTUP MANAGER
        // =====================================================================
        private void BtnRegOpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(AppSettingsService.BackupsDirectory))
                {
                    Directory.CreateDirectory(AppSettingsService.BackupsDirectory);
                }
                Process.Start(new ProcessStartInfo(AppSettingsService.BackupsDirectory) { UseShellExecute = true });
            }
            catch { }
        }

        private async void BtnRegScan_Click(object sender, RoutedEventArgs e)
        {
            BtnRegScan.IsEnabled = false;
            BtnViewActiveStartup.IsEnabled = false;
            BtnRegToggleStartup.IsEnabled = false;
            BtnRegClean.IsEnabled = false;
            TxtRegStatus.Text = "🔍 레지스트리 정밀 스캔 중...";
            TxtRegStatus.Foreground = AppBrushes.NeonCyan;

            try
            {
                _registryMasterList = await Task.Run(RegistryScannerService.ScanAllBrokenItems);

                if (CbRegFilter.SelectedIndex >= 5)
                    CbRegFilter.SelectedIndex = 0;
                else
                    ApplyRegistryFilter();

                TxtRegTotalCount.Text = _registryMasterList.Count.ToString("N0");
                if (_registryMasterList.Count > 0)
                {
                    TxtRegStatus.Text = $"⚠️ {_registryMasterList.Count}개의 정리 대상 발견";
                    TxtRegStatus.Foreground = AppBrushes.NeonAmber;
                }
                else
                {
                    TxtRegStatus.Text = "✅ 유령 레지스트리 없이 깨끗합니다!";
                    TxtRegStatus.Foreground = AppBrushes.NeonMint;
                }
            }
            finally
            {
                BtnRegScan.IsEnabled = true;
                BtnViewActiveStartup.IsEnabled = true;
                BtnRegToggleStartup.IsEnabled = true;
                BtnRegClean.IsEnabled = true;
            }
        }

        private async void BtnViewActiveStartup_Click(object sender, RoutedEventArgs e)
        {
            BtnRegScan.IsEnabled = false;
            BtnViewActiveStartup.IsEnabled = false;
            BtnRegToggleStartup.IsEnabled = false;
            BtnRegClean.IsEnabled = false;
            TxtRegStatus.Text = "🚀 시작프로그램(정상/중지) 조회 중...";

            try
            {
                _registryMasterList = await Task.Run(() => RegistryScannerService.ScanStartupItems(onlyBroken: false));
                CbRegFilter.SelectedIndex = 0;
                ApplyRegistryFilter();

                TxtRegTotalCount.Text = _registryMasterList.Count.ToString("N0");
                int activeCount = _registryMasterList.Count(x => x.Category == "정상 시작앱");
                int disabledCount = _registryMasterList.Count(x => x.Category == "중지된 시작앱");
                TxtRegStatus.Text = $"🚀 시작앱 총 {_registryMasterList.Count}개 (사용 {activeCount} · 중지 {disabledCount})";
                TxtRegStatus.Foreground = AppBrushes.NeonCyan;
            }
            finally
            {
                BtnRegScan.IsEnabled = true;
                BtnViewActiveStartup.IsEnabled = true;
                BtnRegToggleStartup.IsEnabled = true;
                BtnRegClean.IsEnabled = true;
            }
        }

        private void BtnRegToggleStartup_Click(object sender, RoutedEventArgs e)
        {
            if (DgRegistry.ItemsSource is not List<RegistryItem> items) return;

            var targets = items.Where(x => x.IsChecked && x.Category is "정상 시작앱" or "중지된 시작앱").ToList();
            if (targets.Count == 0 &&
                DgRegistry.SelectedItem is RegistryItem selected &&
                selected.Category is "정상 시작앱" or "중지된 시작앱")
            {
                targets.Add(selected);
            }

            if (targets.Count == 0)
            {
                MessageBox.Show("사용/중지 상태를 전환할 시작프로그램 항목을 체크하거나 행을 선택해 주세요.\n(먼저 [🚀 시작앱 조회]를 눌러 목록을 불러올 수 있습니다.)", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int toggled = 0;
            var errors = new List<string>();
            foreach (var item in targets)
            {
                bool enable = item.Category == "중지된 시작앱";
                if (RegistryScannerService.ToggleStartupEnabled(item, enable, out string err))
                {
                    item.IsChecked = false;
                    toggled++;
                }
                else if (!string.IsNullOrWhiteSpace(err))
                {
                    errors.Add(err);
                }
            }

            ApplyRegistryFilter();
            DgRegistry.Items.Refresh();

            int activeCount = _registryMasterList.Count(x => x.Category == "정상 시작앱");
            int disabledCount = _registryMasterList.Count(x => x.Category == "중지된 시작앱");

            if (toggled > 0)
            {
                TxtRegStatus.Text = $"🔄 {toggled}개 상태 전환 완료 (사용 {activeCount} · 중지 {disabledCount})";
                TxtRegStatus.Foreground = AppBrushes.NeonMint;
            }
            else
            {
                TxtRegStatus.Text = "⚠️ 시작프로그램 상태를 전환하지 못했습니다.";
                TxtRegStatus.Foreground = AppBrushes.NeonCrimson;
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "일부 항목 전환 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRegClean_Click(object sender, RoutedEventArgs e)
        {
            if (DgRegistry.ItemsSource is not List<RegistryItem> items) return;
            var targets = items.Where(x => x.IsChecked).ToList();

            if (targets.Count == 0)
            {
                MessageBox.Show("정리할 레지스트리 항목을 선택해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool hasActiveStartup = targets.Any(x => x.Category is "정상 시작앱" or "중지된 시작앱");
            string confirmMsg = hasActiveStartup
                ? $"선택한 {targets.Count}개 항목 중 시작프로그램이 포함되어 있습니다.\n(삭제 대신 [🔄 사용/중지 전환]을 사용할 수도 있습니다.)\n\n정리 전 백업 파일을 자동 생성한 후 삭제할까요?"
                : $"선택한 {targets.Count}개의 불필요한 레지스트리 항목을 삭제하시겠습니까?\n(정리 직전 Backups 폴더에 복구 백업이 자동 저장됩니다.)";

            if (MessageBox.Show(confirmMsg, "레지스트리 정리 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            bool hasRegistryItems = targets.Any(x => !x.KeyPath.EndsWith("StartupFolder", StringComparison.OrdinalIgnoreCase));
            bool hasStartupFolderFiles = targets.Any(x =>
                x.KeyPath.EndsWith("StartupFolder", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(x.Data) &&
                File.Exists(x.Data));

            string? regBackupPath = hasRegistryItems ? RegistryScannerService.BackupRegistryItems(targets) : null;
            string? startupBackupDir = hasStartupFolderFiles ? RegistryScannerService.BackupStartupFolderFiles(targets) : null;

            bool regBackupFailed = hasRegistryItems && regBackupPath == null;
            bool startupBackupFailed = hasStartupFolderFiles && startupBackupDir == null;
            if (regBackupFailed || startupBackupFailed)
            {
                var proceed = MessageBox.Show(
                    "정리 전 백업 파일 생성에 실패했습니다.\n백업 없이 그대로 삭제를 진행하시겠습니까?",
                    "백업 실패 경고",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes)
                {
                    TxtRegStatus.Text = "⚠️ 백업 실패로 정리를 취소했습니다.";
                    TxtRegStatus.Foreground = AppBrushes.NeonAmber;
                    return;
                }
            }

            int deleted = 0;
            foreach (var item in targets)
            {
                if (RegistryScannerService.DeleteRegistryItem(item))
                {
                    _registryMasterList.Remove(item);
                    deleted++;
                }
            }

            ApplyRegistryFilter();
            TxtRegTotalCount.Text = _registryMasterList.Count.ToString("N0");

            string backupInfo = (regBackupPath != null, startupBackupDir != null) switch
            {
                (true, true) => " (.reg 및 시작파일 백업 저장됨)",
                (true, false) => " (.reg 백업 저장됨)",
                (false, true) => " (시작파일 백업 저장됨)",
                _ => string.Empty
            };
            TxtRegStatus.Text = $"✅ {deleted}개 정리 완료{backupInfo}";
            TxtRegStatus.Foreground = AppBrushes.NeonMint;
        }

        private void CbRegFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyRegistryFilter();
        }

        private void ApplyRegistryFilter()
        {
            if (DgRegistry == null) return;
            string filter = (CbRegFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체 리스트";

            var filtered = filter == "전체 리스트"
                ? _registryMasterList.ToList()
                : _registryMasterList.Where(x => x.Category == filter).ToList();

            DgRegistry.ItemsSource = filtered;
            UpdateRegistrySelectedCount();
        }

        private void BtnRegSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (DgRegistry.ItemsSource is List<RegistryItem> items)
            {
                foreach (var item in items) item.IsChecked = true;
                UpdateRegistrySelectedCount();
            }
        }

        private void BtnRegDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (DgRegistry.ItemsSource is List<RegistryItem> items)
            {
                foreach (var item in items) item.IsChecked = false;
                UpdateRegistrySelectedCount();
            }
        }

        private void RegCheckBox_Click(object sender, RoutedEventArgs e) => UpdateRegistrySelectedCount();

        private void UpdateRegistrySelectedCount()
        {
            if (TxtRegSelectedCount == null) return;
            if (DgRegistry.ItemsSource is List<RegistryItem> items)
                TxtRegSelectedCount.Text = items.Count(x => x.IsChecked).ToString("N0");
            else
                TxtRegSelectedCount.Text = "0";
        }

        // =====================================================================
        // MODULE 4: WinSxS COMPONENT STORE (DISM)
        // =====================================================================
        private void AppendSxsLog(string message, Brush? color = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendSxsLog(message, color));
                return;
            }

            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
            para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ") { Foreground = AppBrushes.LogTimestamp });
            para.Inlines.Add(new Run(message) { Foreground = color ?? AppBrushes.TextDefault });
            RtbSxsLog.Document.Blocks.Add(para);
            RtbSxsLog.ScrollToEnd();
        }

        private void SetSxsButtonsState(bool idle)
        {
            BtnSxsAnalyze.IsEnabled = idle;
            BtnSxsCleanup.IsEnabled = idle;
            BtnSxsResetBase.IsEnabled = idle;
            BtnSxsSfc.IsEnabled = idle;
            BtnSxsRestoreHealth.IsEnabled = idle;
            BtnSxsSaveLog.IsEnabled = idle;
            BtnSxsCancel.IsEnabled = !idle;
        }

        private static async Task<(bool IsActive, double WorkingSetMb)> SampleActiveTiWorkerAsync(
            int sampleDelayMs = 450,
            CancellationToken token = default)
        {
            var initialCpuByPid = new Dictionary<int, TimeSpan>();
            var firstPass = Process.GetProcessesByName("TiWorker");
            try
            {
                foreach (var proc in firstPass)
                {
                    try
                    {
                        initialCpuByPid[proc.Id] = proc.TotalProcessorTime;
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
                foreach (var proc in firstPass)
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            if (initialCpuByPid.Count == 0)
                return (false, 0);

            await Task.Delay(sampleDelayMs, token).ConfigureAwait(false);

            double totalDeltaCpuMs = 0;
            double totalMemMb = 0;
            var secondPass = Process.GetProcessesByName("TiWorker");
            try
            {
                foreach (var proc in secondPass)
                {
                    try
                    {
                        totalMemMb += proc.WorkingSet64 / (1024.0 * 1024.0);
                        if (initialCpuByPid.TryGetValue(proc.Id, out var prevCpu))
                        {
                            double deltaMs = (proc.TotalProcessorTime - prevCpu).TotalMilliseconds;
                            if (deltaMs > 0)
                                totalDeltaCpuMs += deltaMs;
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
                foreach (var proc in secondPass)
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            return (totalDeltaCpuMs >= 5.0, totalMemMb);
        }

        private static async Task<(bool IsBusy, string Summary)> GetBusyDismBackgroundProcessesAsync(
            int? excludePid = null,
            CancellationToken token = default)
        {
            string[] targetNames = ["dism", "DismHost"];
            var running = new List<string>();

            foreach (string procName in targetNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(procName);
                    int count = 0;
                    foreach (var proc in procs)
                    {
                        try
                        {
                            if (!excludePid.HasValue || proc.Id != excludePid.Value)
                                count++;
                        }
                        catch
                        {
                            count++;
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }

                    if (count > 0)
                        running.Add(procName);
                }
                catch { }
            }

            var (tiActive, _) = await SampleActiveTiWorkerAsync(450, token);
            if (tiActive)
                running.Add("TiWorker");

            return running.Count > 0
                ? (true, string.Join(", ", running))
                : (false, string.Empty);
        }

        private async Task RefreshSxsIdleStatusAsync()
        {
            if (_isSxsJobRunning) return;

            var (isBusy, summary) = await GetBusyDismBackgroundProcessesAsync();
            if (_isSxsJobRunning) return;

            if (isBusy)
            {
                TxtSxsProgressStatus.Text = $"⚠️ 백그라운드 작업 감지됨 ({summary})";
                TxtSxsProgressStatus.Foreground = AppBrushes.NeonAmber;
            }
            else
            {
                TxtSxsProgressStatus.Text = "대기 중...";
                TxtSxsProgressStatus.Foreground = AppBrushes.NeonCyan;
            }
        }

        private static Encoding GetDismConsoleEncoding()
        {
            try
            {
                int oemCp = CultureInfo.InstalledUICulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(oemCp);
            }
            catch
            {
                return Encoding.GetEncoding("euc-kr");
            }
        }

        private static async Task ReadConsoleStreamAsync(StreamReader reader, CancellationToken token, Action<string> onLine)
        {
            char[] buffer = new char[512];
            var sb = new StringBuilder();
            while (!token.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (c == '\r' || c == '\n')
                    {
                        if (sb.Length > 0)
                        {
                            string line = sb.ToString();
                            sb.Clear();
                            onLine(line);
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            if (sb.Length > 0)
            {
                onLine(sb.ToString());
            }
        }

        private Task<int> RunDismAsync(string arguments, string jobTitle, bool checkBackgroundBusy = true) =>
            RunSystemToolAsync("dism.exe", arguments, jobTitle, isSfc: false, checkBackgroundBusy: checkBackgroundBusy);

        // 시스템 도구(DISM / SFC) 실행 후 종료 코드를 반환합니다. (0 = 성공, 3010 = 재부팅 필요, 그 외 = 실패, -1 = 실행 불가/중지)
        private async Task<int> RunSystemToolAsync(string fileName, string arguments, string jobTitle, bool isSfc, bool checkBackgroundBusy = true)
        {
            if (!isSfc && checkBackgroundBusy)
            {
                var (isBusy, busySummary) = await GetBusyDismBackgroundProcessesAsync();
                if (isBusy)
                {
                    AppendSxsLog($"⚠️ [자동 감지] 백그라운드에서 Windows 모듈 작업({busySummary})이 이미 실행 중입니다.", Brushes.Gold);
                    var choice = MessageBox.Show(
                        $"현재 백그라운드에서 DISM 또는 Windows 업데이트 모듈({busySummary})이 이미 작업 중입니다.\n" +
                        "이 상태에서 바로 실행하면 앞선 작업이 끝날 때까지 0%에서 오래 대기할 수 있습니다.\n\n" +
                        "• [예]: 백그라운드 작업이 끝날 때까지 자동으로 기다린 뒤 시작 (권장)\n" +
                        "• [아니요]: 기다리지 않고 지금 즉시 실행\n" +
                        "• [취소]: 작업 실행 취소",
                        "⚠️ 백그라운드 DISM 작업 감지",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (choice == MessageBoxResult.Cancel)
                    {
                        AppendSxsLog("ℹ️ 백그라운드 작업 감지로 인해 실행을 취소했습니다.", Brushes.Orange);
                        await RefreshSxsIdleStatusAsync();
                        return -1;
                    }

                    if (choice == MessageBoxResult.Yes)
                    {
                        _isSxsJobRunning = true;
                        SetSxsButtonsState(idle: false);
                        PbSxs.IsIndeterminate = true;
                        PbSxs.Value = 0;

                        var waitCts = new CancellationTokenSource();
                        _activeDismWaitCts = waitCts;
                        DateTime waitStart = DateTime.Now;
                        AppendSxsLog($"⏳ 백그라운드 작업({busySummary})이 끝날 때까지 자동 대기합니다. (중지 버튼으로 대기 취소 가능)", Brushes.DeepSkyBlue);

                        try
                        {
                            while (true)
                            {
                                var (stillBusy, currentSummary) = await GetBusyDismBackgroundProcessesAsync(token: waitCts.Token);
                                if (!stillBusy)
                                {
                                    await Task.Delay(1000, waitCts.Token);
                                    var (recheckBusy, recheckSummary) = await GetBusyDismBackgroundProcessesAsync(token: waitCts.Token);
                                    if (!recheckBusy)
                                        break;
                                    currentSummary = recheckSummary;
                                }

                                var waitElapsed = DateTime.Now - waitStart;
                                TxtSxsProgressStatus.Text = $"백그라운드 작업({currentSummary}) 종료 대기 중... (경과: {waitElapsed:mm\\:ss})";
                                TxtSxsProgressStatus.Foreground = AppBrushes.NeonAmber;

                                await Task.Delay(2000, waitCts.Token);
                            }

                            AppendSxsLog($"✅ 백그라운드 작업이 종료되어 [{jobTitle}] 작업을 바로 시작합니다.", Brushes.SpringGreen);
                        }
                        catch (OperationCanceledException)
                        {
                            AppendSxsLog("🛑 사용자에 의해 백그라운드 작업 대기가 취소되었습니다.", Brushes.OrangeRed);
                            _isSxsJobRunning = false;
                            PbSxs.IsIndeterminate = false;
                            PbSxs.Value = 0;
                            SetSxsButtonsState(idle: true);
                            await RefreshSxsIdleStatusAsync();
                            return -1;
                        }
                        finally
                        {
                            _activeDismWaitCts = null;
                            waitCts.Dispose();
                        }
                    }
                    else
                    {
                        AppendSxsLog("⚠️ 백그라운드 작업이 실행 중인 상태에서 즉시 실행을 진행합니다.", Brushes.Orange);
                    }
                }
            }

            _isSxsJobRunning = true;
            SetSxsButtonsState(idle: false);
            PbSxs.IsIndeterminate = true;
            PbSxs.Value = 0;
            TxtSxsProgressStatus.Foreground = AppBrushes.NeonCyan;
            DateTime startTime = DateTime.Now;
            TxtSxsProgressStatus.Text = $"{jobTitle} 준비 중... (00:00)";
            AppendSxsLog($"🚀 [{jobTitle}] 작업을 시작합니다. ({fileName} {arguments})", Brushes.DeepSkyBlue);
            _dismLogBuffer.Clear();

            if (!isSfc && arguments.Contains("/AnalyzeComponentStore", StringComparison.OrdinalIgnoreCase))
            {
                AppendSxsLog("💡 [안내] 최신 Windows 11은 백그라운드 절전 모드(EcoQoS)로 고압축 누적 업데이트 패키지(MumX)를 전수 대조하므로 분석에 5~15분 정도 소요될 수 있습니다.", Brushes.DeepSkyBlue);
                AppendSxsLog("💡 진행률이 특정 구간(예: 10%~20%)에서 수 분간 멈춰 보여도 정상 계산 중이니 안심하고 기다려 주세요.", Brushes.DeepSkyBlue);
            }
            else if (!isSfc && arguments.Contains("/StartComponentCleanup", StringComparison.OrdinalIgnoreCase))
            {
                AppendSxsLog("💡 [안내] WinSxS 정리는 시스템 파일 및 수십만 개의 백업 링크를 전수 대조·압축·정리하므로 용량에 따라 5분~20분 이상 소요될 수 있습니다.", Brushes.DeepSkyBlue);
                AppendSxsLog("💡 초기 2~4분간은 준비 및 의존성 계산 단계로 진행률이 0%에 머무를 수 있습니다. PC가 멈춘 것이 아니니 안심하고 기다려 주세요.", Brushes.DeepSkyBlue);
            }
            else if (isSfc || arguments.Contains("/RestoreHealth", StringComparison.OrdinalIgnoreCase))
            {
                AppendSxsLog("💡 [안내] Windows 시스템 파일 및 이미지 무결성 검사는 환경에 따라 5~15분 정도 소요될 수 있습니다.", Brushes.DeepSkyBlue);
            }

            int exitCode = -1;
            using var cts = new CancellationTokenSource();
            double lastPercentage = -1;
            DateTime lastOutputTime = DateTime.Now;
            int reminderMinute = 0;

            // 경과 시간 및 주기적 안심 안내 백그라운드 루프 (1초 주기)
            var timerTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    bool isAnalyze = !isSfc && arguments.Contains("/AnalyzeComponentStore", StringComparison.OrdinalIgnoreCase);
                    var elapsed = DateTime.Now - startTime;
                    bool isQuietPeriod = (DateTime.Now - lastOutputTime).TotalSeconds >= 30;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (lastPercentage >= 0)
                        {
                            TxtSxsProgressStatus.Text = isAnalyze && isQuietPeriod
                                ? $"{jobTitle}: {lastPercentage:F1}% · 패키지 정밀 대조 중... (경과: {elapsed:mm\\:ss})"
                                : $"{jobTitle}: {lastPercentage:F1}% 진행 중... (경과: {elapsed:mm\\:ss})";
                        }
                        else
                        {
                            TxtSxsProgressStatus.Text = isAnalyze
                                ? $"WinSxS 저장소 상태 분석 중... (경과: {elapsed:mm\\:ss})"
                                : $"{jobTitle} 진행 중... (경과: {elapsed:mm\\:ss})";
                        }
                    });

                    // 1~3분은 1분마다, 3분 이후에는 3분마다(6분, 9분, 12분...) 새 출력이 없으면 안심 메시지 출력
                    int elapsedMinutes = (int)elapsed.TotalMinutes;
                    bool shouldRemind = elapsedMinutes <= 3
                        ? elapsedMinutes > reminderMinute
                        : elapsedMinutes >= reminderMinute + 3;

                    if (shouldRemind && (DateTime.Now - lastOutputTime).TotalSeconds >= 45)
                    {
                        reminderMinute = elapsedMinutes;
                        string statusSuffix = $"(경과 시간: {elapsed:mm\\:ss})";
                        if (!isSfc)
                        {
                            try
                            {
                                var (tiActive, tiMemMb) = await SampleActiveTiWorkerAsync(250, cts.Token);
                                if (tiActive)
                                {
                                    statusSuffix = $"(TiWorker 정상 가동 중 · 메모리 약 {tiMemMb:F0} MB, 경과: {elapsed:mm\\:ss})";
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }

                        string hint = isSfc
                            ? "⏳ [작업 진행 중] Windows 리소스 보호가 보호된 시스템 파일의 무결성을 계속 검증하고 있습니다."
                            : isAnalyze
                                ? reminderMinute switch
                                {
                                    1 => "⏳ [작업 진행 중] Windows 모듈 작업자(TiWorker)가 WinSxS 폴더 트리와 구성 요소를 스캔하고 있습니다.",
                                    2 => "⏳ [작업 진행 중] 하드링크 공유 용량 및 패키지 종속성을 정밀 계산하고 있습니다.",
                                    3 => "⏳ [작업 진행 중] 최신 누적 업데이트 압축 컨테이너(MumX) 내부 패키지를 대조 중입니다. (절전 모드로 인해 수 분 이상 소요될 수 있습니다)",
                                    _ => "⏳ [작업 진행 중] 대용량 누적 업데이트 패키지(MumX) 정밀 대조가 계속 진행 중입니다. 잠시만 더 기다려 주세요."
                                }
                                : reminderMinute switch
                                {
                                    1 => "⏳ [작업 진행 중] Windows 모듈 작업자(TiWorker)가 백그라운드에서 작업을 정상 수행하고 있습니다.",
                                    2 => "⏳ [작업 진행 중] 시스템 구성 요소 검사 및 정리를 계속 진행하고 있습니다.",
                                    3 => "⏳ [작업 진행 중] 대용량 패키지 처리가 진행 중입니다. 최대 10~20분 소요될 수 있습니다.",
                                    _ => "⏳ [작업 진행 중] 작업이 안전하게 진행 중입니다. 잠시만 더 기다려 주세요."
                                };
                        AppendSxsLog($"{hint} {statusSuffix}", Brushes.DeepSkyBlue);
                    }
                }
            });

            void ProcessOutputLine(string rawLine)
            {
                string trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return;

                lock (_dismLogBuffer)
                {
                    _dismLogBuffer.AppendLine(trimmed);
                }

                var m = DismPercentRegex().Match(trimmed);
                double pct = 0;
                bool isPercentLine = m.Success && double.TryParse(m.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pct);
                if (isPercentLine)
                {
                    double capturedPct = pct;
                    lastPercentage = capturedPct;
                    lastOutputTime = DateTime.Now;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (PbSxs.IsIndeterminate)
                            PbSxs.IsIndeterminate = false;
                        PbSxs.Value = capturedPct;
                        var elapsed = DateTime.Now - startTime;
                        TxtSxsProgressStatus.Text = $"{jobTitle}: {capturedPct:F1}% 진행 중... (경과: {elapsed:mm\\:ss})";
                    });
                }

                if (!(trimmed.StartsWith('[') && trimmed.Contains('%')) &&
                    !(isSfc && isPercentLine) &&
                    !trimmed.StartsWith("배포 이미지") &&
                    !trimmed.StartsWith("이미지 버전") &&
                    !trimmed.StartsWith("Deployment Image") &&
                    !trimmed.StartsWith("Image Version"))
                {
                    lastOutputTime = DateTime.Now;
                    AppendSxsLog(trimmed);
                }
            }

            void ProcessErrorLine(string rawLine)
            {
                string trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return;

                lock (_dismLogBuffer)
                {
                    _dismLogBuffer.AppendLine(trimmed);
                }
                lastOutputTime = DateTime.Now;
                AppendSxsLog($"⚠️ {trimmed}", Brushes.Tomato);
            }

            try
            {
                try
                {
                    Encoding consoleEncoding = isSfc ? Encoding.Unicode : GetDismConsoleEncoding();
                    await Task.Run(async () =>
                    {
                        try
                        {
                            using var proc = new Process();
                            _activeDismProcess = proc;
                            proc.StartInfo.FileName = fileName;
                            proc.StartInfo.Arguments = arguments;
                            proc.StartInfo.UseShellExecute = false;
                            proc.StartInfo.RedirectStandardOutput = true;
                            proc.StartInfo.RedirectStandardError = true;
                            proc.StartInfo.CreateNoWindow = true;
                            proc.StartInfo.StandardOutputEncoding = consoleEncoding;
                            proc.StartInfo.StandardErrorEncoding = consoleEncoding;

                            proc.Start();

                            var stdoutTask = ReadConsoleStreamAsync(proc.StandardOutput, cts.Token, ProcessOutputLine);
                            var stderrTask = ReadConsoleStreamAsync(proc.StandardError, cts.Token, ProcessErrorLine);

                            await proc.WaitForExitAsync(cts.Token);
                            await Task.WhenAll(stdoutTask, stderrTask);
                            exitCode = proc.ExitCode;
                        }
                        catch (OperationCanceledException)
                        {
                            exitCode = -1;
                        }
                        catch (Exception ex)
                        {
                            AppendSxsLog($"❌ 실행 오류: {ex.Message}", Brushes.Tomato);
                        }
                        finally
                        {
                            _activeDismProcess = null;
                        }
                    });
                }
                finally
                {
                    cts.Cancel();
                    try { await timerTask; } catch { }
                }

                // 종료 코드에 따라 성공/재부팅 필요/실패를 명확히 안내
                if (exitCode == 0)
                {
                    AppendSxsLog($"✅ [{jobTitle}] 작업이 정상 종료되었습니다. (종료 코드 0)", Brushes.SpringGreen);
                }
                else if (exitCode == 3010)
                {
                    AppendSxsLog($"🔄 [{jobTitle}] 작업은 끝났지만 재부팅이 필요합니다. (종료 코드 3010) PC를 재시작하면 정리가 마무리됩니다.", Brushes.Gold);
                }
                else if (exitCode == -1)
                {
                    AppendSxsLog($"❌ [{jobTitle}] 작업이 정상적으로 끝나지 않았습니다. (실행 오류 또는 사용자 중지)", Brushes.Tomato);
                }
                else
                {
                    AppendSxsLog($"❌ [{jobTitle}] 작업이 실패했습니다. (종료 코드 {exitCode} / 0x{exitCode:X8}) 위의 오류 메시지를 확인하세요. 재부팅 후 다시 시도하면 해결되는 경우가 많습니다.", Brushes.Tomato);
                }

                PbSxs.Value = (exitCode is 0 or 3010 ? 100 : 0);
            }
            finally
            {
                _isSxsJobRunning = false;
                PbSxs.IsIndeterminate = false;
                SetSxsButtonsState(idle: true);
                TxtSxsProgressStatus.Text = "대기 중...";
                TxtSxsProgressStatus.Foreground = AppBrushes.NeonCyan;
                _ = RefreshSxsIdleStatusAsync();
            }

            return exitCode;
        }

        // DISM 분석 결과를 화면 카드에 그대로 반영하고, "실제 크기"를 MB 단위로 반환합니다. (찾지 못하면 null)
        private double? ParseDismOutput(string fullOutput)
        {
            var sizeRegex = DismSizeRegex();
            string[] lines = fullOutput.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);
            double? actualMb = null;
            bool parsedAny = false;

            foreach (string line in lines)
            {
                if (line.Contains("구성 요소 저장소 정리 권장", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Component Store Cleanup Recommended", StringComparison.OrdinalIgnoreCase))
                {
                    bool recommended = line.Contains('예') || line.Contains("Yes", StringComparison.OrdinalIgnoreCase);

                    if (!recommended)
                    {
                        TxtSxsStatusCard.Text = "✅ 아주 깨끗한 상태";
                        TxtSxsStatusCard.Foreground = AppBrushes.NeonMint;
                    }
                    else
                    {
                        TxtSxsStatusCard.Text = "📢 정리 권장됨";
                        TxtSxsStatusCard.Foreground = AppBrushes.NeonCrimson;
                    }

                    _settings.LastSxsStatus = TxtSxsStatusCard.Text;
                    _settings.LastSxsRecommended = recommended;
                    parsedAny = true;
                    continue;
                }

                var m = sizeRegex.Match(line);
                if (!m.Success) continue;

                string size = m.Groups["size"].Value.Trim();
                string unit = m.Groups["unit"].Value.Trim();

                if (line.Contains("실제 크기", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Actual Size of Component Store", StringComparison.OrdinalIgnoreCase))
                {
                    TxtSxsActual.Text = $"{size} {unit}";
                    actualMb = ToMegabytes(size, unit);
                    _settings.LastSxsActual = TxtSxsActual.Text;
                    _settings.LastSxsActualMb = actualMb;
                    parsedAny = true;
                }
                else if (line.Contains("Windows와 공유됨", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Shared with Windows", StringComparison.OrdinalIgnoreCase))
                {
                    TxtSxsShared.Text = $"{size} {unit}";
                    _settings.LastSxsShared = TxtSxsShared.Text;
                    parsedAny = true;
                }
                else if (line.Contains("백업 및 기능 사용 안 함", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Backups and Disabled Features", StringComparison.OrdinalIgnoreCase))
                {
                    TxtSxsExpected.Text = $"{size} {unit}";
                    _settings.LastSxsExpected = TxtSxsExpected.Text;
                    parsedAny = true;
                }
            }

            if (parsedAny)
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                _settings.LastSxsAnalyzedAt = stamp;
                TxtSxsLastAnalyzed.Text = $"최근 분석: {stamp}";
                SaveCurrentSettings();
            }

            return actualMb;
        }

        private static double? ToMegabytes(string size, string unit)
        {
            if (!double.TryParse(size, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                return null;

            return unit.ToUpperInvariant() switch
            {
                "GB" => v * 1024,
                "MB" => v,
                "KB" => v / 1024,
                "BYTES" => v / 1024 / 1024,
                _ => null
            };
        }

        private static string FormatMb(double mb) =>
            Math.Abs(mb) >= 1024 ? $"{mb / 1024:F2} GB" : $"{mb:F0} MB";

        // 저장소 분석을 실행하고 "실제 크기"(MB)를 반환
        private async Task<double?> AnalyzeSxsAsync(bool checkBackgroundBusy = true)
        {
            int code = await RunDismAsync("/Online /Cleanup-Image /AnalyzeComponentStore", "WinSxS 저장소 분석", checkBackgroundBusy);
            double? actualMb = ParseDismOutput(_dismLogBuffer.ToString());
            if (code == 0)
                AppendSxsLog("✅ 구성 요소 저장소 분석이 완료되었습니다.", Brushes.SpringGreen);
            if (actualMb.HasValue)
                _lastActualSizeMb = actualMb;
            _lastDismMode = string.Empty;
            return actualMb;
        }

        private async void BtnSxsAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeSxsAsync();
        }

        // 정리 실행 → 재분석 → 정리 전/후 실제 크기를 비교해 결과를 알려줌
        private async Task RunCleanupAndCompareAsync(string arguments, string jobTitle)
        {
            double? beforeMb = _lastActualSizeMb;
            string mode = _lastDismMode; // 재분석 과정에서 값이 초기화되므로 미리 보관
            int code = await RunDismAsync(arguments, jobTitle);

            // 작업이 정상 종료(0)되었거나 재부팅 필요(3010) 상태인 경우에만 재분석을 진행
            if (code != 0 && code != 3010)
                return;

            AppendSxsLog("🔍 정리 결과 확인을 위해 최신 상태를 다시 분석합니다...", Brushes.Gold);
            double? afterMb = await AnalyzeSxsAsync(checkBackgroundBusy: false);

            if (beforeMb.HasValue && afterMb.HasValue)
            {
                double freed = beforeMb.Value - afterMb.Value;
                if (freed >= 1)
                    AppendSxsLog($"🎉 정리 결과: {FormatMb(beforeMb.Value)} → {FormatMb(afterMb.Value)} (약 {FormatMb(freed)} 확보)", Brushes.SpringGreen);
                else
                {
                    AppendSxsLog($"ℹ️ 정리 결과: {FormatMb(beforeMb.Value)} → {FormatMb(afterMb.Value)} (변화 없음)", Brushes.Orange);
                    if (mode == "ResetBase")
                    {
                        // 심층 정리까지 했는데도 변화가 없는 경우: 사용자가 헷갈리지 않도록 안내
                        AppendSxsLog("💡 안내: 심층 정리까지 마쳤는데도 용량이 그대로라면, 남은 용량은 Windows가 업데이트·복구를 위해 꼭 보관하는 파일입니다.", Brushes.DeepSkyBlue);
                        AppendSxsLog("   더 줄일 수 있는 용량이 거의 없는 정상 상태이므로 추가 정리는 필요하지 않습니다. ('정리 권장: 예' 표시가 계속 남아 있어도 괜찮습니다.)", Brushes.DeepSkyBlue);
                    }
                    else
                    {
                        AppendSxsLog("💡 안내: 일반 정리로 줄일 수 있는 용량이 더 없습니다. 더 줄이고 싶다면 '심층 정리(ResetBase)'를 시도해 보세요.", Brushes.DeepSkyBlue);
                    }
                }
            }
            else if (code == 0 || code == 3010)
            {
                AppendSxsLog("ℹ️ 정리 전 분석 기록이 없어 용량 비교는 생략했습니다. (다음부터는 '저장소 분석' 후 정리하면 전/후 비교가 표시됩니다.)", Brushes.Orange);
            }
        }

        private async void BtnSxsCleanup_Click(object sender, RoutedEventArgs e)
        {
            _lastDismMode = "Cleanup";
            await RunCleanupAndCompareAsync("/Online /Cleanup-Image /StartComponentCleanup", "WinSxS 일반 정리");
        }

        private async void BtnSxsResetBase_Click(object sender, RoutedEventArgs e)
        {
            string warn = "심층 정리(/ResetBase)를 수행하면 이전에 설치된 Windows 업데이트를 제거(롤백)할 수 없게 됩니다.\n가장 많은 용량을 확보할 수 있습니다. 진행하시겠습니까?";
            if (MessageBox.Show(warn, "⚠️ 심층 정리 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _lastDismMode = "ResetBase";
            await RunCleanupAndCompareAsync("/Online /Cleanup-Image /StartComponentCleanup /ResetBase", "WinSxS 심층 정리 (ResetBase)");
        }

        private async void BtnSxsSfc_Click(object sender, RoutedEventArgs e)
        {
            await RunSystemToolAsync("sfc.exe", "/scannow", "SFC 시스템 파일 검사", isSfc: true);
        }

        private async void BtnSxsRestoreHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunDismAsync("/Online /Cleanup-Image /RestoreHealth", "DISM 시스템 이미지 복구");
        }

        private void BtnSxsCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_activeDismWaitCts != null && !_activeDismWaitCts.IsCancellationRequested)
                {
                    _activeDismWaitCts.Cancel();
                    AppendSxsLog("🛑 백그라운드 작업 대기를 취소했습니다.", Brushes.OrangeRed);
                    return;
                }

                if (_activeDismProcess != null && !_activeDismProcess.HasExited)
                {
                    _activeDismProcess.Kill(entireProcessTree: true);
                    AppendSxsLog("🛑 사용자에 의해 작업이 중지되었습니다.", Brushes.OrangeRed);
                    AppendSxsLog("ℹ️ Windows 시스템 모듈이 백그라운드 작업을 안전하게 롤백/정리하는 데 수십 초 정도 소요될 수 있습니다.", Brushes.Orange);
                }
            }
            catch (Exception ex)
            {
                AppendSxsLog($"중지 오류: {ex.Message}", Brushes.Tomato);
            }
        }

        private void BtnSxsSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = $"SystemCareSuite_WinSxS_Log_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };
            if (sfd.ShowDialog() == true)
            {
                string rtbText = new TextRange(RtbSxsLog.Document.ContentStart, RtbSxsLog.Document.ContentEnd).Text;
                string content = !string.IsNullOrWhiteSpace(rtbText)
                    ? rtbText
                    : _dismLogBuffer.ToString();
                File.WriteAllText(sfd.FileName, content, Encoding.UTF8);
                AppendSxsLog($"💾 로그 저장 완료: {sfd.FileName}", Brushes.SpringGreen);
            }
        }

        [GeneratedRegex(@"(?<num>\d+(\.\d+)?)%")]
        private static partial Regex DismPercentRegex();

        [GeneratedRegex(@"(?<size>\d+(\.\d+)?)\s*(?<unit>GB|MB|KB|bytes)", RegexOptions.IgnoreCase, "ko-KR")]
        private static partial Regex DismSizeRegex();

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveCurrentSettings();
            _dashboardTimer?.Stop();
            _monitorService.Dispose();
            try
            {
                if (_activeDismWaitCts != null && !_activeDismWaitCts.IsCancellationRequested)
                    _activeDismWaitCts.Cancel();
            }
            catch { }
            try
            {
                if (_activeDismProcess != null && !_activeDismProcess.HasExited)
                    _activeDismProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }
}
