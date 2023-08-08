using System;
using System.IO;
using System.Text;

namespace IBPatcher;

internal static class Program
{
    private static void Main(string[] args)
    {
        // Force working dir to application
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Console.Title = Globals.TitleString;
        Console.OutputEncoding = Encoding.Default;  // So we can output unicode mod names.
        
#if DEBUG
        args = new[] { @"C:\Users\User 1\Documents\.IPAs\IB2\Infinity Blade II v1.3.5 (64-bit).ipa" };
#endif
        
        if (args.Length == 0)
        {
            Globals.PrintInfo();
            Console.WriteLine("\nDrag and drop an IPA or zip file to get started.\n");
            Globals.Close();
            return;
        }
        
        using var ipa = new IPA(args[0]);
        if (ipa.HasError)
        {
            Globals.PrintInfo();
            Console.WriteLine(ipa.ErrorContext);
            Globals.Close();
            return;
        }
        
        // Announce the loaded game.
        Console.WriteLine(Globals.Separator);
        Globals.PrintColor($" {UnLib.Globals.GameToString(ipa.Game)}\n", ConsoleColor.Green);
        Console.WriteLine(Globals.Separator);

        using var modContext = new ModContext(ipa);
        if (modContext.ModCount == 0)
        {
            Console.WriteLine($"\nCouldn't find any mods under '{modContext.ModDirectory}'!");
            Console.WriteLine("Place some mods in the mod folder and run the patcher again.\n");
            Globals.Close();
            return;
        }
        
        modContext.PrepareFiles();
        modContext.ApplyMods();
        modContext.Output();
        
        Globals.Close();
    }
}