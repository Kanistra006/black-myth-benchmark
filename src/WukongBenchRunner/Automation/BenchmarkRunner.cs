using System.Diagnostics;
using WukongBenchRunner.Game;
using WukongBenchRunner.Hardware;
using WukongBenchRunner.Results;
using WukongBenchRunner.Settings;

namespace WukongBenchRunner.Automation;

public sealed record RunnerOptions
{
    /// <summary>Max time from launch until the benchmark scene starts (first launch compiles shaders).</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(15);
    /// <summary>Max time for the benchmark pass itself (it normally takes about 2.5 minutes).</summary>
    public TimeSpan PassTimeout { get; init; } = TimeSpan.FromMinutes(8);
    /// <summary>Launch through Steam (recommended) or start the game executable directly.</summary>
    public bool LaunchViaSteam { get; init; } = true;
}

public sealed record PassResult(
    BenchmarkProfile Profile,
    int OutputWidth,
    int OutputHeight,
    string HistoryFile,
    BenchmarkHistoryFile Raw,
    PassMetrics Metrics,
    LoadSummary Load);

/// <summary>Runs one benchmark pass: applies settings, launches the game, starts the benchmark, collects the result.</summary>
public sealed class BenchmarkRunner(GamePaths paths, RunnerOptions options)
{
    // Positions in the game's 1280x720 UI layout (measured from screenshots).
    private const double MenuBenchmarkX = 147, MenuBenchmarkY = 324;   // "Benchmark" item of the main menu
    private const double DialogConfirmX = 502, DialogConfirmY = 424;   // "Confirm" button of the start dialog

    public static string HistoryDir =>
        Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");

    public PassResult Run(BenchmarkProfile profile, int outputWidth, int outputHeight, CancellationToken ct)
    {
        CloseGame();

        Log.Step($"[{profile.Name}] Applying settings to {paths.GameUserSettingsIni}");
        var ini = IniDocument.Load(paths.GameUserSettingsIni);
        GameSettingsWriter.Apply(ini, profile, outputWidth, outputHeight);
        ini.Save(paths.GameUserSettingsIni);

        var knownResults = ListHistoryFiles();
        try
        {
            Launch();
            var process = WaitForGameWindow(ct);
            StartBenchmark(process, profile, ct);

            string file;
            LoadSummary load;
            using (var monitor = new LoadMonitor())
            {
                file = WaitForResult(process, knownResults, ct);
                load = monitor.Stop();
            }

            Log.Step($"[{profile.Name}] Result file: {file}");
            var raw = ReadResultFile(file, ct);
            return new PassResult(profile, outputWidth, outputHeight, file, raw, PassMetrics.From(raw), load);
        }
        finally
        {
            CloseGame();
        }
    }

    private void Launch()
    {
        if (options.LaunchViaSteam)
        {
            // Launching through Steam avoids the "launch with custom parameters" prompt
            // Steam shows when the game's bootstrapper is started directly.
            Log.Info($"Launching via steam://rungameid/{GameLocator.SteamAppId}");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{GameLocator.SteamAppId}") { UseShellExecute = true });
        }
        else
        {
            Log.Info($"Launching {paths.GameExe}");
            Process.Start(new ProcessStartInfo(paths.GameExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(paths.GameExe)! });
        }
    }

