using System.Collections.Generic;
using System.Reactive.Linq;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Pocket;
using static Pocket.Logger<dotnet_repl.KernelCompletion>;
using PrettyPrompt.Documents;
using System.Threading;
using System;

namespace dotnet_repl;

public class KernelCompletion
{
    private readonly Kernel _kernel;

    public KernelCompletion(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async ValueTask<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var command = new RequestCompletions(
            text,
            new LinePosition(0, caret));

        var result = await _kernel.SendAsync(command);

        var completionsProduced = result
                                  .Events
                                  .OfType<CompletionsProduced>()
                                  .FirstOrDefault();

        if (completionsProduced is null)
        {
            return Array.Empty<CompletionItem>();
        }

        return completionsProduced.Completions.ToArray();


        /*
        var matches = FilterItems(text, spanToBeReplaced, completionsProduced);

        return matches;
        */
    }

    private unsafe CompletionItem[] FilterItems(string text, TextSpan spanToBeReplaced, CompletionsProduced completionsProduced)
    {
        if (spanToBeReplaced.Length == 0)
        {
            return completionsProduced.Completions.ToArray();
        }

        // prefix matching time
        ReadOnlySpan<char> prefix = text.AsSpan(spanToBeReplaced.Start, spanToBeReplaced.Length);

        // if the span to be replaced has multiple parts, just use the last one for filtering
        int lastPartIndex = MemoryExtensions.LastIndexOf(prefix, '.');
        if (lastPartIndex >= 0)
        {
            prefix = prefix.Slice(lastPartIndex + 1);
        }

        fixed (char* ptr = prefix)
        {// sneak the pointer into the lambda. i promise it won't be used outside the lifetime of this method :)
            var prefixPtr = ptr; 
            var prefixLen = prefix.Length;
            CompletionItem[] matches = completionsProduced
                          .Completions
                          .OrderBy(c => c.InsertText)
                          .Where(c =>
                          {
                              // recreate the span
                              ReadOnlySpan<char> lastPart = new(prefixPtr, prefixLen);
                              return c.InsertText is not null && c.InsertText.AsSpan().StartsWith(lastPart);
                          })
                          .ToArray();

            Log.Info(
                "buffer: {buffer}, code: {code}, matches: {matches}",
                text,
                prefix.ToString(),
                string.Join(",", matches.Select(m => m.InsertText)));
            return matches;
        }
    }
}