using System.Collections.ObjectModel;
using System.Diagnostics;
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

        // Module 2: Cleaner
        private readonly ObservableCollection<CleanerTargetItem> _cleanerTargets = [];
        private readonly List<CleanerFileItem> _cleanerMasterFiles = [];

        // Module 3: Registry
        private List<RegistryItem> _registryMasterList = [];

        // Module 4: WinSxS DISM
        private Process? _activeDismProcess;
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
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null)
            {
                TxtSidebarVersion.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build} · .NET 8.0 Portable";
            }

            TxtCoreCount.Text = $"{Environment.ProcessorCount} Threads";

            foreach (var target in CleanerService.CreateDefaultTargets())
            {
                _cleanerTargets.Add(target);
            }
            IcCleanerTargets.ItemsSource = _cleanerTargets;
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
            if (_tickCounter % 4 == 0 && PageDashboard.Visibility == Visibility.Visible)
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
                var warnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D));
                TxtRamPercent.Foreground = warnBrush;
                PbRam.Foreground = warnBrush;
                TxtRamBadge.Text = "⚠️ 부족 주의";
                TxtRamBadge.Foreground = warnBrush;
            }
            else
            {
                var normalBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xA3));
                TxtRamPercent.Foreground = normalBrush;
                PbRam.Foreground = normalBrush;
                TxtRamBadge.Text = "정상";
                TxtRamBadge.Foreground = normalBrush;
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
            BtnQuickRamBoost.IsEnabled = false;
            BtnQuickRamBoost.Content = "⚡ 메모리 최적화 중...";

            // 제외(화이트리스트) 목록 중 활성화된 항목 로드
            var exclusions = RamExclusionService.LoadExclusions()
                .Where(x => x.IsEnabled)
                .Select(x => x.ProcessName)
                .ToList();

            var (freedMb, count, skipped) = await Task.Run(() => _monitorService.OptimizeRam(exclusions));

            UpdateDashboardMetrics();
            RefreshTopProcesses();

            string skippedMsg = skipped > 0 ? $", 제외 {skipped}개" : string.Empty;
            TxtRamBoostResult.Text = $"✅ {count}개 프로세스 정리 (약 {freedMb:F0} MB 확보{skippedMsg})";
            BtnQuickRamBoost.Content = "⚡ 원클릭 RAM 메모리 부스트";
            BtnQuickRamBoost.IsEnabled = true;
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
            }
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

            int total = _cleanerMasterFiles.Count;
            int selected = _cleanerMasterFiles.Count(x => x.IsSelected);
            long selectedBytes = _cleanerMasterFiles.Where(x => x.IsSelected).Sum(x => x.FileSizeBytes);

            TxtCleanerTotalCount.Text = $"{total:N0}개";
            TxtCleanerSelectedCount.Text = $"{selected:N0}개";
            TxtCleanerEstimatedTotal.Text = CleanerTargetItem.FormatBytes(selectedBytes);
        }

        private async void BtnScanCleaner_Click(object sender, RoutedEventArgs e)
        {
            BtnScanCleaner.IsEnabled = false;
            BtnRunCleaner.IsEnabled = false;
            PbCleaner.Value = 0;
            TxtCleanerStatus.Text = "파일 정밀 스캔 중...";
            TxtCleanerLogStatus.Text = "스캔 중...";
            TxtCleanerLog.Text = $"[{DateTime.Now:HH:mm:ss}] 파일 정밀 스캔을 시작합니다...\n";

            string devRoot = TxtDevRootPath.Text.Trim();
            var activeTargetIds = _cleanerTargets.Where(x => x.IsSelected).Select(x => x.Id).ToList();

            if (activeTargetIds.Count == 0)
            {
                // 선택된 범주가 없으면 전체 활성화
                foreach (var t in _cleanerTargets) t.IsSelected = true;
                activeTargetIds = _cleanerTargets.Select(x => x.Id).ToList();
            }

            var progress = new Progress<string>(msg =>
            {
                TxtCleanerLog.Text += $"{msg}\n";
                SvCleanerLog.ScrollToBottom();
            });

            var scannedFiles = await Task.Run(() => CleanerService.ScanDetailedFiles(activeTargetIds, devRoot, progress));

            // 카테고리별 요약 크기 업데이트
            foreach (var t in _cleanerTargets)
            {
                long targetBytes = scannedFiles.Where(x => x.TargetId == t.Id).Sum(x => x.FileSizeBytes);
                t.EstimatedBytes = targetBytes;
                t.StatusText = targetBytes > 0 ? $"{scannedFiles.Count(x => x.TargetId == t.Id):N0}개 파일" : "깨끗함";
            }

            _cleanerMasterFiles.Clear();
            _cleanerMasterFiles.AddRange(scannedFiles);

            ApplyCleanerFilter();

            // 스캔 완료 후 파일 목록 탭으로 자동 전환하여 보여주기
            RbCleanerTabFiles.IsChecked = true;

            long totalBytes = _cleanerMasterFiles.Sum(x => x.FileSizeBytes);
            string totalSizeText = CleanerTargetItem.FormatBytes(totalBytes);

            TxtCleanerStatus.Text = $"스캔 완료 ({_cleanerMasterFiles.Count:N0}개 발견)";
            TxtCleanerLogStatus.Text = "스캔 완료";
            TxtCleanerLog.Text += $"\n✅ 정밀 스캔 완료! 총 {_cleanerMasterFiles.Count:N0}개 파일 ({totalSizeText})\n";
            SvCleanerLog.ScrollToBottom();

            BtnScanCleaner.IsEnabled = true;
            BtnRunCleaner.IsEnabled = true;
        }

        private async void BtnRunCleaner_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = _cleanerMasterFiles.Where(x => x.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show("삭제할 파일이 선택되지 않았습니다.\n목록에서 지우고 싶은 파일의 체크박스를 확인해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            long selectedTotalBytes = selectedFiles.Sum(x => x.FileSizeBytes);
            string selectedSizeText = CleanerTargetItem.FormatBytes(selectedTotalBytes);

            var confirm = MessageBox.Show(
                $"선택한 {selectedFiles.Count:N0}개의 파일(약 {selectedSizeText})을 영구 삭제하시겠습니까?\n\n※ 임시/캐시 파일은 삭제 후 복구할 수 없습니다.",
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
            TxtCleanerLog.Text += $"\n[{DateTime.Now:HH:mm:ss}] 선택된 {selectedFiles.Count:N0}개 파일 삭제를 시작합니다...\n";

            var progress = new Progress<string>(msg =>
            {
                TxtCleanerLog.Text += $"{msg}\n";
                SvCleanerLog.ScrollToBottom();
            });

            var (freedBytes, deletedCount, failedCount) = await Task.Run(() => CleanerService.DeleteSelectedFiles(selectedFiles, progress));

            // 삭제된 파일들을 마스터 목록에서 제거
            var deletedPathSet = new HashSet<string>(selectedFiles.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
            _cleanerMasterFiles.RemoveAll(x => deletedPathSet.Contains(x.FullPath) && !File.Exists(x.FullPath));

            // 카테고리별 요약도 갱신
            foreach (var t in _cleanerTargets)
            {
                long remainBytes = _cleanerMasterFiles.Where(x => x.TargetId == t.Id).Sum(x => x.FileSizeBytes);
                t.EstimatedBytes = remainBytes;
                t.StatusText = remainBytes > 0 ? $"{_cleanerMasterFiles.Count(x => x.TargetId == t.Id):N0}개 파일" : "정리됨";
            }

            ApplyCleanerFilter();

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
            BtnScanCleaner.IsEnabled = true;
            BtnRunCleaner.IsEnabled = true;

            MessageBox.Show(
                $"총 {deletedCount:N0}개의 파일이 성공적으로 삭제되었습니다.\n확보된 디스크 용량: {finalSize}",
                "정리 완료",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // =====================================================================
        // MODULE 3: REGISTRY & STARTUP MANAGER
        // =====================================================================
        private async void BtnRegScan_Click(object sender, RoutedEventArgs e)
        {
            BtnRegScan.IsEnabled = false;
            BtnViewActiveStartup.IsEnabled = false;
            BtnRegClean.IsEnabled = false;
            TxtRegStatus.Text = "🔍 레지스트리 정밀 스캔 중...";
            TxtRegStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));

            _registryMasterList = await Task.Run(RegistryScannerService.ScanAllBrokenItems);

            if (CbRegFilter.SelectedIndex == 5)
                CbRegFilter.SelectedIndex = 0;
            else
                ApplyRegistryFilter();

            TxtRegTotalCount.Text = _registryMasterList.Count.ToString("N0");
            if (_registryMasterList.Count > 0)
            {
                TxtRegStatus.Text = $"⚠️ {_registryMasterList.Count}개의 정리 대상 발견";
                TxtRegStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00));
            }
            else
            {
                TxtRegStatus.Text = "✅ 유령 레지스트리 없이 깨끗합니다!";
                TxtRegStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xA3));
            }

            BtnRegScan.IsEnabled = true;
            BtnViewActiveStartup.IsEnabled = true;
            BtnRegClean.IsEnabled = true;
        }

        private async void BtnViewActiveStartup_Click(object sender, RoutedEventArgs e)
        {
            BtnRegScan.IsEnabled = false;
            BtnViewActiveStartup.IsEnabled = false;
            TxtRegStatus.Text = "🚀 정상 등록된 시작프로그램 조회 중...";

            _registryMasterList = await Task.Run(() => RegistryScannerService.ScanStartupItems(onlyBroken: false));
            CbRegFilter.SelectedIndex = 0;
            ApplyRegistryFilter();

            TxtRegTotalCount.Text = _registryMasterList.Count.ToString("N0");
            TxtRegStatus.Text = $"🚀 활성 시작프로그램 {_registryMasterList.Count}개 조회됨";
            TxtRegStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));

            BtnRegScan.IsEnabled = true;
            BtnViewActiveStartup.IsEnabled = true;
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

            bool hasActiveStartup = targets.Any(x => x.Category == "정상 시작앱");
            string confirmMsg = hasActiveStartup
                ? $"선택한 {targets.Count}개 항목 중 '정상 시작앱'이 포함되어 있습니다.\n정말 레지스트리에서 삭제하시겠습니까?"
                : $"선택한 {targets.Count}개의 불필요한 레지스트리 항목을 안전하게 삭제하시겠습니까?";

            if (MessageBox.Show(confirmMsg, "레지스트리 정리 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

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
            TxtRegStatus.Text = $"✅ {deleted}개 항목 정리 완료!";
            TxtRegStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xA3));
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
            para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ") { Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81)) });
            para.Inlines.Add(new Run(message) { Foreground = color ?? new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)) });
            RtbSxsLog.Document.Blocks.Add(para);
            RtbSxsLog.ScrollToEnd();
        }

        private void SetSxsButtonsState(bool idle)
        {
            BtnSxsAnalyze.IsEnabled = idle;
            BtnSxsCleanup.IsEnabled = idle;
            BtnSxsResetBase.IsEnabled = idle;
            BtnSxsSaveLog.IsEnabled = idle;
            BtnSxsCancel.IsEnabled = !idle;
        }

        // DISM 실행 후 종료 코드를 반환합니다. (0 = 성공, 3010 = 재부팅 필요, 그 외 = 실패, -1 = 실행 불가/중지)
        private async Task<int> RunDismAsync(string arguments, string jobTitle)
        {
            SetSxsButtonsState(idle: false);
            PbSxs.IsIndeterminate = true;
            PbSxs.Value = 0;
            DateTime startTime = DateTime.Now;
            TxtSxsProgressStatus.Text = $"{jobTitle} 준비 중... (00:00)";
            AppendSxsLog($"🚀 [{jobTitle}] 작업을 시작합니다. (dism.exe {arguments})", Brushes.DeepSkyBlue);
            _dismLogBuffer.Clear();

            if (arguments.Contains("/StartComponentCleanup", StringComparison.OrdinalIgnoreCase))
            {
                AppendSxsLog("💡 [안내] WinSxS 정리는 시스템 파일 및 수십만 개의 백업 링크를 전수 대조·압축·정리하므로 용량에 따라 5분~20분 이상 소요될 수 있습니다.", Brushes.DeepSkyBlue);
                AppendSxsLog("💡 초기 2~4분간은 준비 및 의존성 계산 단계로 진행률이 0%에 머무를 수 있습니다. PC가 멈춘 것이 아니니 안심하고 기다려 주세요.", Brushes.DeepSkyBlue);
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

                    bool isAnalyze = arguments.Contains("/AnalyzeComponentStore", StringComparison.OrdinalIgnoreCase);
                    var elapsed = DateTime.Now - startTime;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (lastPercentage >= 0)
                            TxtSxsProgressStatus.Text = $"{jobTitle}: {lastPercentage:F1}% 진행 중... (경과: {elapsed:mm\\:ss})";
                        else
                        {
                            TxtSxsProgressStatus.Text = isAnalyze
                                ? $"WinSxS 저장소 상태 분석 중... (경과: {elapsed:mm\\:ss})"
                                : $"{jobTitle} 진행 중... (경과: {elapsed:mm\\:ss})";
                        }
                    });

                    // 60초마다 새 출력이 없으면 안심 메시지 출력
                    int elapsedMinutes = (int)elapsed.TotalMinutes;
                    if (elapsedMinutes > reminderMinute && (DateTime.Now - lastOutputTime).TotalSeconds >= 45)
                    {
                        reminderMinute = elapsedMinutes;
                        string hint = isAnalyze
                            ? reminderMinute switch
                            {
                                1 => "⏳ [작업 진행 중] Windows 모듈 작업자(TiWorker)가 WinSxS 저장소 구성 요소를 정밀 검사하고 있습니다.",
                                2 => "⏳ [작업 진행 중] 패키지 종속성을 확인하고 실제 점유 용량을 정밀 계산 중입니다.",
                                3 => "⏳ [작업 진행 중] 시스템 저장소 상세 분석 및 정리 권장 여부를 집계 중입니다. 잠시만 기다려 주세요.",
                                _ => "⏳ [작업 진행 중] WinSxS 저장소 상세 분석 작업이 계속 진행 중입니다. 잠시만 더 기다려 주세요."
                            }
                            : reminderMinute switch
                            {
                                1 => "⏳ [작업 진행 중] Windows 모듈 작업자(TiWorker)가 백그라운드에서 파일 정리를 정상 수행하고 있습니다.",
                                2 => "⏳ [작업 진행 중] WinSxS 패키지 종속성 검사 및 정리를 계속 진행하고 있습니다.",
                                3 => "⏳ [작업 진행 중] 수많은 백업 패키지를 압축하고 안전하게 제거 중입니다. 최대 10~20분 소요될 수 있습니다.",
                                _ => "⏳ [작업 진행 중] 대용량 구성 요소 정리가 안전하게 진행 중입니다. 잠시만 더 기다려 주세요."
                            };
                        AppendSxsLog($"{hint} (경과 시간: {elapsed:mm\\:ss})", Brushes.DeepSkyBlue);
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
                if (m.Success && double.TryParse(m.Groups["num"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                {
                    lastPercentage = pct;
                    lastOutputTime = DateTime.Now;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (PbSxs.IsIndeterminate)
                            PbSxs.IsIndeterminate = false;
                        PbSxs.Value = pct;
                        var elapsed = DateTime.Now - startTime;
                        TxtSxsProgressStatus.Text = $"{jobTitle}: {pct:F1}% 진행 중... (경과: {elapsed:mm\\:ss})";
                    });
                }

                if (!(trimmed.StartsWith('[') && trimmed.Contains('%')) &&
                    !trimmed.StartsWith("배포 이미지") &&
                    !trimmed.StartsWith("이미지 버전"))
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
                await Task.Run(async () =>
                {
                    try
                    {
                        using var proc = new Process();
                        _activeDismProcess = proc;
                        proc.StartInfo.FileName = "dism.exe";
                        proc.StartInfo.Arguments = arguments;
                        proc.StartInfo.UseShellExecute = false;
                        proc.StartInfo.RedirectStandardOutput = true;
                        proc.StartInfo.RedirectStandardError = true;
                        proc.StartInfo.CreateNoWindow = true;
                        proc.StartInfo.StandardOutputEncoding = Encoding.GetEncoding("euc-kr");
                        proc.StartInfo.StandardErrorEncoding = Encoding.GetEncoding("euc-kr");

                        proc.Start();

                        var stdoutTask = Task.Run(async () =>
                        {
                            char[] buffer = new char[512];
                            var sb = new StringBuilder();
                            var reader = proc.StandardOutput;
                            while (!cts.Token.IsCancellationRequested)
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
                                            ProcessOutputLine(line);
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
                                ProcessOutputLine(sb.ToString());
                            }
                        });

                        var stderrTask = Task.Run(async () =>
                        {
                            char[] buffer = new char[512];
                            var sb = new StringBuilder();
                            var reader = proc.StandardError;
                            while (!cts.Token.IsCancellationRequested)
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
                                            string errLine = sb.ToString();
                                            sb.Clear();
                                            ProcessErrorLine(errLine);
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
                                ProcessErrorLine(sb.ToString());
                            }
                        });

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
                        AppendSxsLog($"❌ DISM 실행 오류: {ex.Message}", Brushes.Tomato);
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
                AppendSxsLog($"✅ [{jobTitle}] DISM이 정상 종료되었습니다. (종료 코드 0)", Brushes.SpringGreen);
            }
            else if (exitCode == 3010)
            {
                AppendSxsLog($"🔄 [{jobTitle}] 작업은 끝났지만 재부팅이 필요합니다. (종료 코드 3010) PC를 재시작하면 정리가 마무리됩니다.", Brushes.Gold);
            }
            else if (exitCode == -1)
            {
                AppendSxsLog($"❌ [{jobTitle}] DISM이 정상적으로 끝나지 않았습니다. (실행 오류 또는 사용자 중지)", Brushes.Tomato);
            }
            else
            {
                AppendSxsLog($"❌ [{jobTitle}] DISM 작업이 실패했습니다. (종료 코드 {exitCode} / 0x{exitCode:X8}) 위의 오류 메시지를 확인하세요. 재부팅 후 다시 시도하면 해결되는 경우가 많습니다.", Brushes.Tomato);
            }

            PbSxs.IsIndeterminate = false;
            PbSxs.Value = (exitCode == 0 ? 100 : 0);
            TxtSxsProgressStatus.Text = "대기 중";
            SetSxsButtonsState(idle: true);
            return exitCode;
        }

        // DISM 분석 결과를 화면 카드에 그대로 반영하고, "실제 크기"를 MB 단위로 반환합니다. (찾지 못하면 null)
        private double? ParseDismOutput(string fullOutput)
        {
            var sizeRegex = DismSizeRegex();
            string[] lines = fullOutput.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);
            double? actualMb = null;

            foreach (string line in lines)
            {
                if (line.Contains("구성 요소 저장소 정리 권장"))
                {
                    // DISM이 알려준 권장 여부를 그대로 표시 (임의 고정 표시 제거)
                    bool recommended = line.Contains('예') || line.Contains("Yes", StringComparison.OrdinalIgnoreCase);

                    if (!recommended)
                    {
                        TxtSxsStatusCard.Text = "✅ 아주 깨끗한 상태";
                        TxtSxsStatusCard.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xA3));
                    }
                    else
                    {
                        TxtSxsStatusCard.Text = "📢 정리 권장됨";
                        TxtSxsStatusCard.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D));
                    }
                    continue;
                }

                var m = sizeRegex.Match(line);
                if (!m.Success) continue;

                string size = m.Groups["size"].Value.Trim();
                string unit = m.Groups["unit"].Value.Trim();

                if (line.Contains("실제 크기"))
                {
                    TxtSxsActual.Text = $"{size} {unit}";
                    actualMb = ToMegabytes(size, unit);
                }
                else if (line.Contains("Windows와 공유됨"))
                    TxtSxsShared.Text = $"{size} {unit}";
                else if (line.Contains("백업 및 기능 사용 안 함"))
                    TxtSxsExpected.Text = $"{size} {unit}"; // DISM이 알려준 실제 값을 항상 표시
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
        private async Task<double?> AnalyzeSxsAsync()
        {
            int code = await RunDismAsync("/Online /Cleanup-Image /AnalyzeComponentStore", "WinSxS 저장소 분석");
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
            double? afterMb = await AnalyzeSxsAsync();

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

        private void BtnSxsCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_activeDismProcess != null && !_activeDismProcess.HasExited)
                {
                    _activeDismProcess.Kill(entireProcessTree: true);
                    AppendSxsLog("🛑 사용자에 의해 DISM 작업이 중지되었습니다.", Brushes.OrangeRed);
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
                string content = _dismLogBuffer.Length > 0
                    ? _dismLogBuffer.ToString()
                    : new TextRange(RtbSxsLog.Document.ContentStart, RtbSxsLog.Document.ContentEnd).Text;
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
            _dashboardTimer?.Stop();
            _monitorService.Dispose();
            try
            {
                if (_activeDismProcess != null && !_activeDismProcess.HasExited)
                    _activeDismProcess.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }
}
