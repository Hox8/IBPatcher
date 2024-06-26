﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnrealLib;
using UnrealLib.Config;
using UnrealLib.Core;
using UnrealLib.Enums;

namespace IBPatcher.Mod;

#region Enums

public enum ModError
{
    None = 0,

    // .JSON
    Json_HasUnexpectedValueType,
    Json_HasBadArrayValue,
    Json_UnexpectedArrayValue,
    Json_HasMissingComma,
    Json_HasTrailingComma,
    Json_HasBadValue,
    Json_UnhandledException,
    Json_BadEncoding,
    Json_UnsupportedVersion,

    // .INI
    Ini_HasNoSections,
    Ini_HasDuplicateSections,
    Ini_UnexpectedSize,
    Ini_BadSizeFloat,
    Ini_BadSize,

    // .BIN
    Coalesced_InvalidFile,
    Coalesced_WrongGame,
    Coalesced_BadLocaleFolder,
    Coalesced_BadLocaleExtension,

    // Game
    Generic_UnspecifiedGame,
    Generic_BadGame,
    Generic_WrongGame,

    // File
    Generic_UnspecifiedFile,
    Generic_FileNotFound,
    Generic_FileFailedLoad,

    // FileType
    Generic_UnspecifiedFileType,
    Generic_BadFileType,

    // Patch type
    Generic_UnspecifiedPatchType,
    Generic_UnexpectedPatchType,
    Generic_BadPatchType,

    // Patch offset
    Generic_UnspecifiedOffset,
    Generic_UnexpectedOffset_Coalesced,
    Generic_UnexpectedOffset_Upk,
    Generic_BadOffset,

    // Patch value
    Generic_UnspecifiedValue,
    Generic_BadValue,
    Generic_BadValueString,

    // Patch enabled
    Generic_BadEnabled,

    // Arrays
    Generic_EmptyFiles,
    Generic_EmptyObjects,
    Generic_EmptyPatches,

    // Linking stage
    Generic_UnspecifiedObject,
    Generic_ExportNotFound,
    Generic_UnexpectedSection,
    Generic_ReplaceRequiresObject,
}

public enum ModFormat
{
    Ini,
    Json,
    Bin
}

public enum PatchType
{
    Unspecified = 0,
    Invalid,

    Bool,
    UBool,
    UInt8,
    Int32,
    Float,

    String,
    Strings,    // Treated specially. Not exposed as a user option!

    Byte,       // Strictly hex string. For numerical byte, use UInt8
    Replace
}

public enum FileType
{
    Unspecified = 0,
    Invalid,

    Upk,
    Coalesced
}

#endregion

#region Helpers

public static class EnumConverters
{
    public static Game GetGame(string? value) => value?.Replace(" ", "").ToLowerInvariant() switch
    {
        "vote" or "vote!!!" => Game.Vote,
        "infinitybladei" or "ib1" => Game.IB1,
        "infinitybladeii" or "ib2" => Game.IB2,
        "infinitybladeiii" or "ib3" => Game.IB3,
        _ => Game.Unknown
    };

    public static FileType GetFileType(string? value) => value?.ToLowerInvariant() switch
    {
        "upk" => FileType.Upk,
        "coalesced" => FileType.Coalesced,
        _ => FileType.Unspecified
    };

    public static PatchType GetPatchType(string? value) => value?.ToLowerInvariant() switch
    {
        // Original Niko_KV formats:
        // INTEGER
        // FLOAT
        // BOOLEAN
        // STRING
        // BYTE

        // Extra cases are included for compatibility with original Niko spec
        "bool" or "boolean" => PatchType.Bool,
        "ubool" => PatchType.UBool,
        "uint8" => PatchType.UInt8,
        "int32" or "integer" => PatchType.Int32,
        "float" => PatchType.Float,
        "byte" => PatchType.Byte,
        "string" => PatchType.String,
        "replace" => PatchType.Replace,
        null => PatchType.Unspecified,
        _ => PatchType.Invalid
    };
}

#endregion

[StructLayout(LayoutKind.Explicit)]
public struct ModPatchValue
{
    [FieldOffset(0)] public byte Byte;
    [FieldOffset(0)] public int Int;
    [FieldOffset(0)] public float Float;

    [FieldOffset(8)] public string String;
    [FieldOffset(8)] public List<string> Strings;   // List<> due to how data is read in JSON

