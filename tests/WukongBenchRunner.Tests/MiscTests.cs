using WukongBenchRunner.Automation;
using WukongBenchRunner.Hardware;

namespace WukongBenchRunner.Tests;

public class MiscTests
{
    [Theory]
    // 1280x720 is the reference layout: coordinates pass through unchanged.
    [InlineData(1280, 720, 147, 324, UiAnchor.Left, 147, 324)]
    [InlineData(1280, 720, 502, 424, UiAnchor.Center, 502, 424)]
    // 2560x1440: everything scales by 2.
    [InlineData(2560, 1440, 147, 324, UiAnchor.Left, 294, 648)]
    [InlineData(2560, 1440, 502, 424, UiAnchor.Center, 1004, 848)]
    // 16:10 (1920x1200): scale by height 1200/720, centred elements stay centred.
    [InlineData(1920, 1200, 502, 424, UiAnchor.Center, 730, 707)]
    public void MapUiPoint_ScalesByHeight(int w, int h, double rx, double ry, UiAnchor anchor, int ex, int ey)
    {
        var (x, y) = GameInput.MapUiPoint(w, h, rx, ry, anchor);
        Assert.Equal(ex, x);
        Assert.Equal(ey, y);
    }

    [Fact]
    public void Cli_ParsesOptions()
    {
        var o = CliOptions.Parse(["--only", "gpu", "--game-dir", @"D:\Bench", "--cpu-window", "1600x900", "--keep-settings", "-y"]);

        Assert.True(o.NoCountdown);
        Assert.False(o.RunCpu);
        Assert.True(o.RunGpu);
        Assert.Equal(@"D:\Bench", o.GameDir);
        Assert.Equal((1600, 900), o.CpuWindow);
        Assert.True(o.KeepSettings);
    }

    [Theory]
    [InlineData("--only", "both")]
    [InlineData("--cpu-scale", "10")]
    [InlineData("--cpu-window", "huge")]
    [InlineData("--unknown", "x")]
    public void Cli_RejectsBadInput(string flag, string value)
    {
        Assert.Throws<ArgumentException>(() => CliOptions.Parse([flag, value]));
    }

    [Theory]
    [InlineData("99, 1710, 34.30, 88", new[] { 99, 1710, 34.3, 88 })]
    [InlineData("45, 2100, [N/A], 70", new[] { 45, 2100, double.NaN, 70 })]
    public void SmiLine_IsParsed(string line, double[] expected)
    {
        Assert.Equal(expected, LoadMonitor.ParseSmiLine(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No devices were found")]
    public void SmiLine_IgnoresNoise(string line)
    {
        Assert.Null(LoadMonitor.ParseSmiLine(line));
    }

    [Theory]
    [InlineData("GUID схемы питания: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Высокая производительность)", "Высокая производительность")]
    [InlineData("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n", "Balanced")]
    [InlineData("garbage", null)]
    public void PowerPlan_IsParsed(string output, string? expected)
    {
        Assert.Equal(expected, SystemInfoCollector.ParsePowerPlan(output));
    }
}
