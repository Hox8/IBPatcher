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
    public class IniPatch
    {
        public string Name;
        public string File;
        public int Offset;
        public PatchType Type;
        public string Value;
        public byte[] Bytes;
        public int? Size;
        public bool Enabled;
    }

    /// <summary>
    /// Container for all of the patches found in a single .ini file
    /// </summary>
    public class IniMod
    {
        public string Name { get; set; }

        public ModError Error = ModError.None;
        public List<IniPatch> Patches = new();

        /// <summary>
        /// Takes an .ini file path and attempts to read all of its patches into memory
        /// </summary>
        public IniMod(string filePath, IPA ipa, Mods mods)
        {
            Name = Path.GetFileNameWithoutExtension(filePath);

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';')) continue;
                if (line.StartsWith('['))
                {
                    Patches.Add(new IniPatch() { Name = line[1..^1], Enabled = true });
                }
                else
                {
                    if ((Error = ProcessLine(line, Patches[^1], ipa, mods)) != ModError.None) return;
                }
            }
        }

        private static ModError ProcessLine(string line, IniPatch patch, IPA ipa, Mods mods)
        {
            string[] sub = line.Split('=', 2);

            if (sub.Length < 2)
            {
                return ModError.BadIni;
            }

            string key = sub[0].Trim();
            string value = sub[1].Trim();

            switch (key.ToUpper())
            {
                case "FILE":
                    patch.File = value;
                    if (!mods.RequiredFiles.Contains(patch.File))
                    {
                        if (!ipa.Archive.ContainsEntry($"{ipa.AppFolder}CookedIPhone/{patch.File}")) return ModError.BadFile;
                        mods.RequiredFiles.Add(patch.File);
                    }
                    return ModError.None;

                case "OFFSET":
                    var sb = new StringBuilder();
                    foreach (char c in value) { if (!char.IsWhiteSpace(c)) sb.Append(c); }

                    string[] offsets = sb.ToString().Split('+');

                    if (!StringToInt(offsets[0], out patch.Offset))
                    {
                        return ModError.BadOffset;
                    }

                    for (int i = 1; i < offsets.Length; i++)
                    {
                        if (StringToInt(offsets[i], out int incr))
                        {
                            patch.Offset += incr;
                        }
                        else return ModError.BadOffset;
                    }
                    return ModError.None;

                case "TYPE":
                    return Mods.ParseType(value, out patch.Type);

                case "VALUE":
                    patch.Value = value;
                    return ModError.None;

                case "SIZE":
                    if (StringToInt(value, out int _size))
                    {
                        patch.Size = _size;
                        return ModError.None;
                    }
                    return ModError.BadSize;

                case "ENABLE":
                case "ENABLED":
                    if (value.ToUpper() == "FALSE") patch.Enabled = false;
                    else if (int.TryParse(value, out int result))
                    {
                        if (result == 0) patch.Enabled = false;
                    }
                    return ModError.None;

                case "ORIGINAL":
                    return ModError.None;

                default:
                    return ModError.BadKey;
            }
        }

        public static bool StringToInt(string value, out int intOut)
        {
            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    intOut = Convert.ToInt32(value[2..], 16);
                    return true;
                }
                intOut = int.Parse(value);
                return true;
            }
            catch
            {
                intOut = default;
                return false;
            }
        }
    }
}