    [FieldOffset(8)] public byte[] Bytes;
}

public class ModPatch
{
    #region Mod-mapped members

    public string SectionName;
    public PatchType Type;
    public int? Offset;
    public ModPatchValue Value;
    public bool Enabled = true;

    #endregion

    #region Transient

    // Populated after Link() has been called on the parent mod
    internal Section _sectionReference;

    // Any sections starting with '!' will set this to true
    internal bool SectionWantsToClearProperties;

    #endregion

    public bool TryParseValue(string value)
    {
        try
        {
            switch (Type)
            {
                case PatchType.Bool: Value.Byte = (byte)(value == "1" || value == "true" ? 0x01 : 0x00); break;
                case PatchType.UBool: Value.Byte = (byte)(value == "1" || value == "true" ? 0x27 : 0x28); break;
                case PatchType.UInt8: Value.Byte = byte.Parse(value); break;
                case PatchType.Int32: Value.Int = int.Parse(value); break;
                case PatchType.Float: Value.Float = float.Parse(value); break;

                case PatchType.String: Value.String = value;  break;
                // PatchType.Strings is handled during Json init

                case PatchType.Replace:
                case PatchType.Byte: Value.Bytes = Convert.FromHexString(value.Replace(" ", "")); break;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }
}

public class ModObject
{
    #region Mod format members

    public string ObjectName;
    public List<ModPatch> Patches = [];

    #endregion

    #region Transient

    // References to respective objects, populated after Link() has been called
    internal Ini? Ini;
    internal FObjectExport? Export;

    #endregion

    #region Constructors

    public ModObject() { }

    public ModObject(string objectName)
    {
        ObjectName = objectName;
    }

    #endregion

    public override string ToString() => !string.IsNullOrEmpty(ObjectName) ? ObjectName : "<Nameless Object>" + $" | {Patches.Count} patches";
}

public class ModFile
{
    #region Mod format members

    public string FileName;
    public FileType FileType;
    public List<ModObject> Objects = [];

    #endregion

    #region Transient

    /// <summary>
    /// A lower-case, fully-qualified IPA filepath.
    /// </summary>
    internal string QualifiedIpaPath;
    internal CachedArchive? Archive;

    #endregion

    #region Constructors

    public ModFile() { }

    public ModFile(string userPath, string qualifiedPath, FileType fileType)
    {
        FileName = userPath;
        FileType = fileType;

        QualifiedIpaPath = qualifiedPath;
    }

    #endregion

    public ModObject GetObject(string objectName)
    {
        foreach (var obj in Objects)
        {
            if (obj.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase))
            {
                return obj;
            }
        }

        // @TODO: Alert developers about condensing multiple identical objects
        Objects.Add(new ModObject(objectName));
        return Objects[^1];
    }

    public override string ToString() => $"{QualifiedIpaPath} | {Objects.Count} objects";
}

public class ModBase : ErrorHelper<ModError>
{
    #region Mod format members

    // Reserved for future use. JSON mods using a higher version than the patcher supports will be handled appropriately
    public const int CurrentJsonVersion = 1;

    public string Name;
    public Game Game;
    public List<ModFile> Files = [];
    public int JsonVersion = CurrentJsonVersion;

    #endregion

    internal ModFormat ModType;
    internal string ModPath;     // Path to mod file on disk.

    #region Constructors

    public ModBase(string modPath, ModFormat type)
    {
        Name = Path.GetFileName(modPath);

        ModType = type;
        ModPath = modPath;
    }

    public ModBase(string modPath, ModFormat type, Game game)
    {
        Name = Path.GetFileName(modPath);
        Game = game;

        ModType = type;
        ModPath = modPath;
    }

    #endregion

    #region ErrorHelper

    /// <summary>
    /// This list will store all unrecognized JSON keys this mod encounters during initialization.
    /// They'll be printed after mods have been written.
    /// </summary>
    public List<string>? UnrecognizedKeys = null;

    public string GetErrorLocation(ModFile file, ModObject? obj = null, ModPatch? patch = null)
    {
        var sb = new StringBuilder(string.IsNullOrWhiteSpace(file.FileName) ? $"File: {Files.IndexOf(file)}" : file.FileName);

        if (obj is not null)
        {
            sb.Append(string.IsNullOrWhiteSpace(obj.ObjectName) ? $", Object: {file.Objects.IndexOf(obj)}" : $" | {obj.ObjectName}");

            if (patch is not null)
            {
                if (file.FileType is FileType.Upk)
                {
                    sb.Append(" | Patch: " + obj.Patches.IndexOf(patch));
                }
                else
                {
                    sb.Append(" | Section: " + patch.SectionName);
                }
            }
        }

        return sb.ToString();
    }

