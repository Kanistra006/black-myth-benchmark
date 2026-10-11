namespace WukongBenchRunner.Results;

public sealed record PassMetrics
{
    // Reported by the Benchmark Tool.
    public required double AvgFps { get; init; }
    public required double MaxFps { get; init; }
    public required double MinFps { get; init; }
    public required double Percentile5Fps { get; init; }
    public required double VramUsedGb { get; init; }
    public required double GpuUtilizationAvg { get; init; }

    // Derived from per-frame records.
    public required int FrameCount { get; init; }
    public required double DurationSec { get; init; }
    public required double OnePercentLowFps { get; init; }
    public required double AvgCpuFrameTimeMs { get; init; }
    public required double AvgGpuFrameTimeMs { get; init; }
    /// <summary>
    /// Share of frames where the game's CPU time exceeded its GPU time (0..1). Both timings include
    /// waits for each other, so this is informational only; see LoadSummary for measured load.
    /// </summary>
    public required double CpuBoundShare { get; init; }

    public static PassMetrics From(BenchmarkHistoryFile file)
    {
        var records = file.Records;
        var fps = records.Select(r => r.FrameRate).Where(f => f > 0).OrderBy(f => f).ToList();
        var duration = records.Count > 1 ? (records[^1].TimeStamp - records[0].TimeStamp) / 1000.0 : 0;

        return new PassMetrics
        {
            AvgFps = file.FPSAvg,
            MaxFps = file.FPSMax,
            MinFps = file.FPSMin,
            Percentile5Fps = file.FPS95,
            VramUsedGb = Math.Round(file.VideoMem, 2),
            GpuUtilizationAvg = file.GPUAvg,
            FrameCount = records.Count,
            DurationSec = Math.Round(duration, 1),
            OnePercentLowFps = fps.Count == 0 ? 0 : Math.Round(Percentile(fps, 0.01), 1),
            AvgCpuFrameTimeMs = records.Count == 0 ? 0 : Math.Round(records.Average(r => r.CPUFrameTime), 2),
            AvgGpuFrameTimeMs = records.Count == 0 ? 0 : Math.Round(records.Average(r => r.GPUFrameTime), 2),
            CpuBoundShare = records.Count == 0 ? 0 :
                Math.Round(records.Count(r => r.CPUFrameTime > r.GPUFrameTime) / (double)records.Count, 3),
        };
    }

    /// <summary>Nearest-rank percentile of an ascending list.</summary>
    internal static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        var rank = (int)Math.Ceiling(p * sortedAscending.Count) - 1;
        return sortedAscending[Math.Clamp(rank, 0, sortedAscending.Count - 1)];
    }
}
