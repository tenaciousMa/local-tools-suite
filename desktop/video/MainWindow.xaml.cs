using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace VideoGridDesktop;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v",
            ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".rmvb",
            ".vob", ".ogv",
        };

    private static readonly (int Frames, int Columns)[] FrameLayouts =
    {
        (2, 2), (4, 2), (5, 5), (6, 3), (8, 4), (9, 3), (12, 4), (16, 4),
        (20, 5), (24, 6),
    };

    private readonly List<VideoEntry> _entries = new();
    private readonly HashSet<string> _addedPaths = new(StringComparer.OrdinalIgnoreCase);

    private bool _initializing = true;
    private bool _busy;
    private bool _suppressSelection;
    private bool _customFrameMode;
    private int _previewVersion;
    private int _selectedFrameCount = 9;
    private int _selectedColumns = 3;
    private string? _selectedEntryId;
    private string? _exportFolder;
    private bool _folderPickedByUser;
    private CancellationTokenSource? _probeCancellation;
    private CancellationTokenSource? _activeWorkCancellation;

    public MainWindow()
    {
        InitializeComponent();
        PopulateFramePresets();
        _initializing = false;
        UpdateExportModeLabels();
        RefreshPlan();
        UpdateSourceSummary();
    }

    private void PopulateFramePresets()
    {
        Style? buttonStyle = FindResource("GridToggleButton") as Style;
        foreach ((int frames, int columns) in FrameLayouts)
        {
            int rows = (int)Math.Ceiling(frames / (double)columns);
            var button = new ToggleButton
            {
                Content = $"{frames} 张",
                Tag = frames,
                IsChecked = frames == _selectedFrameCount,
                Style = buttonStyle,
                ToolTip = $"{columns} 列 x {rows} 行，按时长等分 {frames} 段",
            };
            button.Click += FramePreset_Click;
            FramePresetPanel.Children.Add(button);
        }

        var customButton = new ToggleButton
        {
            Content = "自定义",
            Tag = "custom",
            IsChecked = false,
            Style = buttonStyle,
            ToolTip = "输入任意每页张数",
        };
        customButton.Click += FramePreset_Click;
        FramePresetPanel.Children.Add(customButton);

        var manualButton = new ToggleButton
        {
            Content = "播放器选帧",
            Tag = "manual",
            IsChecked = false,
            Style = buttonStyle,
            ToolTip = "打开播放器，手动添加截图时间点",
        };
        manualButton.Click += FramePreset_Click;
        FramePresetPanel.Children.Add(manualButton);

        UpdateLayoutHint();
    }

    private void FramePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked)
        {
            return;
        }

        if (clicked.Tag is string tag && tag == "custom")
        {
            _customFrameMode = true;
            foreach (ToggleButton button in FramePresetPanel.Children.OfType<ToggleButton>())
            {
                button.IsChecked = button == clicked;
            }

            CustomCountPanel.Visibility = Visibility.Visible;
            LayoutHint.Text = "输入自定义张数后点击“应用”";
            CustomCountBox.Focus();
            CustomCountBox.SelectAll();
            return;
        }

        if (clicked.Tag is string manualTag && manualTag == "manual")
        {
            OpenManualFramePicker(clicked);
            return;
        }

        if (clicked.Tag is not int frames)
        {
            return;
        }

        foreach (ToggleButton button in FramePresetPanel.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button == clicked;
        }

        CustomCountPanel.Visibility = Visibility.Collapsed;
        _customFrameMode = false;
        _selectedFrameCount = frames;
        (int _, int columns) = FrameLayouts.First(layout => layout.Frames == frames);
        _selectedColumns = columns;
        ClearSelectedManualCapture();
        UpdateLayoutHint();
        RefreshPlan();
    }

    private void UpdateLayoutHint()
    {
        int rows = (int)Math.Ceiling(_selectedFrameCount / (double)_selectedColumns);
        LayoutHint.Text =
            $"{_selectedColumns} 列 x {rows} 行，视频按时长等分为 " +
            $"{_selectedFrameCount} 段，每格取该段中间画面";
    }

    private void ApplyCustomCount_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CustomCountBox.Text.Trim(), out int count) ||
            count < 1 ||
            count > 120)
        {
            StatusText.Text = "张数请输入 1 到 120 之间的整数";
            return;
        }

        foreach (ToggleButton button in FramePresetPanel.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button.Tag is string custom && custom == "custom";
        }

        CustomCountPanel.Visibility = Visibility.Visible;
        _customFrameMode = true;
        _selectedFrameCount = count;
        _selectedColumns = GuessColumnsForFrameCount(count);
        ClearSelectedManualCapture();
        UpdateLayoutHint();
        RefreshPlan();
    }

    private static int GuessColumnsForFrameCount(int frames)
    {
        if (frames <= 1) return 1;
        if (frames <= 2) return 2;
        if (frames <= 4) return 2;
        if (frames == 5) return 5;
        if (frames <= 6) return 3;
        if (frames <= 9) return 3;
        if (frames <= 12) return 4;
        if (frames <= 16) return 4;
        if (frames <= 20) return 5;
        if (frames <= 24) return 6;
        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(frames)), 1, 8);
    }

    private async void AddVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要截图的视频",
            Multiselect = true,
            Filter =
                "视频文件 (*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v;*.flv;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp)|" +
                "*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v;*.flv;*.mpg;*.mpeg;*.ts;*.m2ts;*.3gp|" +
                "所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择包含视频的文件夹",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            await AddPathsAsync(EnumerateVideoFiles(dialog.SelectedPath));
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        await AddPathsAsync(paths);
    }

    private IEnumerable<string> EnumerateVideoFiles(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    pending.Push(directory);
                }

                foreach (string file in Directory.EnumerateFiles(current))
                {
                    if (VideoExtensions.Contains(Path.GetExtension(file)))
                    {
                        result.Add(file);
                    }
                }
            }
            catch
            {
                // Unreadable folders should not stop the rest of the import.
            }
        }

        return result;
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        _probeCancellation?.Cancel();
        _probeCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _probeCancellation.Token;

        var newFiles = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    newFiles.AddRange(EnumerateVideoFiles(path));
                }
                else if (File.Exists(path) &&
                         (VideoExtensions.Contains(Path.GetExtension(path)) ||
                          Path.GetExtension(path).Length == 0))
                {
                    newFiles.Add(path);
                }
            }
            catch
            {
                // Skip files that disappear while opening.
            }
        }

        var added = new List<VideoEntry>();
        foreach (string file in newFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file) || !_addedPaths.Add(file))
            {
                continue;
            }

            var info = new FileInfo(file);
            var entry = new VideoEntry
            {
                FilePath = file,
                OriginalName = info.Name,
                DirectoryPath = Path.GetDirectoryName(file) ?? string.Empty,
                LastModified = info.LastWriteTime,
                Order = _entries.Count + added.Count,
            };
            _entries.Add(entry);
            added.Add(entry);
        }

        if (added.Count == 0)
        {
            if (paths.Any())
            {
                StatusText.Text = "没有发现可处理的视频文件";
            }

            return;
        }

        if (!_folderPickedByUser && string.IsNullOrWhiteSpace(_exportFolder))
        {
            _exportFolder = added[0].DirectoryPath;
            OutputFolderText.Text = _exportFolder;
        }

        UpdateSourceSummary();
        RefreshPlan();

        foreach (VideoEntry entry in added)
        {
            if (!_entries.Contains(entry))
            {
                break;
            }

            entry.StatusText = "正在读取时长...";
            RefreshPlan();
            try
            {
                VideoProbeResult result = await FfmpegRunner.ProbeVideoAsync(
                    entry.FilePath,
                    cancellationToken);
                if (cancellationToken.IsCancellationRequested || !_entries.Contains(entry))
                {
                    break;
                }

                entry.DurationSeconds = result.DurationSeconds;
                entry.VideoWidth = result.Width;
                entry.VideoHeight = result.Height;
                entry.VideoStreamIndex = result.VideoStreamIndex;
                entry.FrameRate = result.FrameRate;
                entry.ProbeSucceeded = true;
                entry.StatusText = "待导出";
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_entries.Contains(entry))
                {
                    entry.ProbeFailed = true;
                    entry.StatusText = "无法读取";
                }
            }
        }

        RefreshPlan();
        UpdateSourceSummary();
        int ready = _entries.Count(entry => entry.ProbeSucceeded);
        StatusText.Text = ready > 0
            ? $"已载入 {_entries.Count} 个视频，其中 {ready} 个可处理"
            : "已载入视频，但没有读取到可用画面信息";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _probeCancellation?.Cancel();
        ClearItems();
    }

    private void ClearItems()
    {
        _entries.Clear();
        _addedPaths.Clear();
        _selectedEntryId = null;
        _exportFolder = null;
        _folderPickedByUser = false;
        PreviewImage.Source = null;
        PreviewEmptyText.Text = "点击“生成预览”查看拼图效果";
        OutputFolderText.Text = "尚未选择，将保存到第一个视频所在目录";
        UpdateSourceSummary();
        RefreshPlan();
    }

    private void UpdateSourceSummary()
    {
        int ready = _entries.Count(entry => entry.ProbeSucceeded);
        SourceSummary.Text = _entries.Count == 0
            ? "支持把多个视频文件或文件夹拖进窗口"
            : $"已载入 {_entries.Count} 个视频，可处理 {ready} 个，可拖入更多文件继续添加";
    }

    private NameRule ReadNameRule()
    {
        int.TryParse(StartNumberBox.Text, out int start);
        int.TryParse(GetSelectedTag(PadCombo), out int pad);
        return new NameRule
        {
            Formula = string.IsNullOrWhiteSpace(FormulaBox.Text)
                ? "{name}_截图"
                : FormulaBox.Text,
            DateFormat = GetSelectedTag(DateFormatCombo) ?? "yyyy-MM-dd_HH-mm-ss",
            UseModifiedTime = GetSelectedTag(DateSourceCombo) != "now",
            StartNumber = Math.Max(0, start),
            PadDigits = Math.Max(0, pad),
            StripMarkers = StripCheckBox.IsChecked == true,
            LowercaseName = LowerCheckBox.IsChecked == true,
            AutoSuffix = AutoSuffixCheckBox.IsChecked == true,
            OutputExtension = GetSelectedTag(OutputFormatCombo) ?? "png",
        };
    }

    private SheetSettings ReadSheetSettings()
    {
        string aspectTag = GetSelectedTag(AspectCombo) ?? "original";
        SheetAspect aspect = aspectTag switch
        {
            "16x9" => SheetAspect.Ratio16By9,
            "4x3" => SheetAspect.Ratio4By3,
            "square" => SheetAspect.Square,
            _ => SheetAspect.Original,
        };

        double.TryParse(GetSelectedTag(BigTitleSizeCombo), out double titleSize);
        double.TryParse(GetSelectedTag(CaptionSizeCombo), out double captionSize);
        int.TryParse(TotalFramesBox.Text.Trim(), out int totalFrames);
        if (totalFrames < 0)
        {
            totalFrames = 0;
        }

        if (totalFrames > 2000)
        {
            totalFrames = 2000;
        }

        OutputSizeKind outputSize = GetSelectedTag(SizePresetCombo) switch
        {
            "1080p" => OutputSizeKind.P1080,
            "2k" => OutputSizeKind.P2K,
            "4k" => OutputSizeKind.P4K,
            "8k" => OutputSizeKind.P8K,
            "16k" => OutputSizeKind.P16K,
            "a4v" => OutputSizeKind.A4Portrait,
            "a4h" => OutputSizeKind.A4Landscape,
            "a3v" => OutputSizeKind.A3Portrait,
            "a3h" => OutputSizeKind.A3Landscape,
            _ => OutputSizeKind.Auto,
        };
        if (titleSize <= 0)
        {
            titleSize = 44;
        }

        if (captionSize <= 0)
        {
            captionSize = 22;
        }

        return new SheetSettings(
            aspect,
            _selectedFrameCount,
            _selectedColumns,
            BigTitleCheckBox.IsChecked == true,
            BigTitleBox.Text,
            titleSize,
            CaptionCheckBox.IsChecked == true,
            CaptionBox.Text,
            GetSelectedTag(TimeFormatCombo) ?? "mm:ss",
            captionSize,
            totalFrames,
            outputSize);
    }

    private ExportMode ReadExportMode()
    {
        return GetSelectedTag(ExportModeCombo) switch
        {
            "merged" => ExportMode.Merged,
            "both" => ExportMode.Both,
            _ => ExportMode.Separate,
        };
    }

    private List<FramePick> BuildVideoPicks(VideoEntry entry, SheetSettings settings)
    {
        return entry.ManualCapture && entry.ManualTimes.Count > 0
            ? ContactSheetRenderer.BuildManualPicks(entry)
            : ContactSheetRenderer.BuildAutoPicks(entry, settings);
    }

    private List<List<FramePick>> BuildPages(List<FramePick> picks, int pageCapacity)
    {
        var pages = new List<List<FramePick>>();
        for (int index = 0; index < picks.Count; index += pageCapacity)
        {
            int count = Math.Min(pageCapacity, picks.Count - index);
            pages.Add(picks.GetRange(index, count));
        }

        return pages;
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _busy)
        {
            return;
        }

        UpdateExportModeLabels();
        RefreshPlan();
        UpdatePreviewButtons();
    }

    private void UpdateExportModeLabels()
    {
        bool pdf = string.Equals(
            GetSelectedTag(OutputFormatCombo),
            "pdf",
            StringComparison.OrdinalIgnoreCase);
        var items = ExportModeCombo.Items.OfType<ComboBoxItem>().ToList();
        if (items.Count < 3)
        {
            return;
        }

        if (pdf)
        {
            items[0].Content = "单个视频分别生成 PDF";
            items[1].Content = "所有视频合并成一个 PDF";
            items[2].Content = "分别生成 PDF + 合并 PDF";
            ExportModeHint.Text = "合并 PDF 会按视频顺序把所有排版页写入一个文件。";
        }
        else
        {
            items[0].Content = "单个视频分别导出";
            items[1].Content = "所有视频合并导出";
            items[2].Content = "分别导出 + 合并都做";
            ExportModeHint.Text = "每个视频生成自己的图片；合并模式会拼成同一份结果。";
        }
    }

    private void RefreshPlan()
    {
        if (_initializing)
        {
            return;
        }

        NameRule rule = ReadNameRule();
        var ordered = _entries.OrderBy(entry => entry.Order).ToList();
        for (int index = 0; index < ordered.Count; index++)
        {
            VideoEntry entry = ordered[index];
            if (entry.ProbeFailed)
            {
                entry.TargetName = "";
            }
            else
            {
                entry.TargetName = NameComposer.BuildTargetName(entry, rule, index);
            }

            if (entry.ExportFailed)
            {
                entry.StatusText = "导出失败";
            }
            else if (entry.Exported)
            {
                entry.StatusText = "已导出";
            }
            else if (entry.ProbeFailed)
            {
                entry.StatusText = "无法读取";
            }
            else if (!entry.ProbeSucceeded)
            {
                entry.StatusText = "等待读取";
            }
            else if (entry.StatusText != "导出中...")
            {
                entry.StatusText = "待导出";
            }
        }

        int selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(_selectedEntryId))
        {
            int found = _entries.FindIndex(entry => entry.Id == _selectedEntryId);
            if (found >= 0)
            {
                selectedIndex = found;
            }
        }

        _suppressSelection = true;
        VideoGrid.ItemsSource = null;
        VideoGrid.ItemsSource = _entries;

        PreviewVideoCombo.Items.Clear();
        foreach (VideoEntry entry in ordered)
        {
            PreviewVideoCombo.Items.Add(entry);
        }

        if (ordered.Count > 0)
        {
            VideoEntry selected = ordered[Math.Min(selectedIndex, ordered.Count - 1)];
            PreviewVideoCombo.SelectedItem = selected;
            VideoGrid.SelectedItem = selected;
            _selectedEntryId = selected.Id;
        }
        else
        {
            PreviewVideoCombo.SelectedItem = null;
            VideoGrid.SelectedItem = null;
            _selectedEntryId = null;
        }
        _suppressSelection = false;

        UpdateFrameModeSelection();
        PreviewCountText.Text = _entries.Count.ToString();
        UpdatePreviewButtons();
        UpdateBottomStatus();
    }

    private void UpdatePreviewButtons()
    {
        var selected = PreviewVideoCombo.SelectedItem as VideoEntry;
        bool canWork = !_busy &&
            _entries.Count > 0 &&
            _entries.Any(entry => entry.ProbeSucceeded);
        PreviewButton.IsEnabled = canWork;
        GeneratePreviewButton.IsEnabled = canWork && selected?.ProbeSucceeded == true;
        ClearManualButton.IsEnabled = canWork && selected?.ProbeSucceeded == true;
        ClearManualButton.Visibility = selected?.ManualCapture == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExportButton.IsEnabled = canWork && !string.IsNullOrWhiteSpace(_exportFolder);
        ClearButton.IsEnabled = _entries.Count > 0 && !_busy;
        AddVideoButton.IsEnabled = !_busy;
        AddFolderButton.IsEnabled = !_busy;
        CancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBottomStatus()
    {
        if (_busy)
        {
            return;
        }

        int ready = _entries.Count(entry => entry.ProbeSucceeded);
        StatusText.Text = ready == 0
            ? _entries.Count == 0
                ? "请先添加视频"
                : "视频信息读取失败，请确认文件仍可播放"
            : $"共 {_entries.Count} 个视频，可导出 {ready} 张拼图大图";
    }

    private void VideoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _initializing || _busy)
        {
            return;
        }

        if (VideoGrid.SelectedItem is VideoEntry entry &&
            PreviewVideoCombo.SelectedItem != entry)
        {
            PreviewVideoCombo.SelectedItem = entry;
            _selectedEntryId = entry.Id;
        }

        UpdatePreviewButtons();
        UpdateFrameModeSelection();
    }

    private void PreviewVideoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _initializing)
        {
            return;
        }

        if (PreviewVideoCombo.SelectedItem is VideoEntry entry)
        {
            _selectedEntryId = entry.Id;
            if (VideoGrid.SelectedItem != entry)
            {
                VideoGrid.SelectedItem = entry;
            }
        }

        UpdatePreviewButtons();
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await GeneratePreviewV2Async();
    }

    private void OpenManualFramePicker(ToggleButton clicked)
    {
        if (_busy || PreviewVideoCombo.SelectedItem is not VideoEntry entry ||
            !entry.ProbeSucceeded)
        {
            StatusText.Text = "请先选择一个已经读取完成的视频";
            UpdateFrameModeSelection();
            return;
        }

        var player = new FramePlayerWindow(entry)
        {
            Owner = this,
        };
        if (player.ShowDialog() == true)
        {
            entry.ManualTimes.Clear();
            entry.ManualTimes.AddRange(player.ResultTimes);
            entry.ManualCapture = true;
            RefreshPlan();
            StatusText.Text = $"“{entry.OriginalName}”已使用 {entry.ManualTimes.Count} 个手动时间点";
        }
        else
        {
            UpdateFrameModeSelection();
        }
    }

    private void ClearSelectedManualCapture()
    {
        if (PreviewVideoCombo.SelectedItem is VideoEntry entry && entry.ManualCapture)
        {
            entry.ManualTimes.Clear();
            entry.ManualCapture = false;
        }
    }

    private void UpdateFrameModeSelection()
    {
        VideoEntry? selected = PreviewVideoCombo.SelectedItem as VideoEntry;
        bool manual = selected?.ManualCapture == true;
        foreach (ToggleButton button in FramePresetPanel.Children.OfType<ToggleButton>())
        {
            if (button.Tag is string tag)
            {
                button.IsChecked = tag == "manual"
                    ? manual
                    : !manual && tag == "custom" && _customFrameMode;
            }
            else if (button.Tag is int frames)
            {
                button.IsChecked = !manual &&
                    !_customFrameMode &&
                    frames == _selectedFrameCount;
            }
        }

        if (manual && selected is not null)
        {
            CustomCountPanel.Visibility = Visibility.Collapsed;
            int pages = Math.Max(
                1,
                (int)Math.Ceiling(selected.ManualTimes.Count / (double)_selectedFrameCount));
            LayoutHint.Text =
                $"播放器选帧：{selected.ManualTimes.Count} 个时间点，" +
                $"每页 {_selectedFrameCount} 格，共 {pages} 页";
        }
        else if (_customFrameMode)
        {
            CustomCountPanel.Visibility = Visibility.Visible;
            UpdateLayoutHint();
        }
        else
        {
            CustomCountPanel.Visibility = Visibility.Collapsed;
            UpdateLayoutHint();
        }
    }

    private void ClearManualButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewVideoCombo.SelectedItem is not VideoEntry entry)
        {
            return;
        }

        entry.ManualTimes.Clear();
        entry.ManualCapture = false;
        RefreshPlan();
        StatusText.Text = $"“{entry.OriginalName}”已恢复为自动等分取帧";
    }

    private async void GeneratePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await GeneratePreviewV2Async();
    }

    private async Task GeneratePreviewAsync()
    {
        if (_busy || PreviewVideoCombo.SelectedItem is not VideoEntry entry ||
            !entry.ProbeSucceeded)
        {
            return;
        }

        if (!FfmpegRunner.IsAvailable)
        {
            MessageBox.Show(
                this,
                "未找到 ffmpeg 文件夹，请保持应用目录完整后再试。",
                "缺少视频处理组件",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _activeWorkCancellation?.Cancel();
        _activeWorkCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _activeWorkCancellation.Token;
        int previewVersion = ++_previewVersion;
        string previewPath = Path.Combine(
            Path.GetTempPath(),
            "VideoGridDesktop",
            $"preview-{Guid.NewGuid():N}.png");

        SetWorking(true);
        PreviewEmptyText.Text = "正在生成预览...";
        PreviewEmptyText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        StatusText.Text = $"正在为“{entry.OriginalName}”生成预览";

        try
        {
            var options = new RenderOptions(
                previewPath,
                ReadSheetSettings(),
                NameComposer.CreatePageTokens(
                    entry,
                    ReadNameRule(),
                    _entries.OrderBy(item => item.Order).ToList().IndexOf(entry)));
            var progress = new Progress<string>(message => StatusText.Text = message);

            RenderResult result = await ContactSheetRenderer.RenderAsync(
                entry,
                options,
                progress,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || previewVersion != _previewVersion)
            {
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(result.OutputPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            StatusText.Text = $"预览完成：{result.Width} x {result.Height} 像素";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "预览已取消";
            PreviewEmptyText.Text = "预览已取消";
        }
        catch (Exception exception)
        {
            StatusText.Text = "预览生成失败";
            PreviewEmptyText.Text = "生成失败";
            MessageBox.Show(
                this,
                exception.Message,
                "生成预览失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetWorking(false);
            try
            {
                if (File.Exists(previewPath))
                {
                    File.Delete(previewPath);
                }
            }
            catch
            {
                // Preview temp files may be locked briefly by the decoder.
            }
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        await ExportAllV2Async();
    }

    private async Task ExportAllAsync()
    {
        if (_busy)
        {
            return;
        }

        List<VideoEntry> pending = _entries
            .Where(entry => entry.ProbeSucceeded)
            .OrderBy(entry => entry.Order)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        string? outputFolder = ResolveExportFolder();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            StatusText.Text = "请先选择导出文件夹";
            return;
        }

        Directory.CreateDirectory(outputFolder);
        NameRule rule = ReadNameRule();
        SheetSettings sheetSettings = ReadSheetSettings();
        _activeWorkCancellation?.Cancel();
        _activeWorkCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _activeWorkCancellation.Token;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        int success = 0;
        bool cancelled = false;
        var sourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        SetWorking(true);
        StatusText.Text = "正在准备导出...";

        try
        {
            for (int index = 0; index < pending.Count; index++)
            {
                VideoEntry entry = pending[index];
                cancellationToken.ThrowIfCancellationRequested();

                entry.ExportFailed = false;
                string candidateName = PickOutputName(
                    outputFolder,
                    entry.TargetName,
                    usedNames,
                    rule.AutoSuffix);
                string outputPath = Path.Combine(outputFolder, candidateName);
                if (!rule.AutoSuffix &&
                    (usedNames.Contains(candidateName) || File.Exists(outputPath)))
                {
                    entry.ExportFailed = true;
                    failed.Add($"{entry.OriginalName}: 输出目录已存在同名文件");
                    RefreshPlan();
                    continue;
                }

                usedNames.Add(candidateName);
                var options = new RenderOptions(
                    outputPath,
                    sheetSettings,
                    NameComposer.CreatePageTokens(entry, rule, index));

                entry.StatusText = "导出中...";
                RefreshPlan();
                int current = index + 1;
                var progress = new Progress<string>(message =>
                {
                    if (!_busy)
                    {
                        return;
                    }

                    StatusText.Text = $"[{current}/{pending.Count}] {message}";
                });

                try
                {
                    RenderResult result = await ContactSheetRenderer.RenderAsync(
                        entry,
                        options,
                        progress,
                        cancellationToken);
                    entry.Exported = true;
                    entry.ExportFailed = false;
                    success++;
                    sourceDirectories.Add(outputFolder);
                    StatusText.Text = $"已导出 {success}/{pending.Count}：{candidateName}";
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception exception)
                {
                    entry.ExportFailed = true;
                    entry.StatusText = "导出失败";
                    failed.Add($"{entry.OriginalName}: {exception.Message}");
                }
            }
        }
        finally
        {
            RefreshPlan();
            SetWorking(false);
        }

        if (success > 0 && OpenFolderCheckBox.IsChecked == true)
        {
            OpenDirectories(sourceDirectories);
        }

        StatusText.Text = cancelled
            ? $"已取消导出：完成 {success} 张"
            : failed.Count == 0
                ? $"导出完成：{success} 张截图已保存到 {outputFolder}"
                : $"完成 {success} 张，失败 {failed.Count} 张";

        if (failed.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, failed.Take(8)),
                "部分视频导出失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task GeneratePreviewV2Async()
    {
        if (_busy || PreviewVideoCombo.SelectedItem is not VideoEntry entry ||
            !entry.ProbeSucceeded)
        {
            return;
        }

        if (!FfmpegRunner.IsAvailable)
        {
            MessageBox.Show(
                this,
                "未找到 ffmpeg 文件夹，请保持应用目录完整后再试。",
                "缺少视频处理组件",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _activeWorkCancellation?.Cancel();
        _activeWorkCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _activeWorkCancellation.Token;
        int previewVersion = ++_previewVersion;
        string previewPath = Path.Combine(
            Path.GetTempPath(),
            "VideoGridDesktop",
            $"preview-{Guid.NewGuid():N}.png");

        SetWorking(true);
        PreviewEmptyText.Text = "正在生成预览...";
        PreviewEmptyText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        StatusText.Text = $"正在为“{entry.OriginalName}”生成预览";

        try
        {
            SheetSettings settings = ReadSheetSettings();
            SheetSettings previewSettings = settings with
            {
                OutputSize = OutputSizeKind.Auto,
            };
            List<FramePick> picks = BuildVideoPicks(entry, previewSettings);
            List<List<FramePick>> pages = BuildPages(picks, previewSettings.FrameCount);
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("请先在播放器里添加至少一个截图时间点。");
            }

            NameRule rule = ReadNameRule();
            int videoIndex = _entries.OrderBy(item => item.Order).ToList().IndexOf(entry);
            var options = new RenderOptions(
                previewPath,
                previewSettings,
                NameComposer.CreatePageTokens(entry, rule, videoIndex, 1, pages.Count));
            var progress = new Progress<string>(message => StatusText.Text = message);

            RenderResult result = await ContactSheetRenderer.RenderPageAsync(
                pages[0],
                options,
                progress,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested || previewVersion != _previewVersion)
            {
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(result.OutputPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            StatusText.Text = pages.Count == 1
                ? $"预览完成：{result.Width} x {result.Height} 像素"
                : $"预览第 1/{pages.Count} 页完成：{result.Width} x {result.Height} 像素";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "预览已取消";
            PreviewEmptyText.Text = "预览已取消";
        }
        catch (Exception exception)
        {
            StatusText.Text = "预览生成失败";
            PreviewEmptyText.Text = "生成失败";
            MessageBox.Show(
                this,
                exception.Message,
                "生成预览失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetWorking(false);
            try
            {
                if (File.Exists(previewPath))
                {
                    File.Delete(previewPath);
                }
            }
            catch
            {
                // Preview temp files may be locked briefly by the decoder.
            }
        }
    }

    private async Task ExportAllV2Async()
    {
        if (_busy)
        {
            return;
        }

        List<VideoEntry> pending = _entries
            .Where(entry => entry.ProbeSucceeded)
            .OrderBy(entry => entry.Order)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        string? outputFolder = ResolveExportFolder();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            StatusText.Text = "请先选择导出文件夹";
            return;
        }

        Directory.CreateDirectory(outputFolder);
        NameRule rule = ReadNameRule();
        SheetSettings sheetSettings = ReadSheetSettings();
        ExportMode mode = ReadExportMode();
        bool pdf = string.Equals(rule.OutputExtension, "pdf", StringComparison.OrdinalIgnoreCase);

        _activeWorkCancellation?.Cancel();
        _activeWorkCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _activeWorkCancellation.Token;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        int successFiles = 0;
        bool cancelled = false;
        int outputIndex = 0;
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoGridDesktop",
            $"export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        SetWorking(true);
        StatusText.Text = "正在准备导出...";

        try
        {
            if (mode is ExportMode.Separate or ExportMode.Both)
            {
                foreach (VideoEntry entry in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entry.ExportFailed = false;
                    entry.StatusText = "导出中...";
                    RefreshPlan();

                    try
                    {
                        List<FramePick> picks = BuildVideoPicks(entry, sheetSettings);
                        List<List<FramePick>> pages = BuildPages(picks, sheetSettings.FrameCount);
                        if (pages.Count == 0)
                        {
                            throw new InvalidOperationException("没有可用的截图时间点。");
                        }

                        if (pdf)
                        {
                            string targetName = PickOutputName(
                                outputFolder,
                                NameComposer.BuildTargetName(entry, rule, outputIndex),
                                usedNames,
                                rule.AutoSuffix);
                            if (!EnsureWritable(outputFolder, targetName, usedNames, rule.AutoSuffix, failed, entry))
                            {
                                entry.ExportFailed = true;
                                continue;
                            }

                            usedNames.Add(targetName);
                            string pdfPath = Path.Combine(outputFolder, targetName);
                            await RenderPdfAsync(
                                pages,
                                sheetSettings,
                                entry,
                                rule,
                                outputIndex,
                                pdfPath,
                                tempRoot,
                                progressText => StatusText.Text = progressText,
                                cancellationToken);
                            entry.Exported = true;
                            successFiles++;
                            outputIndex++;
                            StatusText.Text = $"已导出 {successFiles} 个 PDF：{targetName}";
                        }
                        else
                        {
                            for (int page = 0; page < pages.Count; page++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                string baseName = NameComposer.BuildPageTargetName(
                                    entry,
                                    rule,
                                    outputIndex,
                                    page + 1,
                                    pages.Count);
                                string targetName = PickOutputName(
                                    outputFolder,
                                    baseName,
                                    usedNames,
                                    rule.AutoSuffix);
                                if (!EnsureWritable(outputFolder, targetName, usedNames, rule.AutoSuffix, failed, entry))
                                {
                                    entry.ExportFailed = true;
                                    continue;
                                }

                                usedNames.Add(targetName);
                                string outputPath = Path.Combine(outputFolder, targetName);
                                int videoIndex = pending.IndexOf(entry);
                                var options = new RenderOptions(
                                    outputPath,
                                    sheetSettings,
                                    NameComposer.CreatePageTokens(
                                        entry,
                                        rule,
                                        outputIndex,
                                        page + 1,
                                        pages.Count));
                                await RenderOnePageAsync(
                                    pages[page],
                                    options,
                                    progressText => StatusText.Text =
                                        $"{entry.OriginalName}：{progressText}",
                                    cancellationToken);
                                successFiles++;
                                outputIndex++;
                                StatusText.Text = $"已导出 {successFiles} 张：{targetName}";
                            }

                            if (!entry.ExportFailed)
                            {
                                entry.Exported = true;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception exception)
                    {
                        entry.ExportFailed = true;
                        entry.StatusText = "导出失败";
                        failed.Add($"{entry.OriginalName}: {exception.Message}");
                    }
                }
            }

            if (mode is ExportMode.Merged or ExportMode.Both &&
                !cancelled)
            {
                var allPicks = new List<FramePick>();
                foreach (VideoEntry entry in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    allPicks.AddRange(BuildVideoPicks(entry, sheetSettings));
                }

                List<List<FramePick>> mergedPages = BuildPages(allPicks, sheetSettings.FrameCount);
                if (mergedPages.Count > 0)
                {
                    string outputExtension = rule.OutputExtension;
                    if (pdf)
                    {
                        string targetName = PickOutputName(
                            outputFolder,
                            NameComposer.BuildMergedTargetName(rule, outputIndex, 1, 1),
                            usedNames,
                            rule.AutoSuffix);
                        if (EnsureWritable(outputFolder, targetName, usedNames, rule.AutoSuffix, failed, null))
                        {
                            usedNames.Add(targetName);
                            await RenderPdfAsync(
                                mergedPages,
                                sheetSettings,
                                null,
                                rule,
                                outputIndex,
                                Path.Combine(outputFolder, targetName),
                                tempRoot,
                                text => StatusText.Text = text,
                                cancellationToken);
                            successFiles++;
                            outputIndex++;
                            StatusText.Text = $"已导出合并 PDF：{targetName}";
                        }
                    }
                    else
                    {
                        for (int page = 0; page < mergedPages.Count; page++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string targetName = PickOutputName(
                                outputFolder,
                                NameComposer.BuildMergedTargetName(
                                    rule,
                                    outputIndex,
                                    page + 1,
                                    mergedPages.Count),
                                usedNames,
                                rule.AutoSuffix);
                            if (!EnsureWritable(outputFolder, targetName, usedNames, rule.AutoSuffix, failed, null))
                            {
                                continue;
                            }

                            usedNames.Add(targetName);
                            var options = new RenderOptions(
                                Path.Combine(outputFolder, targetName),
                                sheetSettings,
                                NameComposer.CreateMergedPageTokens(
                                    rule,
                                    outputIndex,
                                    page + 1,
                                    mergedPages.Count));
                            await RenderOnePageAsync(
                                mergedPages[page],
                                options,
                                text => StatusText.Text = text,
                                cancellationToken);
                            successFiles++;
                            outputIndex++;
                            StatusText.Text = $"已导出合并截图 {page + 1}/{mergedPages.Count}";
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Temporary PDF pages are best effort cleanup.
            }

            RefreshPlan();
            SetWorking(false);
        }

        if (successFiles > 0 && OpenFolderCheckBox.IsChecked == true)
        {
            OpenDirectories(new[] { outputFolder });
        }

        StatusText.Text = cancelled
            ? $"已取消导出：完成 {successFiles} 个文件"
            : failed.Count == 0
                ? $"导出完成：{successFiles} 个文件已保存到 {outputFolder}"
                : $"完成 {successFiles} 个文件，失败 {failed.Count} 组";

        if (failed.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, failed.Take(8)),
                "部分视频导出失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task RenderOnePageAsync(
        List<FramePick> picks,
        RenderOptions options,
        Action<string> progressText,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<string>(progressText);
        await ContactSheetRenderer.RenderPageAsync(
            picks,
            options,
            progress,
            cancellationToken);
    }

    private static bool EnsureWritable(
        string outputFolder,
        string targetName,
        HashSet<string> usedNames,
        bool autoSuffix,
        List<string> failed,
        VideoEntry? entry)
    {
        string path = Path.Combine(outputFolder, targetName);
        if (autoSuffix || (!usedNames.Contains(targetName) && !File.Exists(path)))
        {
            return true;
        }

        string label = entry?.OriginalName ?? "合并输出";
        failed.Add($"{label}: 输出目录已存在同名文件 {targetName}");
        return false;
    }

    private async Task RenderPdfAsync(
        List<List<FramePick>> pages,
        SheetSettings settings,
        VideoEntry? entry,
        NameRule rule,
        int outputIndex,
        string outputPath,
        string tempRoot,
        Action<string> progressText,
        CancellationToken cancellationToken)
    {
        var pageImages = new List<PdfPageImage>();
        string fileStem = Path.GetFileNameWithoutExtension(outputPath);
        for (int page = 0; page < pages.Count; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string tempJpeg = Path.Combine(tempRoot, $"{fileStem}-{Guid.NewGuid():N}.jpg");
            var options = new RenderOptions(
                tempJpeg,
                settings,
                entry is null
                    ? NameComposer.CreateMergedPageTokens(rule, outputIndex, page + 1, pages.Count)
                    : NameComposer.CreatePageTokens(entry, rule, outputIndex, page + 1, pages.Count));
            RenderResult result = await ContactSheetRenderer.RenderPageAsync(
                pages[page],
                options,
                new Progress<string>(progressText),
                cancellationToken);
            pageImages.Add(new PdfPageImage(File.ReadAllBytes(tempJpeg), result.Width, result.Height));
            File.Delete(tempJpeg);
            progressText($"PDF 第 {page + 1}/{pages.Count} 页已合成");
        }

        PdfPageWriter.Write(outputPath, pageImages);
    }

    private static string PickOutputName(
        string outputFolder,
        string targetName,
        HashSet<string> usedNames,
        bool autoSuffix)
    {
        string initialPath = Path.Combine(outputFolder, targetName);
        if (!usedNames.Contains(targetName) && !File.Exists(initialPath))
        {
            return targetName;
        }

        if (!autoSuffix)
        {
            return targetName;
        }

        string stem = Path.GetFileNameWithoutExtension(targetName);
        string extension = Path.GetExtension(targetName);
        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{stem}_{suffix}{extension}";
            suffix++;
        }
        while (usedNames.Contains(candidate) ||
               File.Exists(Path.Combine(outputFolder, candidate)));

        return candidate;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activeWorkCancellation?.Cancel();
        _probeCancellation?.Cancel();
        StatusText.Text = "正在取消...";
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择导出文件夹",
            ShowNewFolderButton = true,
            SelectedPath = ResolveExportFolder() ??
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _exportFolder = dialog.SelectedPath;
            _folderPickedByUser = true;
            OutputFolderText.Text = _exportFolder;
            UpdatePreviewButtons();
        }
    }

    private string? ResolveExportFolder()
    {
        if (!string.IsNullOrWhiteSpace(_exportFolder))
        {
            return _exportFolder;
        }

        VideoEntry? first = _entries.FirstOrDefault();
        if (first is not null && Directory.Exists(first.DirectoryPath))
        {
            _exportFolder = first.DirectoryPath;
            OutputFolderText.Text = _exportFolder;
            return _exportFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void SetWorking(bool working)
    {
        _busy = working;
        CancelButton.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        PreviewButton.IsEnabled = !working && _entries.Any(entry => entry.ProbeSucceeded);
        ExportButton.IsEnabled = !working &&
            _entries.Any(entry => entry.ProbeSucceeded) &&
            !string.IsNullOrWhiteSpace(_exportFolder);
        GeneratePreviewButton.IsEnabled = !working &&
            PreviewVideoCombo.SelectedItem is VideoEntry selected &&
            selected.ProbeSucceeded;
        ClearManualButton.IsEnabled = !working &&
            PreviewVideoCombo.SelectedItem is VideoEntry clearSelected &&
            clearSelected.ProbeSucceeded;
        ClearManualButton.Visibility =
            PreviewVideoCombo.SelectedItem is VideoEntry manualEntry &&
            manualEntry.ManualCapture &&
            !working
                ? Visibility.Visible
                : Visibility.Collapsed;
        ClearButton.IsEnabled = !working && _entries.Count > 0;
        AddVideoButton.IsEnabled = !working;
        AddFolderButton.IsEnabled = !working;
    }

    private void ResetRuleButton_Click(object sender, RoutedEventArgs e)
    {
        SetFramePreset(9);
        SelectComboByTag(AspectCombo, "original");
        TotalFramesBox.Text = "0";
        SelectComboByTag(SizePresetCombo, "auto");
        SelectComboByTag(ExportModeCombo, "separate");
        BigTitleCheckBox.IsChecked = true;
        BigTitleBox.Text = "{name}";
        SelectComboByTag(BigTitleSizeCombo, "44");
        CaptionCheckBox.IsChecked = true;
        CaptionBox.Text = "{time}";
        SelectComboByTag(TimeFormatCombo, "mm:ss");
        SelectComboByTag(CaptionSizeCombo, "22");
        FormulaBox.Text = "{name}_截图";
        SelectComboByTag(DateSourceCombo, "modified");
        SelectComboByTag(DateFormatCombo, "yyyy-MM-dd_HH-mm-ss");
        StartNumberBox.Text = "1";
        SelectComboByTag(PadCombo, "2");
        StripCheckBox.IsChecked = false;
        LowerCheckBox.IsChecked = false;
        AutoSuffixCheckBox.IsChecked = true;
        SelectComboByTag(OutputFormatCombo, "png");
        RefreshPlan();
    }

    private void SetFramePreset(int frames)
    {
        if (!FrameLayouts.Any(layout => layout.Frames == frames))
        {
            return;
        }

        foreach (ToggleButton button in FramePresetPanel.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button.Tag is int count && count == frames;
        }

        CustomCountPanel.Visibility = Visibility.Collapsed;
        _customFrameMode = false;
        _selectedFrameCount = frames;
        _selectedColumns = FrameLayouts.First(layout => layout.Frames == frames).Columns;
        UpdateLayoutHint();
    }

    private void InsertToken(TextBox box, string token)
    {
        int caret = box.CaretIndex;
        box.Text = box.Text.Insert(caret, token);
        box.CaretIndex = caret + token.Length;
        box.Focus();
    }

    private void FormulaTokenClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            InsertToken(FormulaBox, token);
        }
    }

    private void FormulaPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string formula })
        {
            FormulaBox.Text = formula;
        }
    }

    private void TitleTokenClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            InsertToken(BigTitleBox, token);
        }
    }

    private void CaptionTokenClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token })
        {
            InsertToken(CaptionBox, token);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            _activeWorkCancellation?.Cancel();
            _probeCancellation?.Cancel();
            StatusText.Text = "正在取消当前任务，请稍后关闭窗口";
            e.Cancel = true;
            return;
        }

        _probeCancellation?.Cancel();
        _activeWorkCancellation?.Cancel();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!control)
        {
            return;
        }

        if (e.Key == Key.O && shift)
        {
            AddFolderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.O)
        {
            AddVideoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P)
        {
            PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E || e.Key == Key.Enter)
        {
            ExportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }

    private static string? GetSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    private static void SelectComboByTag(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }
    }

    private void OpenDirectories(IEnumerable<string> directories)
    {
        foreach (string directory in directories.Where(Directory.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Opening Explorer after export is best effort.
            }
        }
    }
}
