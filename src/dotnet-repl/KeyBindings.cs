﻿using System;
using dotnet_repl.LineEditorCommands;

namespace dotnet_repl;

internal static class KeyBindings
{
    public static void AddKeyBindings(this Repl repl)
    {
        //    if (repl.LineEditor is not { } editor)
        //    {
        //        return;
        //    }

        //    // Remove old keybinding for autocomplete
        //    editor.KeyBindings.Remove(ConsoleKey.Tab);
        //    editor.KeyBindings.Remove(ConsoleKey.Tab, ConsoleModifiers.Control);

        //    editor.KeyBindings.Add(
        //        ConsoleKey.Tab,
        //        () => new CompletionCommand(AutoComplete.Next));

        //    editor.KeyBindings.Add(
        //        ConsoleKey.Tab,
        //        ConsoleModifiers.Shift,
        //        () => new CompletionCommand(AutoComplete.Previous));

        //    editor.KeyBindings.Add(
        //        ConsoleKey.C,
        //        ConsoleModifiers.Control,
        //        () => new Clear());

        //    editor.KeyBindings.Add(
        //        ConsoleKey.D,
        //        ConsoleModifiers.Control,
        //        () => new Quit(repl.QuitAction));

        //    editor.KeyBindings.Add<Clear>(
        //        ConsoleKey.C,
        //        ConsoleModifiers.Control | ConsoleModifiers.Alt);
        //}
    }
}