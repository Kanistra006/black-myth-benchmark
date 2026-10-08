using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace WukongBenchRunner.Hardware;

/// <summary>Average system load measured independently of the game while a pass was running.</summary>
public sealed record LoadSummary(
    int Samples,
    double CpuTotalAvg,
    double CpuBusiestCoreAvg,
    double? GpuUtilAvg,
    double? GpuClockAvgMhz,
    double? GpuPowerAvgW,
    double? GpuTempMaxC);

/// <summary>
/// Samples CPU load (Windows performance counters) and, on NVIDIA GPUs, GPU load via nvidia-smi
/// once per second. This shows which component was actually saturated during a pass.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoadMonitor : IDisposable
{
    private readonly List<double> _cpuTotal = [];
    private readonly List<double> _cpuBusiest = [];
    private readonly List<double[]> _gpu = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _cpuThread;
    private Process? _smi;

    public LoadMonitor()
    {
        _cpuThread = new Thread(SampleCpu) { IsBackground = true, Name = "cpu-sampler" };
        _cpuThread.Start();
        StartNvidiaSmi();
    }

    private void SampleCpu()
    {
        try
        {
            var category = new PerformanceCounterCategory("Processor Information");
            var cores = category.GetInstanceNames()
                .Where(n => !n.Contains("_Total", StringComparison.OrdinalIgnoreCase))
                .Select(n => new PerformanceCounter("Processor Information", "% Processor Utility", n))
                .ToList();
            using var total = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            total.NextValue();
            cores.ForEach(c => c.NextValue());
            while (!_cts.Token.WaitHandle.WaitOne(1000))
            {
                var t = Math.Min(100, total.NextValue());
                var busiest = cores.Count == 0 ? t : Math.Min(100, cores.Max(c => c.NextValue()));
                lock (_cpuTotal)
                {
                    _cpuTotal.Add(t);
                    _cpuBusiest.Add(busiest);
                }
            }
            cores.ForEach(c => c.Dispose());
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Warn($"CPU load counters unavailable: {e.Message}");
        }
    }

    private void StartNvidiaSmi()
    {
        try
        {
            _smi = Process.Start(new ProcessStartInfo("nvidia-smi",
                "--query-gpu=utilization.gpu,clocks.gr,power.draw,temperature.gpu --format=csv,noheader,nounits -lms 1000")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (_smi is null) return;
            _smi.OutputDataReceived += (_, e) =>
            {
                if (ParseSmiLine(e.Data) is { } v)
                    lock (_gpu) _gpu.Add(v);
            };
            _smi.BeginOutputReadLine();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No NVIDIA driver / nvidia-smi on PATH: GPU load is reported only by the game.
            _smi = null;
        }
    }

    /// <summary>Parses "util, clock, power, temp"; "[N/A]" fields become NaN.</summary>
    internal static double[]? ParseSmiLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split(',').Select(p => p.Trim()).ToArray();
        // Several GPUs print one line each; the first field must be numeric for a valid sample.
        if (parts.Length < 4 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return null;
        return parts.Take(4)
            .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
            .ToArray();
    }

    public LoadSummary Stop()
    {
        _cts.Cancel();
        _cpuThread.Join(3000);
        StopSmi();

        lock (_cpuTotal)
        lock (_gpu)
        {
            double? Avg(int i) => _gpu.Count == 0 ? null : Round(_gpu.Select(g => g[i]).Where(d => !double.IsNaN(d)).DefaultIfEmpty(double.NaN).Average());
            double? Max(int i) => _gpu.Count == 0 ? null : Round(_gpu.Select(g => g[i]).Where(d => !double.IsNaN(d)).DefaultIfEmpty(double.NaN).Max());
            return new LoadSummary(
                Samples: _cpuTotal.Count,
                CpuTotalAvg: _cpuTotal.Count == 0 ? 0 : Round(_cpuTotal.Average())!.Value,
                CpuBusiestCoreAvg: _cpuBusiest.Count == 0 ? 0 : Round(_cpuBusiest.Average())!.Value,
                GpuUtilAvg: Avg(0),
                GpuClockAvgMhz: Avg(1),
                GpuPowerAvgW: Avg(2),
                GpuTempMaxC: Max(3));
        }
    }

    private static double? Round(double v) => double.IsNaN(v) ? null : Math.Round(v, 1);

    private void StopSmi()
    {
        if (_smi is null) return;
        try
        {
            if (!_smi.HasExited) _smi.Kill();
            _smi.WaitForExit(2000);
        }
        catch (InvalidOperationException)
        {
        }
        _smi.Dispose();
        _smi = null;
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested) Stop();
        _cts.Dispose();
    }
}
