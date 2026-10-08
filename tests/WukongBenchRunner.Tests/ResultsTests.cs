using WukongBenchRunner.Results;

namespace WukongBenchRunner.Tests;

public class ResultsTests
{
    // Shape of a real file from %TEMP%\b1\BenchMarkHistory\Tool, trimmed to four frames.
    private const string Sample = """
        {"TimeStamp":1791458956,"BenckMarkVersion":0,"FPSAvg":103,"FPSMax":131,"FPSMin":55,"FPS95":91,
         "CPUAvg":1,"GPUAvg":93,"GPULeak":0,"VideoMem":4.0,"GameVer":"1.0.3.14649","SysVer":"10.0.26200.1.256.64bit",
         "CPUModel":"12th Gen Intel(R) Core(TM) i5-12450H","GPUModel":"NVIDIA GeForce RTX 4060 Laptop GPU",
         "GpuDriverVer":"31.0.101.4255","VideoMemSize":"8GB","SysMem":"16GB","ScreenMode":1,
         "ScreenResolution":"2560 × 1440","QualityLevel":1,"ImageQuality":25,"ViewDistance":1,"AntiAliasing":1,
         "PostProcessing":1,"ShadowQuality":1,"TextureQuality":1,"MaterialQuality":1,"VegetationQuality":1,
         "MotionBlur":2,"Rtx":0,"Dlss":0,"InsertFrame":0,"Dx12":1,
         "Records":[
           {"TimeStamp":1791458800000,"FrameRate":100.0,"CPUUsage":1.0,"GPUUsage":90.0,"CPUFrameTime":9.0,"GPUFrameTime":8.0,"VideoMemoryUsage":3.9},
           {"TimeStamp":1791458800500,"FrameRate":50.0,"CPUUsage":1.0,"GPUUsage":90.0,"CPUFrameTime":19.0,"GPUFrameTime":8.0,"VideoMemoryUsage":3.9},
           {"TimeStamp":1791458801000,"FrameRate":120.0,"CPUUsage":1.0,"GPUUsage":90.0,"CPUFrameTime":7.0,"GPUFrameTime":8.0,"VideoMemoryUsage":4.0},
           {"TimeStamp":1791458802000,"FrameRate":110.0,"CPUUsage":1.0,"GPUUsage":90.0,"CPUFrameTime":8.0,"GPUFrameTime":6.0,"VideoMemoryUsage":4.0}
         ]}
        """;

    [Fact]
    public void Parse_ReadsSummaryAndRecords()
    {
        var file = BenchmarkHistoryFile.Parse(Sample);

        Assert.Equal(103, file.FPSAvg);
        Assert.Equal(91, file.FPS95);
        Assert.Equal("2560 × 1440", file.ScreenResolution);
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", file.GPUModel);
        Assert.Equal(25, file.ImageQuality);
        Assert.Equal(4, file.Records.Count);
        Assert.Equal(19.0, file.Records[1].CPUFrameTime);
    }

    [Fact]
    public void Metrics_CombineReportedAndDerivedValues()
    {
        var m = PassMetrics.From(BenchmarkHistoryFile.Parse(Sample));

        Assert.Equal(103, m.AvgFps);
        Assert.Equal(55, m.MinFps);
        Assert.Equal(4, m.FrameCount);
        Assert.Equal(2.0, m.DurationSec);
        Assert.Equal(50.0, m.OnePercentLowFps);       // lowest of 4 frames
        Assert.Equal(10.75, m.AvgCpuFrameTimeMs);
        Assert.Equal(7.5, m.AvgGpuFrameTimeMs);
        Assert.Equal(0.75, m.CpuBoundShare);
    }

    [Fact]
    public void Report_ShowsSettingsReportedByTheGame()
    {
        var raw = BenchmarkHistoryFile.Parse(Sample.Replace("\"Rtx\":0,\"Dlss\":0", "\"Rtx\":2,\"Dlss\":3"));
        var profile = Settings.BenchmarkProfile.Gpu(true, Settings.RayTracingLevel.Medium);
        var load = new Hardware.LoadSummary(10, 50, 90, 99, 1450, 40, 90);
        var pass = new Automation.PassResult(profile, 2560, 1440, "file", raw, PassMetrics.From(raw), load);

        var rows = Report.ReportBuilder.FromPass(pass).Settings.ToDictionary(r => r.Name, r => r.Value);

        Assert.StartsWith("Вкл., средний (игра: Вкл., средний)", rows["Полная трассировка лучей"]);
        Assert.Equal("Unreal TSR (игра: Unreal TSR)", rows["Апскейлер"]);
        Assert.Equal("Выкл. (игра: Выкл.)", rows["Генерация кадров"]);
        Assert.Equal("100% → 2560x1440 (игра: 25%)", rows["Масштаб рендера"]);
    }

    [Theory]
    [InlineData(0.01, 1)]
    [InlineData(0.5, 50)]
    [InlineData(0.99, 99)]
    [InlineData(1.0, 100)]
    public void Percentile_UsesNearestRank(double p, double expected)
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.Equal(expected, PassMetrics.Percentile(values, p));
    }
}
