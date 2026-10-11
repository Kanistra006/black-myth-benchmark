using WukongBenchRunner.Automation;
using WukongBenchRunner.Hardware;
using WukongBenchRunner.Results;
using WukongBenchRunner.Settings;

namespace WukongBenchRunner.Report;

/// <summary>Everything the tool reports after a run; serialised as-is to report.json.</summary>
public sealed record BenchmarkReport(
    DateTime StartedAt,
    DateTime FinishedAt,
    string GameVersion,
    string OutputResolution,
    SystemInfo System,
    IReadOnlyList<PassReport> Passes);

public sealed record PassReport(
    string Name,
    string Purpose,
    IReadOnlyList<SettingRow> Settings,
    PassMetrics Metrics,
    LoadSummary Load,
    string HistoryFile);

public sealed record SettingRow(string Name, string Value);

public static class ReportBuilder
{
    public static PassReport FromPass(PassResult pass)
    {
        var p = pass.Profile;
        var raw = pass.Raw;
        var (outputWidth, outputHeight) = (pass.OutputWidth, pass.OutputHeight);
        var renderW = (int)Math.Round(outputWidth * p.ResolutionScale / 100.0);
        var renderH = (int)Math.Round(outputHeight * p.ResolutionScale / 100.0);

        // Values in brackets are what the game itself reported in the result file,
        // which confirms the settings were really applied.
        var settings = new List<SettingRow>
        {
            new("Режим отображения", DisplayName(p.Display)),
            new("Разрешение вывода", $"{outputWidth}x{outputHeight} (игра: {raw.ScreenResolution})"),
            new("Масштаб рендера", $"{p.ResolutionScale}% → {renderW}x{renderH} (игра: {raw.ImageQuality}%)"),
            // The result file stores the upscaler in a field called "Dlss" (same numbering as SuperResolutionSampling).
            new("Апскейлер", $"{UpscalerName(p.Upscaler)} (игра: {UpscalerName((Upscaler)raw.Dlss)})"),
            new("Пресет качества", $"{QualityName(p.Quality)} (игра: {QualityName((Quality)raw.QualityLevel)})"),
            new("Детализация, тени, текстуры, эффекты, растительность, GI, отражения",
                $"{QualityName(p.Quality)} (игра: {DetailSummary(raw)})"),
            // The result file stores the full ray tracing level in "Rtx": 0 = off, 1..3 = Low..Ultra.
            new("Полная трассировка лучей",
                RtSetting(p.RayTracing, p.RayTracingLevel) + $" (игра: {RtSetting(raw.Rtx > 0, (RayTracingLevel)raw.Rtx)})"),
            new("Генерация кадров", $"{OnOff(p.FrameGeneration)} (игра: {OnOff(raw.InsertFrame == 1)})"),
            new("Вертикальная синхронизация / лимит FPS", "Выкл. / без лимита"),
        };

        if (p.RayTracingReason is not null)
            settings.Insert(settings.FindIndex(r => r.Name == "Полная трассировка лучей") + 1,
                new("Выбор уровня трассировки", p.RayTracingReason));

        if (GpuProfileSelector.VramSaturated(raw.VideoMem, ParseGb(raw.VideoMemSize)))
            settings.Add(new("Внимание", $"использовано {raw.VideoMem:0.0} ГБ из {raw.VideoMemSize} видеопамяти — " +
                                         "возможно переполнение, результат может быть занижен"));

        var purpose = p.Name == "CPU"
            ? "Минимальная нагрузка на GPU: FPS ограничивается процессором"
            : "Максимальная нагрузка на GPU: нативное разрешение, кинематографический пресет, трассировка лучей";

        return new PassReport(p.Name, purpose, settings, pass.Metrics, pass.Load, pass.HistoryFile);
    }

    private static string OnOff(bool on) => on ? "Вкл." : "Выкл.";

    /// <summary>The per-option quality values the game reported; one name if they all match.</summary>
    private static string DetailSummary(Results.BenchmarkHistoryFile raw)
    {
        int[] values = [raw.ViewDistance, raw.AntiAliasing, raw.PostProcessing, raw.ShadowQuality,
                        raw.TextureQuality, raw.MaterialQuality, raw.VegetationQuality];
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1
            ? QualityName((Quality)distinct[0])
            : string.Join(", ", values.Select(v => QualityName((Quality)v)));
    }

    private static string RtSetting(bool on, RayTracingLevel level) => on ? $"Вкл., {RtName(level)}" : "Выкл.";

    /// <summary>Parses the game's "8GB" style memory size.</summary>
    internal static double? ParseGb(string? s) =>
        s is not null && double.TryParse(s.Replace("GB", "", StringComparison.OrdinalIgnoreCase).Trim(),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    public static string DisplayName(DisplayMode d) => d switch
    {
        DisplayMode.Fullscreen => "Полный экран",
        DisplayMode.Borderless => "Окно без рамок",
        DisplayMode.Windowed => "Оконный",
        _ => d.ToString(),
    };

    public static string QualityName(Quality q) => q switch
    {
        Quality.Low => "Низкое",
        Quality.Medium => "Среднее",
        Quality.High => "Высокое",
        Quality.VeryHigh => "Очень высокое",
        Quality.Cinematic => "Кинематографическое",
        _ => $"Пользовательское ({(int)q})",
    };

    public static string UpscalerName(Upscaler u) => u switch
    {
        Upscaler.Fsr => "AMD FSR",
        Upscaler.XeSS => "Intel XeSS",
        Upscaler.Dlss => "NVIDIA DLSS",
        Upscaler.Tsr => "Unreal TSR",
        _ => u.ToString(),
    };

    public static string RtName(RayTracingLevel l) => l switch
    {
        RayTracingLevel.Low => "низкий",
        RayTracingLevel.Medium => "средний",
        RayTracingLevel.Ultra => "«Ультра»",
        _ => ((int)l).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
