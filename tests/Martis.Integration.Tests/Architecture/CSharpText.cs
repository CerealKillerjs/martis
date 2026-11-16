using System.Text;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// Drops comments so a source scan sees executable tokens and string literals.
/// Documentation may name a forbidden API; a call site must not.
/// Interpolation holes are scanned as code.
/// </summary>
internal static class CSharpText
{
    public static string WithoutComments(string source)
    {
        var builder = new StringBuilder(source.Length);
        var index = 0;
        Scan(source, builder, ref index, stopAtBrace: false);
        return builder.ToString();
    }

    private static void Scan(string source, StringBuilder builder, ref int index, bool stopAtBrace)
    {
        while (index < source.Length)
        {
            if (stopAtBrace && source[index] == '}')
            {
                return;
            }

            if (TryCopyString(source, builder, ref index))
            {
                continue;
            }

            if (StartsWith(source, index, "//"))
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                index += 2;
                while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
                {
                    index++;
                }

                index = index + 1 < source.Length ? index + 2 : source.Length;
                builder.Append(' ');
                continue;
            }

            builder.Append(source[index]);
            index++;
        }
    }

    private static bool TryCopyString(string source, StringBuilder builder, ref int index)
    {
        if (source[index] == '\'')
        {
            CopyCharLiteral(source, builder, ref index);
            return true;
        }

        var cursor = index;
        var interpolated = false;
        var verbatim = false;

        if (cursor < source.Length && source[cursor] == '$')
        {
            interpolated = true;
            cursor++;
        }

        if (cursor < source.Length && source[cursor] == '@')
        {
            verbatim = true;
            cursor++;
        }

        if (!interpolated && cursor < source.Length && source[cursor] == '$')
        {
            interpolated = true;
            cursor++;
        }

        if (cursor >= source.Length || source[cursor] != '"')
        {
            return false;
        }

        var quoteCount = 0;
        while (cursor + quoteCount < source.Length && source[cursor + quoteCount] == '"')
        {
            quoteCount++;
        }

        if (quoteCount >= 3)
        {
            CopyRawString(source, builder, ref index, quoteCount);
            return true;
        }

        builder.Append(source.AsSpan(index, cursor - index));
        index = cursor;
        CopyQuotedString(source, builder, ref index, interpolated, verbatim);
        return true;
    }

    private static void CopyQuotedString(
        string source,
        StringBuilder builder,
        ref int index,
        bool interpolated,
        bool verbatim)
    {
        builder.Append(source[index]);
        index++;

        while (index < source.Length)
        {
            if (!verbatim && source[index] == '\\' && index + 1 < source.Length)
            {
                builder.Append(source[index]);
                builder.Append(source[index + 1]);
                index += 2;
                continue;
            }

            if (verbatim && source[index] == '"' && index + 1 < source.Length && source[index + 1] == '"')
            {
                builder.Append("\"\"");
                index += 2;
                continue;
            }

            if (source[index] == '"')
            {
                builder.Append('"');
                index++;
                return;
            }

            if (interpolated && source[index] == '{')
            {
                if (index + 1 < source.Length && source[index + 1] == '{')
                {
                    builder.Append("{{");
                    index += 2;
                    continue;
                }

                builder.Append('{');
                index++;
                Scan(source, builder, ref index, stopAtBrace: true);
                if (index < source.Length && source[index] == '}')
                {
                    builder.Append('}');
                    index++;
                }

                continue;
            }

            builder.Append(source[index]);
            index++;
        }
    }

    private static void CopyRawString(string source, StringBuilder builder, ref int index, int quoteCount)
    {
        var closing = new string('"', quoteCount);
        var contentStart = source.IndexOf('"', index);
        if (contentStart < 0)
        {
            builder.Append(source.AsSpan(index));
            index = source.Length;
            return;
        }

        contentStart += quoteCount;
        var closingAt = source.IndexOf(closing, contentStart, StringComparison.Ordinal);
        var end = closingAt < 0 ? source.Length : closingAt + quoteCount;
        builder.Append(source.AsSpan(index, end - index));
        index = end;
    }

    private static void CopyCharLiteral(string source, StringBuilder builder, ref int index)
    {
        builder.Append(source[index]);
        index++;
        if (index < source.Length && source[index] == '\\' && index + 1 < source.Length)
        {
            builder.Append(source[index]);
            builder.Append(source[index + 1]);
            index += 2;
        }
        else if (index < source.Length)
        {
            builder.Append(source[index]);
            index++;
        }

        if (index < source.Length && source[index] == '\'')
        {
            builder.Append(source[index]);
            index++;
        }
    }

    private static bool StartsWith(string source, int index, string value)
    {
        return index + value.Length <= source.Length
            && string.CompareOrdinal(source, index, value, 0, value.Length) == 0;
    }
}
