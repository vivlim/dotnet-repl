using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Documents;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Pocket;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;
using Spectre.Console;
using static dotnet_repl.AnsiConsoleExtensions;
using Formatter = Microsoft.DotNet.Interactive.Formatting.Formatter;

namespace dotnet_repl;

public class Repl : IDisposable
{
    private readonly CompositeKernel _kernel;

    private readonly CompositeDisposable _disposables = new();

    private readonly CancellationTokenSource _disposalTokenSource = new();

    private readonly Subject<Unit> _readyForInput = new();

    private Prompt GetLineEditorLanguageLocal(string kernelName)
    {
        var prompt = new Prompt(
            callbacks: new ReplPromptCallbacks(this),
            configuration: new PromptConfiguration(
                prompt: new FormattedString($"{kernelName}> ", new FormatSpan(0, 1, AnsiColor.Red), new FormatSpan(1, 1, AnsiColor.Yellow), new FormatSpan(2, 1, AnsiColor.Green)),
                completionItemDescriptionPaneBackground: AnsiColor.Rgb(30, 30, 30),
                selectedCompletionItemBackground: AnsiColor.Rgb(30, 30, 30),
                selectedTextBackground: AnsiColor.Rgb(20, 61, 102))
            );

        return prompt;
    }

    public Repl(
        CompositeKernel kernel,
        Action quit,
        IAnsiConsole ansiConsole,
        SystemConsole? inputSource = null)
    {
        _kernel = kernel;
        QuitAction = quit;
        AnsiConsole = ansiConsole;
        InputSource = inputSource;
        Theme = KernelSpecificTheme.GetTheme(kernel.DefaultKernelName) ?? new CSharpTheme();

        _disposables.Add(() => _disposalTokenSource.Cancel());

        if (ansiConsole.Profile.Capabilities.Ansi)
        {
            LineEditorProvider = new LineEditorServiceProvider(new KernelCompletion(_kernel));
            LineEditor = GetLineEditorLanguageLocal(_kernel.DefaultKernelName);
        }
        
        _kernel.AddMiddleware(async (command, context, next) =>
        {
            await next(command, context);

            KernelCommand root = command;

            while (root.Parent is { } parent)
            {
                root = parent;
            }

            if (_kernel.Directives.FirstOrDefault(c => $"Directive: {c.Name}" == command.ToString()) is { } directive)
                LineEditor = GetLineEditorLanguageLocal(directive.Name[2..]);
        });

        SetTheme();

        this.AddKeyBindings();
    }

    public IObservable<Unit> ReadyForInput => _readyForInput;

    public IAnsiConsole AnsiConsole { get; }

    public SystemConsole? InputSource { get; }

    public Prompt? LineEditor { get; private set; }

    internal Action QuitAction { get; }

    public KernelSpecificTheme Theme { get; set; }

    private LineEditorServiceProvider? LineEditorProvider { get; }

    public async Task StartAsync()
    {
        var ready = ReadyForInput.Replay();
        var _ = Task.Run(() => RunAsync(_ => { }));
        await ready.FirstAsync();
    }

    public async Task RunAsync(
        Action<int> setExitCode,
        InteractiveDocument? notebook = null,
        bool exitAfterRun = false)
    {
        var queuedSubmissions = new Queue<string>(notebook?.Elements.Select(c => $"#!{c.KernelName}\n{c.Contents}") ?? Array.Empty<string>());

        if (!queuedSubmissions.Any())
        {
            exitAfterRun = false;
        }

        while (!_disposalTokenSource.IsCancellationRequested)
        {
            await Task.Yield();

            if (!queuedSubmissions.TryDequeue(out var input))
            {
                if (!exitAfterRun)
                {
                    SetTheme();
                    _readyForInput.OnNext(Unit.Default);
                    var response = await LineEditor!.ReadLineAsync();
                    // viv todo: should more of the response be kept?
                    input = response.Text;
                }
            }

            if (_disposalTokenSource.IsCancellationRequested || input is null)
            {
                setExitCode(129);
                break;
            }
            else
            {
                var result = await RunKernelCommand(new SubmitCode(input)); // viv: enter is pressed

                if (result.Events.Last() is CommandFailed)
                {
                    setExitCode(2);
                }

                if (exitAfterRun && queuedSubmissions.Count == 0)
                {
                    break;
                }
            }
        }

        _readyForInput.OnCompleted();
    }

    private void SetTheme()
    {
        //if (KernelSpecificTheme.GetTheme(_kernel.DefaultKernelName) is { } theme)
        //{
        //    Theme = theme;

        //    if (LineEditor?.Prompt is DelegatingPrompt d)
        //    {
        //        d.InnerPrompt = theme.Prompt;
        //    }
        //}
    }

