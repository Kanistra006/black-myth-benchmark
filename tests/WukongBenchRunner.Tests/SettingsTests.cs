using WukongBenchRunner.Settings;

namespace WukongBenchRunner.Tests;

public class SettingsTests
{
    private static string FixtureIni => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameUserSettings.ini"));

    [Fact]
    public void UiSettingData_RoundTripsExactly()
    {
        var ini = IniDocument.Parse(FixtureIni);
        var raw = ini.Get(GameSettingsWriter.MainSection, "UISettingData")!;

        var parsed = UiSettingData.Parse(raw);

        Assert.Equal(32, parsed.Entries.Count);
        Assert.Equal("5", parsed["QualityLevel"]);
        Assert.Equal(raw, parsed.ToString());
    }

    [Fact]
    public void UiSettingData_SetUpdatesInPlaceAndAppendsNewKeys()
    {
        var data = UiSettingData.Parse("((\"A\", \"1\"),(\"B\", \"2\"))");

        data["A"] = "9";
        data["C"] = "3";

        Assert.Equal("((\"A\", \"9\"),(\"B\", \"2\"),(\"C\", \"3\"))", data.ToString());
    }

    [Fact]
    public void IniDocument_PreservesUntouchedLinesAndCrLf()
    {
        var ini = IniDocument.Parse(FixtureIni);
        ini.Set("ScalabilityGroups", "sg.ShadowQuality", "0");

        var text = ini.ToString();

        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\r\r", text);
        Assert.Contains("sg.ShadowQuality=0", text);
        Assert.Contains("MainMonitorID=\"MONITOR\\\\XMI27B1", text);
        Assert.Equal(FixtureIni.Replace("\r\n", "\n").Split('\n').Length, text.Split("\r\n").Length);
    }

    [Fact]
    public void IniDocument_AddsMissingKeyAndSection()
    {
        var ini = IniDocument.Parse("[A]\r\nx=1\r\n\r\n[B]\r\ny=2\r\n");

        ini.Set("A", "z", "3");
        ini.Set("C", "w", "4");

        Assert.Equal("3", ini.Get("A", "z"));
        Assert.Equal("4", ini.Get("C", "w"));
        Assert.Equal("[A]\r\nx=1\r\nz=3\r\n\r\n[B]\r\ny=2\r\n\r\n[C]\r\nw=4", ini.ToString());
    }

    [Fact]
    public void Writer_AppliesCpuProfile()
    {
        var ini = IniDocument.Parse(FixtureIni);

        GameSettingsWriter.Apply(ini, BenchmarkProfile.Cpu, 1280, 720);

        var ui = UiSettingData.Parse(ini.Get(GameSettingsWriter.MainSection, "UISettingData"));
        Assert.Equal("1", ui["QualityLevel"]);
        Assert.All(GameSettingsWriter.DetailKeys, k => Assert.Equal("1", ui[k]));
        Assert.Equal("180", ui["ImageQuality"]);          // 25% of 720
        Assert.Equal("0", ui["SuperResolutionSampling"]); // FSR
        Assert.Equal("0", ui["InsertFrame"]);
        Assert.Equal("0", ui["Rtx"]);
        Assert.Equal("2", ui["ScreenMode"]);              // windowed
        Assert.Equal("0", ui["Vsync"]);
        Assert.Equal("0", ui["LockFrameRate"]);
        Assert.Equal("25", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.All(GameSettingsWriter.ScalabilityKeys, k => Assert.Equal("0", ini.Get("ScalabilityGroups", k)));
        Assert.Equal("False", ini.Get("RayTracing", "r.RayTracing.EnableInGame"));
        Assert.Equal("1280", ini.Get(GameSettingsWriter.MainSection, "ResolutionSizeX"));
        Assert.Equal("2", ini.Get(GameSettingsWriter.MainSection, "FullscreenMode"));
        Assert.Equal("True", ini.Get(GameSettingsWriter.MainSection, "FirstSettingFinish"));
        Assert.Equal("0", ini.Get(GameSettingsWriter.MainSection, "PrivacyAgreement"));
    }

    [Fact]
    public void Writer_AppliesGpuProfile()
    {
        var ini = IniDocument.Parse(FixtureIni);

        GameSettingsWriter.Apply(ini, BenchmarkProfile.Gpu(rayTracing: true, RayTracingLevel.Ultra), 2560, 1440);

        var ui = UiSettingData.Parse(ini.Get(GameSettingsWriter.MainSection, "UISettingData"));
        Assert.Equal("5", ui["QualityLevel"]);
        Assert.Equal("1440", ui["ImageQuality"]);
        Assert.Equal("3", ui["SuperResolutionSampling"]); // TSR
        Assert.Equal("0", ui["InsertFrame"]);
        Assert.Equal("1", ui["Rtx"]);
        Assert.Equal("3", ui["RtxLevel"]);
        Assert.Equal("1", ui["ScreenMode"]);              // borderless
        Assert.Equal("100", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.All(GameSettingsWriter.ScalabilityKeys, k => Assert.Equal("4", ini.Get("ScalabilityGroups", k)));
        Assert.Equal("True", ini.Get("RayTracing", "r.RayTracing.EnableInGame"));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4090", 24.0, true, RayTracingLevel.Ultra)]
    [InlineData("NVIDIA GeForce RTX 4070", 12.0, true, RayTracingLevel.Ultra)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", 8.0, true, RayTracingLevel.Medium)]
    [InlineData("NVIDIA GeForce RTX 3050", 6.0, false, RayTracingLevel.Low)]
    [InlineData("AMD Radeon RX 7900 XTX", 24.0, false, RayTracingLevel.Low)]
    [InlineData("NVIDIA GeForce GTX 1080 Ti", 11.0, false, RayTracingLevel.Low)]
    public void Selector_PicksRayTracingByVendorAndVram(string gpu, double vram, bool rt, RayTracingLevel level)
    {
        var gpus = new List<Hardware.GpuInfo>
        {
            new("Intel(R) UHD Graphics", "1", 1, null),
            new(gpu, "1", vram, null),
        };

        var choice = GpuProfileSelector.ChooseRayTracing(gpus);

        Assert.Equal(rt, choice.RayTracing);
        if (rt) Assert.Equal(level, choice.Level);
    }

    [Theory]
    [InlineData(7.6, 8.0, true)]
    [InlineData(7.2, 8.0, false)]
    [InlineData(7.6, null, false)]
    public void Selector_DetectsSaturatedVram(double used, double? total, bool expected)
    {
        Assert.Equal(expected, GpuProfileSelector.VramSaturated(used, total));
    }

    [Fact]
    public void Writer_DoesNotOverrideExistingPrivacyChoice()
    {
        var ini = IniDocument.Parse(FixtureIni.Replace("PrivacyAgreement=0", "PrivacyAgreement=1"));

        GameSettingsWriter.Apply(ini, BenchmarkProfile.Cpu, 1280, 720);

        Assert.Equal("1", ini.Get(GameSettingsWriter.MainSection, "PrivacyAgreement"));
    }
}
