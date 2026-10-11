using static WukongBenchRunner.Automation.NativeMethods;

namespace WukongBenchRunner.Automation;

internal static class NativeKeepAwake
{
    public static void Enable() =>
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
}
