using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace RenamerDesktop;

public partial class MainWindow : Window
{
    private readonly List<FileEntry> _entries = new();
    private readonly HashSet<string> _addedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenamePlan> _plan = new();
    private bool _initializing = true;
    private string? _defaultExportFolder;

    public MainWindow()
    {
        InitializeComponent();
        _initializing = false;
        RefreshPreview();
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要改名的文件",
            Multiselect = true,
            Filter = "所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _defaultExportFolder = dialog.FileNames.Length > 0
                ? Path.GetDirectoryName(dialog.FileNames[0])
                : null;
            AddPaths(dialog.FileNames);
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择包含文件的文件夹",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _defaultExportFolder = dialog.SelectedPath;
            AddPaths(EnumerateFilesRecursive(dialog.SelectedPath));
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _entries.Clear();
        _addedPaths.Clear();
        _plan.Clear();
        _defaultExportFolder = null;
        RefreshPreview();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    added += AddSinglePath(path, recurseDirectory: true);
                }
                else if (File.Exists(path))
                {
                    added += AddSinglePath(path, recurseDirectory: false);
                }
            }
            catch
            {
                // Skip files that disappear or cannot be read; the preview still shows valid entries.
            }
        }

        RefreshPreview();
    }

    private int AddSinglePath(string path, bool recurseDirectory)
    {
        int added = 0;
        if (recurseDirectory)
        {
            foreach (string file in EnumerateFilesRecursive(path))
            {
                added += AddSingleFile(file);
            }
        }
        else
        {
            added += AddSingleFile(path);
        }

        return added;
    }

    private int AddSingleFile(string path)
    {
        if (!File.Exists(path) || !_addedPaths.Add(path))
        {
            return 0;
        }

        var info = new FileInfo(path);
        var entry = new FileEntry
        {
            CurrentPath = path,
            OriginalName = info.Name,
            DirectoryPath = Path.GetDirectoryName(path) ?? string.Empty,
            LastModified = info.LastWriteTime,
            Order = _entries.Count,
        };
        _entries.Add(entry);
        return 1;
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string root)
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
                    result.Add(file);
                }
            }
            catch
            {
                // Inaccessible subfolders should not stop the whole import.
            }
        }

        return result;
    }

    private void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string token)
        {
            return;
        }

        int caret = FormulaBox.CaretIndex;
        FormulaBox.Text = FormulaBox.Text.Insert(caret, token);
        FormulaBox.CaretIndex = caret + token.Length;
        FormulaBox.Focus();
        RefreshPreview();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string formula)
        {
            return;
        }

        FormulaBox.Text = formula;
        FormulaBox.CaretIndex = FormulaBox.Text.Length;
        RefreshPreview();
    }

    private void ResetRuleButton_Click(object sender, RoutedEventArgs e)
    {
        FormulaBox.Text = "{date}_{num}";
        SelectComboByTag(DateSourceCombo, "modified");
        SelectComboByTag(DateFormatCombo, "yyyy-MM-dd_HH-mm-ss");
        StartNumberBox.Text = "1";
        SelectComboByTag(PadCombo, "2");
        StripCheckBox.IsChecked = false;
        LowerCheckBox.IsChecked = false;
        ChangedOnlyCheckBox.IsChecked = false;
        RefreshPreview();
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

    private static string GetSelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private RuleOptions GetRuleOptions()
    {
        int.TryParse(StartNumberBox.Text, out int start);
        int.TryParse(GetSelectedTag(PadCombo), out int pad);

        return new RuleOptions
        {
            Formula = FormulaBox.Text,
            DateFormat = GetSelectedTag(DateFormatCombo) ?? "yyyy-MM-dd_HH-mm-ss",
            UseModifiedTime = GetSelectedTag(DateSourceCombo) == "modified",
            StartNumber = Math.Max(0, start),
            PadDigits = Math.Max(0, pad),
            StripMarkers = StripCheckBox.IsChecked == true,
            LowercaseName = LowerCheckBox.IsChecked == true,
        };
    }

    private void RuleChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        RefreshPreview();
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        RuleOptions rule = GetRuleOptions();
        BuildPlan(rule);

        int totalCount = _entries.Count;
        int visibleIndex = 0;
        var visible = new List<FileEntry>(_entries.Count);

        foreach (FileEntry entry in _entries)
        {
            RenamePlan plan = _plan[entry.Id];
            entry.TargetName = plan.TargetName;
            entry.StatusText = plan.Changed
                ? entry.Renamed ? "将再次改名" : "将改名"
                : entry.Renamed ? "已改名" : "保持不变";

            if (ChangedOnlyCheckBox.IsChecked != true || plan.Changed)
            {
                visibleIndex++;
                entry.Order = visibleIndex;
                visible.Add(entry);
            }
        }

        PreviewCountText.Text = ChangedOnlyCheckBox.IsChecked == true
            ? visible.Count.ToString()
            : totalCount.ToString();
        SourceSummary.Text = totalCount == 0
            ? "尚未选择文件"
            : ZipModeRadio.IsChecked == true
                ? $"已载入 {totalCount} 个文件，导出后不会修改原文件"
                : $"已载入 {totalCount} 个文件，将直接改本地原文件";

        FileGrid.ItemsSource = null;
        FileGrid.ItemsSource = visible;

        int pending = _plan.Values.Count(plan => plan.Changed);
        bool zipMode = ZipModeRadio.IsChecked == true;
        ApplyButton.Content = zipMode ? "导出 ZIP" : "一键改名";
        OpenFolderYesRadio.Content = zipMode
            ? "导出后打开原文件夹"
            : "改名后打开原文件夹";
        OpenFolderNoRadio.Content = zipMode
            ? "导出后不打开原文件夹"
            : "改名后不打开原文件夹";

        if (totalCount == 0)
        {
            ApplyButton.IsEnabled = false;
            StatusText.Text = "请先添加文件或文件夹";
        }
        else if (zipMode)
        {
            ApplyButton.IsEnabled = true;
            StatusText.Text = pending == 0
                ? $"ZIP 将包含 {totalCount} 个文件，名称保持不变"
                : $"共 {totalCount} 个文件，将按新名称写入 ZIP";
        }
        else
        {
            ApplyButton.IsEnabled = pending > 0;
            StatusText.Text = pending == 0
                ? "没有需要改名的文件"
                : $"共 {totalCount} 个文件，准备改 {pending} 个";
        }
    }

    private void BuildPlan(RuleOptions rule)
    {
        _plan.Clear();
        var ordered = _entries.OrderBy(entry => entry.Order).ToList();
        var originalsByDirectory = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        var usedByDirectory = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (FileEntry entry in _entries)
        {
            string directoryKey = Path.GetDirectoryName(entry.CurrentPath) ?? string.Empty;
            if (!originalsByDirectory.TryGetValue(directoryKey, out var names))
            {
                names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                originalsByDirectory[directoryKey] = names;
            }
            names[entry.CurrentName] = entry.Id;
        }

        int index = 0;
        foreach (FileEntry entry in ordered)
        {
            string directory = Path.GetDirectoryName(entry.CurrentPath) ?? string.Empty;
            if (!usedByDirectory.TryGetValue(directory, out var used))
            {
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedByDirectory[directory] = used;
            }

            string desiredBase = ComposeName(entry, rule, index);
            string candidate = string.IsNullOrEmpty(entry.Extension)
                ? desiredBase
                : desiredBase + entry.Extension;
            string targetName = MakeUniqueTarget(
                entry,
                directory,
                candidate,
                used,
                originalsByDirectory.GetValueOrDefault(directory));

            used.Add(targetName);
            bool changed = !string.Equals(entry.CurrentName, targetName, StringComparison.Ordinal);
            _plan[entry.Id] = new RenamePlan(entry, targetName, changed);
            index++;
        }
    }

    private static string MakeUniqueTarget(
        FileEntry entry,
        string directory,
        string candidate,
        HashSet<string> used,
        Dictionary<string, string>? originals)
    {
        string stem = Path.GetFileNameWithoutExtension(candidate);
        string extension = Path.GetExtension(candidate);
        int suffix = 2;

        while (IsTargetOccupied(entry, directory, candidate, used, originals))
        {
            Match match = Regex.Match(stem, @"^(.*)_(\d+)$");
            if (match.Success)
            {
                stem = $"{match.Groups[1].Value}_{int.Parse(match.Groups[2].Value) + 1}";
            }
            else
            {
                stem = $"{stem}_{suffix++}";
            }
            candidate = string.IsNullOrEmpty(extension) ? stem : stem + extension;
        }

        return candidate;
    }

    private static bool IsTargetOccupied(
        FileEntry entry,
        string directory,
        string targetName,
        HashSet<string> used,
        Dictionary<string, string>? originals)
    {
        if (used.Contains(targetName))
        {
            return true;
        }

        if (originals is not null &&
            originals.TryGetValue(targetName, out string? ownerId) &&
            ownerId != entry.Id)
        {
            return true;
        }

        string targetPath = Path.Combine(directory, targetName);
        bool isSamePhysicalFile = string.Equals(
            targetPath,
            entry.CurrentPath,
            StringComparison.OrdinalIgnoreCase);
        return File.Exists(targetPath) && !isSamePhysicalFile;
    }

    private string ComposeName(FileEntry entry, RuleOptions rule, int index)
    {
        string cleanBase = CleanOriginalName(entry, rule);
        DateTime date = rule.UseModifiedTime ? entry.LastModified : DateTime.Now;
        int sequence = rule.StartNumber + index;
        string paddedNumber = sequence.ToString().PadLeft(rule.PadDigits, '0');
        string formattedDate = date.ToString(rule.DateFormat);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["date"] = formattedDate,
            ["time"] = date.ToString("HH-mm-ss"),
            ["num"] = paddedNumber,
            ["name"] = cleanBase,
            ["YYYY"] = date.Year.ToString("D4"),
            ["M"] = date.Month.ToString(),
            ["MM"] = date.Month.ToString("D2"),
            ["D"] = date.Day.ToString(),
            ["DD"] = date.Day.ToString("D2"),
            ["H"] = date.Hour.ToString(),
            ["HH"] = date.Hour.ToString("D2"),
            ["m"] = date.Minute.ToString(),
            ["mm"] = date.Minute.ToString("D2"),
            ["s"] = date.Second.ToString(),
            ["ss"] = date.Second.ToString("D2"),
        };

        string composed = ReplaceTokens(rule.Formula, tokens);
        string sanitized = SanitizeBaseName(composed);
        return string.IsNullOrWhiteSpace(sanitized) ? cleanBase : sanitized;
    }

    private string CleanOriginalName(FileEntry entry, RuleOptions rule)
    {
        string value = entry.OriginalBase;
        if (rule.StripMarkers)
        {
            value = Regex.Replace(value, @"\s*\(\d+\)\s*$", "");
            value = Regex.Replace(value, @"\s*\[\d+\]\s*$", "");
            value = Regex.Replace(value, @"\s*-\s*副本\s*$", "");
            value = Regex.Replace(value, @"\s*副本\s*$", "");
            value = Regex.Replace(value, @"\s*\(副本\)\s*$", "");
        }

        if (rule.LowercaseName)
        {
            value = value.ToLowerInvariant();
        }

        return value;
    }

    private static string ReplaceTokens(string template, Dictionary<string, string> tokens)
    {
        var output = new StringBuilder(template.Length);
        for (int i = 0; i < template.Length; i++)
        {
            char current = template[i];
            if (current == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    output.Append('{');
                    i++;
                    continue;
                }

                int end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    output.Append(current);
                    continue;
                }

                string key = template.Substring(i + 1, end - i - 1);
                output.Append(tokens.TryGetValue(key, out string? value) ? value : $"{{{key}}}");
                i = end;
                continue;
            }

            if (current == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                output.Append('}');
                i++;
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }

    private static string SanitizeBaseName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '-' : character);
        }

        string result = builder.ToString().Trim().Trim('.', ' ');
        if (result.Length == 0)
        {
            return "file";
        }

        string upper = result.ToUpperInvariant();
        if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
            upper.StartsWith("COM", StringComparison.Ordinal) ||
            upper.StartsWith("LPT", StringComparison.Ordinal))
        {
            return "_" + result;
        }

        return result;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ZipModeRadio.IsChecked == true)
        {
            await ExportZipAsync();
            return;
        }

        var pending = _plan.Values
            .Where(plan => plan.Changed)
            .OrderBy(plan => plan.Entry.Order)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        MessageBoxResult confirmed = MessageBox.Show(
            this,
            $"将直接修改 {pending.Count} 个本地文件的文件名，是否继续？",
            "确认改名",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        var failures = new List<string>();
        var sourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int success = 0;

        await Task.Run(() =>
        {
            foreach (RenamePlan plan in pending)
            {
                FileEntry entry = plan.Entry;
                string destination = Path.Combine(
                    Path.GetDirectoryName(entry.CurrentPath) ?? string.Empty,
                    plan.TargetName);
                try
                {
                    MoveFile(entry.CurrentPath, destination);
                    entry.CurrentPath = destination;
                    entry.Renamed = true;
                    success++;
                    string sourceDirectory = Path.GetDirectoryName(destination) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(sourceDirectory))
                    {
                        sourceDirectories.Add(sourceDirectory);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{entry.CurrentName}: {exception.Message}");
                }
            }
        });

        Mouse.OverrideCursor = null;
        RefreshPreview();

        if (success > 0 && OpenFolderYesRadio.IsChecked == true)
        {
            OpenDirectories(sourceDirectories);
        }

        if (failures.Count == 0)
        {
            StatusText.Text = success == 0
                ? "没有文件改名成功"
                : $"成功改名 {success} 个文件，可用“一键清空”清空列表";
        }
        else
        {
            StatusText.Text = $"成功 {success} 个，失败 {failures.Count} 个保留在列表";
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, failures.Take(8)),
                "部分文件改名失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task ExportZipAsync()
    {
        var files = _entries
            .Where(entry => File.Exists(entry.CurrentPath))
            .OrderBy(entry => entry.Order)
            .ToList();
        if (files.Count == 0)
        {
            return;
        }

        string defaultDirectory = GetDefaultExportDirectory();
        var dialog = new SaveFileDialog
        {
            Title = "选择 ZIP 保存位置",
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = GetDefaultZipName(defaultDirectory),
            InitialDirectory = Directory.Exists(defaultDirectory)
                ? defaultDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string outputPath = dialog.FileName;
        ApplyButton.IsEnabled = false;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        bool exported = false;
        string? errorMessage = null;

        try
        {
            await Task.Run(async () =>
            {
                using var fileStream = new FileStream(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (FileEntry entry in files)
                {
                    string entryName = MakeUniqueZipName(entry.TargetName, usedNames);
                    usedNames.Add(entryName);
                    ZipArchiveEntry zipEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using FileStream source = new FileStream(
                        entry.CurrentPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    await using Stream destination = zipEntry.Open();
                    await source.CopyToAsync(destination);
                }
            });
            exported = true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            RefreshPreview();
        }

        if (exported)
        {
            StatusText.Text = $"ZIP 已导出：{outputPath}";
            if (OpenFolderYesRadio.IsChecked == true)
            {
                OpenDirectories(files.Select(entry => entry.DirectoryPath));
            }
        }
        else if (errorMessage is not null)
        {
            StatusText.Text = "ZIP 导出失败";
            MessageBox.Show(
                this,
                errorMessage,
                "导出失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetDefaultExportDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_defaultExportFolder) &&
            Directory.Exists(_defaultExportFolder))
        {
            return _defaultExportFolder;
        }

        string? firstDirectory = _entries.FirstOrDefault()?.DirectoryPath;
        return string.IsNullOrWhiteSpace(firstDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : firstDirectory;
    }

    private string GetDefaultZipName(string directory)
    {
        string folderName = string.IsNullOrWhiteSpace(directory)
            ? "文件"
            : Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = "文件";
        }

        return $"{folderName}_改名导出_{DateTime.Now:yyyyMMdd_HHmm}.zip";
    }

    private static string MakeUniqueZipName(string fileName, HashSet<string> usedNames)
    {
        if (usedNames.Add(fileName))
        {
            return fileName;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{stem} ({suffix}){extension}";
            suffix++;
        }
        while (!usedNames.Add(candidate));

        return candidate;
    }

    private static void MoveFile(string source, string destination)
    {
        bool samePath = string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
        bool sameCase = string.Equals(source, destination, StringComparison.Ordinal);

        if (!samePath)
        {
            if (File.Exists(destination))
            {
                throw new InvalidOperationException("目标文件已存在");
            }

            File.Move(source, destination);
            return;
        }

        if (sameCase)
        {
            return;
        }

        string directory = Path.GetDirectoryName(source) ?? string.Empty;
        string tempPath = Path.Combine(
            directory,
            $".renamer-{Guid.NewGuid():N}-{Path.GetFileName(destination)}");
        File.Move(source, tempPath);
        File.Move(tempPath, destination);
    }

    private void OpenDirectories(IEnumerable<string> directories)
    {
        if (OpenFolderYesRadio.IsChecked != true)
        {
            return;
        }

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
                // Opening Explorer is best effort after a successful rename.
            }
        }
    }

    private sealed record RenamePlan(FileEntry Entry, string TargetName, bool Changed);

    private sealed class RuleOptions
    {
        public string Formula { get; init; } = "{date}_{num}";
        public string DateFormat { get; init; } = "yyyy-MM-dd_HH-mm-ss";
        public bool UseModifiedTime { get; init; } = true;
        public int StartNumber { get; init; }
        public int PadDigits { get; init; } = 2;
        public bool StripMarkers { get; init; }
        public bool LowercaseName { get; init; }
    }
}
