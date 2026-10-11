using System.Text.Json;
using System.Text.Json.Serialization;

namespace WukongBenchRunner.Results;

/// <summary>
/// Raw contents of a file the Benchmark Tool writes to
/// <c>%TEMP%\b1\BenchMarkHistory\Tool\&lt;unix-time&gt;</c> after every pass.
/// The file is JSON without an extension: a summary block plus one record per rendered frame.
/// Property names match the game's spelling (including "BenckMarkVersion").
/// </summary>
public sealed class BenchmarkHistoryFile
{
    public long TimeStamp { get; set; }
    [JsonPropertyName("BenckMarkVersion")] public int BenchmarkVersion { get; set; }

    public double FPSAvg { get; set; }
    public double FPSMax { get; set; }
    public double FPSMin { get; set; }
    /// <summary>Shown in the game UI as "5th percentile" (95% of frames were faster).</summary>
    public double FPS95 { get; set; }
    public double CPUAvg { get; set; }
    public double GPUAvg { get; set; }
    public double GPULeak { get; set; }
    /// <summary>Peak VRAM used by the game, GB.</summary>
    public double VideoMem { get; set; }

    public string? GameVer { get; set; }
    public string? SysVer { get; set; }
    public string? CPUModel { get; set; }
    public string? GPUModel { get; set; }
    public string? GpuDriverVer { get; set; }
    public string? VideoMemSize { get; set; }
    public string? SysMem { get; set; }

    // Settings the pass actually ran with, as reported by the game itself.
    public int ScreenMode { get; set; }
    public string? ScreenResolution { get; set; }
    public int QualityLevel { get; set; }
    public int ImageQuality { get; set; }
    public int ViewDistance { get; set; }
    public int AntiAliasing { get; set; }
    public int PostProcessing { get; set; }
    public int ShadowQuality { get; set; }
    public int TextureQuality { get; set; }
    public int MaterialQuality { get; set; }
    public int VegetationQuality { get; set; }
    public int MotionBlur { get; set; }
    /// <summary>Full ray tracing level: 0 = off, 1 = Low, 2 = Medium, 3 = Ultra.</summary>
    public int Rtx { get; set; }
    /// <summary>Upscaler, despite the name: 0 = FSR, 1 = XeSS, 2 = DLSS, 3 = TSR.</summary>
    public int Dlss { get; set; }
    public int InsertFrame { get; set; }
    public int Dx12 { get; set; }

    public List<FrameRecord> Records { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static BenchmarkHistoryFile Parse(string json) =>
        JsonSerializer.Deserialize<BenchmarkHistoryFile>(json, Options)
        ?? throw new InvalidDataException("Файл результатов бенчмарка пуст.");

    public static BenchmarkHistoryFile Load(string path) => Parse(File.ReadAllText(path));
}

public sealed class FrameRecord
{
    /// <summary>Unix time in milliseconds.</summary>
    public long TimeStamp { get; set; }
    public double FrameRate { get; set; }
    public double CPUUsage { get; set; }
    public double GPUUsage { get; set; }
    /// <summary>Time the CPU (game + render threads) spent on the frame, ms.</summary>
    public double CPUFrameTime { get; set; }
    /// <summary>Time the GPU spent on the frame, ms.</summary>
    public double GPUFrameTime { get; set; }
    public double VideoMemoryUsage { get; set; }
}
