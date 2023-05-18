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

using Ionic.Zip;
using UnrealLib;

namespace IBPatcher
{
    public class IPA
    {
        public static void SaveProgress(object sender, SaveProgressEventArgs e)
        {
            if (e.EventType == ZipProgressEventType.Saving_BeforeWriteEntry)
            {
                string percentage = ((float)e.EntriesSaved / e.EntriesTotal * 100).ToString("00.0");
                Console.Write(new string('\b', 7) + percentage + "% ]");
            }
        }

        public enum ZipError : byte
        {
            None = 0,
            FileNotFound,
            InvalidArchive,
            NonAppleArchive,
            UnknownGame,
        }

        public ZipFile Archive { get; private set; }
        public ZipError Error { get; private set; }
        public Game Game { get; private set; }
        public string AppFolder { get; private set; }
        public string Filepath { get; private set; }
        public string Filename { get; private set; }

        public IPA(string filePath)
        {
            Filepath = filePath;
            Filename = Path.GetFileName(filePath);

            if ((Error = ValidateArchive(filePath)) != ZipError.None) return;

            Archive = ZipFile.Read(filePath);

            // Test for the presence of the "Payload" folder
            // We cannot check if "Payload/" exists as directory entries are not guaranteed
            if (!Archive.EntryFileNames.Any(file => file.StartsWith("Payload/")))
            {
                Error = ZipError.NonAppleArchive;
                return;
            }

            if (Archive.ContainsEntry("Payload/VoteGame.app/"))
            {
                AppFolder = "Payload/VoteGame.app/";
                Game = Game.VOTE;
            }
            else if (Archive.ContainsEntry("Payload/SwordGame.app/CookedIPhone/Entry.xxx"))
            {
                AppFolder = "Payload/SwordGame.app/";
                using (var br = new BinaryReader(new MemoryStream()))
                {
                    Archive.SelectEntries("Entry.xxx", "Payload/SwordGame.app/CookedIPhone/").First().Extract(br.BaseStream);
                    br.BaseStream.Position = 4;
                    Game = br.ReadInt16() switch
                    {
                        > 864 => Game.IB3,
                        > 788 => Game.IB2,
                        _ => Game.IB1,
                    };
                }
            }
            else
            {
                Error = ZipError.UnknownGame;
                return;
            }
        }

        public string GetErrorString()
        {
            return Error switch
            {
                ZipError.FileNotFound => $"'{Filepath}' is not a valid path.\nDouble-check the file exists, or try drag and dropping instead.",
                ZipError.InvalidArchive => $"{Filename} is not a valid zip archive. Try using a fresh IPA and try again.",
                ZipError.NonAppleArchive => $"{Filename} is not an Apple app archive!",
                ZipError.UnknownGame => $"{Filename} is not a valid game. Make sure all of its files are intact.",
                _ => ""
            };
        }

        public string GetGameString()
        {
            return Game switch
            {
                Game.IB3 => "Infinity Blade III",
                Game.IB2 => "Infinity Blade II",
                Game.IB1 => "Infinity Blade I",
                Game.VOTE => "VOTE!!!",
                _ => "Unknown Game"
            };
        }

        private static ZipError ValidateArchive(string filePath)
        {
            if (!File.Exists(filePath)) return ZipError.FileNotFound;
            if (!ZipFile.IsZipFile(filePath)) return ZipError.InvalidArchive;
            return ZipError.None;
        }
    }
}