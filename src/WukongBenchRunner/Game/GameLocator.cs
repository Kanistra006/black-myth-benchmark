using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WukongBenchRunner.Game;

/// <summary>Finds the Black Myth: Wukong Benchmark Tool installation through Steam.</summary>
public static partial class GameLocator
{
    public const int SteamAppId = 3132990;

    public static GamePaths Locate(string? overrideDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return GamePaths.FromInstallDir(overrideDir);

        var steam = FindSteamPath()
            ?? throw new InvalidOperationException("Steam installation not found. Pass --game-dir explicitly.");

        foreach (var library in EnumerateLibraries(steam))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var installDir = InstallDirRegex().Match(File.ReadAllText(manifest)).Groups[1].Value;
            var dir = Path.Combine(library, "steamapps", "common", installDir);
            if (Directory.Exists(dir)) return GamePaths.FromInstallDir(dir);
        }

        throw new InvalidOperationException(
            $"Benchmark Tool (Steam AppID {SteamAppId}) is not installed. Install it from Steam or pass --game-dir.");
    }

    private static string? FindSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return path is null ? null : Path.GetFullPath(path);
    }

    internal static IEnumerable<string> EnumerateLibraries(string steamPath)
    {
        yield return steamPath;
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in LibraryPathRegex().Matches(File.ReadAllText(vdf)))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"")]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRegex();
}

public sealed record GamePaths(string InstallDir)
{
    public string GameExe => Path.Combine(InstallDir, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");
    public string LauncherExe => Path.Combine(InstallDir, "b1_benchmark.exe");
    public string SavedDir => Path.Combine(InstallDir, "b1", "Saved");
    public string GameUserSettingsIni => Path.Combine(SavedDir, "Config", "Windows", "GameUserSettings.ini");

    public const string ProcessName = "b1-Win64-Shipping";

    public static GamePaths FromInstallDir(string dir)
    {
        var paths = new GamePaths(Path.GetFullPath(dir));
        if (!File.Exists(paths.GameExe))
            throw new FileNotFoundException($"Benchmark executable not found in '{dir}'.", paths.GameExe);
        return paths;
    }
}
