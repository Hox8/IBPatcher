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

using JsonTest2;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnrealLib;
using UnrealLib.Core;
using UnrealLib.Coalesced;

namespace IBPatcher
{
    public enum PatchType : byte
    {
        Byte = 0,
        Boolean,
        UInt8,
        Int32,
        Float,
        String
    }

    // @TODO this has been really neglected, almost entirely unused by Json mods
    public enum ModError : byte
    {
        None = 0,

        // Format
        BadIni,
        BadJson,

        WrongGame,

        // Patch fields
        BadFile,
        NoFile,
        BadOffset,
        NoOffset,
        BadType,
        NoType,
        BadSize,
        BadKey,

        // Data types
        BadByte,
        BadBool,
        BadUInt8,
        BadInt32,
        BadFloat,
        BadString,   // Always overflow?

        BadFName,
        BadFNameInstance,
        BadUObject,
        ObjectNotExport,
        UObjectOverflow,
    }

    public class Mods
    {
        static readonly Regex RegexObjectReference = new(@"\[(.*?)\]", RegexOptions.Compiled);
        static readonly Regex RegexNameReference = new(@"\{(.*?)\}", RegexOptions.Compiled);

        public List<string> RequiredFiles = new();

        public List<IniMod> IniMods = new();
        public List<JsonMod> JsonMods = new();
        public List<string> CopyMods = new();

        public Dictionary<string, UPK> UPKStreams;
        public Dictionary<string, Coalesced> CoalStreams;

        // Paths
        public string PathMods;
        public string PathIPAOut;
        public string PathFilesOut;

        // Stats + misc variables
        public int MaxCharLength;
        public int TotalScannedMods = 0;    // Amount of mods scanned in, regardless of any errors incurred
        public int CurrentModIndex = 0;    // Amount of mods which have passed stage 2 processing?. Incremented through PrintModString()
        public int SkippedMods = 0;

        public Mods(IPA ipa, int MaxTextLen)
        {
            // Initialize paths
            PathMods = $"{AppContext.BaseDirectory}Mods\\{ipa.Game}";
            PathIPAOut = ipa.Filepath;
            PathIPAOut = $"{Path.ChangeExtension(PathIPAOut, null)} - Modded.ipa";
            PathFilesOut = $"{AppContext.BaseDirectory}Output\\{ipa.Game}";
            MaxCharLength = MaxTextLen;

            if (!Directory.Exists(PathMods)) Directory.CreateDirectory(PathMods);
            foreach (string filePath in Directory.GetFiles(PathMods))
            {
                string fileName = Path.GetFileName(filePath);
                switch (Path.GetExtension(fileName).ToUpper())
                {
                    case ".INI":
                        IniMods.Add(new IniMod(filePath, ipa, this));
                        break;
                    case ".BIN":
                        CopyMods.Add(fileName);
                        break;
                    case ".JSON":
                        JsonMods.Add(new JsonMod(filePath, ipa, this));
                        break;
                    default:
                        if (fileName == "Commands.txt" || fileName == "UE3CommandLine.txt")
                        {
                            CopyMods.Add(fileName);
                        }
                        break;
                }
            }
            TotalScannedMods = IniMods.Count + CopyMods.Count + JsonMods.Count;
        }

        public static string StringError(ModError err)
        {
            return err switch
            {
                ModError.BadIni => "Invalid ini format",
                ModError.BadFile => "File could not be found",
                ModError.NoFile => "File was not specified",
                ModError.BadOffset => "Offset is invalid",
                ModError.NoOffset => "Offset was not specified",
                ModError.BadType => "Type is invalid",
                ModError.NoType => "Type was not specified",
                ModError.BadSize => "Size is not a valid integer",
                ModError.BadKey => "Invalid key",
                ModError.BadByte => "Byte value is invalid",
                ModError.BadBool => "Boolean value is invalid",
                ModError.BadUInt8 => "UInt8 value is invalid",
                ModError.BadInt32 => "Int32 value is invalid",
                ModError.BadFloat => "Float value is invalid",
                ModError.BadString => "String value overflowed size value",
                ModError.BadFName => "Name reference was not found",
                ModError.BadFNameInstance => "Name reference instance is an invalid integer",
                ModError.BadUObject => "UObject reference could not be found",
                ModError.ObjectNotExport => "Object reference does not point to an export object",
                ModError.UObjectOverflow => "Patch exceeds the bounds of its UObject",
                _ => "Unhandled error"
            };
        }

