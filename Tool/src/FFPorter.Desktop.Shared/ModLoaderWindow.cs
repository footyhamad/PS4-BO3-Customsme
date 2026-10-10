using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace FFPorter.Desktop;

internal sealed class ModLoaderWindow : Window
{
    private readonly ObservableCollection<ModCandidate> _mods = [];
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly TextBox _details;
    private readonly Action<IEnumerable<string>> _queuePaths;
    private readonly Action<IEnumerable<string>> _queueMods;
    private readonly Action<string> _log;
    private string? _root;

    public ModLoaderWindow(string? gameFolder, Action<IEnumerable<string>> queuePaths, Action<IEnumerable<string>> queueMods, Action<string> log)
    {
        _root = gameFolder;
        _queuePaths = queuePaths;
        _queueMods = queueMods;
        _log = log;

        Title = "PC Mod Loader";
        Width = 1180;
        Height = 720;
        MinWidth = 900;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Mod Loader",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Scan PC BO3 mods and usermaps. Mod packages are converted as one package; map candidates continue through the existing map conversion path.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _status = new TextBlock
        {
            Text = "Choose the PC BO3 folder, or a folder that directly contains mods/usermaps.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        footer.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var chooseMaps = new Button { Content = "Select map candidates", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
        chooseMaps.Click += (_, _) =>
        {
            foreach (ModCandidate candidate in _mods)
                candidate.Selected = candidate.Category.Equals("Map candidate", StringComparison.OrdinalIgnoreCase);
            _grid.Items.Refresh();
        };
        buttons.Children.Add(chooseMaps);

        var clearSelection = new Button { Content = "Clear selection", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
        clearSelection.Click += (_, _) =>
        {
            foreach (ModCandidate candidate in _mods)
                candidate.Selected = false;
            _grid.Items.Refresh();
        };
        buttons.Children.Add(clearSelection);

        var export = new Button { Content = "Export report…", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
        export.Click += (_, _) => ExportReport();
        buttons.Children.Add(export);

        var openFolder = new Button { Content = "Open selected folder", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
        openFolder.Click += (_, _) => OpenSelectedFolder();
        buttons.Children.Add(openFolder);

        var choose = new Button { Content = "Choose PC folder…", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        choose.Click += (_, _) => ChooseRoot();
        buttons.Children.Add(choose);

        var scan = new Button { Content = "Rescan", Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        scan.Click += (_, _) => Scan();
        buttons.Children.Add(scan);

        var queue = new Button { Content = "Queue selected for conversion", Padding = new Thickness(14, 7, 14, 7) };
        queue.Click += (_, _) => QueueSelected();
        buttons.Children.Add(queue);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star), MinWidth = 470 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star), MinWidth = 300 });

        _grid = new DataGrid
        {
            ItemsSource = _mods,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MinHeight = 250
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Queue",
            Binding = new Binding(nameof(ModCandidate.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 58
        });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Package", Binding = new Binding(nameof(ModCandidate.Title)), IsReadOnly = true, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding(nameof(ModCandidate.Category)), IsReadOnly = true, Width = 92 });
        _grid.Columns.Add(new DataGridTextColumn { Header = ".ff", Binding = new Binding(nameof(ModCandidate.Fastfiles)), IsReadOnly = true, Width = 46 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(ModCandidate.Status)), IsReadOnly = true, Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => ShowDetails(_grid.SelectedItem as ModCandidate);
        body.Children.Add(_grid);

        _details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Padding = new Thickness(10),
            Text = "Select a package to inspect its files, workshop metadata and declared dependencies."
        };
        Grid.SetColumn(_details, 2);
        body.Children.Add(_details);

        layout.Children.Add(body);
        Content = layout;
        Loaded += (_, _) => Scan();
    }

    private void ChooseRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the PC BO3 folder or a folder containing mods/usermaps" };
        if (!string.IsNullOrWhiteSpace(_root) && Directory.Exists(_root))
            dialog.InitialDirectory = _root;
        if (dialog.ShowDialog(this) == true)
        {
            _root = dialog.FolderName;
            Scan();
        }
    }

    private void Scan()
    {
        _mods.Clear();
        if (string.IsNullOrWhiteSpace(_root) || !Directory.Exists(_root))
        {
            _status.Text = "PC game folder is not configured. Choose the game folder or a folder containing mods/usermaps.";
            return;
        }

        int skipped = 0;
        int scanned = 0;
        foreach ((string folder, string category) in DiscoverPackageFolders(_root))
        {
            scanned++;
            try
            {
                ModCandidate? candidate = Inspect(folder, category);
                if (candidate is null)
                {
                    skipped++;
                    continue;
                }
                _mods.Add(candidate);
            }
            catch (Exception ex)
            {
                _log($"Mod Loader: inspection failed for '{folder}': {ex.GetType().Name}: {ex.Message}");
                skipped++;
            }
        }

        _status.Text = $"{_mods.Count} package candidate(s) found from {scanned} folder(s); {skipped} had no usable fastfiles or could not be inspected. Status is a file-based assessment, not a PS4 compatibility guarantee.";
        _log($"Mod Loader scan: root='{_root}', scanned={scanned}, candidates={_mods.Count}, skipped={skipped}.");
        if (_mods.Count > 0)
            _grid.SelectedIndex = 0;
    }

    private static IEnumerable<(string Folder, string Category)> DiscoverPackageFolders(string root)
    {
        string full = Path.GetFullPath(root);
        string leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string? directCategory = leaf.Equals("mods", StringComparison.OrdinalIgnoreCase) ? "Mod"
            : leaf.Equals("usermaps", StringComparison.OrdinalIgnoreCase) ? "Map" : null;
        if (directCategory != null)
        {
            foreach (string child in SafeDirectories(full))
                yield return (child, directCategory);
            yield break;
        }

        bool foundContainers = false;
        foreach (string categoryName in new[] { "mods", "usermaps" })
        {
            string container = Path.Combine(full, categoryName);
            if (!Directory.Exists(container))
                continue;
            foundContainers = true;
            foreach (string child in SafeDirectories(container))
                yield return (child, categoryName.Equals("mods", StringComparison.OrdinalIgnoreCase) ? "Mod" : "Map");
        }
        if (foundContainers)
            yield break;

        if (Directory.Exists(Path.Combine(full, "zone")) || Directory.EnumerateFiles(full, "*.ff", SearchOption.TopDirectoryOnly).Any())
            yield return (full, "Folder");
    }

    private static IReadOnlyList<string> SafeDirectories(string root)
    {
        string[] directories;
        try { directories = Directory.EnumerateDirectories(root).ToArray(); }
        catch (Exception) { return Array.Empty<string>(); }

        var safe = new List<string>(directories.Length);
        foreach (string directory in directories)
        {
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                    safe.Add(directory);
            }
            catch (Exception) { }
        }
        return safe;
    }

    private static ModCandidate? Inspect(string folder, string category)
    {
        var files = EnumerateFilesBounded(folder, 50000).ToArray();
        string[] fastfiles = files.Where(p => Path.GetExtension(p).Equals(".ff", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fastfiles.Length == 0)
            return null;

        string[] xpaks = files.Where(p => Path.GetExtension(p).Equals(".xpak", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[] soundBanks = files.Where(p =>
        {
            string ext = Path.GetExtension(p);
            return ext.Equals(".sabs", StringComparison.OrdinalIgnoreCase) || ext.Equals(".sabl", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        string[] movies = files.Where(p => Path.GetExtension(p).Equals(".mkv", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[] scripts = files.Where(p =>
        {
            string ext = Path.GetExtension(p);
            return ext.Equals(".gsc", StringComparison.OrdinalIgnoreCase) || ext.Equals(".csc", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lua", StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        string title = Path.GetFileName(folder);
        string description = "";
        string workshopId = "";
        var dependencies = new List<string>();
        string manifestPath = files.FirstOrDefault(p => Path.GetFileName(p).Equals("workshop.json", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (manifestPath.Length > 0)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                JsonElement root = doc.RootElement;
                title = ReadString(root, "Title") ?? ReadString(root, "title") ?? title;
                description = ReadString(root, "Description") ?? ReadString(root, "description") ?? "";
                workshopId = ReadString(root, "UGC") ?? ReadString(root, "ugc") ?? ReadString(root, "WorkshopId") ?? ReadString(root, "workshopId") ?? "";
                foreach (string key in new[] { "Dependencies", "dependencies", "RequiredItems", "requiredItems" })
                {
                    if (!root.TryGetProperty(key, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (JsonElement value in values.EnumerateArray())
                    {
                        string? dependency = value.ValueKind == JsonValueKind.String ? value.GetString()
                            : value.ValueKind == JsonValueKind.Object ? ReadString(value, "UGC") ?? ReadString(value, "id") ?? ReadString(value, "name") : null;
                        if (!string.IsNullOrWhiteSpace(dependency))
                            dependencies.Add(dependency);
                    }
                }
            }
            catch (Exception ex)
            {
                description = $"workshop.json could not be parsed: {ex.Message}";
            }
        }

        bool mapLike = category.Equals("Map", StringComparison.OrdinalIgnoreCase)
            || fastfiles.Any(p => Path.GetFileNameWithoutExtension(p).StartsWith("zm_", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(p).StartsWith("mp_", StringComparison.OrdinalIgnoreCase));
        string status = mapLike
            ? "Map-like fastfiles; conversion can still reject unsupported assets"
            : "PC mod fastfiles; generic gameplay/script mods are not automatically portable";
        if (scripts.Length > 0)
            status += $"; {scripts.Length} source/script file(s) found";
        if (files.Length >= 50000)
            status += "; scan capped at 50,000 files";

        return new ModCandidate
        {
            Name = Path.GetFileName(folder),
            Title = title,
            Path = folder,
            Category = mapLike ? "Map candidate" : "PC mod",
            Status = status,
            Fastfiles = fastfiles.Length,
            TotalFiles = files.Length,
            Xpaks = xpaks.Length,
            SoundBanks = soundBanks.Length,
            Movies = movies.Length,
            Scripts = scripts.Length,
            WorkshopId = workshopId,
            Description = description,
            ManifestPath = manifestPath,
            Dependencies = dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SampleFiles = files.Take(120).Select(p => Path.GetRelativePath(folder, p)).ToArray()
        };
    }

    private static IEnumerable<string> EnumerateFilesBounded(string root, int maxFiles)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        int emitted = 0;
        while (pending.Count > 0 && emitted < maxFiles)
        {
            string current = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current).ToArray(); }
            catch (Exception) { continue; }

            foreach (string file in files)
            {
                yield return file;
                if (++emitted >= maxFiles)
                    yield break;
            }

            foreach (string directory in SafeDirectories(current))
            {
                string name = Path.GetFileName(directory);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("cache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("zone_cache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("shadercache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("work", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(directory);
            }
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void ShowDetails(ModCandidate? candidate)
    {
        if (candidate is null)
        {
            _details.Text = "Select a package to inspect it.";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(candidate.Title);
        text.AppendLine(new string('=', Math.Min(candidate.Title.Length, 60)));
        text.AppendLine($"Type: {candidate.Category}");
        text.AppendLine($"Folder: {candidate.Path}");
        text.AppendLine($"Workshop ID: {(candidate.WorkshopId.Length > 0 ? candidate.WorkshopId : "not declared")}");
        text.AppendLine($"Files scanned: {candidate.TotalFiles}");
        text.AppendLine($"Fastfiles (.ff): {candidate.Fastfiles}");
        text.AppendLine($"XPAKs: {candidate.Xpaks}");
        text.AppendLine($"Sound banks (.sabs/.sabl): {candidate.SoundBanks}");
        text.AppendLine($"Movies (.mkv): {candidate.Movies}");
        text.AppendLine($"Script/source files (.gsc/.csc/.lua): {candidate.Scripts}");
        text.AppendLine($"Metadata: {(candidate.ManifestPath.Length > 0 ? candidate.ManifestPath : "workshop.json not found")}");
        text.AppendLine();
        text.AppendLine("Assessment:");
        text.AppendLine(candidate.Status);
        text.AppendLine();
        text.AppendLine("Description:");
        text.AppendLine(candidate.Description.Length > 0 ? candidate.Description : "(none in workshop.json)");
        text.AppendLine();
        text.AppendLine("Declared dependencies:");
        text.AppendLine(candidate.Dependencies.Length > 0 ? string.Join(Environment.NewLine, candidate.Dependencies.Select(d => " • " + d)) : "(none declared)");
        text.AppendLine();
        text.AppendLine("Sample file inventory:");
        foreach (string file in candidate.SampleFiles)
            text.AppendLine(" • " + file);
        if (candidate.TotalFiles > candidate.SampleFiles.Length)
            text.AppendLine($"… inventory display limited to {candidate.SampleFiles.Length} entries.");
        _details.Text = text.ToString();
    }

    private void OpenSelectedFolder()
    {
        if (_grid.SelectedItem is not ModCandidate candidate || !Directory.Exists(candidate.Path))
        {
            _status.Text = "Select a package with an accessible folder first.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = candidate.Path, UseShellExecute = true });
            _log($"Mod Loader: opened package folder '{candidate.Path}'.");
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open folder: {ex.Message}";
            _log($"Mod Loader: open folder failed for '{candidate.Path}': {ex.Message}");
        }
    }

    private void ExportReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Mod Loader scan report",
            Filter = "JSON report (*.json)|*.json",
            FileName = "bo3-mod-loader-report.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var report = _mods.Select(m => new
            {
                m.Title, m.Name, m.Category, m.Status, m.Path, m.Fastfiles, m.TotalFiles,
                m.Xpaks, m.SoundBanks, m.Movies, m.Scripts, m.WorkshopId, m.Description,
                m.ManifestPath, m.Dependencies, m.SampleFiles, m.Selected
            }).ToArray();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = $"Exported {_mods.Count} package record(s) to {dialog.FileName}.";
            _log($"Mod Loader: exported scan report '{dialog.FileName}' with {_mods.Count} package record(s).");
        }
        catch (Exception ex)
        {
            _status.Text = $"Report export failed: {ex.Message}";
            _log($"Mod Loader: report export failed: {ex.Message}");
        }
    }

    private void QueueSelected()
    {
        ModCandidate[] selected = _mods.Where(m => m.Selected).ToArray();
        if (selected.Length == 0)
        {
            _status.Text = "Select at least one candidate first.";
            return;
        }

        ModCandidate[] selectedMods = selected
            .Where(m => m.Category.Equals("PC mod", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ModCandidate[] selectedMaps = selected
            .Where(m => !m.Category.Equals("PC mod", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (selectedMods.Length > 0)
            _queueMods(selectedMods.Select(m => m.Path).ToArray());
        if (selectedMaps.Length > 0)
            _queuePaths(selectedMaps.Select(m => m.Path).ToArray());

        _status.Text = $"Queued {selectedMods.Length} mod package(s) and {selectedMaps.Length} map candidate(s). Conversion reports identify any source assets that remain unconverted.";
        _log($"Mod Loader: queued {selectedMods.Length} PC mod package(s) for package conversion and {selectedMaps.Length} map candidate(s) for map conversion.");
    }

    private sealed class ModCandidate
    {
        public bool Selected { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Status { get; set; } = "";
        public string Path { get; set; } = "";
        public int Fastfiles { get; set; }
        public int TotalFiles { get; set; }
        public int Xpaks { get; set; }
        public int SoundBanks { get; set; }
        public int Movies { get; set; }
        public int Scripts { get; set; }
        public string WorkshopId { get; set; } = "";
        public string Description { get; set; } = "";
        public string ManifestPath { get; set; } = "";
        public string[] Dependencies { get; set; } = [];
        public string[] SampleFiles { get; set; } = [];
    }
}
