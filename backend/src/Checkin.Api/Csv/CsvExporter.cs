using System.Globalization;
using System.Text;

namespace Checkin.Api.Csv;

/// <summary>Serializa listas para CSV — separador ";" (padrão do Excel em PT-BR) com BOM UTF-8,
/// pra abrir com acentuação correta sem passo extra de importação. Usado pelos endpoints
/// .../export dos relatórios (ver ReportsController) — a recepção/financeiro da academia
/// costuma precisar bater os números com a própria planilha.</summary>
public static class CsvExporter
{
    public static byte[] Write<T>(IEnumerable<T> rows, params (string Header, Func<T, object?> Value)[] columns)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', columns.Select(c => Escape(c.Header))));

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(';', columns.Select(c => Escape(c.Value(row)))));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Escape(object? value)
    {
        var text = value switch
        {
            null => "",
            DateTime dt => dt.ToString("dd/MM/yyyy HH:mm"),
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        return text.Contains(';') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
