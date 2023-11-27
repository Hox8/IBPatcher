using System.Text;

namespace IBPatcher
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Title = Globals.AppTitle;
            Console.OutputEncoding = Encoding.Default;

            // Ensure we're working relative to the application's directory and not the IPA's.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

#if UNIX
            // macOS prints some junk at the top of each terminal window which we'll get rid of here
            Globals.ClearConsole();
#endif

            // Print application info
            Console.WriteLine(Globals.Separator);
            Globals.PrintColor(Globals.AppTitle, ConsoleColor.Green);
            Console.WriteLine($"\nCopyright © 2023 Hox, GPL v3.0\n{Globals.Separator}\n");

            if (args.Length != 1)
            {
#if DEBUG
                args = new[] { @"C:\Users\User 1\Downloads\Infinity Blade II v1.3.2 (32-bit).ipa" };
#elif UNIX
                // Unix cannot drag-and-drop onto executables, so drag-and-drop into live Terminal window instead
                Console.Write("Drag an IPA onto this window to begin: ");

                // Trim leading/trailing whitespace, quotation chars, and any escaped whitespace
                args = [Console.ReadLine()?.Trim().Trim('\"').Replace("\\", "") ?? ""];
                Console.WriteLine();
#else
                // Disallow drag-and-dropping into Console for Windows
                Console.WriteLine("Start the patcher by drag-and-dropping an IPA onto the executable.");
                Globals.PressAnyKey();
                return;
#endif
            }

            // IPA requires cache directory to be present
            Directory.CreateDirectory(Globals.CachePath);

            // Try to load the IPA and, if any errors occur, print them to the console
            var ipa = new IPA(args[0]);
            if (ipa.HasError)
            {
                Globals.PrintColor($" - {ipa.ErrorString}\n", ConsoleColor.Red);
                Globals.PressAnyKey();
                return;
            }

            PrintGameString(ipa);

            var modCtx = new ModContext(ipa);
            modCtx.LoadMods();

            // Alert user if no mods were found, where to get some
            if (modCtx.ModCount == 0)
            {
                string modFolderRelative = modCtx.ModFolder[AppContext.BaseDirectory.Length..];
                Console.WriteLine($" - No mods found under '{modFolderRelative}'!\n   Place some mods in the folder and re-run the patcher.");
                Globals.PressAnyKey();
                return;
            }

            modCtx.ApplyMods();

            Globals.PressAnyKey();
        }

        /// <summary>
        /// Prints the loaded game's title and engine info to the console.
        /// </summary>
        /// <param name="ipa"></param>
        private static void PrintGameString(IPA ipa)
        {
            Globals.ClearConsole();
            Console.WriteLine(Globals.Separator);

            string gameTitle = UnrealLib.Globals.GetString(ipa.Game, false);
            string gameVersion = $"v{ipa.EngineVersion}, {ipa.EngineBuild}";
            ConsoleColor color = ipa.IsLatestVersion ? ConsoleColor.Green : ConsoleColor.DarkYellow;

            // Print game title (left-hand side)
            Globals.PrintColor(gameTitle, color);

            // Print game version info (right-hand side)
            Console.SetCursorPosition(Globals.MaxStringLength - gameVersion.Length, Console.CursorTop);
            Globals.PrintColor($"{gameVersion}\n", color);

            Console.WriteLine($"{Globals.Separator}\n");
        }
    }
}
