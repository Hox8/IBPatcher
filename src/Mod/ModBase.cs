using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnrealLib;
using UnrealLib.Config;
using UnrealLib.Core;
using UnrealLib.Enums;

namespace IBPatcher.Mod;

#region Enums

public enum ModError : byte // @TODO organize this. See the GetString() method at the bottom of this file
{
    None,

    // Json syntax
    JsonBadCast,
    JsonMissingComma,
    JsonTrailingComma,
    JsonUnhandled,

    // Json?
    InvalidFileType,
    InvalidGame,
    UnspecifiedGame,
    GameMismatch,
    EmptyFiles,
    EmptyObjects,
    // InvalidFile,
    FileNotFound,
    UnspecifiedFileType,
    ExportNotFound,
    IniNotFound,
    UnspecifiedObject,
    EmptyPatches,
    InappropriateSection,
    // SectionNotFound,
    InappropriatePatchType,
    UnspecifiedObjectReplace,
    InvalidPatchType,
    InappropriateOffsetCoalesced,
    InappropriateOffsetReplace,
    InvalidOffset,
    UnspecifiedOffset,
    UnspecifiedValue,
    InvalidValue,
    NonAsciiUpkString,

    // Ini
    DuplicateSection,
    UnspecifiedFile,
    UnspecifiedType,
    InvalidOffsetPrimary,
    InvalidOffsetTertiary,
    InvalidType,
    InappropriateSize,
    InvalidSize,
    InvalidEnabled,
    Ini_Empty,

    // Coalesced
    CoalescedInvalid,
    CoalescedUnexpectedGame,
    CoalescedDecryptionFailed,
    ArchiveLoadFailed
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

    // Nameof is pretty ugly here.
    public override string GetErrorString() => ErrorType switch
    {
        // Json
        ModError.JsonBadCast => "JSON syntax: unexpected value type",
        ModError.JsonMissingComma => "JSON syntax: missing comma",
        ModError.JsonTrailingComma => "JSON syntax: trailing comma",
        ModError.JsonUnhandled => "JSON syntax: unhandled exception",

        // Ini
        ModError.DuplicateSection => "Contains a duplicate section",
        ModError.UnspecifiedType => "Type was not specified",
        ModError.InvalidOffsetPrimary => "Primary offset was invalid",
        ModError.InvalidOffsetTertiary => "Tertiary offset was invalid",
        ModError.InvalidType => "Type was invalid",
        ModError.InappropriateSize => "Size can only be specified for the integer type",
        ModError.InvalidSize => "Size must equal either '1' or '4'",
        ModError.InvalidEnabled => "Invalid 'Enabled' value",
        ModError.Ini_Empty => "Ini does not contain any sections",

        // ModBase
        ModError.UnspecifiedGame => "Game was not specified",
        ModError.InvalidGame => "Game does not correspond to a valid Game",
        ModError.GameMismatch => "Game does not match the currently-loaded IPA",

        // Generic
        ModError.EmptyFiles => "Files array was not specified or empty",
        ModError.EmptyObjects => "Objects array was not specified or empty",
        ModError.EmptyPatches => "Patches array was not specified or empty",

        // ModFile
        ModError.UnspecifiedFile => $"{nameof(ModFile.FileName)} was not specified",
        // ModError.InvalidFile => $"{nameof(ModFile.FileName)} was not valid",
        ModError.FileNotFound => $"File was not found within the currently-loaded IPA",
        ModError.UnspecifiedFileType => $"{nameof(ModFile.FileType)} was not specified'",
        ModError.InvalidFileType => $"{nameof(ModFile.FileType)} was not set to a valid file type",

        // ModObject
        ModError.ExportNotFound => "Export object was not found within the UPK file",
        ModError.IniNotFound => "Ini file was not found within the Coalesced file",
        ModError.UnspecifiedObject => $"{nameof(ModObject.ObjectName)} was not specified",

        // ModPatch
        ModError.InappropriateSection => $"{nameof(ModPatch._sectionReference)} cannot be specified for UPK patches",
        // ModError.SectionNotFound => $"{nameof(ModPatch._sectionReference)} was not found within the Ini file",
        ModError.InappropriatePatchType => $"{nameof(ModPatch.Type)} cannot be specified for Coalesced patches",
        ModError.UnspecifiedObjectReplace => $"{nameof(ModObject.ObjectName)} in the parent ModObject must be specified in order to use Replace type",
        ModError.InvalidPatchType => $"{nameof(ModPatch.Type)} does not correspond to a valid patch type",
        ModError.InappropriateOffsetCoalesced => $"{nameof(ModPatch.Offset)} cannot be specified for Coalesced patches",
        ModError.InappropriateOffsetReplace => $"{nameof(ModPatch.Offset)} cannot be specified for replace type patches",
        ModError.InvalidOffset => $"{nameof(ModPatch.Offset)} must be more than or equal to 0",
        ModError.UnspecifiedOffset => $"{nameof(ModPatch.Offset)} was not specified",
        ModError.UnspecifiedValue => $"{nameof(ModPatch.Value)} was not specified",
        ModError.InvalidValue => $"{nameof(ModPatch.Value)} could not be parsed",
        ModError.NonAsciiUpkString => $"{nameof(ModPatch.Value)} contains non-ASCII characters. UPK files only support ASCII encoding",

        // Coalesced
        ModError.CoalescedUnexpectedGame => "Coalesced file does not match the loaded game",
        ModError.CoalescedDecryptionFailed => "Failed to decrypt the Coalesced file",
        ModError.ArchiveLoadFailed => "Archive failed to load",
        ModError.CoalescedInvalid => "Not a valid Coalesced file"
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
                SetError(ModError.InvalidGame);
                return;
            }