    private Process WaitForGameWindow(CancellationToken ct)
    {
        Log.Info("Waiting for the game window...");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromMinutes(3))
        {
            ct.ThrowIfCancellationRequested();
            var p = Process.GetProcessesByName(GamePaths.ProcessName).FirstOrDefault();
            if (p is not null)
            {
                p.Refresh();
                if (p.MainWindowHandle != 0) return p;
            }
            Thread.Sleep(1000);
        }
        throw new TimeoutException("The game window did not appear within 3 minutes. Is Steam running and logged in?");
    }

    /// <summary>
    /// Drives the menus until the benchmark scene starts:
    /// any key (title screen) → "Benchmark" in the main menu → "Confirm" in the dialog.
    /// The sequence is repeated because the first launch may spend minutes compiling shaders and input is
    /// ignored until the title screen is up.
    /// <para>
    /// Start detection: after "Confirm" the game switches to a pure black loading screen and rewrites
    /// GameUserSettings.ini. While it boots the game also writes that file and shows ~20 s of black frames
    /// (and a freshly created window can flash white), so clicking only begins once the window has shown a
    /// normal picture — title, menu or shader compilation — for several consecutive checks.
    /// </para>
    /// </summary>
    private void StartBenchmark(Process process, BenchmarkProfile profile, CancellationToken ct)
    {
        Log.Step($"[{profile.Name}] Starting the benchmark from the main menu...");
        var sw = Stopwatch.StartNew();
        var attempt = 0;
        var brightStreak = 0;
        while (sw.Elapsed < options.StartTimeout)
        {
            ct.ThrowIfCancellationRequested();
            EnsureRunning(process);
            var hwnd = process.MainWindowHandle;
            GameInput.BringToFront(hwnd);

            if (brightStreak < 3)
            {
                brightStreak = ScreenProbe.MeanLuma(hwnd) > ScreenProbe.BrightThreshold ? brightStreak + 1 : 0;
                Sleep(1000, ct);
                continue;
            }

            attempt++;
            GameInput.ParkCursor(hwnd);
            GameInput.PressKey(GameInput.VK_E);                // dismisses "Press any key"
            Sleep(1500, ct);
            GameInput.ClickUi(hwnd, MenuBenchmarkX, MenuBenchmarkY, UiAnchor.Left);
            Sleep(1500, ct);
            var iniBefore = File.GetLastWriteTimeUtc(paths.GameUserSettingsIni);
            GameInput.ClickUi(hwnd, DialogConfirmX, DialogConfirmY, UiAnchor.Center);
            GameInput.ParkCursor(hwnd);

            var darkFrames = 0;
            var iniWritten = false;
            for (var i = 0; i < 12; i++)
            {
                Sleep(700, ct);
                if (ScreenProbe.MeanLuma(hwnd) < ScreenProbe.DarkThreshold) darkFrames++;
                iniWritten |= File.GetLastWriteTimeUtc(paths.GameUserSettingsIni) > iniBefore;
                if (darkFrames > 0 && iniWritten || darkFrames >= 4)
                {
                    Log.Info($"Benchmark started (attempt {attempt}, {sw.Elapsed.TotalSeconds:F0}s after the window appeared).");
                    return;
                }
            }

            if (attempt % 5 == 0)
                Log.Info($"Still waiting for the main menu ({sw.Elapsed.TotalSeconds:F0}s; the first launch compiles shaders)...");
        }
        throw new TimeoutException($"Could not start the benchmark within {options.StartTimeout.TotalMinutes:F0} minutes.");
    }

    private string WaitForResult(Process process, HashSet<string> knownResults, CancellationToken ct)
    {
        Log.Info("Benchmark is running, waiting for results (about 2-3 minutes)...");
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        while (sw.Elapsed < options.PassTimeout)
        {
            ct.ThrowIfCancellationRequested();
            var fresh = ListHistoryFiles().Except(knownResults).OrderBy(f => f).LastOrDefault();
            if (fresh is not null) return fresh;
            EnsureRunning(process);

            if (sw.Elapsed - lastReport >= TimeSpan.FromSeconds(30))
            {
                lastReport = sw.Elapsed;
                Log.Info($"  ...{sw.Elapsed.TotalSeconds:F0}s");
            }
            Sleep(2000, ct);
        }
        throw new TimeoutException($"No result file appeared in {HistoryDir} within {options.PassTimeout.TotalMinutes:F0} minutes.");
    }

    /// <summary>The game may still be writing the file when it first appears; retry until it parses.</summary>
    private static BenchmarkHistoryFile ReadResultFile(string path, CancellationToken ct)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                Sleep(1500, ct);
                return BenchmarkHistoryFile.Load(path);
            }
            catch (Exception e) when (i < 10 && e is IOException or System.Text.Json.JsonException)
            {
                // File not complete yet.
            }
        }
    }

    private static HashSet<string> ListHistoryFiles() =>
        Directory.Exists(HistoryDir)
            ? Directory.GetFiles(HistoryDir).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static void EnsureRunning(Process process)
    {
        if (process.HasExited)
            throw new InvalidOperationException($"The game exited unexpectedly (exit code {process.ExitCode}).");
    }

    /// <summary>Closes the game and its bootstrapper. Results are already on disk, so a hard stop is safe.</summary>
    public static void CloseGame()
    {
        foreach (var name in new[] { GamePaths.ProcessName, "b1_benchmark" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    Log.Info($"Closing {name} (pid {p.Id})");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(15000);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        }
    }

    private static void Sleep(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }
}
