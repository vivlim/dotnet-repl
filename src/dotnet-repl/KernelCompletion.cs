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

    public async Task<CompletionsProduced?> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, string? kernelName, int inputOffset, CancellationToken cancellationToken)
    {
        var linePosition = TextUtils.GetLineNumberForCharIndex(text, caret);
        var command = new RequestCompletions(
            text,
            linePosition, kernelName);

        var result = await _kernel.SendAsync(command);

        var completionsProduced = result
                                  .Events
                                  .OfType<CompletionsProduced>()
                                  .FirstOrDefault();

        if (inputOffset > 0 && completionsProduced is not null && completionsProduced.LinePositionSpan.Start.Line == 0)
        {
            var spanToRemap = completionsProduced.LinePositionSpan;
            var start = new LinePosition(0, spanToRemap.Start.Character + inputOffset);
            var end = spanToRemap.End.Line == 0 ? new LinePosition(0, spanToRemap.End.Character + inputOffset) : spanToRemap.End;

            var offsetSpan = new LinePositionSpan(start, end);
            return new CompletionsProduced(completionsProduced.Completions, completionsProduced.Command as RequestCompletions, offsetSpan);
        }

        return completionsProduced;
    }

    public async Task<SignatureHelpProduced?> GetSignatureHelpAsync(string text, LinePosition linePosition, string? kernel, CancellationToken cancellationToken)
    {
        var command = new RequestSignatureHelp(text, linePosition, kernel);

        var result = await _kernel.SendAsync(command);

        var signatureHelpProduced = result
                                  .Events
                                  .OfType<SignatureHelpProduced>()
                                  .FirstOrDefault();
        return signatureHelpProduced;
    }

    public async Task<HoverTextProduced?> GetHoverTextAsync(string text, LinePosition linePosition, string? kernel, CancellationToken cancellationToken)
    {
        var command = new RequestHoverText(text, linePosition, kernel);

        var result = await _kernel.SendAsync(command);

        var hoverTextProduced = result
                                  .Events
                                  .OfType<HoverTextProduced>()
                                  .FirstOrDefault();
        return hoverTextProduced;
    }

    // totally unnecessary. we don't filter here
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