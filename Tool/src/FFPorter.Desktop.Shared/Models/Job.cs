using System.IO;

namespace FFPorter.Desktop.Models;

public sealed class Job : Observable
{
    private string _state = RunStates.Ready, _statusText = "Ready", _stageText = "";
    private double _progress;
    private FidelityView? _report;

    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string MainFile { get; init; }
    public string? PackageFolder { get; init; }
    public int PackageFastFiles { get; init; }
    public IReadOnlyList<string> CompanionZones { get; init; } = [];
    public string CompanionSummary { get; init; } = "";
    public int Streams { get; init; }
    public int SoundBanks { get; init; }
    public long Length { get; init; }

    public string GameLabel => Edition.Current.GameLabel;
    public string GameName => Edition.Current.GameName;

    public string Codename => Edition.Current.Codename;

    public string Size => SizeText(Length);

    public static string SizeText(long length) =>
        length >= 1073741824 ? $"{length / 1073741824d:0.##} GB" : length >= 1048576 ? $"{length / 1048576d:0.#} MB" : $"{length / 1024d:0.#} KB";

    public string Detail
    {
        get
        {
            var parts = new List<string> { $"{GameName} {Kind.ToLowerInvariant()}" };
            if (PackageFolder is { Length: > 0 })
                parts.Add($"{PackageFastFiles} package fastfile(s)");
            if (CompanionZones.Count > 0 && CompanionSummary.Length > 0)
                parts.Add(CompanionSummary);
            if (Streams > 0)
                parts.Add($"{Streams} xpak");
            if (SoundBanks > 0)
                parts.Add($"{SoundBanks} sound bank{(SoundBanks == 1 ? "" : "s")}");
            return string.Join("  ·  ", parts);
        }
    }

    public string State { get => _state; set { if (Set(ref _state, value)) Raise(nameof(IsRunning)); } }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string StageText { get => _stageText; set => Set(ref _stageText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool IsRunning => State == RunStates.Running;

    public FidelityView? Report { get => _report; set { if (Set(ref _report, value)) Raise(nameof(HasScore)); } }
    public bool HasScore => Report != null;
}

public static class JobScanner
{
    private const int FolderDepth = 3;

    public static List<Job> Scan(IEnumerable<string> paths, Action<string> log)
    {
        Edition edition = Edition.Current;
        var files = new List<string>();
        foreach (string input in paths)
        {
            try
            {
                string path = Path.GetFullPath(input);
                if (Directory.Exists(path))
                    files.AddRange(FastFilesIn(path, FolderDepth));
                else if (File.Exists(path) && path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
                    files.Add(path);
                else
                    log($"Skipped {input}: not a .ff file or a folder");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                log($"Skipped {input}: {error.Message}");
            }
        }

        var mine = new List<string>();
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (edition.Accepts(file, out string? why))
                mine.Add(file);
            else
                log($"Skipped {file}: {why}");
        }
        return [.. edition.Jobs(mine)];
    }

    // Package discovery is intentionally separate from the normal drag/drop scanner:
    // mods can place zones deeper than usermap folders, but symlinks and caches are not traversed.
    public static List<Job> ScanPackage(string folder, Action<string> log)
    {
        Edition edition = Edition.Current;
        string root = Path.GetFullPath(folder);
        List<string> candidates = [.. PackageFastFiles(root, 50000, log)];
        var accepted = new List<string>();
        foreach (string file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (edition.Accepts(file, out string? why))
                accepted.Add(file);
            else
                log($"Skipped mod package fastfile {file}: {why}");
        }
        return [.. edition.Jobs(accepted)];
    }

    private static IEnumerable<string> PackageFastFiles(string root, int maxFiles, Action<string> log)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        int emitted = 0;
        while (pending.Count > 0 && emitted < maxFiles)
        {
            string current = pending.Pop();
            string[] fastfiles;
            try
            {
                fastfiles = Directory.EnumerateFiles(current, "*.ff")
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log($"Could not scan mod package directory '{current}': {error.Message}");
                continue;
            }

            foreach (string file in fastfiles)
            {
                yield return Path.GetFullPath(file);
                if (++emitted >= maxFiles)
                {
                    log($"Mod package queue scan reached its {maxFiles:N0}-fastfile limit; the converter's package report will check the full package limit.");
                    yield break;
                }
            }

            string[] directories;
            try { directories = Directory.EnumerateDirectories(current).ToArray(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log($"Could not scan mod package subdirectories in '{current}': {error.Message}");
                continue;
            }

            foreach (string child in directories)
            {
                string name = Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("cache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("zone_cache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("shadercache", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("work", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch (Exception) { continue; }
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> FastFilesIn(string folder, int depth)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.ff"))
            yield return file;
        if (depth <= 0)
            yield break;
        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            foreach (string file in FastFilesIn(child, depth - 1))
                yield return file;
        }
    }
}