        private void PrintModString(string modName)
        {
            CurrentModIndex++;
            var sb = new StringBuilder();
            sb.Append($"  {CurrentModIndex.ToString("D2")} - {Path.GetFileNameWithoutExtension(modName)}");

            // Truncate name if it exceeds TargetNameLength
            sb.Length = Math.Min(sb.Length, MaxCharLength - 4);
            sb.Append(' ');
            sb.Append('.', MaxCharLength - sb.Length);
            sb.Append(' ');

            Console.Write(sb);
        }

        /// <summary>
        /// Extracts the list of files from RequiredFiles from the IPA
        /// </summary>
        /// <param name="ipa"></param>
        public void ExtractRequiredFiles(IPA ipa)
        {
            UPKStreams = new();
            CoalStreams = new();

            Console.Write($"Unpacking game data... [ 00.0% ]");
            for (int i = 0; i < RequiredFiles.Count; i++)
            {
                var ms = new MemoryStream();
                ipa.Archive.SelectEntries(RequiredFiles[i], $"{ipa.AppFolder}CookedIPhone/").First().Extract(ms);

                if (RequiredFiles[i].EndsWith(".bin"))  // Coalesced file
                {
                    CoalStreams.Add(RequiredFiles[i], new Coalesced(ms, ipa.Game));
                }
                else  // UPK file
                {
                    UPKStreams.Add(RequiredFiles[i], new UPK(ms));
                }

                string percentage = ((float)i / RequiredFiles.Count * 100).ToString("00.0");
                Console.Write(new string('\b', 7) + percentage + "% ]");
            }
            Console.WriteLine(new string('\b', 9) + "[SUCCESS]\n");
        }

        #region PatchProcessing

        public static string RemoveWhitespace(string value)
        {
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        public static ModError ParseBytes(string contents, UPK UPK, out byte[] bytes)
        {
            // not using RemoveWhitespace because following ref converters can use the same stringbuilder object
            var sb = new StringBuilder();

            // Find and remove all whitespace.
            foreach (char c in contents)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }

            if (contents.Contains('{')) // Convert any name references ('{}') using regex matching
            {
                foreach (Match match in RegexNameReference.Matches(sb.ToString()))
                {
                    string[] sub = match.Groups[1].Value.Split(',', 2);
                    int instance = 0;

                    if (sub.Length == 2)    // If an instance was provided, try and parse it
                    {
                        try
                        {
                            instance = int.Parse(sub[1]) + 1;
                        }
                        catch
                        {
                            bytes = default;
                            return ModError.BadFNameInstance;
                        }
                    }

                    int nameIndex = UPK.GetNameIndex(sub[0]);
                    if (nameIndex == -1)
                    {
                        bytes = default;
                        return ModError.BadFName;
                    }

                    string _name = BinaryPrimitives.ReverseEndianness(nameIndex).ToString("X8");
                    string _instance = BinaryPrimitives.ReverseEndianness(instance).ToString("X8");
                    sb.Replace(match.Value, $"{_name}{_instance}");
                }
            }

            if (contents.Contains('['))  // Object references ('[]')
            {
                foreach (Match match in RegexObjectReference.Matches(sb.ToString()))
                {
                    int objectIndex = UPK.GetObjectIndex(match.Groups[1].Value);
                    if (objectIndex == 0)
                    {
                        bytes = default;
                        return ModError.BadUObject;
                    }

                    sb.Replace(match.Value, $"{BinaryPrimitives.ReverseEndianness(objectIndex):X}");
                }
            }

            try
            {
                bytes = Convert.FromHexString(sb.ToString());
                return ModError.None;
            }
            catch (FormatException)
            {
                bytes = null;
                return ModError.BadByte;
            }
        }

