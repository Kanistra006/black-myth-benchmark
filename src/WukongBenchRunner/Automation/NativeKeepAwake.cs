using static WukongBenchRunner.Automation.NativeMethods;

namespace WukongBenchRunner.Automation;

/// <summary>Prevents sleep and display power-off for the lifetime of the process.</summary>
internal static class NativeKeepAwake
{
    public static void Enable() =>
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
}
