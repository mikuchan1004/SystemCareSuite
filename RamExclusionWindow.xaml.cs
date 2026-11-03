using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SystemCareSuite.Models;
using SystemCareSuite.Services;

namespace SystemCareSuite
{
    public partial class RamExclusionWindow : Window
    {
        private readonly ObservableCollection<RamExclusionItem> _exclusions = [];
        private List<string> _allRunningProcesses = [];

        public RamExclusionWindow()
        {
            InitializeComponent();
            Loaded += RamExclusionWindow_Loaded;
        }

        private void RamExclusionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var loaded = RamExclusionService.LoadExclusions();
            _exclusions.Clear();
            foreach (var item in loaded)
            {
                _exclusions.Add(item);
            }
            IcExcludedItems.ItemsSource = _exclusions;

            RefreshRunningProcesses();
            UpdateSummary();
        }

        private void RefreshRunningProcesses()
        {
            _allRunningProcesses = RamExclusionService.GetRunningProcessNames();
            ApplyRunningFilter();
        }

        private void ApplyRunningFilter()
        {
            string query = TxtSearchRunning?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                LbRunningProcesses.ItemsSource = _allRunningProcesses;
            }
            else
            {
                LbRunningProcesses.ItemsSource = _allRunningProcesses
                    .Where(p => p.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void TxtSearchRunning_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyRunningFilter();
        }

        private void BtnRefreshRunning_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningProcesses();
        }

        private void LbRunningProcesses_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LbRunningProcesses.SelectedItem is string procName)
            {
                AddExclusion(procName, procName);
            }
        }

        private void BtnAddCustom_Click(object sender, RoutedEventArgs e)
        {
            string rawName = TxtCustomProcessName.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                MessageBox.Show("추가할 프로세스 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cleanName = rawName;
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = Path.GetFileNameWithoutExtension(cleanName);
            }

            string dispName = TxtCustomDisplayName.Text.Trim();
            if (string.IsNullOrWhiteSpace(dispName))
            {
                dispName = cleanName;
            }

            AddExclusion(cleanName, dispName);
            TxtCustomProcessName.Text = string.Empty;
            TxtCustomDisplayName.Text = string.Empty;
        }

        private void AddExclusion(string procName, string dispName)
        {
            if (_exclusions.Any(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"'{procName}'은(는) 이미 제외 목록에 등록되어 있습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _exclusions.Add(new RamExclusionItem
            {
                ProcessName = procName,
                DisplayName = dispName,
                IsEnabled = true
            });

            UpdateSummary();
        }

        private void BtnRemoveExclusion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RamExclusionItem item)
            {
                _exclusions.Remove(item);
                UpdateSummary();
            }
        }

        private void BtnEnableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _exclusions) item.IsEnabled = true;
        }

        private void BtnDisableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _exclusions) item.IsEnabled = false;
        }

        private void UpdateSummary()
        {
            int total = _exclusions.Count;
            int enabled = _exclusions.Count(x => x.IsEnabled);
            TxtExclusionSummary.Text = $"등록된 제외 프로그램: 총 {total}개 (활성 {enabled}개)";
        }

        private void BtnSaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            RamExclusionService.SaveExclusions(_exclusions);
            DialogResult = true;
            Close();
        }
    }
}
