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

        // Module 3: Registry
        private List<RegistryItem> _registryMasterList = [];

        // Module 4: WinSxS DISM
        private Process? _activeDismProcess;
        private string _lastDismMode = string.Empty;
        private readonly StringBuilder _dismLogBuffer = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeModules();
            Loaded += MainWindow_Loaded;
        }

        private void InitializeModules()
        {
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

        private async void BtnRamBoost_Click(object sender, RoutedEventArgs e)
        {
            BtnQuickRamBoost.IsEnabled = false;
            BtnQuickRamBoost.Content = "⚡ 메모리 최적화 중...";

            var (freedMb, count) = await Task.Run(() => _monitorService.OptimizeRam());

            UpdateDashboardMetrics();
            RefreshTopProcesses();

            TxtRamBoostResult.Text = $"✅ {count}개 프로세스 정리 (약 {freedMb:F0} MB 확보)";
            BtnQuickRamBoost.Content = "⚡ 원클릭 RAM 메모리 부스트";
            BtnQuickRamBoost.IsEnabled = true;
        }

        // =====================================================================
        // MODULE 2: SYSTEM & DEV CLEANER
        // =====================================================================
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

        private void BtnSelectAllCleaner_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _cleanerTargets) item.IsSelected = true;
        }

        private void BtnDeselectAllCleaner_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _cleanerTargets) item.IsSelected = false;
        }

        private async void BtnScanCleaner_Click(object sender, RoutedEventArgs e)
        {
            BtnScanCleaner.IsEnabled = false;
            BtnRunCleaner.IsEnabled = false;
            PbCleaner.Value = 0;
            TxtCleanerStatus.Text = "사전 용량 스캔 중...";
            TxtCleanerLog.Text = $"[{DateTime.Now:HH:mm:ss}] 선택한 항목의 예상 정리 용량을 계산합니다...\n";

            string devRoot = TxtDevRootPath.Text.Trim();
            var targets = _cleanerTargets.ToList();
            long totalEstimated = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (!t.IsSelected)
                {
                    t.EstimatedBytes = 0;
                    t.StatusText = "선택 제외됨";
                    continue;
                }

                t.StatusText = "스캔 중...";
                long bytes = await Task.Run(() => CleanerService.ScanTargetBytes(t.Id, devRoot));
                t.EstimatedBytes = bytes;
                t.StatusText = bytes > 0 ? "정리 가능" : "깨끗함";
                totalEstimated += bytes;

                PbCleaner.Value = ((i + 1) / (double)targets.Count) * 100.0;
                TxtCleanerLog.Text += $" • [{t.Title}] -> {CleanerTargetItem.FormatBytes(bytes)}\n";
                SvCleanerLog.ScrollToBottom();
            }

            TxtCleanerEstimatedTotal.Text = CleanerTargetItem.FormatBytes(totalEstimated);
            TxtCleanerStatus.Text = "사전 스캔 완료";
            TxtCleanerLog.Text += $"\n✅ 스캔 완료! 예상 확보 가능 총 용량: {CleanerTargetItem.FormatBytes(totalEstimated)}\n";
            SvCleanerLog.ScrollToBottom();

            BtnScanCleaner.IsEnabled = true;
            BtnRunCleaner.IsEnabled = true;
        }

        private async void BtnRunCleaner_Click(object sender, RoutedEventArgs e)
        {
            var selected = _cleanerTargets.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("정리할 항목을 하나 이상 체크해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnScanCleaner.IsEnabled = false;
            BtnRunCleaner.IsEnabled = false;
            PbCleaner.Value = 0;
            TxtCleanerStatus.Text = "정리 진행 중...";
            TxtCleanerLog.Text = $"[{DateTime.Now:HH:mm:ss}] 선택된 {selected.Count}개 카테고리 정리를 시작합니다...\n\n";

            string devRoot = TxtDevRootPath.Text.Trim();
            long totalFreed = 0;

            var progress = new Progress<string>(msg =>
            {
                TxtCleanerLog.Text += $"{msg}\n";
                SvCleanerLog.ScrollToBottom();
            });

            for (int i = 0; i < selected.Count; i++)
            {
                var item = selected[i];
                item.StatusText = "정리 중...";
                long freed = await Task.Run(() => CleanerService.CleanTarget(item.Id, devRoot, progress));
                totalFreed += freed;
                item.EstimatedBytes = 0;
                item.StatusText = $"정리 완료 ({CleanerTargetItem.FormatBytes(freed)})";

                PbCleaner.Value = ((i + 1) / (double)selected.Count) * 100.0;
                TxtCleanerStatus.Text = $"진행률 {PbCleaner.Value:F0}% ({item.Title})";
            }

            string finalSize = CleanerTargetItem.FormatBytes(totalFreed);
            TxtCleanerEstimatedTotal.Text = $"확보됨: {finalSize}";
            TxtCleanerStatus.Text = $"완료 ({finalSize} 확보)";
            TxtCleanerLog.Text += "\n============================================\n";
            TxtCleanerLog.Text += $"✨ 최종 확보 용량: {finalSize}\n";
            TxtCleanerLog.Text += "============================================\n";
            SvCleanerLog.ScrollToBottom();

            RefreshDrives();
            BtnScanCleaner.IsEnabled = true;
            BtnRunCleaner.IsEnabled = true;
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

        private async Task RunDismAsync(string arguments, string jobTitle)
        {
            SetSxsButtonsState(idle: false);
            PbSxs.Value = 0;
            TxtSxsProgressStatus.Text = $"{jobTitle} 준비 중... (0%)";
            AppendSxsLog($"🚀 [{jobTitle}] 작업을 시작합니다. (dism.exe {arguments})", Brushes.DeepSkyBlue);
            _dismLogBuffer.Clear();

            await Task.Run(() =>
            {
                try
                {
                    using var proc = new Process();
                    _activeDismProcess = proc;
                    proc.StartInfo.FileName = "dism.exe";
                    proc.StartInfo.Arguments = arguments;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.StandardOutputEncoding = Encoding.GetEncoding("euc-kr");

                    proc.OutputDataReceived += (_, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        lock (_dismLogBuffer)
                        {
                            _dismLogBuffer.AppendLine(e.Data);
                        }

                        var m = DismPercentRegex().Match(e.Data);
                        if (m.Success && double.TryParse(m.Groups["num"].Value, out double pct))
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                PbSxs.Value = pct;
                                TxtSxsProgressStatus.Text = $"{jobTitle}: {pct:F1}% 진행 중...";
                            });
                        }

                        string trimmed = e.Data.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed) &&
                            !(trimmed.StartsWith('[') && trimmed.Contains('%')) &&
                            !trimmed.StartsWith("배포 이미지") &&
                            !trimmed.StartsWith("이미지 버전"))
                        {
                            AppendSxsLog(trimmed);
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.WaitForExit();
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

            PbSxs.Value = 100;
            TxtSxsProgressStatus.Text = "대기 중";
            SetSxsButtonsState(idle: true);
        }

        private void ParseDismOutput(string fullOutput)
        {
            var sizeRegex = DismSizeRegex();
            string[] lines = fullOutput.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                if (line.Contains("구성 요소 저장소 정리 권장"))
                {
                    bool recommended = line.Contains('예') || line.Contains("Yes", StringComparison.OrdinalIgnoreCase);
                    bool isTiny = TxtSxsExpected.Text.Contains("0.00") || TxtSxsExpected.Text.StartsWith("0 ");

                    if (_lastDismMode == "ResetBase" || !recommended || isTiny)
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
                    TxtSxsActual.Text = $"{size} {unit}";
                else if (line.Contains("Windows와 공유됨"))
                    TxtSxsShared.Text = $"{size} {unit}";
                else if (line.Contains("백업 및 기능 사용 안 함"))
                    TxtSxsExpected.Text = string.IsNullOrEmpty(_lastDismMode) ? $"{size} {unit}" : "0 MB";
            }
        }

        private async void BtnSxsAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await RunDismAsync("/Online /Cleanup-Image /AnalyzeComponentStore", "WinSxS 저장소 분석");
            ParseDismOutput(_dismLogBuffer.ToString());
            AppendSxsLog("✅ 구성 요소 저장소 분석이 완료되었습니다.", Brushes.SpringGreen);
            _lastDismMode = string.Empty;
        }

        private async void BtnSxsCleanup_Click(object sender, RoutedEventArgs e)
        {
            _lastDismMode = "Cleanup";
            await RunDismAsync("/Online /Cleanup-Image /StartComponentCleanup", "WinSxS 일반 정리");
            AppendSxsLog("✨ 일반 정리가 완료되었습니다. 최신 상태를 다시 분석합니다...", Brushes.Gold);
            BtnSxsAnalyze_Click(sender, e);
        }

        private async void BtnSxsResetBase_Click(object sender, RoutedEventArgs e)
        {
            string warn = "심층 정리(/ResetBase)를 수행하면 이전에 설치된 Windows 업데이트를 제거(롤백)할 수 없게 됩니다.\n가장 많은 용량을 확보할 수 있습니다. 진행하시겠습니까?";
            if (MessageBox.Show(warn, "⚠️ 심층 정리 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _lastDismMode = "ResetBase";
            await RunDismAsync("/Online /Cleanup-Image /StartComponentCleanup /ResetBase", "WinSxS 심층 정리 (ResetBase)");
            AppendSxsLog("🔥 심층 정리(ResetBase)가 완료되었습니다. 최신 상태를 다시 분석합니다...", Brushes.Gold);
            BtnSxsAnalyze_Click(sender, e);
        }

        private void BtnSxsCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_activeDismProcess != null && !_activeDismProcess.HasExited)
                {
                    _activeDismProcess.Kill(entireProcessTree: true);
                    AppendSxsLog("🛑 사용자에 의해 DISM 작업이 중지되었습니다.", Brushes.OrangeRed);
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
