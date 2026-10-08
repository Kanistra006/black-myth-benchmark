namespace WukongBenchRunner;

/// <summary>Timestamped progress output to the console.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write(message, ConsoleColor.Gray);
    public static void Step(string message) => Write(message, ConsoleColor.Cyan);
    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);
    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor color)
    {
        lock (Gate)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previous;
        }
    }
}
