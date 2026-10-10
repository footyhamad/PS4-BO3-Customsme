using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FFPorter.Desktop;

internal sealed class ModLoaderWindow : Window
{
    private readonly ObservableCollection<ModCandidate> _mods = [];
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly Action<IEnumerable<string>> _queuePaths;
    private readonly Action<string> _log;
    private string? _root;

    public ModLoaderWindow(string? gameFolder, Action<IEnumerable<string>> queuePaths, Action<string> log)
    {
        _root = gameFolder;
        _queuePaths = queuePaths;
        _log = log;

        Title = "PC Mod Loader · Discovery";
        Width = 1000;
        Height = 620;
        MinWidth = 760;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Mod Loader",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Discover PC BO3 mod/map folders, then queue their files for the existing conversion pipeline.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 12, 0, 0) };
        _status = new TextBlock
        {
            Text = "Scan the game folder or choose a folder containing mods/usermaps.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        footer.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var choose = new Button { Content = "Choose PC folder…", Padding = new Thickness(12, 7), Margin = new Thickness(0, 0, 8, 0) };
        choose.Click += (_, _) => ChooseRoot();
        buttons.Children.Add(choose);

        var scan = new Button { Content = "Scan", Padding = new Thickness(16, 7), Margin = new Thickness(0, 0, 8, 0) };
        scan.Click += (_, _) => Scan();
        buttons.Children.Add(scan);

        var queue = new Button { Content = "Queue selected for conversion", Padding = new Thickness(14, 7) };
        queue.Click += (_, _) => QueueSelected();
        buttons.Children.Add(queue);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        _grid = new DataGrid
        {
            ItemsSource = _mods,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MinHeight = 220
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Queue",
            Binding = new System.Windows.Data.Binding(nameof(ModCandidate.Selected)) { Mode = System.Windows.Data.BindingMode.TwoWay },
            Width = 58
        });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Mod / map", Binding = new System.Windows.Data.Binding(nameof(ModCandidate.Name)), IsReadOnly = true, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "PC files", Binding = new System.Windows.Data.Binding(nameof(ModCandidate.FileCount)), IsReadOnly = true, Width = 72 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Discovery status", Binding = new System.Windows.Data.Binding(nameof(ModCandidate.Status)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Folder", Binding = new System.Windows.Data.Binding(nameof(ModCandidate.Path)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        layout.Children.Add(_grid);

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
        foreach (string containerName in new[] { "mods", "usermaps" })
        {
            string container = Path.Combine(_root, containerName);
            if (!Directory.Exists(container))
                continue;

            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(container).ToArray(); }
            catch (Exception ex)
            {
                _log($"Mod Loader: cannot scan '{container}': {ex.Message}");
                skipped++;
                continue;
            }

            foreach (string folder in folders)
            {
                try
                {
                    string zone = Path.Combine(folder, "zone");
                    string scanRoot = Directory.Exists(zone) ? zone : folder;
                    string[] fastfiles = Directory.EnumerateFiles(scanRoot, "*.ff", SearchOption.TopDirectoryOnly).ToArray();
                    if (fastfiles.Length == 0)
                    {
                        skipped++;
                        continue;
                    }

                    bool hasScriptTree = Directory.Exists(Path.Combine(zone, "gamedata"));
                    string status = hasScriptTree
                        ? "Fastfiles found; script/dependency compatibility not verified"
                        : "Fastfiles found; PS4 compatibility not verified";
                    _mods.Add(new ModCandidate
                    {
                        Name = Path.GetFileName(folder),
                        Path = folder,
                        FileCount = fastfiles.Length,
                        Status = status
                    });
                }
                catch (Exception ex)
                {
                    _log($"Mod Loader: skipped '{folder}': {ex.Message}");
                    skipped++;
                }
            }
        }

        _status.Text = $"{_mods.Count} candidate folder(s) found; {skipped} folder(s) had no top-level .ff or could not be read. Discovery is not a compatibility verdict.";
        _log($"Mod Loader scan: root='{_root}', candidates={_mods.Count}, skipped={skipped}. No package was converted or deployed.");
    }

    private void QueueSelected()
    {
        ModCandidate[] selected = _mods.Where(m => m.Selected).ToArray();
        if (selected.Length == 0)
        {
            _status.Text = "Select at least one candidate first.";
            return;
        }

        _queuePaths(selected.Select(m => m.Path).ToArray());
        _status.Text = $"Queued {selected.Length} folder(s) for the existing conversion pipeline. Review its reports before deploying anything.";
        _log($"Mod Loader: queued {selected.Length} selected candidate folder(s) for conversion.");
    }

    private sealed class ModCandidate
    {
        public bool Selected { get; set; }
        public string Name { get; set; } = "";
        public int FileCount { get; set; }
        public string Status { get; set; } = "";
        public string Path { get; set; } = "";
    }
}
