using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFPorter.Core;
using FFPorter.Core.Common.Fidelity;

namespace FFPorter.Core.T7.Port;

public sealed class T7ModPortOptions
{
    public required string PackageFolder { get; init; }
    public required string OutputFolder { get; init; }
    public required string WorkDirectory { get; init; }
    public required string Ps4LoaderDirectory { get; init; }
    public required string PcImage { get; init; }
    public required T7DonorLibrary Donors { get; init; }
    public required Workspace Workspace { get; init; }
    public Streams.T7Ps4StreamIndex? Ps4StreamIndex { get; init; }
    public IReadOnlyList<string>? PcStreamXPaks { get; init; }
    public T7PcShaderLibrary? ShaderLibrary { get; init; }
    public string? Acts { get; init; }
    public bool GscRecompile { get; init; }
    public bool Force { get; init; }
    public bool DonorFallbackForAllTypes { get; init; }
    public bool ConvertStreams { get; init; } = true;
    public bool ConvertSound { get; init; } = true;
    public bool ConvertMovies { get; init; } = true;
    public bool ApplyDelta { get; init; } = true;
    public IReadOnlyCollection<string>? Languages { get; init; }
    public Shaders.T7ShaderCompiler? ShaderCompiler { get; init; }
    public Action<string> Log { get; init; } = _ => { };
    public Action<FidelitySnapshot>? FidelityProgress { get; init; }
}

public sealed record T7ModPortResult(
    bool Success,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings,
    string ReportPath);

