using WukongBenchRunner.Hardware;

namespace WukongBenchRunner.Settings;

/// <summary>
/// Chooses the full ray tracing level for the GPU pass.
/// <para>
/// The heaviest setting is not automatically the highest GPU load: when the frame no longer fits into VRAM,
/// the GPU stalls while data is moved over PCIe and its utilisation drops. Measured on an RTX 4060 Laptop
/// (8 GB) at 2560x1440 Cinematic: RT off → 6.2 GB, 99% load; RT Low/Medium → 7.2 GB, 99% load;
/// RT Ultra → 7.6 GB (VRAM full), only 86% load at 5 FPS. So the level is picked from the VRAM size.
/// </para>
/// </summary>
public static class GpuProfileSelector
{
    public sealed record Choice(bool RayTracing, RayTracingLevel Level, string Reason);

    public static Choice ChooseRayTracing(IReadOnlyList<GpuInfo> gpus)
    {
        var rtx = gpus.FirstOrDefault(g =>
            g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
            g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase));

        if (rtx is null)
            return new(false, RayTracingLevel.Low, "полная трассировка лучей в бенчмарке доступна только на NVIDIA RTX");

        return rtx.VramGb switch
        {
            >= 11.5 => new(true, RayTracingLevel.Ultra, $"{rtx.VramGb:0.#} ГБ видеопамяти — хватает для уровня «Ультра»"),
            >= 7.5 => new(true, RayTracingLevel.Medium, $"{rtx.VramGb:0.#} ГБ видеопамяти — «Ультра» переполняет память, выбран средний уровень"),
            _ => new(false, RayTracingLevel.Low, $"{rtx.VramGb:0.#} ГБ видеопамяти — недостаточно для полной трассировки лучей"),
        };
    }

    /// <summary>True when the pass used (almost) all VRAM, i.e. results may reflect memory overflow.</summary>
    public static bool VramSaturated(double usedGb, double? totalGb) =>
        totalGb is > 0 && usedGb >= totalGb.Value * 0.94;
}
