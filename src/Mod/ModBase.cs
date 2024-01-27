using System;
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

public enum ModError : byte
{
    None = 0,

    // .JSON
    Json_HasUnexpectedValue,
    Json_HasMissingComma,
    Json_HasTrailingComma,
    Json_UnhandledException,
    
    // .INI
    Ini_HasNoSections,
    Ini_HasDuplicateSections,
    Ini_UnexpectedSize,
    Ini_BadSize,

    // .BIN
    Coalesced_InvalidFile,
    Coalesced_WrongGame,

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
    Generic_IniNotFound,
    Generic_UnexpectedSection,
    Generic_ReplaceRequiresObject,
}

public enum ModFormat : byte
{
    Ini,
    Json,
    Bin
}

public enum PatchType : byte
{
    Unspecified,

    Bool,
    UBool,
    UInt8,
    Int32,
    Float,

    Byte,   // Strictly hex string. For numerical byte, use UInt8
    String,
    Replace
}

public enum FileType : byte
{
    Upk,
    Coalesced,
    Unspecified
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

        _ => PatchType.Unspecified
    };
}

#endregion

[StructLayout(LayoutKind.Explicit)]
public struct ModPatchValue
{
    // UInt8 is shared by Bool, UBool, and UInt8
    [FieldOffset(0)] public byte UInt8;
    [FieldOffset(0)] public int Int32;
    [FieldOffset(0)] public float Float;

    [FieldOffset(8)] public byte[] Bytes;
    [FieldOffset(8)] public string String;
    [FieldOffset(8)] public string[] Strings;
}

public class ModPatch
{
    #region Mod-mapped members

    public string SectionName;
    public PatchType Type = PatchType.Unspecified;
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
            Value = Type switch
            {
                PatchType.Bool => new ModPatchValue { UInt8 = (byte)(value == "1" || value == "true" ? 0x01 : 0x00) },
                PatchType.UBool => new ModPatchValue { UInt8 = (byte)(value == "1" || value == "true" ? 0x27 : 0x28) },
                PatchType.UInt8 => new ModPatchValue { UInt8 = byte.Parse(value) },
                PatchType.Int32 => new ModPatchValue { Int32 = int.Parse(value) },
                PatchType.Float => new ModPatchValue { Float = float.Parse(value) },
                PatchType.String => new ModPatchValue { String = value },
                PatchType.Byte or PatchType.Replace => new ModPatchValue { Bytes = Convert.FromHexString(value.Replace(" ", "")) },
                _ => new ModPatchValue()    // @TODO why am I handling a default case here? Can I not just catch below?
            };
        }
        catch
        {
            return false;
        }

        return true;
    }
}

public class ModObject(string objectName)
{
    #region Mod format members

    public string ObjectName = objectName;
    public List<ModPatch> Patches = [];

    #endregion

    #region Transient

    // References to respective objects, populated after Link() has been called
    internal Ini? Ini;
    internal FObjectExport? Export;

    #endregion

    public override string ToString() => string.IsNullOrEmpty(ObjectName) ? ObjectName : "<Nameless Object>" + $" | {Patches.Count} patches";
}

public class ModFile(string userPath, string qualifiedPath, FileType fileType)
{
    #region Mod format members

    public string FileName = userPath;
    public FileType FileType = fileType;
    public List<ModObject> Objects = [];

    #endregion

    #region Transient

    /// <summary>
    /// A lower-case, fully-qualified IPA filepath.
    /// </summary>
    internal string QualifiedIpaPath = qualifiedPath;
    internal CachedArchive? Archive;

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

public class ModBase(string modPath, ModFormat type, Game game) : ErrorHelper<ModError>
{
    #region Mod format members

    public string Name = Path.GetFileName(modPath);
    public Game Game = game;
    public List<ModFile> Files = [];

    #endregion

    internal ModFormat ModType = type;
    internal string ModPath = modPath;     // Path to mod file on disk.

    #region ErrorHelper

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

        ModError.Json_HasUnexpectedValue => "Unexpected value type is present in the JSON mod file.",
        ModError.Json_HasMissingComma => "Comma is missing from the JSON mod file.",
        ModError.Json_HasTrailingComma => "Trailing comma is present in the JSON mod file.",
        ModError.Json_UnhandledException => "Unhandled exception occurred while parsing the JSON mod file.",

        ModError.Ini_HasNoSections => "No sections were found within the INI mod file.",
        ModError.Ini_HasDuplicateSections => "Duplicate sections were found within the INI mod file.",
        ModError.Ini_UnexpectedSize => "'Size' can only be specified for the integer type.",
        ModError.Ini_BadSize => "'Size' must equal either '1' or '4'.",

        ModError.Coalesced_InvalidFile => "Not a valid Coalesced file.",
        ModError.Coalesced_WrongGame => "Coalesced file does not match the currently-loaded game.",

        // Generic - mod format

        ModError.Generic_UnspecifiedGame => "'Game' was not specified.",
        ModError.Generic_BadGame => "Game does not correspond to any valid game.",
        ModError.Generic_WrongGame => "Mod does not target the currently loaded game.",

        ModError.Generic_UnspecifiedFile => "'File' was not specified.",
        ModError.Generic_FileNotFound => "File was not found within the currently-loaded IPA.",
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
        ModError.Generic_IniNotFound => "Ini file was not found within the Coalesced file",
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

                    DeepCopyCoalesced(templateFile, newFile, languageCode);
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

                    if (patch.Type is not PatchType.Unspecified)
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.Generic_UnexpectedPatchType, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }
                    else if (file.FileType is FileType.Upk)
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
                    else if (patch.Type is not (PatchType.Unspecified or PatchType.Replace))
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

                    if (!file.Archive.Coalesced.TryGetIni(obj.ObjectName, out obj.Ini))
                    {
                        SetError(ModError.Generic_IniNotFound, GetErrorLocation(file, obj));
                        return false;
                    }
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
                                upk.Serialize(ref patch.Value.UInt8);
                                break;
                            case PatchType.Int32:
                                upk.Serialize(ref patch.Value.Int32);
                                break;
                            case PatchType.Float:
                                upk.Serialize(ref patch.Value.Float);
                                break;
                            case PatchType.String:
                            case PatchType.Byte:
                                upk.Write(patch.Value.Bytes);
                                break;
                            case PatchType.Replace:
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

                        if (patch.Value.String is not null)
                        {
                            patch._sectionReference.ParseProperty(patch.Value.String);
                        }
                        else
                        {
                            foreach (var property in patch.Value.Strings)
                            {
                                patch._sectionReference.ParseProperty(property);
                            }
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
    private static void DeepCopyCoalesced(ModFile from, ModFile to, string langCode)
    {
        Debug.Assert(langCode.Length == 3);

        foreach (var fromObject in from.Objects)
        {
            // Filter out inappropriate localization files
            int index = fromObject.ObjectName.Replace('\\', '/').IndexOf("/Localization/", StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                string inputLang = fromObject.ObjectName.Substring(index + "/Localization/".Length, 3).ToUpperInvariant();
                if (inputLang != langCode)
                {
                    // Localization folder isn't for the current language. Skip it.
                    continue;
                }
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
    }
}
