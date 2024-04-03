using System;
using System.IO;
using System.Text;

namespace IBPatcher;

// @TODO 1.3.1: rework mod errors + error messages completely. Do not use ErrorHelper<T>!
// Try not to develop any further updates for CLI version of patcher...

internal static class Program
{
    internal static void Main(string[] args)
    {
        Console.Title = Globals.AppTitle;
        Console.OutputEncoding = Encoding.UTF8;

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);    // Ensure working directory is that of the exe and not IPA
        Directory.CreateDirectory(Globals.CachePath);               // Create a directory to store temporary files. Disposed of during PrepareForExit()

        // Terminal "fluff" is often printed at the top of the window which we'll get rid of here
        Globals.ClearConsole();

        // Create mod folders now, as this has been a confusing point for some users.
        // This needs to be wrapped in a try catch block in case a file exists with one of these names
        try
        {
            Directory.CreateDirectory("Mods/IB1");
            Directory.CreateDirectory("Mods/IB2");
            Directory.CreateDirectory("Mods/IB3");
            Directory.CreateDirectory("Mods/VOTE");
        }
        catch
        {
        }

#if DEBUG
        args = [@"C:\Users\User 1\Downloads\Infinity Blade II v1.3.5 (64-bit & 32-bit).ipa"];
#endif

        // Print instructions if no arguments were passed
        if (args.Length != 1)
        {
            PrintApplicationInfo();
#if UNIX
            // Unix cannot drag-and-drop onto executables, so prompt to drag-and-drop into the active Terminal window instead
            Console.Write("Drag an IPA file onto this window to begin: ");

            // Trim leading/trailing whitespace, quotation chars, and any escaped whitespace
            args = [Console.ReadLine()?.Trim().Trim('\"').Replace("\\", "") ?? ""];
            Console.WriteLine();
            Globals.ClearConsole();
#else
            // Do not allow the above Unix method on Windows. Tell user to drag-and-drop instead
            Console.WriteLine("Drag and drop an IPA file onto the patcher executable to get started.");
            PrepareForExit();
            return;
#endif
        }

        // Attempt to load the passed IPA file
        var ipa = new IPA(args[0]);

        if (ipa.HasError)
        {
            PrintApplicationInfo();
            Globals.PrintColor($" - {ipa.GetErrorString()}\n", ConsoleColor.Red);
            PrepareForExit();
            return;
        }

        // Print game title + version info
        PrintIpaInfo(ipa);

        var modContext = new ModContext(ipa);
        modContext.LoadMods();

        // If there weren't any mods in the loaded game's mod directory, prompt the user to obtain some
        if (modContext.ModCount == 0)
        {
            Console.WriteLine($"\n - No mods found under './{modContext.ModFolderRelative}'!\n   Place mods in the folder and restart the patcher.");
            Console.WriteLine("\n   See the included readme file for info on how to obtain mods.");
            PrepareForExit();
            return;
        }

        modContext.ApplyMods();
        PrepareForExit();
    }

    /// <summary>
    /// Prints the loaded game's title and engine info to the console.
    /// </summary>
    /// <param name="ipa"> The loaded game archive. </param>
    private static void PrintIpaInfo(IPA ipa)
    {
        Console.WriteLine(Globals.Separator);

        var gameTitle = UnrealLib.Globals.GetString(ipa.Game, false);
        var gameVersion = $"v{ipa.PackageVersion}, {ipa.EngineVersion}";

        // Print the IPA title in green if we've got the latest version of that particular game,
        // otherwise, print it in yellow. A warning is also displayed during ModContext::ApplyMods()
        var color = ipa.IsLatestVersion ? ConsoleColor.Green : ConsoleColor.Yellow;

        // Print the game's name on the left...
        Globals.PrintColor(gameTitle, color);

        // ...and its version info on the right
        Console.SetCursorPosition(Globals.MaxStringLength - gameVersion.Length, Console.CursorTop);
        Globals.PrintColor($"{gameVersion}\n", color);

        Console.WriteLine(Globals.Separator);
    }

    /// <summary>
    /// Deletes the temporary file cache directory and awaits final keypress on Windows machines.
    /// </summary>
    private static void PrepareForExit()
    {
        // Delete the cache directory we created at the start of the application
        Directory.Delete(Globals.CachePath, true);

        Globals.PressAnyKey();
    }

    /// <summary>
    /// Prints IBPatcher info to the console.
    /// </summary>
    private static void PrintApplicationInfo()
    {
        Console.WriteLine(Globals.Separator);
        Globals.PrintColor(Globals.AppTitle, ConsoleColor.Green);
        Console.WriteLine($"\nCopyright © 2024 Hox, GPL v3.0\n{Globals.Separator}\n");
    }
}
