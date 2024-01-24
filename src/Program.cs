using System;
using System.IO;
using System.Text;

namespace IBPatcher;

internal static class Program
{
    internal static void Main(string[] args)
    {
        Console.Title = Globals.AppTitle;
        Console.OutputEncoding = Encoding.UTF8;
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);    // Force set the working directory to that of the executable
        Directory.CreateDirectory(Globals.CachePath);               // Create a directory to store temporary files. Disposed of during PrepareForExit()

        // Terminal "fluff" is often printed at the top of the window which we'll get rid of here
        Globals.ClearConsole();

#if DEBUG
        args = [@"C:\Users\User 1\Downloads\Infinity Blade II v1.3.5 (64-bit & 32-bit).ipa"];
#endif

        // Print instructions if no arguments were passed
        if (args.Length != 1)
        {
            PrintApplicationInfo();
#if UNIX
            // Unix cannot drag-and-drop onto executables, so prompt to drag-and-drop into the active Terminal window instead
            Console.Write("Drag an IPA onto this window to begin: ");

            // Trim leading/trailing whitespace, quotation chars, and any escaped whitespace
            args = [Console.ReadLine()?.Trim().Trim('\"').Replace("\\", "") ?? ""];
            Console.WriteLine();
#else
            // Do not allow the above Unix method on Windows. Tell user to drag-and-drop instead
            Console.WriteLine("Start the patcher by drag-and-dropping an IPA onto the executable.");
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

        Globals.ClearConsole();
        PrintIpaInfo(ipa);

        var modContext = new ModContext(ipa);
        modContext.LoadMods();

        // If there weren't any mods in the loaded game's mod directory, prompt the user to obtain some
        if (modContext.ModCount == 0)
        {
            Console.WriteLine($"\n - No mods found under './{modContext.ModFolderRelative}'!\n   Place some mods in the folder and re-run the patcher.");
            Console.WriteLine("\n   Refer to the GitHub readme for info on how to obtain mods.");
            PrepareForExit();
            return;
        }

        modContext.ApplyMods();
        PrepareForExit();
    }

    /// <summary>
    /// Prints the loaded game's title and engine info to the console.
    /// </summary>
    /// <param name="ipa"></param>
    private static void PrintIpaInfo(IPA ipa)
    {
        Console.WriteLine(Globals.Separator);

        string gameTitle = UnrealLib.Globals.GetString(ipa.Game, false);
        string gameVersion = $"v{ipa.PackageVersion}, {ipa.EngineVersion}";
        
        // Print the the IPA title in green if we've got the latest version of that particular game,
        // otherwise, print it in yellow. A warning is also displayed during ModContext::ApplyMods()
        var color = ipa.IsLatestVersion ? ConsoleColor.Green : ConsoleColor.DarkYellow;

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
    /// Prints application info to the console.
    /// </summary>
    private static void PrintApplicationInfo()
    {
        Console.WriteLine(Globals.Separator);
        Globals.PrintColor(Globals.AppTitle, ConsoleColor.Green);
        Console.WriteLine($"\nCopyright © 2024 Hox, GPL v3.0\n{Globals.Separator}\n");
    }
}
