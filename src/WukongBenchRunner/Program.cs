using System.Globalization;
using System.Text;
using WukongBenchRunner;
using WukongBenchRunner.Automation;
using WukongBenchRunner.Game;
using WukongBenchRunner.Hardware;
using WukongBenchRunner.Report;
using WukongBenchRunner.Settings;

Console.OutputEncoding = Encoding.UTF8;

CliOptions cli;
try
{
    cli = CliOptions.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}
if (cli.ShowHelp)
{
    Console.WriteLine(CliOptions.Usage);
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.Warn("Cancelling... the game will be closed and your settings restored.");
    cts.Cancel();
};

// Keep the PC and display awake while the benchmark runs.
NativeKeepAwake.Enable();

GamePaths paths;
try
{
    paths = GameLocator.Locate(cli.GameDir);
}
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException)
{
    Log.Error(e.Message);
    return 1;
}
Log.Step($"Benchmark Tool: {paths.InstallDir}");

var started = DateTime.Now;
var outputDir = Path.GetFullPath(cli.OutputDir ?? Path.Combine("results", started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)));
Directory.CreateDirectory(outputDir);

Log.Step("Collecting system information...");
var system = SystemInfoCollector.Collect();
var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
if (!system.PowerSource.StartsWith("AC", StringComparison.Ordinal))
    Log.Warn($"Power source: {system.PowerSource}. Laptops are much slower on battery; connect the charger for meaningful results.");

var profiles = new List<BenchmarkProfile>();
if (cli.RunCpu)
{
    var cpu = BenchmarkProfile.Cpu;
    if (cli.CpuBorderless) cpu = cpu with { Display = DisplayMode.Borderless, WindowSize = null };
    if (cli.CpuWindow is { } size) cpu = cpu with { Display = DisplayMode.Windowed, WindowSize = size };
    if (cli.CpuScale is { } scale) cpu = cpu with { ResolutionScale = scale };
    profiles.Add(cpu);
}
if (cli.RunGpu)
{
    var rt = GpuProfileSelector.ChooseRayTracing(system.Gpus);
    var gpu = BenchmarkProfile.Gpu(rt.RayTracing, rt.Level);
    if (cli.GpuRt is { } forced)
    {
        gpu = forced == "off"
            ? gpu with { RayTracing = false }
            : gpu with
            {
                RayTracing = true,
                RayTracingLevel = forced switch { "low" => RayTracingLevel.Low, "medium" => RayTracingLevel.Medium, _ => RayTracingLevel.Ultra },
            };
        rt = rt with { Reason = "задано параметром --gpu-rt" };
    }
    Log.Info($"GPU pass ray tracing: {(gpu.RayTracing ? gpu.RayTracingLevel.ToString() : "off")} ({rt.Reason})");
    profiles.Add(gpu with { RayTracingReason = rt.Reason });
}

// Back up the user's settings; they are restored whatever happens.
var backup = Path.Combine(outputDir, "GameUserSettings.backup.ini");
var hadSettings = File.Exists(paths.GameUserSettingsIni);
if (!hadSettings)
{
    Log.Error($"{paths.GameUserSettingsIni} not found. Start the Benchmark Tool once from Steam so it creates its settings, then run this tool again.");
    return 1;
}
File.Copy(paths.GameUserSettingsIni, backup, overwrite: true);
Log.Info($"Settings backed up to {backup}");

var runner = new BenchmarkRunner(paths, new RunnerOptions { LaunchViaSteam = !cli.DirectLaunch });
var passes = new List<PassResult>();
var exitCode = 0;
try
{
    foreach (var profile in profiles)
    {
        var (w, h) = profile.Display == DisplayMode.Windowed && profile.WindowSize is { } ws
            ? ws
            : (primary.Width, primary.Height);
        PassResult result;
        try
        {
            result = runner.Run(profile, w, h, cts.Token);
        }
        catch (TimeoutException e)
        {
            // A missed click or a slow first start: one clean retry from a fresh game launch.
            Log.Warn($"[{profile.Name}] {e.Message} Retrying the pass once...");
            result = runner.Run(profile, w, h, cts.Token);
        }
        passes.Add(result);
        Log.Step($"[{profile.Name}] Average {result.Metrics.AvgFps} FPS, 5th percentile {result.Metrics.Percentile5Fps} FPS");
    }
}
catch (OperationCanceledException)
{
    Log.Warn("Cancelled.");
    exitCode = 130;
}
catch (Exception e) when (e is TimeoutException or InvalidOperationException or IOException)
{
    Log.Error(e.Message);
    exitCode = 1;
}
finally
{
    BenchmarkRunner.CloseGame();
    if (!cli.KeepSettings)
    {
        File.Copy(backup, paths.GameUserSettingsIni, overwrite: true);
        Log.Info("Your original benchmark settings have been restored.");
    }
}

if (passes.Count == 0) return exitCode == 0 ? 1 : exitCode;

var report = new BenchmarkReport(
    StartedAt: started,
    FinishedAt: DateTime.Now,
    GameVersion: passes[0].Raw.GameVer ?? "?",
    OutputResolution: $"{primary.Width}x{primary.Height}",
    System: system,
    Passes: passes.Select(ReportBuilder.FromPass).ToList());

ReportWriter.WriteConsole(report);
File.WriteAllText(Path.Combine(outputDir, "report.md"), ReportWriter.ToMarkdown(report), new UTF8Encoding(false));
File.WriteAllText(Path.Combine(outputDir, "report.json"), ReportWriter.ToJson(report), new UTF8Encoding(false));
foreach (var p in passes)
    File.Copy(p.HistoryFile, Path.Combine(outputDir, $"{p.Profile.Name.ToLowerInvariant()}-pass.raw.json"), overwrite: true);
Log.Step($"Report saved to {outputDir}");
return exitCode;
