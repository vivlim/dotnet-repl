﻿using System;
using Spectre.Console;

namespace dotnet_repl.LineEditorCommands;

/*
public class Quit : LineEditorCommand
{
    private readonly Action _triggerQuit;

    public Quit(Action triggerQuit)
    {
        _triggerQuit = triggerQuit;
    }

    public override void Execute(LineEditorContext context)
    {
        // TODO: (Execute) this doesn't seem to reach the terminal
        AnsiConsole.Write(new Markup("Quitting..."));

        _triggerQuit();
    }
}
*/