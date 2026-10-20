using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SystemCareSuite.Models
{
    public class DriveStatusItem
    {
        public string DriveName { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public double TotalSizeGb { get; set; }
        public double FreeSizeGb { get; set; }
        public double UsedSizeGb => Math.Max(0, TotalSizeGb - FreeSizeGb);
        public double UsagePercent => TotalSizeGb > 0 ? (UsedSizeGb / TotalSizeGb) * 100.0 : 0;
        public string HeaderText => string.IsNullOrWhiteSpace(VolumeLabel) ? $"{DriveName}" : $"{DriveName} ({VolumeLabel})";
        public string UsageText => $"{UsedSizeGb:F1} GB / {TotalSizeGb:F1} GB (여유 {FreeSizeGb:F1} GB)";
        public string PercentText => $"{UsagePercent:F1}%";
        public Brush BarBrush => UsagePercent >= 90
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x6D))
            : UsagePercent >= 75
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00))
                : new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));
    }

    public class ProcessMemoryItem
    {
        public int Rank { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public double MemoryMb { get; set; }
        public string MemoryText => MemoryMb >= 1024 ? $"{MemoryMb / 1024.0:F2} GB" : $"{MemoryMb:F0} MB";
    }

    public class CleanerTargetItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private long _estimatedBytes;
        private string _statusText = "대기 중";

        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public long EstimatedBytes
        {
            get => _estimatedBytes;
            set
            {
                _estimatedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstimatedSizeText));
            }
        }

        public string EstimatedSizeText => _estimatedBytes > 0 ? FormatBytes(_estimatedBytes) : "-";

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024;
                idx++;
            }
            return $"{val:0.##} {units[idx]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RegistryItem : INotifyPropertyChanged
    {
        private bool _isChecked = true;

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;

        public Brush BadgeBackground => Category switch
        {
            "시작 프로그램" => new SolidColorBrush(Color.FromArgb(45, 0, 240, 255)),
            "소프트웨어 잔상" => new SolidColorBrush(Color.FromArgb(45, 0, 229, 163)),
            "MUI 캐시" => new SolidColorBrush(Color.FromArgb(45, 255, 184, 0)),
            "확장자 연결" => new SolidColorBrush(Color.FromArgb(45, 189, 147, 249)),
            "정상 시작앱" => new SolidColorBrush(Color.FromArgb(45, 88, 166, 255)),
            _ => new SolidColorBrush(Color.FromArgb(45, 139, 148, 158))
        };

        public Brush BadgeForeground => Category switch
        {
            "시작 프로그램" => new SolidColorBrush(Color.FromRgb(0, 240, 255)),
            "소프트웨어 잔상" => new SolidColorBrush(Color.FromRgb(0, 229, 163)),
            "MUI 캐시" => new SolidColorBrush(Color.FromRgb(255, 184, 0)),
            "확장자 연결" => new SolidColorBrush(Color.FromRgb(189, 147, 249)),
            "정상 시작앱" => new SolidColorBrush(Color.FromRgb(88, 166, 255)),
            _ => new SolidColorBrush(Color.FromRgb(201, 209, 217))
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
