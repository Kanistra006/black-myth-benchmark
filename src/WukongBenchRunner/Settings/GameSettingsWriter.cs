using System.Globalization;

namespace WukongBenchRunner.Settings;

/// <summary>
/// Writes a <see cref="BenchmarkProfile"/> into the game's GameUserSettings.ini.
/// The game reads graphics options from two places, and both must agree:
/// <list type="bullet">
/// <item>UISettingData — what the settings menu shows and what the game re-applies on start;</item>
/// <item>[ScalabilityGroups] sg.* — the Unreal Engine scalability values actually used for rendering.</item>
/// </list>
/// </summary>
public static class GameSettingsWriter
{
    public const string MainSection = "/Script/GSGameSettings.GSGameUserSettings";
    public const string ScalabilitySection = "ScalabilityGroups";
    public const string RayTracingSection = "RayTracing";

    /// <summary>Per-option keys in UISettingData that follow the preset.</summary>
    internal static readonly string[] DetailKeys =
    [
        "ViewDistance", "AntiAliasing", "PostProcessing", "ShadowQuality", "TextureQuality",
        "FxQuality", "MaterialQuality", "VegetationQuality", "GlobalIllumination", "ReflectionQuality",
    ];

    /// <summary>Unreal scalability groups that follow the preset.</summary>
    internal static readonly string[] ScalabilityKeys =
    [
        "sg.ViewDistanceQuality", "sg.AntiAliasingQuality", "sg.ShadowQuality", "sg.GlobalIlluminationQuality",
        "sg.ReflectionQuality", "sg.PostProcessQuality", "sg.TextureQuality", "sg.EffectsQuality",
        "sg.FoliageQuality", "sg.ShadingQuality",
    ];

    public static void Apply(IniDocument ini, BenchmarkProfile profile, int outputWidth, int outputHeight)
    {
        var ui = UiSettingData.Parse(ini.Get(MainSection, "UISettingData"));
        var q = ((int)profile.Quality).ToString(CultureInfo.InvariantCulture);
        var scale = Math.Clamp(profile.ResolutionScale, 25, 100);
        var renderWidth = (int)Math.Round(outputWidth * scale / 100.0);
        var renderHeight = (int)Math.Round(outputHeight * scale / 100.0);

        ui["QualityLevel"] = q;
        foreach (var key in DetailKeys) ui[key] = q;

        // "ImageQuality" is the render height in pixels the resolution-scale slider produces.
        ui["ImageQuality"] = renderHeight.ToString(CultureInfo.InvariantCulture);
        ui["SuperResolutionSampling"] = ((int)profile.Upscaler).ToString(CultureInfo.InvariantCulture);
        ui["InsertFrame"] = profile.FrameGeneration ? "1" : "0";
        ui["Rtx"] = profile.RayTracing ? "1" : "0";
        ui["RtxLevel"] = ((int)profile.RayTracingLevel).ToString(CultureInfo.InvariantCulture);
        // Never let a frame cap or v-sync hide the real performance.
        ui["LockFrameRate"] = "0";
        ui["Vsync"] = "0";
        var mode = ((int)profile.Display).ToString(CultureInfo.InvariantCulture);
        ui["ScreenMode"] = mode;
        ini.Set(MainSection, "UISettingData", ui.ToString());
        ini.Set(MainSection, "FullscreenMode", mode);
        ini.Set(MainSection, "LastConfirmedFullscreenMode", mode);
        ini.Set(MainSection, "PreferredFullscreenMode", mode);
        ini.Set(MainSection, "ResolutionSizeX", outputWidth.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "ResolutionSizeY", outputHeight.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "LastUserConfirmedResolutionSizeX", outputWidth.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "LastUserConfirmedResolutionSizeY", outputHeight.ToString(CultureInfo.InvariantCulture));

        ini.Set(MainSection, "DesiredScreenWidth", renderWidth.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "DesiredScreenHeight", renderHeight.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "LastUserConfirmedDesiredScreenWidth", renderWidth.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "LastUserConfirmedDesiredScreenHeight", renderHeight.ToString(CultureInfo.InvariantCulture));
        ini.Set(MainSection, "bUseVSync", "False");
        ini.Set(MainSection, "FrameRateLimit", "0.000000");

        // Skip the first-run language/brightness/privacy screens. Data collection stays declined.
        ini.Set(MainSection, "FirstSettingFinish", "True");
        ini.Set(MainSection, "AgreementReaded", "1");
        if (ini.Get(MainSection, "PrivacyAgreement") is null) ini.Set(MainSection, "PrivacyAgreement", "0");

        ini.Set(ScalabilitySection, "sg.ResolutionQuality", scale.ToString(CultureInfo.InvariantCulture));
        var sg = ((int)profile.Quality - 1).ToString(CultureInfo.InvariantCulture);
        foreach (var key in ScalabilityKeys) ini.Set(ScalabilitySection, key, sg);

        ini.Set(RayTracingSection, "r.RayTracing.EnableInGame", profile.RayTracing ? "True" : "False");
    }
}
