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
            new("Апскейлер", $"{UpscalerName(p.Upscaler)}"),
            new("Пресет качества", $"{QualityName(p.Quality)} (игра: {QualityName((Quality)raw.QualityLevel)})"),
            new("Детализация, тени, текстуры, эффекты, растительность, GI, отражения", QualityName(p.Quality)),
            new("Полная трассировка лучей", p.RayTracing ? $"Вкл., {RtName(p.RayTracingLevel)}" : "Выкл." + (raw.Rtx == 1 ? " (игра: Вкл.)" : "")),
            new("Генерация кадров", (p.FrameGeneration ? "Вкл." : "Выкл.") + (raw.InsertFrame == 1 ? " (игра: Вкл.)" : "")),
            new("Вертикальная синхронизация / лимит FPS", "Выкл. / без лимита"),
        };

        var purpose = p.Name == "CPU"
            ? "Минимальная нагрузка на GPU: FPS ограничивается процессором"
            : "Максимальная нагрузка на GPU: нативное разрешение, Cinematic, Full RT";

        return new PassReport(p.Name, purpose, settings, pass.Metrics, pass.Load, pass.HistoryFile);
    }

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
        RayTracingLevel.Low => "низкий уровень",
        RayTracingLevel.Medium => "средний уровень",
        RayTracingLevel.Ultra => "уровень «Ультра»",
        _ => l.ToString(),
    };
}
