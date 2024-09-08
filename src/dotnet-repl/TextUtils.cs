using Microsoft.DotNet.Interactive;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dotnet_repl;

public static class TextUtils
{
    public static LinePosition GetLineNumberForCharIndex(string text, int charPosition)
    {
        var lineEnumerator = MemoryExtensions.EnumerateLines(text.AsSpan());
        int lineNum = 0;
        while (lineEnumerator.MoveNext())
        {
            var currentLine = lineEnumerator.Current;
            if (charPosition <= currentLine.Length)
            {
                return new(lineNum, charPosition);
            }

            charPosition -= currentLine.Length + 1; // add one back for newline.
            lineNum++;
        }

        return new(lineNum, charPosition);
    }

    public static bool CurrentLineHasOnlyWhitespaceToLeftOfPosition(string text, int charPosition)
    {
        var lineEnumerator = MemoryExtensions.EnumerateLines(text.AsSpan());
        int lineNum = 0;
        while (lineEnumerator.MoveNext())
        {
            var currentLine = lineEnumerator.Current;
            if (charPosition <= currentLine.Length)
            {
                return currentLine.Slice(0, charPosition).IsWhiteSpace();
            }

            charPosition -= currentLine.Length + 1; // add one back for newline.
            lineNum++;
        }

        return false;
    }

    public static string? ExtractWordAt(string text, LinePosition position, char[] delimiters)
    {
        var lineEnumerator = MemoryExtensions.EnumerateLines(text.AsSpan());
        int lineNum = 0;
        while (lineEnumerator.MoveNext() && lineNum < position.Line)
        {
            lineNum++;
        }

        var currentLine = lineEnumerator.Current;
        var left = currentLine.Slice(0, position.Character);
        int leftIndex = left.LastIndexOfAny(delimiters);

        if (leftIndex == -1)
        {
            return currentLine.ToString();
        }

        if (currentLine.Length + 1 > position.Character)
        {
            var right = currentLine.Slice(0, position.Character);
            int rightIndex = right.IndexOfAny(delimiters);
            return currentLine.Slice(leftIndex, rightIndex - leftIndex).ToString();
        }

        return currentLine.Slice(leftIndex).ToString();

    }
}