        public static ModError ParseBool(string contents, out byte[] bytes)
        {
            contents = contents.Trim();
            if (bool.TryParse(contents, out bool bValue))
            {
                bytes = new byte[] { bValue ? (byte)1 : (byte)0 };
                return ModError.None;
            }
            if (int.TryParse(contents, out int iValue))
            {
                bytes = new byte[] { iValue != 0 ? (byte)1 : (byte)0 };
                return ModError.None;
            }
            bytes = null;
            return ModError.BadBool;
        }

        public static ModError ParseUInt8(string contents, out byte[] bytes)
        {
            if (byte.TryParse(contents, out byte value))
            {
                bytes = new byte[] { value };
                return ModError.None;
            }

            bytes = null;
            return ModError.BadUInt8;
        }

        public static ModError ParseInt32(string contents, out byte[] bytes)
        {
            if (int.TryParse(contents, out int value))
            {
                bytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
                return ModError.None;
            }

            bytes = null;
            return ModError.BadInt32;
        }

        public static ModError ParseFloat(string contents, out byte[] bytes)
        {
            if (float.TryParse(contents, out float value))
            {
                bytes = new byte[4];
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                return ModError.None;
            }

            bytes = null;
            return ModError.BadFloat;
        }

        // @TODO Needs a tidy-up
        public static ModError ParseString(string contents, int? size, out byte[] bytes)
        {
            bool bIsUnicode = false;

            if (size.HasValue)
            {
                if (size < 0)
                {
                    bIsUnicode = true;
                    size = -size;
                }

                if (contents.Length > size)
                {
                    bytes = null;
                    return ModError.BadString;
                }
            }

            if (bIsUnicode) bytes = MemoryMarshal.Cast<char, byte>(contents + (char)0).ToArray();
            else
            {
                bytes = new byte[contents.Length + (size.HasValue ? 1 : 0)];
                for (int i = 0; i < contents.Length; i++)
                {
                    bytes[i] = (byte)contents[i];
                }
            }
            return ModError.None;
        }

        public static ModError ParseType(string type, out PatchType Type)
        {
            switch (type.ToUpper())
            {
                case "BYTE":
                case "BYTES":
                    Type = PatchType.Byte;
                    return ModError.None;
                case "BOOL":
                case "BOOLEAN":
                    Type = PatchType.Boolean;
                    return ModError.None;
                case "INT8":
                case "UINT8":
                    Type = PatchType.UInt8;
                    return ModError.None;
                case "INT":
                case "INT32":
                case "INTEGER":
                    Type = PatchType.Int32;
                    return ModError.None;
                case "FLOAT":
                    Type = PatchType.Float;
                    return ModError.None;
                case "STRING":
                    Type = PatchType.String;
                    return ModError.None;
                case null:
                    Type = default;
                    return ModError.NoType;
                default:
                    Type = default;
                    return ModError.BadType;
            }
        }

        public static ModError ParsePatch(string contents, UPK UPK, PatchType? type, int? size, out byte[] bytes)
        {
            // @COMPATIBILITY with old ini parameters
            if (size == 1 && type == PatchType.Int32) type = PatchType.UInt8;

            switch (type)
            {
                case PatchType.Byte:
                    return ParseBytes(contents, UPK, out bytes);
                case PatchType.Boolean:
                    return ParseBool(contents, out bytes);
                case PatchType.UInt8:
                    return ParseUInt8(contents, out bytes);
                case PatchType.Int32:
                    return ParseInt32(contents, out bytes);
                case PatchType.Float:
                    return ParseFloat(contents, out bytes);
                case PatchType.String:
                    return ParseString(contents, size, out bytes);
                case null:
                    bytes = default;
                    return ModError.NoType;
                default:
                    bytes = default;
                    return ModError.BadType;
            }
        }

