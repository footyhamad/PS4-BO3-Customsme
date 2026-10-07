using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FFPorter.Core;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.Common.Tools;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;
using FFPorter.Desktop.Theme;
using Microsoft.Win32;

namespace FFPorter.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Job> _jobs = [];
    private readonly BackendProcess _backend = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _running, _stop, _clearing, _testing;
    private readonly bool _firstRun = !Directory.Exists(Workspace.WorkDirectory);
    private Job? _current, _shown;
    private int _index, _count;
    private DateTime _started;
    private string _stage = "", _detail = "";
    private bool _updating;

    private const string ForkVersion = "Fork 1.2.1.3";
    private const string ForkVersionUrl = "https://github.com/footyhamad/PS4-BO3-Customsme/releases/download/fork-latest/FORK_VERSION.txt";
    private const string ForkExeUrl = "https://github.com/footyhamad/PS4-BO3-Customsme/releases/download/fork-latest/PS4.FF.Porter.exe";
    private const string ForkHashUrl = "https://github.com/footyhamad/PS4-BO3-Customsme/releases/download/fork-latest/PS4.FF.Porter.exe.sha256";
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static AppSettings Settings => AppSettings.Current;
    private static Edition Edition => Edition.Current;

    public MainWindow()
    {
        InitializeComponent();
        Title = TitleText.Text = $"{Edition.Title} · {ForkVersion}";
        SubtitleText.Text = $"{Edition.Subtitle} · {ForkVersion}";
        EmptyQueueText.Text = $"Maps, mods, weapons and zones from {Edition.GameName}: .ff files or whole folders.";
        ThemeManager.StyleTitleBar(this);
        JobList.ItemsSource = _jobs;
        _jobs.CollectionChanged += (_, _) => RefreshQueue();
        ShowOutput();
        ShowGameFolder();
        Loaded += (_, _) => { AskForGameFolder(); PrepareTools(); };
        DragOver += (_, e) =>
        {
            e.Effects = !_running && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                AddPaths(paths);
            e.Handled = true;
        };
        Closing += WindowClosing;
        _clock.Tick += (_, _) => ShowProgress();
        RefreshQueue();
        RefreshActionButtons();
    }


    public void AddPaths(IEnumerable<string> paths)
    {
        if (_running)
            return;
        List<Job> found = JobScanner.Scan(paths, Log.Append);
        Job? last = null;
        foreach (Job job in found)
        {
            if (_jobs.Any(j => string.Equals(j.MainFile, job.MainFile, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (_jobs.Any(j => string.Equals(j.Name, job.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Append($"Skipped {job.MainFile}: another {job.Name} is already queued, and both would convert into the same folder.");
                continue;
            }
            LoadPreviousReport(job);
            _jobs.Add(job);
            last = job;
        }
        if (last != null)
            JobList.SelectedItem = last;
        else if (found.Count == 0)
            SetStatus("Nothing to convert there", $"only {Edition.GameName} PC fastfiles convert; the log says what was skipped");
    }

    private static void LoadPreviousReport(Job job)
    {
        string report = job.Name + ".fidelity.json";
        if ((FidelityProtocol.Load(Path.Combine(FFPorter.Core.Workspace.ReportDirectory, report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.OutputFor(job), report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.Output, job.Name, report))) is not { } snapshot)
            return;
        job.Report = FidelityView.From(snapshot);
        job.StatusText = snapshot.State == FidelityStates.Done ? "Converted earlier" : "Last run failed";
    }

    private void RefreshQueue()
    {
        CountText.Text = _jobs.Count.ToString();
        EmptyQueue.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobList.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConvertButton.IsEnabled = !_running && !_clearing && _jobs.Count > 0;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !_running && _jobs.Count > 0;
        if (!_running)
        {
            Progress.Value = 0;
            SetStatus(_jobs.Count == 0 ? "Drop something to convert" : $"{_jobs.Count} ready to convert", "");
        }
        ShowPanel();
    }

    private Job? SingleSelectedJob() => JobList.SelectedItems.Count == 1 ? JobList.SelectedItem as Job : null;

    private void RefreshActionButtons()
    {
        bool idle = !_running && !_clearing && !_updating && !_testing;
        Job? selected = SingleSelectedJob();
        RetryFailedButton.IsEnabled = idle && _jobs.Any(j => j.State == RunStates.Failed);
        ScanMapButton.IsEnabled = idle && selected != null;
        TestOutputButton.IsEnabled = idle && selected != null && File.Exists(Path.Combine(Settings.OutputFor(selected), Path.GetFileName(selected.MainFile)));
        DiagnosticsButton.IsEnabled = idle && selected != null;
        HistoryButton.IsEnabled = idle;
        ProfileComboBox.IsEnabled = idle;
    }

    private void SetTesting(bool testing)
    {
        _testing = testing;
        AddFilesButton.IsEnabled = AddFolderButton.IsEnabled = OutputButton.IsEnabled = GameFolderButton.IsEnabled = ClearCacheButton.IsEnabled = !testing && !_running;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !testing && !_running && _jobs.Count > 0;
        ConvertButton.IsEnabled = !testing && !_running && !_clearing && _jobs.Count > 0;
        SetUpdateButtons();
    }

    private async void ScanMapClick(object sender, RoutedEventArgs e)
    {
        Job? job = SingleSelectedJob();
        if (job == null || _testing || _running || _clearing)
            return;
        SetTesting(true);
        try
        {
            SetStatus("Scanning map", "Reading the PC fastfile…");
            var output = new List<string>();
            var errors = new List<string>();
            int code = await _backend.RunAsync([job.Codename, "info", job.MainFile, "--root", Settings.Workspace],
                Settings.Workspace, line => output.Add(line), line => errors.Add(line));
            if (code != 0)
            {
                MessageBox.Show(this, $"Map scan failed.\n\n{errors.LastOrDefault() ?? "the fastfile could not be read"}",
                    "Scan map", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string json = output.LastOrDefault(line => line.TrimStart().StartsWith("{")) ?? "";
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string stem = Path.GetFileNameWithoutExtension(job.MainFile);
            string folder = Path.GetDirectoryName(job.MainFile)!;
            string sndFolder = Path.Combine(folder, "snd");
            int soundBanks = Directory.Exists(sndFolder)
                ? Directory.EnumerateFiles(sndFolder, stem + ".*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".sabl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sabs", StringComparison.OrdinalIgnoreCase))
                : 0;
            int languageZones = Directory.EnumerateFiles(folder, "*_" + stem + ".ff").Count();
            bool xpak = File.Exists(Path.Combine(folder, stem + ".xpak"));
            bool outputExists = File.Exists(Path.Combine(Settings.OutputFor(job), Path.GetFileName(job.MainFile)));
            string existingReport = Path.Combine(Workspace.ReportDirectory, stem + ".map-port.json");
            bool cachedReport = File.Exists(existingReport);
            var lines = new List<string>
            {
                $"Map: {stem}",
                $"PC fastfile: OK · {Job.SizeText(new FileInfo(job.MainFile).Length)}",
                $"Zone bytes: {root.GetProperty("zone_bytes").GetInt64():N0}",
                $"Assets: {root.GetProperty("assets").GetInt32():N0}",
                $"Language zones: {languageZones}",
                $"Streamed XPAK: {(xpak ? "present" : "none")}",
                $"Sound banks: {soundBanks}",
                $"Previous report: {(cachedReport ? "available" : "none")}",
                $"Output: {(outputExists ? "already built" : "not built")}",
                "Smart resume: enabled; compatible caches are reused."
            };
            MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Map pre-flight", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Map scan complete", $"{stem} · ready to convert");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Map scan failed.\n\n{error.Message}", "Scan map", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetTesting(false);
            RefreshActionButtons();
        }
    }

    private async void TestOutputClick(object sender, RoutedEventArgs e)
    {
        Job? job = SingleSelectedJob();
        if (job == null || _testing || _running || _clearing)
            return;
        string outputFolder = Settings.OutputFor(job);
        string[] fastfiles = Directory.Exists(outputFolder)
            ? Directory.EnumerateFiles(outputFolder, "*.ff", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName).ToArray()
            : [];
        if (fastfiles.Length == 0)
        {
            SetStatus("No converted fastfile", "Convert the map first.");
            return;
        }
        SetTesting(true);
        int failures = 0;
        try
        {
            foreach (string fastfile in fastfiles)
            {
                SetStatus("Testing output", Path.GetFileName(fastfile));
                string walk = Path.Combine(Settings.Workspace, "analysis", "gui_test_walks",
                    Path.GetFileNameWithoutExtension(fastfile) + ".ps4.t7walk");
                Directory.CreateDirectory(Path.GetDirectoryName(walk)!);
                int code = await _backend.RunAsync([job.Codename, "ps4-walk", fastfile, "-o", walk, "--root", Settings.Workspace],
                    Settings.Workspace, line => Log.Append(line), line => { failures++; Log.Append(line); });
                if (code != 0)
                    failures++;
            }
            IEnumerable<string> xpaks = Directory.Exists(outputFolder)
                ? Directory.EnumerateFiles(outputFolder, "*.xpak", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName)
                : Enumerable.Empty<string>();
            foreach (string xpak in xpaks)
            {
                SetStatus("Testing XPAK", Path.GetFileName(xpak));
                int code = await _backend.RunAsync([job.Codename, "xpak-verify", xpak],
                    Settings.Workspace, line => Log.Append(line), line => { failures++; Log.Append(line); });
                if (code != 0)
                    failures++;
            }
            SetStatus(failures == 0 ? "Output validation passed" : "Output validation failed",
                failures == 0 ? $"{job.Name} · PS4 walks and XPAKs are valid" : $"{failures} test failure{(failures == 1 ? "" : "s")} · see Log");
        }
        finally
        {
            SetTesting(false);
            RefreshActionButtons();
        }
    }

    private void DiagnosticsClick(object sender, RoutedEventArgs e)
    {
        Job? job = SingleSelectedJob();
        if (job == null || _testing || _running || _clearing)
            return;
        string path = Path.Combine(Workspace.ReportDirectory, job.Name + ".map-port.json");
        if (!File.Exists(path))
        {
            SetStatus("No diagnostics yet", "Convert the map first.");
            return;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            string[] problems = root.TryGetProperty("problems", out JsonElement p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Select(x => x.GetString() ?? x.ToString()).ToArray() : [];
            string[] warnings = root.TryGetProperty("warnings", out JsonElement w) && w.ValueKind == JsonValueKind.Array
                ? w.EnumerateArray().Select(x => x.GetString() ?? x.ToString()).ToArray() : [];
            var lines = new List<string> { $"Diagnostics: {job.Name}", "", $"Errors / problems: {problems.Length}", $"Warnings: {warnings.Length}", "" };
            lines.AddRange(problems.Select(x => "ERROR  " + x));
            lines.AddRange(warnings.Select(x => "WARN   " + x));
            MessageBox.Show(this, string.Join(Environment.NewLine, lines.Take(120)), "Map diagnostics", MessageBoxButton.OK,
                problems.Length > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not read diagnostics.\n\n{error.Message}", "Map diagnostics", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HistoryClick(object sender, RoutedEventArgs e)
    {
        if (_testing) return;
        string folder = Workspace.ReportDirectory;
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "No conversion history has been recorded yet.", "Conversion history", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = new List<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.fidelity.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(10))
        {
            FidelitySnapshot? snapshot = FidelityProtocol.Load(file);
            if (snapshot == null) continue;
            string score = snapshot.Percent is int p ? $"{p}%" : "—";
            rows.Add($"{File.GetLastWriteTime(file):yyyy-MM-dd HH:mm} · {snapshot.Map} · {score} · {snapshot.State}");
        }
        MessageBox.Show(this, rows.Count == 0 ? "No completed conversions are in the history yet." : string.Join(Environment.NewLine, rows),
            "Conversion history · last 10", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ChangelogClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/footyhamad/PS4-BO3-Customsme/blob/main/CHANGELOG.md") { UseShellExecute = true });
    }

    private void AddFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Fastfiles|*.ff", Multiselect = true, Title = $"Add {Edition.GameName} fastfiles" };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FileNames);
    }

    private void AddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder (a usermap, a mod, a zone folder, or a folder of them)", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FolderNames);
    }

    private void RemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (_running)
            return;
        foreach (Job job in JobList.SelectedItems.Cast<Job>().ToArray())
            _jobs.Remove(job);
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        if (!_running)
            _jobs.Clear();
    }

    private void JobListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void JobSelectionChanged(object sender, SelectionChangedEventArgs e) { ShowPanel(); RefreshActionButtons(); }


    private void ShowPanel()
    {
        Job? job = JobList.SelectedItems.Count == 1 ? JobList.SelectedItem as Job : null;
        job ??= _running ? _current : null;
        if (!ReferenceEquals(job, _shown))
        {
            if (_shown != null)
                _shown.PropertyChanged -= ShownJobChanged;
            if (job != null)
                job.PropertyChanged += ShownJobChanged;
            _shown = job;
        }
        BindingOperations.ClearBinding(Fidelity, DataContextProperty);
        if (job?.Report != null)
        {
            Fidelity.SetBinding(DataContextProperty, new Binding(nameof(Job.Report)) { Source = job });
        }
        else
        {
            Fidelity.DataContext = null;
            Fidelity.EmptyMessage = job == null
                ? "Convert something to see how faithfully each part of it carries over to PS4. The scores fill in live while it converts."
                : $"{job.Name} has not been converted yet. Its port fidelity fills in live while it converts.";
        }
    }

    private void ShownJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Job.Report))
            ShowPanel();
    }


    private List<string> Command(Job job, string workspace)
    {
        List<string> command = [job.Codename, "convert", job.MainFile, "-o", Settings.OutputFor(job), "--force", "--progress", "--root", workspace];
        switch (ProfileComboBox.SelectedIndex)
        {
            case 1:
                command.Add("--donor-all");
                break;
            case 2:
                command.Add("--no-gsc-check");
                command.Add("--no-shader-compile");
                break;
            case 3:
                command.Add("--keep-work");
                break;
        }
        if (Settings.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            command.AddRange([Edition.GameFolderOption, game]);
        return command;
    }

    private async void ConvertClick(object sender, RoutedEventArgs e) => await ConvertAll();

    private async void RetryFailedClick(object sender, RoutedEventArgs e)
    {
        Job[] failed = [.. _jobs.Where(j => j.State == RunStates.Failed)];
        if (failed.Length > 0)
            await ConvertAll(failed);
    }

    private async Task ConvertAll(IReadOnlyList<Job>? requested = null)
    {
        if (_running || _clearing || _testing || _jobs.Count == 0)
            return;
        string workspace = Settings.Workspace;
        try
        {
            Directory.CreateDirectory(Settings.Output);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus("Cannot use the output folder", error.Message);
            return;
        }

        Job[] queue = requested == null ? [.. _jobs] : [.. requested.Where(_jobs.Contains)];
        if (queue.Length == 0)
            return;
        bool retrying = requested != null;
        _stop = false;
        _count = queue.Length;
        SetBusy(true);
        Log.Clear();
        foreach (Job job in queue)
        {
            job.State = RunStates.Queued;
            job.StatusText = "Queued";
            job.Progress = 0;
        }
        int passed = 0, failed = 0;
        _started = DateTime.Now;
        _clock.Start();
        try
        {
            try
            {
                Log.StartFile(Path.Combine(Workspace.Locate(workspace).Join("analysis/gui_logs"), $"convert-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Log.Append($"The log is not saved to disk: {error.Message}");
            }
            for (_index = 0; _index < queue.Length && !_stop; _index++)
            {
                Job job = queue[_index];
                _current = job;
                job.State = RunStates.Running;
                job.StatusText = retrying ? "Retrying" : "Converting";
                job.StageText = "Starting";
                job.Report = new FidelityView();
                JobList.SelectedItem = job;
                JobList.ScrollIntoView(job);
                ShowPanel();
                _stage = $"Converting {job.Name}";
                _detail = "starting";
                ShowProgress();
                Log.Append($"\n[{DateTime.Now:T}] {job.GameName} {job.Kind.ToLowerInvariant()} {job.Name}: {job.MainFile}");
                if (retrying)
                    Log.Append("Smart resume: compatible cached stages will be reused; stale or failed work is rebuilt.");

                var errors = new List<string>();
                int code;
                try
                {
                    code = await _backend.RunAsync(Command(job, workspace), workspace, line => OnOutput(job, line), line =>
                    {
                        errors.Add(line);
                        Log.Append(line);
                    });
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
                {
                    code = -1;
                    errors.Add(error.Message);
                    Log.Append(error.Message);
                }
                if (Finish(job, code, errors))
                    passed++;
                else if (!_stop)
                    failed++;
            }
            foreach (Job job in queue.Where(j => j.State == RunStates.Queued))
            {
                job.State = RunStates.Ready;
                job.StatusText = "Not run";
            }
            string summary = $"{passed} converted" + (failed > 0 ? $" · {failed} failed" : "");
            SetStatus(_stop ? "Cancelled" : failed > 0 ? "Finished with problems" : "Finished", $"{summary} · {Elapsed(DateTime.Now - _started)}");
            Log.Append(_stop ? $"Cancelled · {summary}" : summary);
            if (!_stop)
                Progress.Value = 1;
            PercentText.Text = "";
        }
        finally
        {
            Log.StopFile();
            _clock.Stop();
            _current = null;
            SetBusy(false);
            ShowPanel();
            RefreshActionButtons();
        }
    }

    private void OnOutput(Job job, string line)
    {
        if (FidelityProtocol.TryParse(line, out FidelitySnapshot? snapshot) && snapshot != null)
        {
            job.Report ??= new FidelityView();
            job.Report.Update(snapshot);
            job.Progress = snapshot.Progress;
            job.StageText = snapshot.Stage;
            _stage = snapshot.Stage;
            _detail = snapshot.Detail;
            ShowProgress();
            return;
        }
        Log.Append(line);
    }

    private bool Finish(Job job, int code, List<string> errors)
    {
        if (_stop)
        {
            job.State = RunStates.Cancelled;
            job.StatusText = "Cancelled";
            if (job.Report is { IsRunning: true } report)
                report.Interrupt(FidelityStates.Cancelled, "Cancelled before the conversion finished. The output folder may hold a partial conversion.");
            return false;
        }
        if (code == 0)
        {
            job.State = RunStates.Done;
            job.StatusText = "Converted";
            job.Progress = 1;
            return true;
        }
        job.State = RunStates.Failed;
        IEnumerable<string> reasons = errors.Where(e => !string.IsNullOrWhiteSpace(e)).TakeLast(3);
        job.StatusText = errors.Any(e => e.Contains("needs parts this converter cannot port yet", StringComparison.Ordinal)) ? "Not supported yet" : $"Failed ({code})";
        LogToggle.IsChecked = true;
        if (job.Report == null || job.Report.IsRunning)
        {
            job.Report ??= new FidelityView();
            job.Report.Interrupt(FidelityStates.Failed, "The converter stopped before it finished. Its last lines are below; the log has the rest.", reasons);
        }
        return false;
    }

    private void ShowProgress()
    {
        if (!_running)
            return;
        _current?.Report?.Tick();
        double itemProgress = _current?.Progress ?? 0;
        double overall = _count == 0 ? 0 : Math.Clamp((_index + itemProgress) / _count, 0, 1);
        string position = _count > 1 ? $"{_index + 1} of {_count} · " : "";
        SetStatus(_stage.Length > 0 ? _stage : "Converting", $"{position}{_current?.Name}{(_detail.Length > 0 ? " · " + _detail : "")}");
        Progress.Value = overall;
        PercentText.Text = $"{overall:P0} · {Elapsed(DateTime.Now - _started)}";
    }

    private void SetStatus(string status, string detail)
    {
        StatusText.Text = status;
        DetailText.Text = detail.Length > 0 ? "   " + detail : "";
        if (!_running)
            PercentText.Text = "";
        Progress.Visibility = _running || Progress.Value > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetUpdateProgress(string status, string detail, double? progress = null)
    {
        StatusText.Text = status;
        DetailText.Text = detail.Length > 0 ? "   " + detail : "";
        Progress.IsIndeterminate = !progress.HasValue;
        Progress.Value = progress ?? 0;
        PercentText.Text = progress.HasValue ? $"{progress.Value:P0}" : "Working…";
        Progress.Visibility = Visibility.Visible;

        UpdateTitle.Text = status;
        UpdateDetail.Text = detail;
        UpdateProgress.IsIndeterminate = !progress.HasValue;
        UpdateProgress.Value = progress ?? 0;
        UpdatePercent.Text = progress.HasValue ? $"{progress.Value:P0}" : "Working…";
        UpdateOverlay.Visibility = Visibility.Visible;
    }

    private void ResetUpdateProgress()
    {
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Hidden;
        PercentText.Text = "";

        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdatePercent.Text = "";
        UpdateDetail.Text = "";
        UpdateOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _running = busy;
        AddFilesButton.IsEnabled = AddFolderButton.IsEnabled = OutputButton.IsEnabled = GameFolderButton.IsEnabled = ClearCacheButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !busy && _jobs.Count > 0;
        SetUpdateButtons();
        ConvertButton.IsEnabled = !busy && _jobs.Count > 0;
        ConvertButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckVersionClick(object sender, RoutedEventArgs e)
    {
        if (_running || _clearing || _updating)
            return;
        _updating = true;
        SetUpdateButtons();
        try
        {
            SetUpdateProgress("Checking version", "Reading the latest fork build…");
            string? remote = await ReadRemoteForkVersion();
            string? remoteHash = await ReadRemoteForkHash();
            string exePath = GetCurrentExePath();
            string localHash = await ComputeSha256(exePath);

            if (remote == null || remoteHash == null)
            {
                MessageBox.Show(this, "GitHub did not return a valid fork version/build.", "Check version", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool current = string.Equals(remote, ForkVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase);
            string status = current
                ? $"You are up to date.{Environment.NewLine}{Environment.NewLine}Version: {ForkVersion}{Environment.NewLine}Build: {localHash[..12]}"
                : $"A newer fork build is available.{Environment.NewLine}{Environment.NewLine}Installed: {ForkVersion} · {localHash[..12]}{Environment.NewLine}GitHub: {remote} · {remoteHash[..12]}{Environment.NewLine}{Environment.NewLine}Use UPDATE TOOL to install it.";

            MessageBox.Show(this, status, "Porter version", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not check the fork build.{Environment.NewLine}{Environment.NewLine}{error.Message}", "Check version", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updating = false;
            SetUpdateButtons();
            ResetUpdateProgress();
        }
    }

    private async void UpdateToolClick(object sender, RoutedEventArgs e)
    {
        if (_running || _clearing || _updating)
            return;
        _updating = true;
        SetUpdateButtons();
        string? tempExe = null;
        try
        {
            SetUpdateProgress("Checking for updates", "Reading the latest fork build…");
            string? remote = await ReadRemoteForkVersion();
            string? remoteHash = await ReadRemoteForkHash();
            if (remote == null || remoteHash == null)
            {
                MessageBox.Show(this, "GitHub did not return a valid fork version/build.", "Update tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string exePath = GetCurrentExePath();
            string localHash = await ComputeSha256(exePath);
            bool sameBuild = string.Equals(remote, ForkVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase);
            if (sameBuild)
            {
                MessageBox.Show(this, $"Already up to date.{Environment.NewLine}{Environment.NewLine}Installed: {ForkVersion}", "Update tool", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string change = string.Equals(remote, ForkVersion, StringComparison.OrdinalIgnoreCase)
                ? $"install the latest {remote} build"
                : $"update Porter from {ForkVersion} to {remote}";
            MessageBoxResult answer = MessageBox.Show(
                this,
                $"Do you want to {change}?{Environment.NewLine}{Environment.NewLine}The new EXE will be downloaded and verified before you are asked to restart.",
                "Update tool",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            tempExe = Path.Combine(Path.GetDirectoryName(GetCurrentExePath()) ?? Path.GetTempPath(),
                $".PS4.FF.Porter-update-{Guid.NewGuid():N}.tmp");
            string tempHash = tempExe + ".sha256";

            SetUpdateProgress("Updating the tool", $"Downloading {remote}…", 0);
            await DownloadFile(ForkExeUrl, tempExe, "Porter.exe", 0, 0.80);
            await DownloadFile(ForkHashUrl, tempHash, "checksum", 0.80, 0.02);

            SetUpdateProgress("Validating download", "Checking the executable…", 0.85);
            FileInfo downloaded = new(tempExe);
            if (downloaded.Length < 1024 * 1024)
                throw new InvalidDataException("The downloaded Porter executable is unexpectedly small.");

            await using (FileStream probe = File.OpenRead(tempExe))
            {
                int m = probe.ReadByte();
                int z = probe.ReadByte();
                if (m != 'M' || z != 'Z')
                    throw new InvalidDataException("The downloaded file is not a Windows executable.");
            }

            SetUpdateProgress("Verifying download", "Calculating SHA-256…", 0.90);
            string expectedHash = ParseSha256(File.ReadAllText(tempHash));
            string actualHash = await ComputeSha256(tempExe);
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 mismatch. Expected {expectedHash}, got {actualHash}.");

            try { File.Delete(tempHash); } catch { }

            SetUpdateProgress("Update verified", "The new build is downloaded and verified.", 1);

            MessageBoxResult restart = MessageBox.Show(
                this,
                $"Update {remote} is ready to install.{Environment.NewLine}{Environment.NewLine}Restart requires closing the app. The verified new EXE will replace the old EXE in:{Environment.NewLine}{exePath}{Environment.NewLine}{Environment.NewLine}Close and restart now?",
                "Restart required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (restart != MessageBoxResult.Yes)
            {
                SetUpdateProgress("Update downloaded", "The verified update is ready. Restart from UPDATE TOOL to install it.", 1);
                return;
            }

            SetUpdateProgress("Restarting", "Closing the app and replacing the old EXE…", 1);

            string script = string.Join(Environment.NewLine, new[]
            {
                "$ErrorActionPreference = 'Stop'",
                "$target = " + PsQuote(exePath),
                "$temp = " + PsQuote(tempExe),
                "$targetPid = " + Environment.ProcessId,
                "$failed = $false",
                "",
                "for ($i = 0; $i -lt 120; $i++) {",
                "    if ($null -eq (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) { break }",
                "    Start-Sleep -Milliseconds 250",
                "}",
                "",
                "if ($null -ne (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) {",
                "    $failed = $true",
                "}",
                "else {",
                "    for ($i = 0; $i -lt 40; $i++) {",
                "        try {",
                "            if (-not (Test-Path -LiteralPath $temp)) { throw 'The downloaded update file is missing.' }",
                "            [System.IO.File]::Replace($temp, $target, $null, $true)",
                "            Start-Process -FilePath $target",
                "            exit 0",
                "        }",
                "        catch {",
                "            Start-Sleep -Milliseconds 500",
                "        }",
                "    }",
                "    $failed = $true",
                "}",
                "",
                "if ($failed) {",
                "    try { Start-Process -FilePath $target } catch { }",
                "    exit 1",
                "}",
            });

            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Application.Current.Shutdown();
            tempExe = null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or TaskCanceledException)
        {
            MessageBox.Show(this, $"Update failed.{Environment.NewLine}{Environment.NewLine}{error.Message}", "Update tool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (tempExe != null)
            {
                try { File.Delete(tempExe); } catch { }
                try { File.Delete(tempExe + ".sha256"); } catch { }
            }
            _updating = false;
            SetUpdateButtons();
            if (!Application.Current.Dispatcher.HasShutdownStarted)
                ResetUpdateProgress();
        }
    }

    private static string GetCurrentExePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Could not determine the running Porter executable path.");

    private static async Task<string?> ReadRemoteForkHash()
    {
        using HttpResponseMessage response = await UpdateClient.GetAsync(ForkHashUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            return null;
        return ParseSha256(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string?> ReadRemoteForkVersion()
    {
        using HttpResponseMessage response = await UpdateClient.GetAsync(ForkVersionUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            return null;
        string version = (await response.Content.ReadAsStringAsync()).Trim();
        return version.StartsWith("Fork ", StringComparison.OrdinalIgnoreCase) ? version : null;
    }

    private async Task DownloadFile(string url, string path, string label, double start, double span)
    {
        using HttpResponseMessage response = await UpdateClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;
        long completed = 0;
        byte[] buffer = new byte[64 * 1024];

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None);

        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read));
            completed += read;

            double? fileProgress = total > 0 ? Math.Clamp((double)completed / total, 0, 1) : null;
            double? overall = fileProgress.HasValue ? start + fileProgress.Value * span : null;
            string detail = total > 0
                ? $"Downloading {label} · {Job.SizeText(completed)} / {Job.SizeText(total)}"
                : $"Downloading {label} · {Job.SizeText(completed)}";
            SetUpdateProgress("Updating the tool", detail, overall);
        }
    }

    private static async Task<string> ComputeSha256(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private static string ParseSha256(string text)
    {
        foreach (string token in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length != 64)
                continue;
            bool hex = true;
            foreach (char ch in token)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    hex = false;
                    break;
                }
            }
            if (hex)
                return token.ToUpperInvariant();
        }
        throw new InvalidDataException("The release checksum file did not contain a valid SHA-256 hash.");
    }
    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private void SetUpdateButtons()
    {
        bool enabled = !_running && !_clearing && !_updating && !_testing;
        CheckVersionButton.IsEnabled = enabled;
        UpdateToolButton.IsEnabled = enabled;
        RefreshActionButtons();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => StopRun();

    private void StopRun()
    {
        if (!_running)
            return;
        _stop = true;
        _backend.Cancel();
        SetStatus("Cancelling", "waiting for the converter to stop");
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            StopRun();
        }
    }


    private void ShowGameFolder()
    {
        string? folder = Settings.GameFolder;
        GameFolderLabel.Text = Edition.GameFolderName;
        GameFolderText.Text = folder == null ? "not set" : ShortPath(folder);
        GameFolderButton.ToolTip = folder == null
            ? $"Set the {Edition.GameFolderName}. {Edition.GameFolderHint}"
            : $"{folder}\n\nClick to choose another folder.";
    }

    private void AskForGameFolder()
    {
        if (!Settings.FirstRun || Settings.GameFolder != null)
            return;
        bool choose = MessageDialog.Confirm(this, DialogKind.Info, $"Choose your {Edition.GameName} files",
            $"{Edition.Title} needs them to convert with.\n\n{Edition.GameFolderHint}", "Choose folder", "Not now");
        if (choose)
            ChooseGameFolder();
        else
            SetStatus($"{Edition.GameFolderName} not set", "the Game files button in the header sets it");
    }

    private async void PrepareTools()
    {
        bool showOverlay = Settings.FirstRun;
        Task work = Task.Run(() =>
        {
            Ps4Sdk.EnsureFiles();
            Dispatcher.Invoke(() => PrepareDetail.Text = "Extracting files…");
            ToolData.Extract();
            Dispatcher.Invoke(() => PrepareDetail.Text = "Setting up tools…");
            Edition.PrepareTools(line => Dispatcher.Invoke(() =>
            {
                Log.Append(line);
                PrepareDetail.Text = line;
            }));
        });
        if (showOverlay)
            PrepareOverlay.Visibility = Visibility.Visible;
        try
        {
            await work;
        }
        catch (Exception error)
        {
            Log.Append($"Preparing files failed: {error.Message}");
        }
        finally
        {
            PrepareOverlay.Visibility = Visibility.Collapsed;
            RefreshQueue();
            RefreshActionButtons();
        }
    }

    private void ChooseGameFolderClick(object sender, RoutedEventArgs e) => ChooseGameFolder();

    private void ChooseGameFolder()
    {
        if (_running)
            return;
        while (true)
        {
            var dialog = new OpenFolderDialog { Title = $"Choose the {Edition.GameFolderName}" };
            if (Settings.GameFolder is { } current && Directory.Exists(current))
                dialog.InitialDirectory = current;
            if (dialog.ShowDialog(this) != true)
                return;
            if (Edition.TryGameFolder(dialog.FolderName, out string? folder) && folder != null)
            {
                Settings.SetGameFolder(folder);
                ShowGameFolder();
                SetStatus($"{Edition.GameFolderName} set", folder);
                return;
            }
            if (!MessageDialog.Confirm(this, DialogKind.Warning, $"That folder is not the {Edition.GameFolderName}",
                $"{dialog.FolderName}\n\n{Edition.GameFolderHint}", "Try again"))
            {
                return;
            }
        }
    }

    private void ShowOutput()
    {
        OutputLabel.Text = Settings.OutputOverride == null ? "Output (default)" : "Output";
        OutputText.Text = ShortPath(Settings.Output);
        OutputButton.ToolTip = $"Converted files go to {Path.Combine(Settings.Output, Edition.Codename, "<name>")}. "
            + (Settings.OutputOverride == null ? "Click to pick another folder." : $"Click to pick another folder; right-click to go back to {AppSettings.DefaultOutput}.");
        DefaultOutputItem.IsEnabled = Settings.OutputOverride != null;
        DefaultOutputItem.Header = $"Use the default folder ({AppSettings.DefaultOutput})";
    }

    private static string ShortPath(string path)
    {
        string[] parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return path.Length <= 48 || parts.Length <= 3 ? path : $"…{Path.DirectorySeparatorChar}{string.Join(Path.DirectorySeparatorChar, parts[^2..])}";
    }

    private void ChooseOutputClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = $"Where converted files go (a {Edition.Codename} folder is made inside)" };
        if (Directory.Exists(Settings.Output))
            dialog.InitialDirectory = Settings.Output;
        if (dialog.ShowDialog(this) != true)
            return;
        Settings.SetOutput(dialog.FolderName);
        ShowOutput();
        ReloadEarlierReports();
    }

    private void ReloadEarlierReports()
    {
        foreach (Job job in _jobs.Where(j => j.State == RunStates.Ready))
        {
            job.Report = null;
            job.StatusText = "Ready";
            LoadPreviousReport(job);
        }
        ShowPanel();
    }

    private void DefaultOutputClick(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        Settings.SetOutput(null);
        ShowOutput();
        ReloadEarlierReports();
    }

    private void OpenOutputClick(object sender, RoutedEventArgs e)
    {
        string folder = Settings.Output;
        if (JobList.SelectedItem is Job job)
        {
            if (Directory.Exists(Settings.OutputFor(job)))
                folder = Settings.OutputFor(job);
            else if (Directory.Exists(Path.Combine(folder, job.Codename)))
                folder = Path.Combine(folder, job.Codename);
        }
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        else if (!_running)
            SetStatus("Nothing converted yet", $"{folder} is created by the first conversion");
    }

    private void LogToggled(object sender, RoutedEventArgs e) => Log.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async void ClearCacheClick(object sender, RoutedEventArgs e)
    {
        if (_running || _clearing)
            return;
        SetClearing(true);
        try
        {
            (IReadOnlyList<string> folders, long size) = await Task.Run(() =>
            {
                IReadOnlyList<string> found = Edition.CacheFolders();
                return (found, found.Sum(FolderSize));
            });
            if (size == 0)
            {
                SetStatus("The cache is already empty", "");
                return;
            }
            if (!MessageDialog.Confirm(this, DialogKind.Warning, "Clear the tool cache?",
                $"This deletes {Job.SizeText(size)} of saved work that makes conversions faster: compiled shaders, what was read from "
                + $"{Edition.GameName}'s zones, and each map's saved zone reads and sound. The next conversion takes longer while it is rebuilt.\n\n"
                + "Converted files, reports and settings are kept.",
                "Clear cache", danger: true))
            {
                return;
            }
            SetStatus("Clearing the cache", Job.SizeText(size));
            List<string> failed = await Task.Run(() => DeleteFolders(folders));
            if (failed.Count == 0)
            {
                SetStatus("Cache cleared", $"{Job.SizeText(size)} freed");
                return;
            }
            foreach (string problem in failed)
                Log.Append($"Could not clear {problem}");
            LogToggle.IsChecked = true;
            SetStatus("Cache partly cleared", $"{failed.Count} folder{(failed.Count == 1 ? "" : "s")} could not be deleted; the log says why");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not clear the cache", error.Message);
        }
        finally
        {
            SetClearing(false);
        }
    }

    private void SetClearing(bool clearing)
    {
        _clearing = clearing;
        ClearCacheButton.IsEnabled = !clearing;
        ConvertButton.IsEnabled = !clearing && _jobs.Count > 0;
        SetUpdateButtons();
    }

    private static long FolderSize(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true })
                .Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static List<string> DeleteFolders(IEnumerable<string> folders)
    {
        var failed = new List<string>();
        foreach (string folder in folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{Path.GetFileName(folder)}: {error.Message}");
            }
        }
        return failed;
    }

    private static string Elapsed(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds:00}s" : $"{time.Seconds}s";
}
