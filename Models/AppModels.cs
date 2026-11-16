using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SystemCareSuite.Models
{
    /// <summary>
    /// UI 및 모델 바인딩에서 반복 생성되는 브러시를 Freeze하여 재사용합니다.
    /// </summary>
    public static class AppBrushes
    {
        public static SolidColorBrush NeonCyan { get; } = CreateFrozen(0xFF, 0x00, 0xF0, 0xFF);
        public static SolidColorBrush NeonMint { get; } = CreateFrozen(0xFF, 0x00, 0xE5, 0xA3);
        public static SolidColorBrush NeonAmber { get; } = CreateFrozen(0xFF, 0xFF, 0xB8, 0x00);
        public static SolidColorBrush NeonCrimson { get; } = CreateFrozen(0xFF, 0xFF, 0x4D, 0x6D);
        public static SolidColorBrush NeonPurple { get; } = CreateFrozen(0xFF, 189, 147, 249);
        public static SolidColorBrush NeonBlue { get; } = CreateFrozen(0xFF, 88, 166, 255);
        public static SolidColorBrush TextDefault { get; } = CreateFrozen(0xFF, 201, 209, 217);
        public static SolidColorBrush LogTimestamp { get; } = CreateFrozen(0xFF, 0x6E, 0x76, 0x81);

        public static SolidColorBrush BadgeCyanBg { get; } = CreateFrozen(45, 0, 240, 255);
        public static SolidColorBrush BadgeMintBg { get; } = CreateFrozen(45, 0, 229, 163);
        public static SolidColorBrush BadgeAmberBg { get; } = CreateFrozen(45, 255, 184, 0);
        public static SolidColorBrush BadgePurpleBg { get; } = CreateFrozen(45, 189, 147, 249);
        public static SolidColorBrush BadgeBlueBg { get; } = CreateFrozen(45, 88, 166, 255);
        public static SolidColorBrush BadgeCrimsonBg { get; } = CreateFrozen(45, 255, 77, 109);
        public static SolidColorBrush BadgeGrayBg { get; } = CreateFrozen(45, 139, 148, 158);

        private static SolidColorBrush CreateFrozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }

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
            ? AppBrushes.NeonCrimson
            : UsagePercent >= 75
                ? AppBrushes.NeonAmber
                : AppBrushes.NeonCyan;
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
        private string _category = string.Empty;
        private string _reason = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set
            {
                if (_category == value) return;
                _category = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeBackground));
                OnPropertyChanged(nameof(BadgeForeground));
            }
        }

        public string Name { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;

        public string Reason
        {
            get => _reason;
            set
            {
                if (_reason == value) return;
                _reason = value;
                OnPropertyChanged();
            }
        }

        public Brush BadgeBackground => Category switch
        {
            "시작 프로그램" => AppBrushes.BadgeCyanBg,
            "소프트웨어 잔상" => AppBrushes.BadgeMintBg,
            "MUI 캐시" => AppBrushes.BadgeAmberBg,
            "확장자 연결" => AppBrushes.BadgePurpleBg,
            "정상 시작앱" => AppBrushes.BadgeBlueBg,
            "중지된 시작앱" => AppBrushes.BadgeCrimsonBg,
            _ => AppBrushes.BadgeGrayBg
        };

        public Brush BadgeForeground => Category switch
        {
            "시작 프로그램" => AppBrushes.NeonCyan,
            "소프트웨어 잔상" => AppBrushes.NeonMint,
            "MUI 캐시" => AppBrushes.NeonAmber,
            "확장자 연결" => AppBrushes.NeonPurple,
            "정상 시작앱" => AppBrushes.NeonBlue,
            "중지된 시작앱" => AppBrushes.NeonCrimson,
            _ => AppBrushes.TextDefault
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RamExclusionItem : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private string _processName = string.Empty;
        private string _displayName = string.Empty;

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(); }
        }

        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? _processName : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CleanerFileItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Category { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeText => CleanerTargetItem.FormatBytes(FileSizeBytes);
        public string DirectoryPath => Path.GetDirectoryName(FullPath) ?? string.Empty;

        public Brush CategoryBrush => Category switch
        {
            "사용자 임시" or "시스템 임시" => AppBrushes.BadgeCyanBg,
            "프리패치" => AppBrushes.BadgeMintBg,
            "업데이트 캐시" or "배달 최적화" or "오류·썸네일" => AppBrushes.BadgeAmberBg,
            "디스코드 캐시" or "브라우저 캐시" => AppBrushes.BadgePurpleBg,
            "개발 캐시" or "패키지 캐시" => AppBrushes.BadgeCrimsonBg,
            "휴지통" => AppBrushes.BadgeBlueBg,
            _ => AppBrushes.BadgeGrayBg
        };

        public Brush CategoryForeground => Category switch
        {
            "사용자 임시" or "시스템 임시" => AppBrushes.NeonCyan,
            "프리패치" => AppBrushes.NeonMint,
            "업데이트 캐시" or "배달 최적화" or "오류·썸네일" => AppBrushes.NeonAmber,
            "디스코드 캐시" or "브라우저 캐시" => AppBrushes.NeonPurple,
            "개발 캐시" or "패키지 캐시" => AppBrushes.NeonCrimson,
            "휴지통" => AppBrushes.NeonBlue,
            _ => AppBrushes.TextDefault
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CleanerScanResult
    {
        public List<CleanerFileItem> Files { get; init; } = [];
        public List<string> TruncatedCategories { get; init; } = [];
        public int SkippedRecentCount { get; set; }
    }

    public class AppSettings
    {
        public string DevRootPath { get; set; } = @"F:\Project";
        public Dictionary<string, bool> CleanerTargetStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ProtectRecentCleanerFiles { get; set; } = true;
        public bool AutoRamBoostEnabled { get; set; }
        public string? LastSxsActual { get; set; }
        public string? LastSxsShared { get; set; }
        public string? LastSxsExpected { get; set; }
        public string? LastSxsStatus { get; set; }
        public bool? LastSxsRecommended { get; set; }
        public double? LastSxsActualMb { get; set; }
        public string? LastSxsAnalyzedAt { get; set; }
    }
}
