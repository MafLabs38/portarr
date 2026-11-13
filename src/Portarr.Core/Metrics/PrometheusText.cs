using System.Globalization;

namespace Portarr.Core.Metrics;

/// <summary>A sample line: <c>name{label="value",…} value [timestamp]</c>.</summary>
public readonly record struct Sample(string Name, IReadOnlyList<KeyValuePair<string, string>> Labels, double Value)
{
    public string? Label(string name) => Labels.FirstOrDefault(l => l.Key == name).Value;
}

/// <summary>
/// The Prometheus text exposition format (version 0.0.4), one line at a time (D-72): node_exporter and the exporters Portarr reads
/// (the GPU's, D-120) speak it. Only the families a caller wants are parsed; every other line is skipped without being read, a
/// malformed line is skipped too (one bad sample must not hide the machine).
/// </summary>
public static class PrometheusText
{
    /// <summary>
    /// Parses one sample line of a family <paramref name="isWanted"/> accepts; false for any other family or a malformed line. Label
    /// values may hold escaped <c>\\</c>, <c>\"</c> and <c>\n</c>; values may be NaN, ±Inf or in exponent form.
    /// </summary>
    public static bool TryParseSample(string line, Func<string, bool> isWanted, out Sample sample)
    {
        sample = default;
        var nameEnd = line.IndexOfAny(['{', ' ', '\t']);
        if (nameEnd <= 0)
        {
            return false;
        }

        var name = line[..nameEnd];
        if (!isWanted(name))
        {
            return false;
        }

        var labels = new List<KeyValuePair<string, string>>();
        var position = nameEnd;
        if (line[position] == '{' && !TryParseLabels(line, ref position, labels))
        {
            return false;
        }

        var rest = line.AsSpan(position).Trim();
        var valueEnd = rest.IndexOfAny(' ', '\t');
        var valueText = valueEnd < 0 ? rest : rest[..valueEnd];
        if (!TryParseValue(valueText, out var value))
        {
            return false;
        }

        sample = new Sample(name, labels, value);
        return true;
    }

    /// <summary>From the opening brace to just after the closing one.</summary>
    private static bool TryParseLabels(string line, ref int position, List<KeyValuePair<string, string>> labels)
    {
        position++;
        while (position < line.Length)
        {
            while (position < line.Length && line[position] is ' ' or ',')
            {
                position++;
            }

            if (position < line.Length && line[position] == '}')
            {
                position++;
                return true;
            }

            if (!TryReadLabel(line, ref position, out var label))
            {
                return false;
            }

            labels.Add(label);
        }

        return false;
    }

    /// <summary>One <c>name="value"</c>, from the start of the name to just after the closing quote.</summary>
    private static bool TryReadLabel(string line, ref int position, out KeyValuePair<string, string> label)
    {
        label = default;
        var equals = line.IndexOf('=', position);
        if (equals < 0 || equals + 1 >= line.Length || line[equals + 1] != '"')
        {
            return false;
        }

        var key = line[position..equals].Trim();
        position = equals + 2;
        if (!TryReadQuoted(line, ref position, out var value))
        {
            return false;
        }

        label = new KeyValuePair<string, string>(key, value);
        return true;
    }

    /// <summary>A label value up to its closing quote, with the escapes <c>\\</c>, <c>\"</c> and <c>\n</c> resolved.</summary>
    private static bool TryReadQuoted(string line, ref int position, out string value)
    {
        var text = new System.Text.StringBuilder();
        while (position < line.Length && line[position] != '"')
        {
            if (line[position] == '\\' && position + 1 < line.Length)
            {
                position++;
                text.Append(line[position] == 'n' ? '\n' : line[position]);
            }
            else
            {
                text.Append(line[position]);
            }

            position++;
        }

        value = text.ToString();
        if (position >= line.Length)
        {
            return false;
        }

        position++;
        return true;
    }

    private static bool TryParseValue(ReadOnlySpan<char> text, out double value)
    {
        switch (text)
        {
            case "+Inf" or "Inf":
                value = double.PositiveInfinity;
                return true;
            case "-Inf":
                value = double.NegativeInfinity;
                return true;
            case "NaN":
                value = double.NaN;
                return true;
            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
