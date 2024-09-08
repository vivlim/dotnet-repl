using Microsoft.DotNet.Interactive;
using System;
using System.Collections.Generic;
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
}
