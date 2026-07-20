using System.Buffers;
using System.Text;

namespace Part11_1_PerformanceAdvanced;

public static class CourseCodeParser
{
    private const int StackAllocThreshold = 128;

    public static CourseCodeParseResult? ParseBaseline(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
        {
            return null;
        }

        var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !int.TryParse(parts[1], out var number))
        {
            return null;
        }

        return new CourseCodeParseResult(parts[0], number, parts[2], parts[3]);
    }

    public static CourseCodeParseResult? ParseSpan(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
        {
            return null;
        }

        var utf8Length = Encoding.UTF8.GetByteCount(code);
        byte[]? pooled = null;
        Span<byte> buffer = utf8Length <= StackAllocThreshold
            ? stackalloc byte[StackAllocThreshold]
            : (pooled = ArrayPool<byte>.Shared.Rent(utf8Length));
        try
        {
            Encoding.UTF8.GetBytes(code, buffer);
            return ParseUtf8(buffer[..utf8Length]);
        }
        finally
        {
            if (pooled is not null)
            {
                ArrayPool<byte>.Shared.Return(pooled);
            }
        }
    }

    private static CourseCodeParseResult? ParseUtf8(ReadOnlySpan<byte> utf8)
    {
        Span<Range> ranges = stackalloc Range[4];
        var count = Split(utf8, (byte)'-', ranges);
        if (count != 4)
        {
            return null;
        }

        var subject = Encoding.UTF8.GetString(utf8[ranges[0]]);
        var numberSpan = utf8[ranges[1]];
        if (!TryParseInt(numberSpan, out var number))
        {
            return null;
        }

        var section = Encoding.UTF8.GetString(utf8[ranges[2]]);
        var term = Encoding.UTF8.GetString(utf8[ranges[3]]);
        return new CourseCodeParseResult(subject, number, section, term);
    }

    private static int Split(ReadOnlySpan<byte> source, byte delimiter, Span<Range> ranges)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i < source.Length && count < ranges.Length; i++)
        {
            if (source[i] == delimiter)
            {
                if (i > start)
                {
                    ranges[count++] = start..i;
                }
                start = i + 1;
            }
        }
        if (start < source.Length && count < ranges.Length)
        {
            ranges[count++] = start..source.Length;
        }
        return count;
    }

    private static bool TryParseInt(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        foreach (var b in span)
        {
            if (b < (byte)'0' || b > (byte)'9')
            {
                return false;
            }
            value = value * 10 + (b - (byte)'0');
        }
        return span.Length > 0;
    }
}