    private async Task<KernelCommandResult> RunKernelCommand(KernelCommand command)
    {
        StringBuilder? stdOut = default;
        StringBuilder? stdErr = default;

        Task<KernelCommandResult>? result = default;

        var events = _kernel.KernelEvents.Replay();

        using var _ = events.Connect();

        await AnsiConsole.Status().StartAsync(Theme.StatusMessageGenerator.GetStatusMessage(), async ctx =>
        {
            ctx.Spinner(new ClockSpinner());

            var t = events.FirstOrDefaultAsync(
                e => e is DisplayEvent or CommandFailed or CommandSucceeded);

            result = _kernel.SendAsync(command, _disposalTokenSource.Token);

            await t;
        });

        var waiting = new Panel("").NoBorder();

        var tcs = new TaskCompletionSource();

        await AnsiConsole
              .Live(waiting)
              .StartAsync(async ctx =>
              {
                  using var _ = events.Subscribe(@event =>
                  {
                      switch (@event)
                      {
                          // events that tell us whether the submission was valid
                          case IncompleteCodeSubmissionReceived incomplete when incomplete.Command == command:
                              break;

                          case CompleteCodeSubmissionReceived complete when complete.Command == command:
                              break;

                          case CodeSubmissionReceived codeSubmissionReceived:
                              break;

                          // output / display events

                          case ErrorProduced errorProduced:
                              ctx.UpdateTarget(GetErrorDisplay(errorProduced, Theme));

                              break;

                          case StandardOutputValueProduced standardOutputValueProduced:

                              stdOut ??= new StringBuilder();
                              stdOut.Append(standardOutputValueProduced.PlainTextValue());

                              break;

                          case StandardErrorValueProduced standardErrorValueProduced:

                              stdErr ??= new StringBuilder();
                              stdErr.Append(standardErrorValueProduced.PlainTextValue());

                              break;

                          case DisplayedValueProduced displayedValueProduced:
                              ctx.UpdateTarget(GetSuccessDisplay(displayedValueProduced, Theme));
                              ctx.Refresh();
                              break;

                          case DisplayedValueUpdated displayedValueUpdated:
                              ctx.UpdateTarget(GetSuccessDisplay(displayedValueUpdated, Theme));
                              break;

                          case ReturnValueProduced returnValueProduced:

                              if (returnValueProduced.Value is DisplayedValue)
                              {
                                  break;
                              }

                              ctx.UpdateTarget(GetSuccessDisplay(returnValueProduced, Theme));
                              break;

                          // command completion events

                          case CommandFailed failed when failed.Command == command:
                              AnsiConsole.RenderBufferedStandardOutAndErr(Theme, stdOut, stdErr);
                              ctx.UpdateTarget(GetErrorDisplay(failed, Theme));
                              tcs.SetResult();

                              break;

                          case CommandSucceeded succeeded when succeeded.Command == command:
                              AnsiConsole.RenderBufferedStandardOutAndErr(Theme, stdOut, stdErr);
                              tcs.SetResult();

                              break;
                      }
                  });

                  await tcs.Task;
              });

        return await result!;
    }

    public void Dispose() => _disposables.Dispose();

    public static void UseDefaultSpectreFormatting()
    {
        Formatter.ResetToDefault();
        Formatter.DefaultMimeType = PlainTextFormatter.MimeType;
        new DefaultSpectreFormatterSet().Register();
    }

    private class ReplPromptCallbacks(Repl repl) : PromptCallbacks
    {
        protected override async Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
        {
            if (repl.LineEditorProvider is not IServiceProvider services)
            {
                return Array.Empty<PrettyPrompt.Completion.CompletionItem>();
            }
            if (services.GetService(typeof(KernelCompletion)) is not KernelCompletion kernelCompletion)
            {
                return Array.Empty<PrettyPrompt.Completion.CompletionItem>();
            }

            var kernelItems = await kernelCompletion.GetCompletionItemsAsync(text, caret, spanToBeReplaced, cancellationToken);
            var prettyPromptItems = ConvertCompletionItem(kernelItems).ToArray();
            return prettyPromptItems;
        }

        private IEnumerable<PrettyPrompt.Completion.CompletionItem> ConvertCompletionItem(IEnumerable<CompletionItem> items)
        {
            foreach (var item in items)
            {
                var color = item.Kind switch
                {
                    _ => AnsiColor.White,
                };
                var displayText = new FormattedString(item.DisplayText, new FormatSpan(0, item.DisplayText.Length, color));
                var description = new FormattedString(item.Documentation);
                yield return new PrettyPrompt.Completion.CompletionItem(
                    replacementText: item.InsertText,
                    displayText: item.DisplayText,
                    filterText: item.FilterText ?? item.InsertText,
                    getExtendedDescription: _ => Task.FromResult(description));
            }

        }

    }
}