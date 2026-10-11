namespace WukongBenchRunner;

public sealed record CliOptions
{
    public string? GameDir { get; init; }
    public string? OutputDir { get; init; }
    public bool RunCpu { get; init; } = true;
    public bool RunGpu { get; init; } = true;
    public bool DirectLaunch { get; init; }
    public bool KeepSettings { get; init; }
    public bool NoCountdown { get; init; }
    public bool ShowHelp { get; init; }
    public (int Width, int Height)? CpuWindow { get; init; }
    public bool CpuBorderless { get; init; }
    public int? CpuScale { get; init; }
    /// <summary>Full ray tracing level for the GPU pass; "off" disables it, null keeps the automatic choice.</summary>
    public string? GpuRt { get; init; }

    public const string Usage = """
        WukongBenchRunner — автоматически выполняет два прохода Black Myth: Wukong Benchmark Tool
        (CPU-тест и GPU-тест) и выводит отчёт.

        Использование: WukongBenchRunner [параметры]

          --game-dir <путь>   папка Benchmark Tool (по умолчанию находится через Steam)
          --output <путь>     папка для report.md / report.json (по умолчанию results\<дата-время>)
          --only <cpu|gpu>    выполнить только один проход
          --direct-launch     запускать exe игры напрямую, а не через steam://
          --keep-settings     не восстанавливать исходные настройки бенчмарка
          --no-countdown      не ждать 5 секунд перед стартом
          --cpu-window <ШxВ|desktop>
                              окно CPU-теста (по умолчанию 1280x720);
                              desktop — окно без рамок на весь экран
          --cpu-scale <25-100>
                              масштаб рендера CPU-теста в процентах (по умолчанию 25)
          --gpu-rt <off|low|medium|ultra>
                              уровень трассировки лучей в GPU-тесте (по умолчанию — по объёму видеопамяти)
          -h, --help          эта справка
        """;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Не указано значение для {args[i]}");
            o = args[i] switch
            {
                "--game-dir" => o with { GameDir = Value() },
                "--output" => o with { OutputDir = Value() },
                "--only" => Value().ToLowerInvariant() switch
                {
                    "cpu" => o with { RunGpu = false },
                    "gpu" => o with { RunCpu = false },
                    var v => throw new ArgumentException($"--only: ожидается cpu или gpu, получено '{v}'"),
                },
                "--direct-launch" => o with { DirectLaunch = true },
                "--keep-settings" => o with { KeepSettings = true },
                "--no-countdown" or "-y" or "--yes" => o with { NoCountdown = true },
                "--cpu-window" => Value() switch
                {
                    var v when v.Equals("desktop", StringComparison.OrdinalIgnoreCase) => o with { CpuBorderless = true },
                    var v => o with { CpuWindow = ParseSize(v) },
                },
                "--gpu-rt" => Value().ToLowerInvariant() switch
                {
                    var v and ("off" or "low" or "medium" or "ultra") => o with { GpuRt = v },
                    var v => throw new ArgumentException($"--gpu-rt: ожидается off, low, medium или ultra, получено '{v}'"),
                },
                "--cpu-scale" => int.TryParse(Value(), out var s) && s is >= 25 and <= 100
                    ? o with { CpuScale = s }
                    : throw new ArgumentException("--cpu-scale: ожидается число от 25 до 100"),
                "-h" or "--help" or "/?" => o with { ShowHelp = true },
                var a => throw new ArgumentException($"Неизвестный параметр '{a}'"),
            };
        }
        return o;
    }

    private static (int, int) ParseSize(string v)
    {
        var parts = v.ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w >= 640 && h >= 360)
            return (w, h);
        throw new ArgumentException($"--cpu-window: ожидается размер вида 1280x720, получено '{v}'");
    }
}