    public override string GetErrorString() => ErrorType switch
    {
        ModError.None => "Mod does not contain an error.",

        // Format-specific

        ModError.Json_HasUnexpectedValueType => "An unexpected value type is present in the JSON mod file.",
        ModError.Json_HasBadArrayValue => "Coalesced array values must consist of only strings.",
        ModError.Json_UnexpectedArrayValue => "Only Coalesced patches may use array-type values.",
        ModError.Json_HasMissingComma => "A comma is missing from the JSON mod file.",
        ModError.Json_HasTrailingComma => "A trailing comma is present in the JSON mod file.",
        ModError.Json_HasBadValue => "An invalid value is present in the JSON mod file.",
        ModError.Json_UnhandledException => "An unhandled exception occurred while parsing the JSON mod file.",
        ModError.Json_BadEncoding => "JSON mod file uses UTF-16 encoding. Please re-save with UTF-8.",
        ModError.Json_UnsupportedVersion => "JSON version is newer than this patcher supports. Please update if you wish to use this mod.",

        ModError.Ini_HasNoSections => "No sections were found within the INI mod file.",
        ModError.Ini_HasDuplicateSections => "Duplicate sections were found within the INI mod file.",
        ModError.Ini_UnexpectedSize => "'Size' can only be specified for the integer type.",
        ModError.Ini_BadSizeFloat => "'Size' must be set to '4' for float values.",
        ModError.Ini_BadSize => "'Size' must equal either '1' or '4'.",

        ModError.Coalesced_InvalidFile => "Not a valid Coalesced file.",
        ModError.Coalesced_WrongGame => "Coalesced file does not match the loaded game.",
        ModError.Coalesced_BadLocaleFolder => "Invalid localization subfolder.",
        ModError.Coalesced_BadLocaleExtension => "Locale file extension must match its parent folder.",

        // Generic - mod format

        ModError.Generic_UnspecifiedGame => "'Game' was not specified.",
        ModError.Generic_BadGame => "Game does not correspond to any valid game.",
        ModError.Generic_WrongGame => "Mod does not target the loaded game.",

        ModError.Generic_UnspecifiedFile => "'File' was not specified.",
        ModError.Generic_FileNotFound => "File was not found within the loaded IPA.",
        ModError.Generic_FileFailedLoad => "Failed to parse file.",

        ModError.Generic_UnspecifiedFileType => $"'Type' was not specified.",
        ModError.Generic_BadFileType => "'Type' does not correspond to any valid file type.",

        ModError.Generic_UnspecifiedPatchType => "'Type' was not specified.",
        ModError.Generic_UnexpectedPatchType => "'Type' cannot be specified for Coalesced-type patches.",
        ModError.Generic_BadPatchType => "'Type' does not correspond to any valid patch type.",

        ModError.Generic_UnspecifiedOffset => "'Offset' was not specified.",
        ModError.Generic_UnexpectedOffset_Coalesced => "'Offset' cannot be specified for Coalesced-type patches.",
        ModError.Generic_UnexpectedOffset_Upk => "'Offset' cannot be specified for replace-type patches.",
        ModError.Generic_BadOffset => "'Offset' recieved a bad value.",

        ModError.Generic_UnspecifiedValue => "'Value' was not specified.",
        ModError.Generic_BadValue => "'Value' could not be parsed.",
        ModError.Generic_BadValueString => "Only ASCII characters are allowed for UPK string values.",

        ModError.Generic_BadEnabled => "'Enabled' recieved a bad value.",

        ModError.Generic_EmptyFiles => "Mod contains no files.",
        ModError.Generic_EmptyObjects => "File contains no objects.",
        ModError.Generic_EmptyPatches => "Object contains no patches.",

        // Generic - linking

        ModError.Generic_UnspecifiedObject => "'Object' was not specified",
        ModError.Generic_ExportNotFound => "Export object was not found within the UPK file",
        ModError.Generic_UnexpectedSection => "'Section' cannot be specified for UPK patches",
        ModError.Generic_ReplaceRequiresObject => "'Object' in the parent ModObject must be specified in order to use Replace type",
    };

