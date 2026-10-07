using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
    private bool _running, _stop, _clearing;
    private readonly bool _firstRun = !Directory.Exists(Workspace.WorkDirectory);
    private Job? _current, _shown;
    private int _index, _count;
    private DateTime _started;
    private string _stage = "", _detail = "";
    private bool _updating;

    private const string ForkVersion = "Fork 1.00";
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

    private void JobSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPanel();


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


    private static List<string> Command(Job job, string workspace)
    {
        List<string> command = [job.Codename, "convert", job.MainFile, "-o", Settings.OutputFor(job), "--force", "--progress", "--root", workspace];
        if (Settings.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            command.AddRange([Edition.GameFolderOption, game]);
        return command;
    }

    private async void ConvertClick(object sender, RoutedEventArgs e) => await ConvertAll();

    private async Task ConvertAll()
    {
        if (_running || _clearing || _jobs.Count == 0)
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

        Job[] queue = [.. _jobs];
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
                job.StatusText = "Converting";
                job.StageText = "Starting";
                job.Report = new FidelityView();
                JobList.SelectedItem = job;
                JobList.ScrollIntoView(job);
                ShowPanel();
                _stage = $"Converting {job.Name}";
                _detail = "starting";
                ShowProgress();
                Log.Append($"\n[{DateTime.Now:T}] {job.GameName} {job.Kind.ToLowerInvariant()} {job.Name}: {job.MainFile}");

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
            string? remote = await ReadRemoteForkVersion();
            if (remote == null)
            {
                MessageBox.Show(this, "GitHub did not return a valid fork version.", "Check version", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool current = string.Equals(remote, ForkVersion, StringComparison.OrdinalIgnoreCase);
            string status = current
                ? $"You are up to date.{Environment.NewLine}{Environment.NewLine}Installed: {ForkVersion}{Environment.NewLine}GitHub: {remote}"
                : $"A newer fork build is available.{Environment.NewLine}{Environment.NewLine}Installed: {ForkVersion}{Environment.NewLine}GitHub: {remote}{Environment.NewLine}{Environment.NewLine}Use UPDATE TOOL to install it.";

            MessageBox.Show(this, status, "Porter version", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
        {
            MessageBox.Show(this, $"Could not reach the fork update service.{Environment.NewLine}{Environment.NewLine}{error.Message}", "Check version", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updating = false;
            SetUpdateButtons();
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
            string? remote = await ReadRemoteForkVersion();
            if (remote == null)
            {
                MessageBox.Show(this, "GitHub did not return a valid fork version.", "Update tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(remote, ForkVersion, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"Already up to date.{Environment.NewLine}{Environment.NewLine}Installed: {ForkVersion}", "Update tool", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult answer = MessageBox.Show(
                this,
                $"Update Porter from {ForkVersion} to {remote}?{Environment.NewLine}{Environment.NewLine}The new EXE will be downloaded from your GitHub fork, verified with its SHA-256 checksum, then installed after this window closes.",
                "Update tool",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine the running Porter executable path.");

            tempExe = Path.Combine(Path.GetTempPath(), $"PS4.FF.Porter-update-{Guid.NewGuid():N}.exe");
            string tempHash = tempExe + ".sha256";

            SetStatus("Updating the tool", $"Downloading {remote}");
            await DownloadFile(ForkExeUrl, tempExe);
            await DownloadFile(ForkHashUrl, tempHash);

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

            string expectedHash = ParseSha256(File.ReadAllText(tempHash));
            string actualHash = await ComputeSha256(tempExe);
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SHA-256 mismatch. Expected {expectedHash}, got {actualHash}.");

            try { File.Delete(tempHash); } catch { }

            string script = $"""
$ErrorActionPreference = 'Stop'
$target = {PsQuote(exePath)}
$temp = {PsQuote(tempExe)}
$targetPid = {Environment.ProcessId}

for ($i = 0; $i -lt 120; $i++) {{
    if ($null -eq (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) {{ break }}
    Start-Sleep -Milliseconds 250
}}

for ($i = 0; $i -lt 40; $i++) {{
    try {{
        [System.IO.File]::Move($temp, $target, $true)
        Start-Process -FilePath $target
        exit 0
    }}
    catch {{
        Start-Sleep -Milliseconds 500
    }}
}}

if (Test-Path -LiteralPath $temp) {{
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
}}
""";

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
        }
    }

    private static async Task<string?> ReadRemoteForkVersion()
    {
        using HttpResponseMessage response = await UpdateClient.GetAsync(ForkVersionUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            return null;
        string version = (await response.Content.ReadAsStringAsync()).Trim();
        return version.StartsWith("Fork ", StringComparison.OrdinalIgnoreCase) ? version : null;
    }

    private static async Task DownloadFile(string url, string path)
    {
        using HttpResponseMessage response = await UpdateClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output);
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
        bool enabled = !_running && !_clearing && !_updating;
        CheckVersionButton.IsEnabled = enabled;
        UpdateToolButton.IsEnabled = enabled;
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
        if (_firstRun || await Task.WhenAny(work, Task.Delay(400)) != work)
            PrepareOverlay.Visibility = Visibility.Visible;
        try
        {
            await work;
        }
        catch (Exception error)
        {
            Log.Append($"Preparing files failed: {error.Message}");
        }
        PrepareOverlay.Visibility = Visibility.Collapsed;
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
