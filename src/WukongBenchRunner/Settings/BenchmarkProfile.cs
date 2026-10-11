namespace WukongBenchRunner.Settings;

/// <summary>Values stored by the game for quality options (UISettingData uses 1..5, sg.* uses 0..4).</summary>
public enum Quality
{
    Low = 1,
    Medium = 2,
    High = 3,
    VeryHigh = 4,
    Cinematic = 5,
}

/// <summary>Values of UISettingData "SuperResolutionSampling".</summary>
public enum Upscaler
{
    Fsr = 0,
    XeSS = 1,
    Dlss = 2,
    Tsr = 3,
}

/// <summary>Values of UISettingData "RtxLevel" (full ray tracing quality).</summary>
public enum RayTracingLevel
{
    Low = 1,
    Medium = 2,
    Ultra = 3,
}

/// <summary>Values of UISettingData "ScreenMode" (same numbering as Unreal's EWindowMode).</summary>
public enum DisplayMode
{
    Fullscreen = 0,
    Borderless = 1,
    Windowed = 2,
}

public sealed record BenchmarkProfile
{
    public required string Name { get; init; }
    public DisplayMode Display { get; init; } = DisplayMode.Borderless;
    /// <summary>Window size for <see cref="DisplayMode.Windowed"/>; ignored otherwise (desktop resolution is used).</summary>
    public (int Width, int Height)? WindowSize { get; init; }
    public required Quality Quality { get; init; }
    /// <summary>Render resolution as a percentage of the output resolution (game allows 25..100).</summary>
    public required int ResolutionScale { get; init; }
    public required Upscaler Upscaler { get; init; }
    public required bool FrameGeneration { get; init; }
    public required bool RayTracing { get; init; }
    public required RayTracingLevel RayTracingLevel { get; init; }
    /// <summary>Why this ray tracing setting was chosen (shown in the report).</summary>
    public string? RayTracingReason { get; init; }

    /// <summary>
    /// CPU pass: everything that scales with pixels or GPU features is at its minimum,
    /// so the GPU finishes each frame quickly and the frame rate is set by the CPU.
    /// </summary>
    public static BenchmarkProfile Cpu => new()
    {
        Name = "CPU",
        Display = DisplayMode.Windowed,
        WindowSize = (1280, 720),
        Quality = Quality.Low,
        ResolutionScale = 25,
        Upscaler = Upscaler.Fsr,
        FrameGeneration = false,
        RayTracing = false,
        RayTracingLevel = RayTracingLevel.Low,
    };

    /// <summary>
    /// GPU pass: native resolution without upscaling, the highest preset and full ray tracing at the
    /// highest level that still fits into VRAM (see <see cref="GpuProfileSelector"/>).
    /// </summary>
    public static BenchmarkProfile Gpu(bool rayTracing, RayTracingLevel level) => new()
    {
        Name = "GPU",
        Quality = Quality.Cinematic,
        ResolutionScale = 100,
        Upscaler = Upscaler.Tsr,
        FrameGeneration = false,
        RayTracing = rayTracing,
        RayTracingLevel = level,
    };
}