        #endregion

        /// <summary>
        /// Performs final processing on mods. Requires access to UPK streams
        /// </summary>
        public void ProcessMods()  // @TODO use a better name
        {
            foreach (IniMod ini in IniMods)
            {
                if (ini.Error != ModError.None) continue;
                foreach (IniPatch patch in ini.Patches)
                {
                    if ((ini.Error = ParsePatch(patch.Value, UPKStreams[patch.File], patch.Type, patch.Size, out patch.Bytes)) != ModError.None) break;
                }
            }

            // @TODO dirty fix to break out of all loops on error. Needs a proper implementation
            foreach (JsonMod json in JsonMods)
            {
                processModsStart:
                if (json.Error is not null) continue;

                foreach (JsonFile file in json.Mod.Files)
                { 
                    if (file.FileType == JsonType.UPK)  // UPK PROCESSING
                    {
                        foreach (JsonObject uobj in file.Objects)
                        {
                            int UObjectIndex = UPKStreams[file.FileName].GetObjectIndex(uobj.ObjectName);

                            if (UObjectIndex < 1)
                            {
                                json.Error = new JsonError() { Error = UObjectIndex == 0 ? ModError.BadUObject : ModError.ObjectNotExport };
                                goto processModsStart;
                            }

                            FObjectExport exportEntry = UPKStreams[file.FileName].Exports[UObjectIndex - 1];
                            uobj.UObjectOffsetInPackage = exportEntry.SerialOffset;
                            uobj.UObjectSerialSize = exportEntry.SerialSize;

                            foreach (JsonPatch patch in uobj.Patches)
                            {
                                ModError error = ParsePatch(patch.Value, UPKStreams[file.FileName], patch.Type, patch.Size, out patch.Bytes);

                                if (error == ModError.None && patch.Offset + patch.Bytes.Length > uobj.UObjectSerialSize) error = ModError.UObjectOverflow;
                                if (error != ModError.None)
                                {
                                    json.Error = new() { Error = error };
                                    goto processModsStart;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void WriteIniMods()
        {
            foreach (IniMod ini in IniMods)
            {
                PrintModString(ini.Name);
                if (ini.Error != ModError.None)
                {
                    Program.PrintColored("[SKIPPED]", ConsoleColor.Red);
                    SkippedMods++;
                    continue;
                }

                foreach (IniPatch patch in ini.Patches)
                {
                    if (!patch.Enabled) continue;
                    UPKStreams[patch.File].UnStream.Position = patch.Offset;
                    UPKStreams[patch.File].UnStream.Write(patch.Bytes);
                    UPKStreams[patch.File].Modified = true;  // If false, file will not be saved on final
                }
                Console.WriteLine("[SUCCESS]");
            }
        }

        public void WriteCopyMods(IPA ipa, bool ShouldOutputIPA)
        {
            foreach (var file in CopyMods)
            {
                PrintModString(file);
                using (var fs = File.OpenRead($"{PathMods}\\{file}"))
                {
                    var ms = new MemoryStream();
                    fs.CopyTo(ms);

                    if (file.EndsWith(".bin"))
                    {
                        CoalStreams[file] = new Coalesced(ms, ipa.Game);
                    }
                    else
                    {
                        // The rest should only be command mods
                        if (ShouldOutputIPA) ipa.Archive.UpdateEntry($"{ipa.AppFolder}/Binaries/{file}", ms);
                        else using (var fs2 = File.Create($"{PathFilesOut}\\{(file == "Commands.txt" ? "Binaries" : "CookedIPhone")}\\{file}")) ms.WriteTo(fs2);
                    }
                }
                Console.WriteLine("[SUCCESS]");
            }
            // If Commands.txt was provided and UE3CommandLine.txt wasn't, generate new UE3CommandLine.txt
            if (CopyMods.Contains("Commands.txt") && !CopyMods.Contains("UE3CommandLine.txt"))
            {
                string contents = $"-exec=\"{"Commands.txt"}\"";
                if (ShouldOutputIPA) ipa.Archive.UpdateEntry($"{ipa.AppFolder}/CookedIPhone/UE3CommandLine.txt", contents);
                else using (var fs = File.Create($"{PathFilesOut}\\CookedIPhone\\UE3CommandLine.txt")) fs.Write(Encoding.ASCII.GetBytes(contents));
            }
        }

        public void WriteJsonMods()
        {
            foreach (JsonMod json in JsonMods)
            {
                PrintModString(json.ModName);
                if (json.Error is not null)
                {
                    Program.PrintColored("[SKIPPED]", ConsoleColor.Red);
                    SkippedMods++;
                    continue;
                }

                foreach (JsonFile file in json.Mod.Files)
                {
                    if (file.FileType == JsonType.UPK)  // UPK
                    {
                        foreach (JsonObject uobj in file.Objects)
                        {
                            foreach (JsonPatch patch in uobj.Patches)
                            {
                                if (patch.Enabled == false) continue;
                                UPKStreams[file.FileName].UnStream.Position = uobj.UObjectOffsetInPackage + patch.Offset;
                                UPKStreams[file.FileName].UnStream.Write(patch.Bytes);
                                UPKStreams[file.FileName].Modified = true;   // If false, file will not be saved on final
                            }
                        }
                    }
                    else  // COALESCED
                    {
                        foreach (JsonIni iniMod in file.Inis)
                        {
                            if (iniMod.Enabled == false) continue;

                            if (iniMod.Mode == JsonMode.Delete)
                            {
                                CoalStreams[file.FileName].TryRemoveIni(iniMod.IniPath);
                                continue;
                            }

                            // Add ini if doesn't exist. If mode is replace, clear its sections
                            Ini currentIni = CoalStreams[file.FileName].GetIni(iniMod.IniPath);
                            if (iniMod.Mode == JsonMode.Overwrite) currentIni.Sections = new();

                            foreach (JsonSection sectionMod in iniMod.Sections)
                            {
                                if (sectionMod.Enabled == false) continue;

                                if (sectionMod.Mode == JsonMode.Delete)
                                {
                                    currentIni.TryRemoveSection(sectionMod.SectionName);
                                    continue;
                                }

                                Section currentSection = currentIni.GetSection(sectionMod.SectionName);
                                if (sectionMod.Mode == JsonMode.Overwrite) currentSection.Properties = new();

                                foreach (string property in sectionMod.Properties)
                                {
                                    string[] sub = property.Split('=', 2);

                                    currentSection.Properties.Add(new Property()
                                    {
                                        Key = sub[0],
                                        Value = sub.Length == 2 ? sub[1] : string.Empty
                                    });
                                }
                            }
                        }
                    }
                }
                Console.WriteLine("[SUCCESS]");
            }
        }

        public void ApplyMods(IPA ipa)
        {
            bool ShouldOutputIPA = !Directory.Exists(AppContext.BaseDirectory + "Output");
            if (ShouldOutputIPA)
            {
                try
                {
                    File.Delete(PathIPAOut);
                }
                catch (FileNotFoundException) { }
                catch (IOException)
                {
                    Console.WriteLine($"{Path.GetFileName(PathIPAOut)} is in use and cannot be updated! Close any apps which may be using it and run the patcher again.");
                    return;
                }
            }
            else
            {
                try
                {
                    Directory.Delete(PathFilesOut, true);
                }
                catch (DirectoryNotFoundException) { }
                catch (IOException)
                {
                    Console.WriteLine($"One or more files in 'Output\\{ipa.Game}\\' are in use! Close them and run the patcher again.");
                    return;
                }

                // Directory.CreateDirectory(PathFilesOut);
                Directory.CreateDirectory(PathFilesOut + "\\CookedIPhone");
                if (CopyMods.Contains("Commands.txt")) Directory.CreateDirectory(PathFilesOut + "\\Binaries");
            }

            ExtractRequiredFiles(ipa);

            ProcessMods();

            WriteIniMods();

            WriteCopyMods(ipa, ShouldOutputIPA);

            WriteJsonMods();

            // Update coalesced streams. Command mods were written during WriteRepMods()
            foreach (KeyValuePair<string, Coalesced> entry in CoalStreams)
            {
                entry.Value.Serialize();

                if (ShouldOutputIPA)
                {
                    ipa.Archive.UpdateEntry($"{ipa.AppFolder}CookedIPhone/{entry.Key}", entry.Value.UnStream.ToArray());
                }
                else
                {
                    using var fs = File.Create($"{PathFilesOut}/CookedIPhone/{entry.Key}");
                    fs.Write(entry.Value.UnStream.ToArray());
                }
            }

            // Update UPK streams
            // @TODO foreach VALUES. Keys is not needed
            foreach (KeyValuePair<string, UPK> entry in UPKStreams)
            {
                if (!entry.Value.Modified)
                {
                    entry.Value.UnStream.Dispose();
                    UPKStreams.Remove(entry.Key);
                    continue;
                }

                entry.Value.UnStream.Position = 0;
                if (ShouldOutputIPA)
                {
                    ipa.Archive.UpdateEntry($"{ipa.AppFolder}CookedIPhone/{entry.Key}", entry.Value.UnStream.ToArray());
                }
                else
                {
                    using var fs = File.Create($"{PathFilesOut}/CookedIPhone/{entry.Key}");
                    fs.Write(entry.Value.UnStream.ToArray());
                }
            }

            int modifiedFilesCount = UPKStreams.Count + CoalStreams.Count + CopyMods.Count;
            string status = $"Saving {modifiedFilesCount} file{(modifiedFilesCount != 1 ? 's' : "")} ({CurrentModIndex - SkippedMods}/{TotalScannedMods} mods) to {(ShouldOutputIPA ? "IPA" : "output folder")}";
            Console.Write($"\n{status} {new string('.', MaxCharLength - status.Length - 1)} [ 00.0% ]");

            if (ShouldOutputIPA && CurrentModIndex != SkippedMods)
            {
                ipa.Archive.SaveProgress += IPA.SaveProgress;
                ipa.Archive.Save(PathIPAOut);
            }
            ipa.Archive.Dispose();
            Console.WriteLine(new string('\b', 9) + (modifiedFilesCount == 0 ? "[SKIPPED]" : "[SUCCESS]"));

            if (SkippedMods > 0)
            {
                Program.PrintColored("\nERROR REPORT: ", ConsoleColor.Red);
                foreach (IniMod ini in IniMods)
                {
                    if (ini.Error != ModError.None)
                    {
                        Console.Write($"{ini.Name}.ini");
                        if (ini.Patches.Count == 0)
                        {
                            Program.PrintColored($" {StringError(ini.Error)}\n", ConsoleColor.Red);
                        }
                        else
                        {
                            Console.Write($"\n\t{ini.Patches[^1].Name}: ");
                            Program.PrintColored($"{StringError(ini.Error)}\n", ConsoleColor.Red);
                        }
                    }
                }

                foreach (JsonMod json in JsonMods)
                {
                    if (json.Error is not null)
                    {
                        Console.Write($"{json.ModName}: ");
                        if (json.Error.Message is not null)
                        {
                            Program.PrintColored(json.Error.Message, ConsoleColor.Red);
                        }
                        else Program.PrintColored(StringError(json.Error.Error), ConsoleColor.Red);
                    }
                }
            }
        }
    }
}