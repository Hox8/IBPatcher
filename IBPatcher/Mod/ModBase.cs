using System.Diagnostics;
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
    InvalidFile,
    FileNotFound,
    UnspecifiedFileType,
    ExportNotFound,
    IniNotFound,
    UnspecifiedObject,
    EmptyPatches,
    InappropriateSection,
    SectionNotFound,
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

    // Coalesced
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
    public string SectionName;
    public PatchType Type = PatchType.Unspecified;
    public int? Offset;
    public ModPatchValue Value;
    public bool Enabled = true;

    internal Section Section;

    public bool TryParseValue(string value)
    {
        try
        {
            Value = Type switch
            {
                PatchType.Bool => new ModPatchValue { UInt8 = (byte)(value == "0" ? 0x00 : 0x01) },
                PatchType.UBool => new ModPatchValue { UInt8 = (byte)(value == "0" ? 0x28 : 0x27) },
                PatchType.UInt8 => new ModPatchValue { UInt8 = byte.Parse(value) },
                PatchType.Int32 => new ModPatchValue { Int32 = int.Parse(value) },
                PatchType.Float => new ModPatchValue { Float = float.Parse(value) },
                PatchType.String => new ModPatchValue { String = value },
                PatchType.Byte => new ModPatchValue { Bytes = Convert.FromHexString(value.Replace(" ", "")) },
                _ => new ModPatchValue()
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
    public string ObjectName = objectName;
    public List<ModPatch> Patches = new();

    internal Ini Ini;
    internal FObjectExport Export;

    public override string ToString() => string.IsNullOrEmpty(ObjectName) ? ObjectName : "<Nameless Object>" + $" | {Patches.Count} patches";
}
public class ModFile(string userPath, string qualifiedPath, FileType fileType)
{
    public string FileName = userPath;
    public FileType FileType = fileType;
    public List<ModObject> Objects = new();

    /// <summary>
    /// A lower-case, fully-qualified IPA filepath.
    /// </summary>
    internal readonly string QualifiedIpaPath = qualifiedPath;
    internal CachedArchive Archive;

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

public class ModBase(string path, ModFormat type, Game game)
    : ErrorHelper<ModError>
{
    public string Name = Path.GetFileName(path);
    public Game Game = game;
    public List<ModFile> Files = new();
    // Other attributes exist in the schema for future-proofing but are not included here

    internal string ModPath = path;     // Path to mod file on disk.
    internal string ErrorContext = "";  // Where the error occurred. Can be ini section name, a json file etc.
    internal ModFormat ModType = type;

    public override bool HasError => Error is not ModError.None;

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

    public override string ToString() => Name;

    public void Setup(ModContext ctx)
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

            if (Game != ctx.Game)
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

        foreach (var file in Files)
        {
            #region Path

            if (string.IsNullOrEmpty(file.FileName))
            {
                SetError(ModError.InvalidFile, file);
                return;
            }

            // Link and register UnrealArchive so it so it can be extracted later
            if (!ctx.TryGetCachedArchive(file, out file.Archive))
            {
                SetError(ModError.FileNotFound, file);
                return;
            }

            #endregion

            #region Type

            if (file.FileType is FileType.Unspecified)
            {
                SetError(ModError.InvalidFileType, file);
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
                    SetError(ModError.EmptyPatches, file, obj);
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
                            SetError(ModError.InvalidValue, file, obj, patch);
                            return;
                        }

                        if (file.FileType is FileType.Upk && patch.Type is PatchType.String && !Ascii.IsValid(patch.Value.String))
                        {
                            SetError(ModError.NonAsciiUpkString, file, obj, patch);
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
                            SetError(ModError.InappropriatePatchType, file, obj, patch);
                            return;
                        }

                        if (patch.Type is PatchType.Replace && obj.Export is null)
                        {
                            SetError(ModError.UnspecifiedObjectReplace, file, obj, patch);
                            return;
                        }
                    }
                    else if (file.FileType is FileType.Upk)
                    {
                        SetError(ModError.InvalidPatchType, file, obj, patch);
                        return;
                    }

                    #endregion

                    #region Offset

                    if (patch.Offset is not null)
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.InappropriateOffsetCoalesced, file, obj, patch);
                            return;
                        }

                        if (patch.Type is PatchType.Replace)
                        {
                            SetError(ModError.InappropriateOffsetReplace, file, obj, patch);
                            return;
                        }

                        if (patch.Offset < 0)
                        {
                            SetError(ModError.InvalidOffset, file, obj, patch);
                            return;
                        }
                    }
                    else if (patch.Type is not PatchType.Unspecified or PatchType.Replace)
                    {
                        SetError(ModError.UnspecifiedOffset, file, obj, patch);
                        return;
                    }

                    #endregion
                }
            }
        }
    }

    public bool Link()
    {
        if (HasError) return false;

        foreach (var file in Files)
        {
            if (file.Archive.HasError)
            {
                SetError(ModError.ArchiveLoadFailed, file);
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
                            SetError(ModError.ExportNotFound, file, obj);
                            return false;
                        }

                        obj.Export = export;
                    }
                }
                else
                {
                    if (!file.Archive.Coalesced.TryGetIni(obj.ObjectName, out obj.Ini))
                    {
                        SetError(ModError.IniNotFound, file, obj);
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
                            SetError(ModError.InappropriateSection, file, obj, patch);
                            return false;
                        }

                        if (!obj.Ini.TryGetSection(patch.SectionName, out patch.Section))
                        {
                            SetError(ModError.SectionNotFound, file, obj, patch);
                            return false;
                        }
                    }
                    else
                    {
                        if (file.FileType is FileType.Coalesced)
                        {
                            SetError(ModError.UnspecifiedFile, file, obj, patch);
                            return false;
                        }
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

    public void Write()
    {
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
                        // @TODO Stream isn't meant to be public... but it makes things so easy.

                        var upk = file.Archive.Upk;
                        upk.Stream.StartSaving();
                        upk.Stream.Position = (int)patch.Offset + (obj.Export?.SerialOffset ?? 0);

                        switch (patch.Type)
                        {
                            case PatchType.Bool:
                            case PatchType.UBool:
                            case PatchType.UInt8:
                                upk.Stream.Serialize(ref patch.Value.UInt8);
                                break;
                            case PatchType.Int32:
                            case PatchType.Float:
                                upk.Stream.Serialize(ref patch.Value.Int32);
                                break;
                            case PatchType.String:
                            case PatchType.Byte:
                                upk.Stream.Write(patch.Value.Bytes);
                                break;
                            case PatchType.Replace:
                                upk.ReplaceExportData(obj.Export, patch.Value.Bytes);
                                break;
                        }
                    }
                    else
                    {
                        Debug.Assert(patch.Section is not null);

                        if (patch.Value.String is not null)
                        {
                            patch.Section.UpdateProperty(patch.Value.String);
                        }
                        else
                        {
                            foreach (var property in patch.Value.Strings)
                            {
                                patch.Section.UpdateProperty(property);
                            }
                        }
                    }

                    file.Archive.Modified = true;
                }
            }
        }
    }

    public void SetError(ModError error, ModFile? file = null, ModObject? obj = null, ModPatch? patch = null)
    {
        Error = error;
        var sb = new StringBuilder();

        if (file is not null)
        {
            sb.Append(string.IsNullOrWhiteSpace(file.FileName) ? $"File: {Files.IndexOf(file)}" : file.FileName);

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

            ErrorContext = sb.ToString();
        }
    }

    public void SetError(ModError error, Section section)
    {
        Error = error;
        ErrorContext = section.Name;
    }

    public override string GetString(ModError error) => error switch
    {
        // Json
        ModError.JsonBadCast => "JSON syntax: unexpected value type",
        ModError.JsonMissingComma => "JSON syntax: missing comma",
        ModError.JsonTrailingComma => "JSON syntax: trailing comma",
        ModError.JsonUnhandled => "JSON syntax: unhandled exception",

        // ModBase
        ModError.UnspecifiedGame => "'Game' was not specified",
        ModError.InvalidGame => "'Game' does not correspond to a valid Game",
        ModError.GameMismatch => "'Game' does not match the currently-loaded IPA",

        // Generic?
        ModError.EmptyFiles => "'Files' array was either null or empty",
        ModError.EmptyObjects => "'Objects' array was either null or empty",
        ModError.EmptyPatches => "'Patches' array was either null or empty",

        // ModFile
        ModError.UnspecifiedFile => $"'{nameof(ModFile.FileName)}' was not specified",
        ModError.InvalidFile => $"'{nameof(ModFile.FileName)}' was not valid",
        ModError.FileNotFound => $"The file was not found within the currently-loaded IPA",
        ModError.UnspecifiedFileType => $"'{nameof(ModFile.FileType)}' was not specified'",
        ModError.InvalidFileType => $"'{nameof(ModFile.FileType)}' does not correspond to a valid file type",

        // ModObject
        ModError.ExportNotFound => "Export object was not found within the UPK file",
        ModError.IniNotFound => "Ini file was not found within the Coalesced file",
        ModError.UnspecifiedObject => $"'{nameof(ModObject.ObjectName)}' must be specified for Coalesced objects",  // @TODO: Do this for UPK too?

        // ModPatch
        ModError.InappropriateSection => $"'{nameof(ModPatch.Section)}' cannot be specified for UPK patches",
        ModError.SectionNotFound => $"{nameof(ModPatch.Section)} was not found within the Ini file",
        ModError.InappropriatePatchType => $"{nameof(ModPatch.Type)} cannot be specified for Coalesced patches",
        ModError.UnspecifiedObjectReplace => $"'{nameof(ModObject.ObjectName)}' in the parent ModObject must be specified in order to use Replace type",
        ModError.InvalidPatchType => $"{nameof(ModPatch.Type)} does not correspond to a valid patch type",
        ModError.InappropriateOffsetCoalesced => $"{nameof(ModPatch.Offset)} cannot be specified for Coalesced patches",
        ModError.InappropriateOffsetReplace => $"{nameof(ModPatch.Offset)} cannot be specified for replace type patches",
        ModError.InvalidOffset => $"{nameof(ModPatch.Offset)} must be more than or equal to 0",
        ModError.UnspecifiedOffset => $"{nameof(ModPatch.Offset)} must be specified for UPK patches",
        ModError.UnspecifiedValue => $"{nameof(ModPatch.Value)} was not specified",
        ModError.InvalidValue => $"Failed to parse {nameof(ModPatch.Value)}",
        ModError.NonAsciiUpkString => $"String {nameof(ModPatch.Value)} contains non-ASCII characters. UPK files only support ASCII encoding",

        // Ini
        ModError.DuplicateSection => "Ini mod contains a duplicate section",
        ModError.UnspecifiedType => "'Type' was not specified",
        ModError.InvalidOffsetPrimary => "Invalid primary offset",
        ModError.InvalidOffsetTertiary => "Invalid tertiary offset",
        ModError.InvalidType => "Invalid type",
        ModError.InappropriateSize => "Size can only be specified for the integer type",
        ModError.InvalidSize => "Size must equal either '1' or '4'",
        ModError.InvalidEnabled => "Invalid 'Enabled' value",

        // Coalesced
        ModError.CoalescedUnexpectedGame => "Coalesced file does not match the requested game",
        ModError.CoalescedDecryptionFailed => "Failed to decrypt the Coalesced file",
        ModError.ArchiveLoadFailed => "Archive failed to load",
    };
}
