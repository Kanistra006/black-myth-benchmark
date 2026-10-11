using System.Runtime.InteropServices;

namespace WukongBenchRunner;

/// <summary>Keeps the console window open when the tool was started by double-clicking the exe.</summary>
internal static partial class ConsoleHost
{
    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    public static void PauseIfOwnWindow()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return;

        // A console created just for this process (double-click in Explorer) lists only us;
        // when started from cmd/PowerShell/dotnet run the shell is attached as well.
        var list = new uint[4];
        if (GetConsoleProcessList(list, (uint)list.Length) != 1) return;

        Console.WriteLine();
        Console.Write("Нажмите любую клавишу, чтобы закрыть окно...");
        Console.ReadKey(intercept: true);
    }
}