            if (Game != context.Game)
            {
                SetError(ModError.GameMismatch);
                return;
            }
        }

        #endregion

        #region Files

        if (Files is null || Files.Count == 0)
        {
            SetError(ModError.EmptyFiles);
            return;
        }

        #endregion

        // Look for "Coalesced_ALL" file separately before main loop, since we might modify/add files
        for (int i = Files.Count - 1; i >= 0; i--)
        {
            if (Files[i].FileType is FileType.Coalesced && Files[i].FileName.Equals("coalesced_all", StringComparison.InvariantCultureIgnoreCase))
            {
                foreach (var languageCode in UnrealLib.Globals.GetLanguages(Game))
                {
                    var templateFile = Files[i];

                    string fileName = $"Coalesced_{languageCode}.bin";
                    string qualifiedPath = context.QualifyPath(fileName);

                    // Create a new Coalesced_{languageCode}.bin file and assign it a DEEP copy of the template's objects.
                    var newFile = GetFile(fileName, qualifiedPath, FileType.Coalesced);

                    DeepCopyCoalesced(templateFile, newFile);
                }

                // Delete this template file
                // We're deleting this instead of renaming it to Coalesced_INT so we ensure it merges with any existing Coalesced_INT
                Files.RemoveAt(i);
            }

            // Do not break here since there's no guarnatee there aren't multiple Coalesced_ALL files...
        }

        foreach (var file in Files)
        {
            #region Path

            if (string.IsNullOrEmpty(file.FileName))
            {
                SetError(ModError.UnspecifiedFile, GetErrorLocation(file));
                return;
            }

            // Link and register UnrealArchive so it so it can be extracted later
            if (!context.TryGetCachedArchive(file, out file.Archive))
            {
                SetError(ModError.FileNotFound, GetErrorLocation(file));
                return;
            }

            #endregion

            #region Type

            if (file.FileType is FileType.Unspecified)
            {
                SetError(ModError.InvalidFileType, GetErrorLocation(file));
                return;
            }

            #endregion

            if (file.Objects is null || file.Objects.Count == 0)
            {
                SetError(ModError.EmptyObjects);
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
                    SetError(ModError.EmptyPatches, GetErrorLocation(file, obj));
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
                            SetError(ModError.InvalidValue, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (file.FileType is FileType.Upk && patch.Type is PatchType.String && !Ascii.IsValid(patch.Value.String))
                        {
                            SetError(ModError.NonAsciiUpkString, GetErrorLocation(file, obj, patch));
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
                            SetError(ModError.InappropriatePatchType, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }
                    else if (file.FileType is FileType.Upk)
                    {
                        SetError(ModError.InvalidPatchType, GetErrorLocation(file, obj, patch));
                        return;
                    }

                    #endregion

                    #region Offset

                    if (patch.Offset is not null)
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.InappropriateOffsetCoalesced, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (patch.Type is PatchType.Replace)
                        {
                            SetError(ModError.InappropriateOffsetReplace, GetErrorLocation(file, obj, patch));
                            return;
                        }

                        if (patch.Offset < 0)
                        {
                            SetError(ModError.InvalidOffset, GetErrorLocation(file, obj, patch));
                            return;
                        }
                    }
                    else if (patch.Type is not (PatchType.Unspecified or PatchType.Replace))
                    {
                        SetError(ModError.UnspecifiedOffset, GetErrorLocation(file, obj, patch));
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
                SetError(ModError.ArchiveLoadFailed, GetErrorLocation(file));
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
                            SetError(ModError.ExportNotFound, GetErrorLocation(file, obj));
                            return false;
                        }

                        obj.Export = export;
                    }
                }
                else
                {
                    if (!file.Archive.Coalesced.TryGetIni(obj.ObjectName, out obj.Ini))
                    {
                        SetError(ModError.IniNotFound, GetErrorLocation(file, obj));
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
                            SetError(ModError.InappropriateSection, GetErrorLocation(file, obj, patch));
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
                            SetError(ModError.UnspecifiedFile, GetErrorLocation(file, obj, patch));
                            return false;
                        }
                    }

                    #endregion

                    #region Type

                    if (patch.Type is PatchType.Replace && obj.Export is null)
                    {
                        SetError(ModError.UnspecifiedObjectReplace, GetErrorLocation(file, obj, patch));
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
    /// <param name="template">The ModFile to copy from.</param>
    /// <param name="target">The ModFile to copy to.</param>
    private static void DeepCopyCoalesced(ModFile template, ModFile target)
    {
        // @TODO conditionally filter out locale files e.g. 'SwordGame/Localization/INT/...'

        CollectionsMarshal.SetCount(target.Objects, template.Objects.Count);
        for (int objIdx = 0; objIdx < target.Objects.Count; objIdx++)
        {
            target.Objects[objIdx] = new(template.Objects[objIdx].ObjectName);

            var objTarget = target.Objects[objIdx];
            var objTemplate = template.Objects[objIdx];

            CollectionsMarshal.SetCount(objTarget.Patches, objTemplate.Patches.Count);
            for (int patchIdx = 0; patchIdx < objTarget.Patches.Count; patchIdx++)
            {
                objTarget.Patches[patchIdx] = new()
                {
                    SectionName = objTemplate.Patches[patchIdx].SectionName,
                    Type = objTemplate.Patches[patchIdx].Type,
                    Offset = objTemplate.Patches[patchIdx].Offset,
                    Value = objTemplate.Patches[patchIdx].Value,
                    Enabled = objTemplate.Patches[patchIdx].Enabled
                };
            }
        }
    }
}
