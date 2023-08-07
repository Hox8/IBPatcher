using System;
using System.IO;

namespace IBPatcher;

public static class Globals
{
    public const string TitleString = "IBPatcher v1.3.0";
    
    /// Config variable affecting max length of separators and mod names when printed during mod processing.
    public const int MaxStrLength = 50;
    public static readonly string Separator = new('=', MaxStrLength + 10);
    public static readonly string TempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public static void PrintColor(string content, ConsoleColor color)
    {
        var old = Console.ForegroundColor;

        Console.ForegroundColor = color;
        Console.Write(content);

        Console.ForegroundColor = old;
    }

    public static void PrintInfo()
    {
        Console.WriteLine(Separator);
        PrintColor($"{TitleString}\n", ConsoleColor.Green);
        Console.WriteLine("Copyright © 2023 Hox, GPL v3.0");
        Console.WriteLine(Separator);
    }

    public static void Close()
    {
        Console.WriteLine("Press and key to close...");
#if !DEBUG
        Console.ReadKey();
#endif
    }
}