public static class T7ModPort
{
    private const int MaxPackageFiles = 50000;
    private static readonly string[] MetadataFiles = ["workshop.json", "previewimage.png", "loadingimage.png"];
    private static readonly HashSet<string> MovieExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".mov", ".avi", ".webm" };

    private static readonly HashSet<string> LooseAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iwi", ".dds", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff",
        ".wav", ".mp3", ".flac", ".ogg", ".wem", ".bnk",
        ".ma", ".mb", ".fbx", ".obj", ".lwo", ".xmodel_export", ".xanim_export", ".atr",
        ".gdt", ".zone", ".menu", ".csv", ".str", ".iwd", ".zip", ".7z", ".rar"
    };

    public static T7ModPortResult Run(T7ModPortOptions options)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.PackageFolder));
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutputFolder));
        string work = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.WorkDirectory));
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Mod package folder does not exist: {source}");
        if (IsWithin(output, source) || IsWithin(work, source))
            throw new IOException("Mod output and work folders must be outside the source package folder. The PC mod files will not be overwritten.");

        Directory.CreateDirectory(output);
        Directory.CreateDirectory(work);

        var outputs = new List<string>();
        var problems = new List<string>();
        var warnings = new List<string>();
        var unconverted = new List<string>();
        var zoneReports = new List<object>();
        List<string> files = [];
        bool scanTruncated = false;
        string reportPath = Path.Combine(output, "mod-port.json");

        void SaveReport()
        {
            var report = new
            {
                schema_version = 1,
                package = source,
                output,
                success = problems.Count == 0,
                generated_utc = DateTimeOffset.UtcNow,
                scanned_files = files.Select(p => Path.GetRelativePath(source, p)).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                scan_truncated = scanTruncated,
                zones = zoneReports,
                summary = new
                {
                    fastfile_groups = zoneReports.Count,
                    output_files = outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    unconverted_files = unconverted.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    problems = problems.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                },
                outputs = outputs.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => Path.GetRelativePath(output, p)).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                unconverted_files = unconverted.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                problems = problems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        T7ModPortResult Finish()
        {
            SaveReport();
            return new T7ModPortResult(
                problems.Count == 0,
                outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                problems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                reportPath);
        }

        (files, scanTruncated) = ScanFiles(source);
        if (scanTruncated)
            problems.Add($"Package scan stopped at {MaxPackageFiles:N0} files; the package report is incomplete.");

        string[] sourceFastfiles = files.Where(p => Path.GetExtension(p).Equals(".ff", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var pcFastfiles = new List<string>();
        var rejectedFastfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string fastfile in sourceFastfiles)
        {
            try
            {
                T7Header header = ReadHeader(fastfile);
                if (header.Platform != 0)
                {
                    rejectedFastfiles.Add(fastfile);
                    problems.Add($"{Path.GetRelativePath(source, fastfile)}: not a PC BO3 fastfile (platform is {header.PlatformName}); not converted.");
                    continue;
                }
                pcFastfiles.Add(fastfile);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                rejectedFastfiles.Add(fastfile);
                problems.Add($"{Path.GetRelativePath(source, fastfile)}: not a readable BO3 fastfile: {error.Message}");
            }
        }

        if (pcFastfiles.Count == 0)
        {
            problems.Add("No readable PC Black Ops III fastfiles were found in this mod package.");
            options.Log($"Mod conversion stopped: no valid PC BO3 fastfiles in '{source}'.");
            return Finish();
        }

        string[] duplicateZones = pcFastfiles
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        string[] duplicateXpaks = files
            .Where(p => Path.GetExtension(p).Equals(".xpak", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        string[] duplicateBanks = files
            .Where(p => Path.GetExtension(p).Equals(".sabl", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(p).Equals(".sabs", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateZones.Length > 0 || duplicateXpaks.Length > 0 || duplicateBanks.Length > 0)
        {
            if (duplicateZones.Length > 0)
                problems.Add("Package has fastfiles with duplicate names that would overwrite each other in the console output: " + string.Join(", ", duplicateZones));
            if (duplicateXpaks.Length > 0)
                problems.Add("Package has XPAKs with duplicate names that would overwrite each other in the console output: " + string.Join(", ", duplicateXpaks));
            if (duplicateBanks.Length > 0)
                problems.Add("Package has sound banks with duplicate names that would overwrite each other in the console output: " + string.Join(", ", duplicateBanks));
            options.Log("Mod conversion stopped before writing zones because package output names collide.");
            return Finish();
        }

        var pcSet = new HashSet<string>(pcFastfiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        string[] mainZones = pcFastfiles
            .Select(p => BaseOf(p, pcSet) ?? p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string main in mainZones)
        {
            string stem = Path.GetFileNameWithoutExtension(main);
            string relative = Path.GetRelativePath(source, main);
            string workKey = WorkKey(relative);
            string zoneWork = Path.Combine(work, workKey);
            var fidelity = new T7Fidelity(stem, options.FidelityProgress);
            fidelity.Prepare();

            options.Log($"Mod conversion: {relative}");
            T7MapPortResult result = T7MapPort.Run(new T7MapPortOptions
            {
                PcMapFastFile = main,
                OutputFolder = output,
                WorkDirectory = zoneWork,
                Ps4LoaderDirectory = options.Ps4LoaderDirectory,
                PcImage = options.PcImage,
                Donors = options.Donors,
                Workspace = options.Workspace,
                Ps4StreamIndex = options.Ps4StreamIndex,
                PcStreamXPaks = options.PcStreamXPaks,
                ShaderLibrary = options.ShaderLibrary,
                Acts = options.Acts,
                GscRecompile = options.GscRecompile,
                Force = options.Force,
                DonorFallbackForAllTypes = options.DonorFallbackForAllTypes,
                ConvertStreams = options.ConvertStreams,
                ConvertSound = options.ConvertSound,
                ConvertMovies = options.ConvertMovies,
                ApplyDelta = options.ApplyDelta,
                PackageRoot = source,
                PackageXPaks = files.Where(p => Path.GetExtension(p).Equals(".xpak", StringComparison.OrdinalIgnoreCase)).ToArray(),
                Languages = options.Languages,
                ShaderCompiler = options.ShaderCompiler,
                Log = message => options.Log($"[{Path.GetFileName(main)}] {message}"),
                Fidelity = fidelity,
            });

            outputs.AddRange(result.Outputs);
            problems.AddRange(result.Problems.Select(p => $"{relative}: {p}"));
            warnings.AddRange(result.Warnings.Select(p => $"{relative}: {p}"));
            zoneReports.Add(new
            {
                source = relative,
                success = result.Success,
                outputs = result.Outputs.Select(p => Path.GetRelativePath(output, p)).ToArray(),
                problems = result.Problems,
                warnings = result.Warnings,
                fidelity = result.Fidelity is { } snapshot
                    ? new
                    {
                        state = snapshot.State,
                        percent = snapshot.Percent,
                        grade = snapshot.Grade,
                        headline = snapshot.Headline,
                        dimensions = snapshot.Dimensions.Select(d => new
                        {
                            name = d.Name,
                            percent = d.Percent,
                            grade = d.Grade,
                            units = d.Units,
                            summary = d.Summary,
                            notes = d.Notes.Select(n => new { grade = n.Grade, text = n.Text }).ToArray(),
                        }).ToArray(),
                        problems = snapshot.Problems,
                    }
                    : null,
            });
        }

        foreach (string metadata in MetadataFiles)
        {
            string from = Path.Combine(source, metadata);
            string to = Path.Combine(output, metadata);
            if (!File.Exists(from))
                continue;
            try
            {
                if (!Path.GetFullPath(from).Equals(Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                    File.Copy(from, to, overwrite: true);
                outputs.Add(to);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{metadata}: could not copy package metadata: {error.Message}");
            }
        }

        var producedPaths = outputs.Where(File.Exists)
            .Select(path => NormalizeRelative(Path.GetRelativePath(output, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Produced(string relative) => producedPaths.Contains(NormalizeRelative(relative));

        foreach (string file in files)
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".ff", StringComparison.OrdinalIgnoreCase))
            {
                if (rejectedFastfiles.Contains(file) || IsFilteredLanguage(file, pcSet, options.Languages))
                    continue;
                if (!Produced(Path.GetFileName(file)))
                    AddUnconverted(file, "no PS4 fastfile was produced for this zone");
                continue;
            }

            if (extension.Equals(".gsc", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".csc", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
            {
                AddUnconverted(file, "loose source script was not injected into a PS4 fastfile; compiled ScriptParseTree assets inside converted fastfiles use the existing GSC conversion path");
                continue;
            }

            if (extension.Equals(".xpak", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sabl", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".sabs", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFilteredLanguageBank(file, options.Languages))
                    continue;
                string expected = extension.Equals(".sabl", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".sabs", StringComparison.OrdinalIgnoreCase)
                    ? SoundOutputRelative(source, file)
                    : Path.GetFileName(file);
                if (expected == null || !Produced(expected))
                    AddUnconverted(file, "sidecar payload was not converted; check its matching fastfile and conversion options");
                continue;
            }

            if (MovieExtensions.Contains(extension))
            {
                string expected = Path.Combine("video", Path.GetFileNameWithoutExtension(file) + ".mkv");
                if (!Produced(expected))
                    AddUnconverted(file, "movie was not prepared for this package; only movies discovered by the package conversion path count as converted");
                continue;
            }

            if (LooseAssetExtensions.Contains(extension))
                AddUnconverted(file, "loose PC source/resource file was not compiled or injected into a PS4 fastfile/XPAK");

            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".asi", StringComparison.OrdinalIgnoreCase))
                AddUnconverted(file, "PC executable/plugin is not a PS4 mod payload and cannot be carried over by fastfile conversion");
        }

        if (unconverted.Count > 0)
            problems.Add($"Package contains {unconverted.Count} unconverted source or sidecar file(s). See {Path.GetFileName(reportPath)}.");

        options.Log($"Mod conversion summary: {mainZones.Length} fastfile group(s), {outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()} output file(s), {problems.Distinct(StringComparer.OrdinalIgnoreCase).Count()} problem(s).");
        return Finish();

        void AddUnconverted(string file, string reason)
        {
            string text = $"{Path.GetRelativePath(source, file)}: {reason}";
            unconverted.Add(text);
            options.Log("UNCONVERTED " + text);
        }
    }

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string? SoundOutputRelative(string sourceRoot, string file)
    {
        string root = Path.GetFullPath(sourceRoot);
        string full = Path.GetFullPath(file);
        string? current = Path.GetDirectoryName(full);
        while (current != null && IsWithin(current, root))
        {
            if (Path.GetFileName(current).Equals("snd", StringComparison.OrdinalIgnoreCase))
                return NormalizeRelative(Path.Combine("snd", Path.GetRelativePath(current, full)));
            string? parent = Path.GetDirectoryName(current);
            if (parent == current)
                break;
            current = parent;
        }
        return null;
    }

    private static T7Header ReadHeader(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] bytes = new byte[T7Header.Size];
        int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (read != bytes.Length)
            throw new InvalidDataException("file is shorter than a T7 fastfile header");
        return T7Header.Parse(bytes);
    }

    private static string? BaseOf(string file, HashSet<string> pcFastfiles)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Length <= 3 || stem[2] != '_' || !char.IsAsciiLetter(stem[0]) || !char.IsAsciiLetter(stem[1]))
            return null;
        string baseFile = Path.Combine(Path.GetDirectoryName(file)!, stem[3..] + ".ff");
        return pcFastfiles.Contains(Path.GetFullPath(baseFile)) ? baseFile : null;
    }

    private static bool IsFilteredLanguage(string file, HashSet<string> pcFastfiles, IReadOnlyCollection<string>? languages)
    {
        if (!HasLanguageFilter(languages))
            return false;
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Length <= 3 || stem[2] != '_' || !char.IsAsciiLetter(stem[0]) || !char.IsAsciiLetter(stem[1]))
            return false;
        string baseFile = Path.Combine(Path.GetDirectoryName(file)!, stem[3..] + ".ff");
        return pcFastfiles.Contains(Path.GetFullPath(baseFile))
            && !IsWantedLanguage(stem[..2], languages);
    }

    private static bool IsFilteredLanguageBank(string file, IReadOnlyCollection<string>? languages)
    {
        if (!HasLanguageFilter(languages))
            return false;
        string language = Path.GetExtension(Path.GetFileNameWithoutExtension(file)).TrimStart('.');
        return language.Length == 2 && !IsWantedLanguage(language, languages);
    }

    private static bool HasLanguageFilter(IReadOnlyCollection<string>? languages) =>
        languages is { Count: > 0 } && !languages.Contains("all", StringComparer.OrdinalIgnoreCase);

    private static bool IsWantedLanguage(string language, IReadOnlyCollection<string>? languages) =>
        !HasLanguageFilter(languages) || languages!.Contains(language, StringComparer.OrdinalIgnoreCase);

    private static string WorkKey(string relativePath)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        string stem = Path.GetFileNameWithoutExtension(relativePath);
        string safeStem = string.Concat(stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return $"{safeStem}-{Convert.ToHexString(digest)[..12].ToLowerInvariant()}";
    }

    private static (List<string> Files, bool Truncated) ScanFiles(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] currentFiles;
            try
            {
                currentFiles = Directory.EnumerateFiles(current).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in currentFiles)
            {
                files.Add(Path.GetFullPath(file));
                if (files.Count >= MaxPackageFiles)
                    return (files, true);
            }

            foreach (string directory in SafeDirectories(current))
            {
                string leaf = Path.GetFileName(directory);
                if (leaf.Equals(".git", StringComparison.OrdinalIgnoreCase)
                    || leaf.Equals("cache", StringComparison.OrdinalIgnoreCase)
                    || leaf.Equals("zone_cache", StringComparison.OrdinalIgnoreCase)
                    || leaf.Equals("shadercache", StringComparison.OrdinalIgnoreCase)
                    || leaf.Equals("work", StringComparison.OrdinalIgnoreCase)
                    || leaf.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(directory);
            }
        }
        return (files, false);
    }

    private static string[] SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(directory =>
                {
                    try { return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0; }
                    catch (Exception) { return false; }
                })
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsWithin(string path, string folder)
    {
        string relative = Path.GetRelativePath(folder, path);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}
