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
using System.Text;
using Markdig.Helpers;
using System.IO;

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

    public async Task<FormattedValue?> GetHoverTextAsync(string text, CompletionItem completionItem, LinePosition linePosition, string? kernel, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));

        if (kernel is "powershell" or "pwsh")
        {
            if (completionItem.Documentation is string documentationString)
            {
                // If the completion item is a file, the documentation string may be an absolute path.
                try
                {
                    if (Path.IsPathFullyQualified(documentationString))
                    {
                        if (Directory.Exists(documentationString))
                        {
                            var entries = Directory.EnumerateFileSystemEntries(documentationString);
                            var files = entries.Take(20).Select(fp => {
                                if (Directory.Exists(fp))
                                {
                                    return $"[{Path.GetFileName(fp)}]";
                                }
                                return Path.GetFileName(fp);
                            }).ToList();
                            if (files.Count == 20)
                            { 
                                if (entries.TryGetNonEnumeratedCount(out int remainingCount))
                                {
                                    files.Add($"... {remainingCount} more");
                                }
                                else
                                {
                                    files.Add($"... more");
                                }
                            }

                            var dirList = "Directory\n" + string.Join(", ", files);
                            return new FormattedValue("text/plain", dirList);
                        }
                        else if (File.Exists(documentationString))
                        {
                            var info = new FileInfo(documentationString);
                            return new FormattedValue("text/plain", $"File\ncreated: {info.CreationTime}, last write: {info.LastWriteTime}");
                        }

                    }
                }
                catch { }
            }


            // try to get the cmdlet name
            var cmdletName = TextUtils.ExtractWordAt(text, linePosition, [' ', '.', ';', '\n', '(', ')', '{', '}', '[', ']', ':']);
            if (string.IsNullOrEmpty(cmdletName))
            {
                return null;
            }
            foreach (var c in cmdletName)
            {
                if (!c.IsAlphaNumeric() && c != '-')
                {
                    return null;
                }
            }
            // todo sanitize cmdlet name

            //var getHelpOutput = await this.RunCodeAndCollectStdout($"Get-Help {cmdletName}", kernel, 10, timeoutCts.Token);
            //var getHelpOutput = await this.RunCodeAndCollectStdout($"Get-Command {cmdletName}", kernel, 10, timeoutCts.Token);
            //return new FormattedValue("text/plain", getHelpOutput);

            return new FormattedValue("text/plain", $"powershell cmdlet?: {cmdletName}");
        }

        var command = new RequestHoverText(text, linePosition, kernel);

        var result = await _kernel.SendAsync(command, timeoutCts.Token);

        var hoverTextProduced = result
                                  .Events
                                  .OfType<HoverTextProduced>()
                                  .FirstOrDefault();
        return hoverTextProduced?.Content.FirstOrDefault();
    }

    public async Task<string> RunCodeAndCollectStdout(string cmdText, string? kernel, int maxLines, CancellationToken cancellationToken)
    {
        var events = _kernel.KernelEvents.Replay();
        using var _ = events.Connect();
        var tcs = new TaskCompletionSource();
        StringBuilder stdOut = new();

        var command = new SubmitCode(cmdText, kernel);
        var sendTask = _kernel.SendAsync(command);

        int linesCollected = 0;

        await Task.Run(async () =>
        {
            using var _ = events.Subscribe(@event =>
            {
                switch (@event)
                {
                    case StandardOutputValueProduced standardOutputValueProduced:
                        if (linesCollected < maxLines)
                        {
                            stdOut.Append(standardOutputValueProduced.PlainTextValue());
                        }

                        linesCollected++;
                        break;

                    // command completion events

                    case CommandFailed failed when failed.Command == command:
                        tcs.TrySetResult();

                        break;

                    case CommandSucceeded succeeded when succeeded.Command == command:
                        tcs.TrySetResult();

                        break;
                }
            });
            await tcs.Task;
        });
        var result = await sendTask;

        return stdOut.ToString();
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