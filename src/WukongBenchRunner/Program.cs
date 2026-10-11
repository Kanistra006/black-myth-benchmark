using System.Globalization;
using System.Text;
using WukongBenchRunner;
using WukongBenchRunner.Automation;
using WukongBenchRunner.Game;
using WukongBenchRunner.Hardware;
using WukongBenchRunner.Report;
using WukongBenchRunner.Settings;

Console.OutputEncoding = Encoding.UTF8;

var exitCode = Run(args);
ConsoleHost.PauseIfOwnWindow();
return exitCode;

static int Run(string[] args)
{
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
        if (!cts.IsCancellationRequested)
            Log.Warn("Отмена... игра будет закрыта, исходные настройки бенчмарка восстановлены.");
        cts.Cancel();
    };

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
    if (!File.Exists(paths.GameUserSettingsIni))
    {
        Log.Error("Не найден файл настроек бенчмарка. Запустите Black Myth: Wukong Benchmark Tool один раз " +
                  "из Steam (он создаст настройки и скомпилирует шейдеры), закройте его и повторите.");
        return 1;
    }

    Log.Info("Собираю характеристики компьютера...");
    var system = SystemInfoCollector.Collect();
    var primary = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
    var profiles = BuildProfiles(cli, system);

    PrintPlan(paths, system, profiles);
    if (!Countdown(cli.NoCountdown ? 0 : 5, cts.Token))
    {
        Log.Warn("Запуск отменён, ничего не изменено.");
        return 130;
    }

    var started = DateTime.Now;
    var outputDir = Path.GetFullPath(cli.OutputDir ??
        Path.Combine("results", started.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)));
    Directory.CreateDirectory(outputDir);

    var backup = Path.Combine(outputDir, "GameUserSettings.backup.ini");
    File.Copy(paths.GameUserSettingsIni, backup, overwrite: true);

    var runner = new BenchmarkRunner(paths, new RunnerOptions { LaunchViaSteam = !cli.DirectLaunch });
    var passes = new List<PassResult>();
    var exitCode = 0;
    try
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            Console.WriteLine();
            Log.Step($"Проход {i + 1}/{profiles.Count}: {profile.Name}-тест");
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
                Log.Warn($"{e.Message} Повторяю проход с нового запуска игры...");
                result = runner.Run(profile, w, h, cts.Token);
            }
            passes.Add(result);
            Log.Step($"{profile.Name}-тест: средний FPS {result.Metrics.AvgFps}, 5-й перцентиль {result.Metrics.Percentile5Fps}");
        }
    }
    catch (OperationCanceledException)
    {
        Log.Warn("Отменено.");
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
            Log.Info("Исходные настройки бенчмарка восстановлены.");
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
    Log.Step($"Отчёт сохранён: {outputDir}");
    return exitCode;
}

static List<BenchmarkProfile> BuildProfiles(CliOptions cli, SystemInfo system)
{
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
        profiles.Add(gpu with { RayTracingReason = rt.Reason });
    }
    return profiles;
}

static void PrintPlan(GamePaths paths, SystemInfo system, IReadOnlyList<BenchmarkProfile> profiles)
{
    Console.WriteLine();
    Log.Step("WukongBenchRunner — автоматический запуск Black Myth: Wukong Benchmark Tool");
    Log.Info($"Бенчмарк: {paths.InstallDir}");
    Log.Info($"Процессор: {system.Cpu}");
    var gpu = system.Gpus.LastOrDefault();
    if (gpu is not null) Log.Info($"Видеокарта: {gpu.Name}");
    foreach (var p in profiles)
    {
        var rt = p.RayTracing ? $"трассировка лучей: {ReportBuilder.RtName(p.RayTracingLevel)}" : "без трассировки лучей";
        Log.Info(p.Name == "CPU"
            ? $"  {p.Name}-тест: пресет «{ReportBuilder.QualityName(p.Quality)}», рендер {p.ResolutionScale}% — нагрузка на процессор"
            : $"  {p.Name}-тест: пресет «{ReportBuilder.QualityName(p.Quality)}», рендер {p.ResolutionScale}%, {rt} — нагрузка на видеокарту");
    }
    Log.Info($"Займёт около {profiles.Count * 4} минут. Игра будет запускаться и управляться автоматически.");
    if (system.OnBattery)
        Log.Warn($"Питание {system.PowerSource}. От батареи ноутбук работает намного медленнее — подключите зарядку.");
    Log.Warn("Не трогайте мышь и клавиатуру и не блокируйте экран до окончания теста. Отмена — Ctrl+C.");
}

/// <summary>Gives the user a few seconds to cancel before the game is touched.</summary>
static bool Countdown(int seconds, CancellationToken ct)
{
    if (seconds <= 0 || Console.IsOutputRedirected) return !ct.IsCancellationRequested;
    for (var s = seconds; s > 0; s--)
    {
        Console.Write($"\rСтарт через {s} с... ");
        if (ct.WaitHandle.WaitOne(1000))
        {
            Console.WriteLine();
            return false;
        }
    }
    Console.WriteLine("\rСтарт!              ");
    return true;
}
