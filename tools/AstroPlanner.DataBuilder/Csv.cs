using System.Text;

namespace AstroPlanner.DataBuilder;

/// <summary>Minimal RFC 4180 reader: delimiter, double-quoted fields with "" escapes, header row.</summary>
internal static class Csv
{
    /// <summary>Reads the rows of <paramref name="reader"/> as dictionaries keyed by the header names.</summary>
    public static IEnumerable<IReadOnlyDictionary<string, string>> Read(TextReader reader, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = reader.ReadLine() is { } headerLine
            ? Split(headerLine, delimiter)
            : throw new InvalidDataException("The CSV file is empty.");

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = Split(line, delimiter);
            var row = new Dictionary<string, string>(header.Count, StringComparer.Ordinal);
            for (var index = 0; index < header.Count; index++)
            {
                row[header[index]] = index < fields.Count ? fields[index] : string.Empty;
            }

            yield return row;
        }
    }

    public static List<string> Split(string line, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(line);

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var c = line[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
