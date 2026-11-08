using System.Text;
using StartupManager.Models;

namespace StartupManager.Services;

public static class CsvExporter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static int Export(string path, IEnumerable<ManagedEntry> entries)
    {
        var rows = entries.ToList();
        var builder = new StringBuilder();
        builder.AppendLine("名称,分类,状态,是否需管理员,命令行,文件路径,启动方式");

        foreach (var entry in rows)
        {
            var filePath = !string.IsNullOrWhiteSpace(entry.FilePath)
                ? entry.FilePath!
                : entry.ExecutablePath;

            builder.Append(Escape(entry.Name)).Append(',')
                .Append(Escape(entry.KindLabel)).Append(',')
                .Append(Escape(entry.StatusLabel)).Append(',')
                .Append(entry.RequiresAdmin ? "是" : "否").Append(',')
                .Append(Escape(entry.CommandLine)).Append(',')
                .Append(Escape(filePath)).Append(',')
                .Append(Escape(entry.StartupModeLabel))
                .Append("\r\n");
        }

        File.WriteAllText(path, builder.ToString(), Utf8WithBom);
        return rows.Count;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
