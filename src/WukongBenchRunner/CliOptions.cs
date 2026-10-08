namespace WukongBenchRunner;

public sealed record CliOptions
{
    public string? GameDir { get; init; }
    public string? OutputDir { get; init; }
    public bool RunCpu { get; init; } = true;
    public bool RunGpu { get; init; } = true;
    public bool DirectLaunch { get; init; }
    public bool KeepSettings { get; init; }
    public bool ShowHelp { get; init; }
    /// <summary>Window size for the CPU pass; null keeps the profile default.</summary>
    public (int Width, int Height)? CpuWindow { get; init; }
    public bool CpuBorderless { get; init; }
    public int? CpuScale { get; init; }
    /// <summary>Full ray tracing level for the GPU pass; "off" disables it, null keeps the automatic choice.</summary>
    public string? GpuRt { get; init; }

    public const string Usage = """
        WukongBenchRunner — runs Black Myth: Wukong Benchmark Tool twice (CPU- and GPU-bound settings)
        and prints a report.

        Usage: WukongBenchRunner [options]

          --game-dir <path>   Benchmark Tool install folder (default: found through Steam)
          --output <path>     Folder for report.md / report.json (default: results\<date-time>)
          --only <cpu|gpu>    Run only one of the passes
          --direct-launch     Start the game executable directly instead of via steam://
          --keep-settings     Do not restore the original benchmark settings afterwards
          --cpu-window <WxH|desktop>
                              Window size of the CPU pass (default 1280x720 window);
                              "desktop" runs it borderless at desktop resolution
          --cpu-scale <25-100>
                              Render scale of the CPU pass in percent (default 25)
          --gpu-rt <off|low|medium|ultra>
                              Full ray tracing level of the GPU pass (default: chosen automatically)
          -h, --help          Show this help
        """;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            o = args[i] switch
            {
                "--game-dir" => o with { GameDir = Value() },
                "--output" => o with { OutputDir = Value() },
                "--only" => Value().ToLowerInvariant() switch
                {
                    "cpu" => o with { RunGpu = false },
                    "gpu" => o with { RunCpu = false },
                    var v => throw new ArgumentException($"--only expects cpu or gpu, got '{v}'"),
                },
                "--direct-launch" => o with { DirectLaunch = true },
                "--keep-settings" => o with { KeepSettings = true },
                "--cpu-window" => Value() switch
                {
                    var v when v.Equals("desktop", StringComparison.OrdinalIgnoreCase) => o with { CpuBorderless = true },
                    var v => o with { CpuWindow = ParseSize(v) },
                },
                "--gpu-rt" => Value().ToLowerInvariant() switch
                {
                    var v and ("off" or "low" or "medium" or "ultra") => o with { GpuRt = v },
                    var v => throw new ArgumentException($"--gpu-rt expects off, low, medium or ultra, got '{v}'"),
                },
                "--cpu-scale" => int.TryParse(Value(), out var s) && s is >= 25 and <= 100
                    ? o with { CpuScale = s }
                    : throw new ArgumentException("--cpu-scale expects a number from 25 to 100"),
                "-h" or "--help" or "/?" => o with { ShowHelp = true },
                var a => throw new ArgumentException($"Unknown argument '{a}'"),
            };
        }
        return o;
    }

    private static (int, int) ParseSize(string v)
    {
        var parts = v.ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w >= 640 && h >= 360)
            return (w, h);
        throw new ArgumentException($"Expected a size like 1280x720, got '{v}'");
    }
}
