﻿using System;
using System.IO;

namespace IBPatcher;

public static class Globals
{
    /// <summary>The name of the application, followed by version number.</summary>
    public const string AppTitle = "IBPatcher v1.3.0";  // Don't forget to change this in app.manifest!

    /// <summary>Determines the maximum length of strings used in mod names and separators.</summary>
    public const int MaxStringLength = 65;
    
    /// <summary>Sequence of characters used for "pretty printing" and separating relevant sections of output.</summary>
    public static readonly string Separator = new('=', MaxStringLength);

    /// <summary>Unique path used to store intermediaries across the entire application lifetime.</summary>
    public static readonly string CachePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    /// <summary>Prints the passed string to the console using the desired color.</summary>
    /// <remarks>
    /// - Uses Console.Write()<br/>
    /// - Reverts Console color to previous value on finish
    /// </remarks>
    public static void PrintColor(string content, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;

        Console.ForegroundColor = color;
        Console.Write(content);
        
        Console.ForegroundColor = previous;
    }

    public static void ClearConsole()
    {
        Console.Clear();
#if UNIX
        Console.Write("\x1b[3J");
        Console.SetCursorPosition(0, 0);
#endif
    }

    // Prints the "Press any key..." dialog and awaits a key press.
    // This is not done on Unix systems as the Terminal behaves differently
    public static void PressAnyKey()
    {
#if !UNIX
        Console.Write("\nPress any key to close...");
    #if !DEBUG
            Console.ReadKey();
    #endif
#endif
    }
}