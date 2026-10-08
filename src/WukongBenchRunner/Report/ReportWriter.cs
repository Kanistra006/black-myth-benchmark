using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WukongBenchRunner.Hardware;
using WukongBenchRunner.Results;

namespace WukongBenchRunner.Report;

public static class ReportWriter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    // ---------- shared table model ----------

    private static List<(string Title, List<string[]> Rows)> Sections(BenchmarkReport r)
    {
        var s = r.System;
        var system = new List<string[]>
        {
            new[] { "Процессор", $"{s.Cpu} ({s.PhysicalCores} ядер / {s.LogicalCores} потоков)" },
            new[] { "Оперативная память", $"{s.RamGb.ToString("0.#", Ru)} ГБ" + (s.RamDetails is null ? "" : $" ({s.RamDetails})") },
        };
        for (var i = 0; i < s.Gpus.Count; i++)
        {
            var g = s.Gpus[i];
            var vram = g.VramGb is null ? "" : $", {g.VramGb.Value.ToString("0.#", Ru)} ГБ";
            system.Add([$"Видеокарта {i + 1}", $"{g.Name}{vram}, драйвер {g.DriverVersion}"]);
        }
        foreach (var d in s.Displays)
            system.Add(["Монитор " + d.Name, $"{d.Width}x{d.Height}{(d.Primary ? " (основной)" : "")}"]);
        if (s.Motherboard is not null) system.Add(["Материнская плата", s.Motherboard]);
        system.Add(["ОС", s.Os]);
        system.Add(["Питание", s.PowerSource + (s.PowerPlan is null ? "" : $", схема «{s.PowerPlan}»")]);
        system.Add(["Версия бенчмарка", r.GameVersion]);

        var results = new List<string[]> { new[] { "Показатель" }.Concat(r.Passes.Select(p => $"{p.Name}-тест")).ToArray() };
        void Row(string name, Func<PassMetrics, string> f) =>
            results.Add(new[] { name }.Concat(r.Passes.Select(p => f(p.Metrics))).ToArray());
        void LoadRow(string name, Func<LoadSummary, string> f) =>
            results.Add(new[] { name }.Concat(r.Passes.Select(p => f(p.Load))).ToArray());
        Row("Средний FPS", m => F(m.AvgFps));
        Row("Минимальный FPS", m => F(m.MinFps));
        Row("Максимальный FPS", m => F(m.MaxFps));
        Row("5-й перцентиль FPS", m => F(m.Percentile5Fps));
        Row("1% low FPS (по кадрам)", m => F(m.OnePercentLowFps));
        Row("Видеопамять, ГБ", m => F(m.VramUsedGb, "0.0"));
        Row("Время кадра CPU (игра), мс", m => F(m.AvgCpuFrameTimeMs, "0.00"));
        Row("Время кадра GPU (игра), мс", m => F(m.AvgGpuFrameTimeMs, "0.00"));
        Row("Загрузка GPU (игра), %", m => F(m.GpuUtilizationAvg));
        LoadRow("Загрузка GPU (nvidia-smi), %", l => Opt(l.GpuUtilAvg));
        LoadRow("Частота GPU, МГц / мощность, Вт", l => $"{Opt(l.GpuClockAvgMhz, "0")} / {Opt(l.GpuPowerAvgW)}");
        LoadRow("Температура GPU (макс.), °C", l => Opt(l.GpuTempMaxC, "0"));
        LoadRow("Загрузка CPU (все ядра), %", l => F(l.CpuTotalAvg));
        LoadRow("Загрузка самого занятого ядра CPU, %", l => F(l.CpuBusiestCoreAvg));
        Row("Кадров / длительность", m => $"{m.FrameCount} / {F(m.DurationSec, "0")} с");

        var list = new List<(string, List<string[]>)> { ("Характеристики компьютера", system), ("Результаты", results) };
        foreach (var p in r.Passes)
            list.Add(($"Настройки {p.Name}-теста — {p.Purpose}", p.Settings.Select(x => new[] { x.Name, x.Value }).ToList()));
        return list;
    }

    private static string F(double v, string fmt = "0.#") => v.ToString(fmt, Ru);
    private static string Opt(double? v, string fmt = "0.#") => v is null ? "н/д" : v.Value.ToString(fmt, Ru);

    // ---------- console ----------

    public static void WriteConsole(BenchmarkReport r)
    {
        foreach (var (title, rows) in Sections(r))
        {
            Console.WriteLine();
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(title);
            Console.ForegroundColor = color;
            WriteTable(rows, hasHeader: title == "Результаты");
        }
        Console.WriteLine();
    }

    private static void WriteTable(List<string[]> rows, bool hasHeader)
    {
        var cols = rows.Max(x => x.Length);
        var widths = Enumerable.Range(0, cols)
            .Select(c => rows.Max(x => c < x.Length ? x[c].Length : 0))
            .ToArray();
        string Line(char l, char m, char r) => l + string.Join(m, widths.Select(w => new string('─', w + 2))) + r;

        Console.WriteLine(Line('┌', '┬', '┐'));
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = Enumerable.Range(0, cols).Select(c =>
            {
                var v = c < rows[i].Length ? rows[i][c] : "";
                return " " + (c == 0 ? v.PadRight(widths[c]) : v.PadLeft(widths[c])) + " ";
            });
            Console.WriteLine("│" + string.Join("│", cells) + "│");
            if (hasHeader && i == 0) Console.WriteLine(Line('├', '┼', '┤'));
        }
        Console.WriteLine(Line('└', '┴', '┘'));
    }

    // ---------- markdown ----------

    public static string ToMarkdown(BenchmarkReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Black Myth: Wukong Benchmark Tool — отчёт");
        sb.AppendLine();
        sb.AppendLine($"Запуск: {r.StartedAt:dd.MM.yyyy HH:mm} – {r.FinishedAt:HH:mm}, разрешение вывода {r.OutputResolution}.");
        foreach (var (title, rows) in Sections(r))
        {
            sb.AppendLine();
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            var header = title == "Результаты" ? rows[0] : new[] { "Параметр", "Значение" };
            var body = title == "Результаты" ? rows.Skip(1) : rows;
            sb.AppendLine("| " + string.Join(" | ", header) + " |");
            sb.AppendLine("|" + string.Join("|", header.Select((_, i) => i == 0 ? "---" : "---:")) + "|");
            foreach (var row in body) sb.AppendLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
        }
        sb.AppendLine();
        sb.AppendLine("Исходные файлы результатов бенчмарка:");
        sb.AppendLine();
        foreach (var p in r.Passes) sb.AppendLine($"- {p.Name}: `{p.HistoryFile}`");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|");

    // ---------- json ----------

    public static string ToJson(BenchmarkReport r) => JsonSerializer.Serialize(r, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    });
}
