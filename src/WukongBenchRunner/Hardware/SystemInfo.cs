using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WukongBenchRunner.Hardware;

public sealed record GpuInfo(string Name, string DriverVersion, double? VramGb, string? CurrentMode);

public sealed record DisplayInfo(string Name, int Width, int Height, bool Primary);

public sealed record SystemInfo(
    string Cpu,
    int PhysicalCores,
    int LogicalCores,
    int? CpuMaxClockMhz,
    double RamGb,
    string? RamDetails,
    IReadOnlyList<GpuInfo> Gpus,
    IReadOnlyList<DisplayInfo> Displays,
    string Os,
    string? Motherboard,
    string PowerSource,
    string? PowerPlan);

[SupportedOSPlatform("windows")]
public static class SystemInfoCollector
{
    static SystemInfoCollector() =>
        // powercfg prints in the console OEM code page (e.g. 866 on Russian Windows).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    public static SystemInfo Collect()
    {
        var cpu = Query("Win32_Processor").FirstOrDefault();
        var cs = Query("Win32_ComputerSystem").FirstOrDefault();
        var os = Query("Win32_OperatingSystem").FirstOrDefault();
        var board = Query("Win32_BaseBoard").FirstOrDefault();

        var ramModules = Query("Win32_PhysicalMemory").ToList();
        var ramDetails = ramModules.Count == 0 ? null :
            $"{ramModules.Count} x {Math.Round(ToLong(ramModules[0]["Capacity"]) / 1024d / 1024 / 1024)} GB" +
            (ToInt(ramModules[0]["ConfiguredClockSpeed"]) is int mhz and > 0 ? $" @ {mhz} MT/s" : "");

        return new SystemInfo(
            Cpu: Str(cpu?["Name"]) ?? "Unknown CPU",
            PhysicalCores: ToInt(cpu?["NumberOfCores"]) ?? 0,
            LogicalCores: ToInt(cpu?["NumberOfLogicalProcessors"]) ?? Environment.ProcessorCount,
            CpuMaxClockMhz: ToInt(cpu?["MaxClockSpeed"]),
            RamGb: Math.Round(ToLong(cs?["TotalPhysicalMemory"]) / 1024d / 1024 / 1024, 1),
            RamDetails: ramDetails,
            Gpus: CollectGpus(),
            Displays: CollectDisplays(),
            Os: $"{Str(os?["Caption"])} {Str(os?["Version"])}".Trim(),
            Motherboard: board is null ? null : $"{Str(board["Manufacturer"])} {Str(board["Product"])}".Trim(),
            PowerSource: CollectPowerSource(),
            PowerPlan: CollectPowerPlan());
    }

    private static List<GpuInfo> CollectGpus()
    {
        var vram = ReadVramFromRegistry();
        return Query("Win32_VideoController")
            .Select(g =>
            {
                var name = Str(g["Name"]) ?? "Unknown GPU";
                // Win32_VideoController.AdapterRAM is a 32-bit field and caps at 4 GB,
                // so prefer the 64-bit value the driver stores in the registry.
                double? gb = vram.TryGetValue(name, out var bytes) ? bytes : ToLong(g["AdapterRAM"]);
                gb = gb > 0 ? Math.Round(gb.Value / 1024 / 1024 / 1024, 1) : null;
                var w = ToInt(g["CurrentHorizontalResolution"]);
                var h = ToInt(g["CurrentVerticalResolution"]);
                var hz = ToInt(g["CurrentRefreshRate"]);
                var mode = w is > 0 && h is > 0 ? $"{w}x{h} @ {hz} Hz" : null;
                return new GpuInfo(name, Str(g["DriverVersion"]) ?? "?", gb, mode);
            })
            .ToList();
    }

    private static Dictionary<string, double> ReadVramFromRegistry()
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using var root = Registry.LocalMachine.OpenSubKey(classKey);
        if (root is null) return result;
        foreach (var sub in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            try
            {
                using var key = root.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string desc) continue;
                if (key.GetValue("HardwareInformation.qwMemorySize") is long qw && qw > 0)
                    result[desc] = qw;
            }
            catch (System.Security.SecurityException)
            {
                // Some adapter keys are not readable without elevation; skip them.
            }
        }
        return result;
    }

    private static List<DisplayInfo> CollectDisplays()
    {
        var list = new List<DisplayInfo>();
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
            list.Add(new DisplayInfo(s.DeviceName.TrimStart('\\', '.'), s.Bounds.Width, s.Bounds.Height, s.Primary));
        return list;
    }

    private static string CollectPowerSource()
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus;
        return status.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => "AC adapter",
            System.Windows.Forms.PowerLineStatus.Offline => $"Battery ({status.BatteryLifePercent:P0})",
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Reads the active power plan name from <c>powercfg /getactivescheme</c>
    /// ("Power Scheme GUID: … (High performance)"); the WMI power namespace is unreliable on some systems.
    /// </summary>
    private static string? CollectPowerPlan()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return ParsePowerPlan(output);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    internal static string? ParsePowerPlan(string powercfgOutput)
    {
        var open = powercfgOutput.LastIndexOf('(');
        var close = powercfgOutput.LastIndexOf(')');
        return open >= 0 && close > open ? powercfgOutput[(open + 1)..close].Trim() : null;
    }

    private static IEnumerable<ManagementBaseObject> Query(string cls)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {cls}");
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Str(object? o) => o?.ToString()?.Trim();
    private static int? ToInt(object? o) => o is null ? null : Convert.ToInt32(o);
    private static long ToLong(object? o) => o is null ? 0 : Convert.ToInt64(o);
}
