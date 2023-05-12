/*
 * IBPatcher
 * Copyright © 2023 Hox
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text;

namespace IBPatcher
{
    internal class Program
    {
        public static void Close()
        {
            Console.Write("\nPress any key to exit...");
            Console.ReadKey();
        }

        public static void PrintColored(string text, ConsoleColor color, bool doNewline = true)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = color;
            if (doNewline) Console.WriteLine(text);
            else Console.Write(text);
            Console.ForegroundColor = original;
        }

        // Displays a basic appplication info when no arguments are passed
        private static void InfoString(int MaxTextLength)
        {
            Console.WriteLine($"{new string('=', MaxTextLength + 10)}");
            PrintColored("IBPatcher v1.2.1", ConsoleColor.Green, doNewline: false);
            Console.WriteLine("\nCopyright © 2023 Hox, GPL v3.0");
            Console.WriteLine(new string('=', MaxTextLength + 10));
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8; // Allows non-Latin characters to be shown in the console
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);  // @TODO Inefficient because it adds ALL codepages instead of just IBM437
            int MaxTextLength = 50;

            // args = new string[] { @"C:\Users\Administrator\Documents\IBPatcher\Infinity Blade II v1.3.5.ipa" };
            if (args.Length == 0)
            {
                InfoString(MaxTextLength);
                Console.WriteLine($"\nDrag and drop an ipa or zip file onto the patcher to get started.");
                Close();
                return;
            }

            var ipa = new IPA(args[0]);
            if (ipa.Error != IPA.ZipError.None)
            {
                InfoString(MaxTextLength);
                Console.WriteLine($"\n{ipa.GetErrorString()}");
                Close();
                return;
            }

            Mods mods = new Mods(ipa, MaxTextLength);

            Console.Write($"{new string('=', MaxTextLength + 10)}\nGame: ");
            PrintColored(ipa.GetGameString(), ConsoleColor.Green);
            Console.WriteLine(new string('=', MaxTextLength + 10));

            if (mods.TotalScannedMods == 0)
            {
                Console.WriteLine($"\nCouldn't find any mods under 'Mods\\{ipa.Game}'!\nPlace some mods in the folder and try running the patcher again.");
                Close();
                return;
            }

            mods.ApplyMods(ipa);
            Close();
        }
    }
}