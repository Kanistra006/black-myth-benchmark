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

        GameSettingsWriter.Apply(ini, BenchmarkProfile.Gpu(rayTracingSupported: true), 2560, 1440);

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

    [Fact]
    public void Writer_DoesNotOverrideExistingPrivacyChoice()
    {
        var ini = IniDocument.Parse(FixtureIni.Replace("PrivacyAgreement=0", "PrivacyAgreement=1"));

        GameSettingsWriter.Apply(ini, BenchmarkProfile.Cpu, 1280, 720);

        Assert.Equal("1", ini.Get(GameSettingsWriter.MainSection, "PrivacyAgreement"));
    }
}