    #endregion

    #region Accessors

    public override string ToString() => Name;

    #endregion

    #region Helpers

    /// <summary>
    /// Gets the ModFile with a matching FilePath.
    /// If not found, a new ModFile is added and returned.
    /// </summary>
    public ModFile GetFile(string userPath, string qualifiedPath, FileType type)
    {
        foreach (var modFile in Files)
        {
            if (modFile.QualifiedIpaPath.Equals(qualifiedPath, StringComparison.OrdinalIgnoreCase))
            {
                return modFile;
            }
        }

        // @TODO: Alert developers about condensing multiple identical files
        Files.Add(new ModFile(userPath, qualifiedPath, type));
        return Files[^1];
    }

    #endregion

    /// <summary>
    /// Checks the mod for errors and lets the passed ModContext know of any files it requires.
    /// </summary>
    public void Setup(ModContext context)
    {
        if (HasError) return;

        #region Game

        // Ini mods don't currently support metadata like this
        if (ModType is ModFormat.Json)
        {
            if (Game is Game.Unspecified)
            {
                SetError(ModError.Generic_UnspecifiedGame);
                return;
            }

            if (Game is Game.Unknown)
            {
                SetError(ModError.Generic_BadGame);
                return;
            }

            if (Game != context.Game)
            {
                SetError(ModError.Generic_WrongGame);
                return;
            }
        }

        #endregion

        #region Files

        if (Files is null || Files.Count == 0)
        {
            SetError(ModError.Generic_EmptyFiles);
            return;
        }

        #endregion

        // Look for "Coalesced_ALL" file separately before main loop, since we might modify/add files
        for (int i = Files.Count - 1; i >= 0; i--)
        {
            if (Files[i].FileType is FileType.Coalesced && Files[i].FileName.Equals("coalesced_all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var languageCode in UnrealLib.Globals.GetLanguages(Game))
                {
                    var templateFile = Files[i];

                    string fileName = $"Coalesced_{languageCode}.bin";
                    string qualifiedPath = context.QualifyPath(fileName);

                    // Get a copy (new or existing) Coalesced_{languageCode}.bin file as copy the template's guts over
                    var newFile = GetFile(fileName, qualifiedPath, FileType.Coalesced);

                    // Try and propgate files to all languages, returning on fail
                    if (!DeepCopyCoalesced(this, templateFile, newFile, languageCode)) return;
                }

                // Delete this template file
                // We're deleting this instead of renaming it to Coalesced_INT so we ensure it merges with any existing Coalesced_INT
                Files.RemoveAt(i);
            }

            // Do not break here since there's no guarantee there aren't multiple Coalesced_ALL files...
        }

        foreach (var file in Files)
        {
            #region Path

            if (string.IsNullOrEmpty(file.FileName))
            {
                SetError(ModError.Generic_UnspecifiedFile, GetErrorLocation(file));
                return;
            }

            // Link and register UnrealArchive so it so it can be extracted later
            if (!context.TryGetCachedArchive(file, out file.Archive))
            {
                SetError(ModError.Generic_FileNotFound, GetErrorLocation(file));
                return;
            }

            #endregion

            #region Type

            if (file.FileType is FileType.Unspecified)
            {
                SetError(ModError.Generic_UnspecifiedFileType, GetErrorLocation(file));
                return;
            }

            if (file.FileType is FileType.Invalid)
            {
                SetError(ModError.Generic_BadFileType, GetErrorLocation(file));
                return;
            }

            #endregion

            if (file.Objects is null || file.Objects.Count == 0)
            {
                SetError(ModError.Generic_EmptyObjects);
                return;
            }

            foreach (var obj in file.Objects)
            {
                #region Path

                // Replace Unix separators with Windows ones to allow nicer-looking Ini paths
                // We also add the relative segment if it was omitted inside the mod
                if (file.FileType is FileType.Coalesced)
                {
                    obj.ObjectName = obj.ObjectName.Replace('/', '\\');

                    if (!obj.ObjectName.StartsWith(@"..\..\"))
                    {
                        obj.ObjectName = @"..\..\" + obj.ObjectName;
                    }
                }

                #endregion

                if (obj.Patches is null || obj.Patches.Count == 0)
                {
                    SetError(ModError.Generic_EmptyPatches, GetErrorLocation(file, obj));
                    return;
                }

                // Object is checked during Link()

                foreach (var patch in obj.Patches)
                {
                    #region Value

                    // Ini mods handle value in their own ReadMod() method
                    if (ModType is ModFormat.Json && file.FileType is FileType.Upk)
                    {
                        if (patch.Value.String is null)
                        {
                            SetError(ModError.Generic_UnspecifiedValue, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (!patch.TryParseValue(patch.Value.String))
                        {
                            SetError(ModError.Generic_BadValue, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (file.FileType is FileType.Upk && patch.Type is PatchType.String && !Ascii.IsValid(patch.Value.String))
                        {
                            SetError(ModError.Generic_BadValueString, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }

                    #endregion

                    // Section is checked during Link()

                    #region Type

                    if (patch.Type != PatchType.Unspecified && patch.Type != PatchType.Strings)
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.Generic_UnexpectedPatchType, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }
                    else if (file.FileType is FileType.Upk)
                    {
                        SetError(ModError.Generic_UnspecifiedPatchType, GetErrorLocation(file, obj, patch));
                        return;
                    }

                    if (patch.Type is PatchType.Invalid)
                    {
                        SetError(ModError.Generic_BadPatchType, GetErrorLocation(file, obj, patch));
                        return;

                    }

                    #endregion

                    #region Offset

                    if (patch.Offset is not null)
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.Generic_UnexpectedOffset_Coalesced, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (patch.Type is PatchType.Replace)
                        {
                            SetError(ModError.Generic_UnexpectedOffset_Upk, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (patch.Offset < 0)
                        {
                            SetError(ModError.Generic_BadOffset, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }
                    else if (patch.Type is not (PatchType.Unspecified or PatchType.Replace or PatchType.Strings))
                    {
                        SetError(ModError.Generic_UnspecifiedOffset, GetErrorLocation(file, obj, patch));
                        return;
                    }

                    #endregion
                }
            }
        }
    }

    /// <summary>
    /// Called once the mod's required archives have been extracted and loaded.<br/><br/>
    /// All UPK patches have their UObjects linked, and Coalesced patches have their Inis and Sections located / created.
    /// </summary>
    /// <returns>True if the mod linked without any issues, False otherwise.</returns>
    public bool Link()
    {
        if (HasError) return false;

        foreach (var file in Files)
        {
            if (file.Archive.HasError)
            {
                SetError(ModError.Generic_FileFailedLoad, GetErrorLocation(file));
                return false;
            }

            foreach (var obj in file.Objects)
            {
                #region Object Name

                if (file.FileType is FileType.Upk)
                {
                    if (!string.IsNullOrEmpty(obj.ObjectName))
                    {
                        if (file.Archive.Upk.FindObject(obj.ObjectName) is not FObjectExport export)
                        {
                            SetError(ModError.Generic_ExportNotFound, GetErrorLocation(file, obj));
                            return false;
                        }

                        obj.Export = export;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(obj.ObjectName))
                    {
                        SetError(ModError.Generic_UnspecifiedObject, GetErrorLocation(file, obj));
                        return false;
                    }

                    file.Archive.Coalesced.TryAddIni(obj.ObjectName, out obj.Ini);
                }

                #endregion

                foreach (var patch in obj.Patches)
                {
                    #region Section

                    if (!string.IsNullOrEmpty(patch.SectionName))
                    {
                        if (file.FileType is FileType.Upk)
                        {
                            SetError(ModError.Generic_UnexpectedSection, GetErrorLocation(file, obj, patch));
                            return false;
                        }

                        // If the section name starts with the '!' character, that tells us we need to clear out the section's properties
                        if (patch.SectionName[0] == '!')
                        {
                            patch.SectionName = patch.SectionName[1..];
                            patch.SectionWantsToClearProperties = true;
                        }

                        // Add Section to Ini if it does not exist
                        obj.Ini.TryAddSection(patch.SectionName, out patch._sectionReference);
                    }
                    else
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.Generic_UnspecifiedFile, GetErrorLocation(file, obj, patch));
                            return false;
                        }
                    }

                    #endregion

                    #region Type

                    if (patch.Type is PatchType.Replace && obj.Export is null)
                    {
                        SetError(ModError.Generic_ReplaceRequiresObject, GetErrorLocation(file, obj, patch));
                        return false;
                    }

                    #endregion

                    #region Offset

                    // @TODO: Check patch value + offset length & UObject/UPK length don't cross

                    #endregion
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Writes each mod patch to its specified file. Called after Setup() and Link().
    /// </summary>
    public void Write()
    {
        if (HasError) return;

        foreach (var file in Files)
        {
            Debug.Assert(!file.Archive.HasError);

            foreach (var obj in file.Objects)
            {
                foreach (var patch in obj.Patches)
                {
                    if (!patch.Enabled) continue;

                    if (file.FileType is FileType.Upk)
                    {
                        var upk = file.Archive.Upk;
                        Debug.Assert(!upk.IsLoading);

                        if (patch.Type is not PatchType.Replace)
                        {
                            upk.Position = (int)patch.Offset + (obj.Export?.GetSerialOffset() ?? 0);
                        }

                        switch (patch.Type)
                        {
                            case PatchType.Bool:
                            case PatchType.UBool:
                            case PatchType.UInt8:
                                upk.Serialize(ref patch.Value.Byte);
                                break;
                            case PatchType.Int32:
                                upk.Serialize(ref patch.Value.Int);
                                break;
                            case PatchType.Float:
                                upk.Serialize(ref patch.Value.Float);
                                break;
                            case PatchType.String:
                            case PatchType.Byte:
                                upk.Write(patch.Value.Bytes);
                                break;
                            case PatchType.Replace:
                                // Null export will have been checked during Link()
                                upk.ReplaceExportData(obj.Export, patch.Value.Bytes);
                                break;
                        }
                    }
                    else
                    {
                        Debug.Assert(patch._sectionReference is not null);

                        // If the section was passed with a '!' prefix, clear out its properties
                        if (patch.SectionWantsToClearProperties)
                        {
                            patch._sectionReference.Properties.Clear();
                        }

                        if (patch.Type == PatchType.Strings)
                        {
                            foreach (var property in patch.Value.Strings)
                            {
                                patch._sectionReference.ParseProperty(property);
                            }
                        }
                        else
                        {
                            patch._sectionReference.ParseProperty(patch.Value.String);
                        }
                    }

                    file.Archive.Modified = true;
                }
            }
        }
    }

    /// <summary>
    /// Deep-copies the objects and files from template to target.
    /// </summary>
    /// <param name="from">The ModFile to copy from.</param>
    /// <param name="to">The ModFile to copy to.</param>
    /// <param name="langCode">The language code of the Coalesced we're copying to.</param>
    private static bool DeepCopyCoalesced(ModBase mod, ModFile from, ModFile to, string langCode)
    {
        const string locString = "/Localization/";

        foreach (var fromObject in from.Objects)
        {
            string normalized = fromObject.ObjectName.Replace('\\', '/');

            // Filter out inappropriate localization files
            int index = normalized.IndexOf(locString, StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                string inLangFolder = normalized.Substring(index + locString.Length, 3).ToUpperInvariant();
                string inLangExt = normalized[^3..].ToUpperInvariant();

                // Error if the localization folder or file extension is bad

                if (!UnrealLib.Globals.Languages.Contains(inLangFolder))
                {
                    mod.SetError(ModError.Coalesced_BadLocaleFolder, normalized);
                    return false;
                }

                if (inLangExt != inLangFolder)
                {
                    mod.SetError(ModError.Coalesced_BadLocaleExtension, normalized);
                    return false;
                }

                // INT locale files should be copied unconditionally. Foreign languages should only be copied to their respective Coalesced files
                // This is done so INT can be used as a fallback if other languages did not recieve the same locale strings.
                if (inLangExt != "INT" && inLangExt != langCode) continue;
            }

            var toObject = new ModObject(fromObject.ObjectName);
            to.Objects.Add(toObject);

            // Copy patches over unconditionally
            // We aren't shallow copying here as that would undoubtedly cause headaches in the future
            toObject.Patches.Capacity = fromObject.Patches.Count;
            foreach (var fromPatch in fromObject.Patches)
            {
                toObject.Patches.Add(new ModPatch
                {
                    SectionName = fromPatch.SectionName,
                    Type = fromPatch.Type,
                    Offset = fromPatch.Offset,
                    Value = fromPatch.Value,
                    Enabled = fromPatch.Enabled
                });
            }
        }

        return true;
    }
}
