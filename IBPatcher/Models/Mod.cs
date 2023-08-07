using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UnLib;
using UnLib.Coalesced;
using UnLib.Core;
using UnLib.Enums;
using UnLib.Interfaces;

namespace IBPatcher.Models;

// @TODO: Are the Dictionaries used in Mod and ModFile really necessary? I don't think I've used them.
// It also makes it difficult to provide error context as dictionaries are not indexable. (What about ordered dicts?)

#region Enums

public enum PatchType : byte
{
    Byte,
    Boolean,  // evil
    UInt8,
    Int32,
    Float,
    String,
    Replace
}

/// <summary>
/// A set of flags influencing how to interact with files, UObjects, Inis, and Sections. 
/// </summary>
public enum FileFlags : byte
{
    /// <summary>
    /// Try to use the existing target. If it does not exist, an error is thrown.
    /// </summary>
    Append,
    
    /// <summary>
    /// Overwrite the target with a new copy regardless of whether it already exists.
    /// </summary>
    Replace,
    
    /// <summary>
    /// Delete the target if it exists. No error is thrown if target does not exist.
    /// </summary>
    Delete
}

public enum FileType : byte
{
    Upk,
    Coalesced
    // Raw?
}

#endregion

public class ModPatch
{
    /// <summary>
    /// Name of the ini section to modify.<br/>
    /// <br/> - Required for coalesced mods. 
    /// </summary>
    public string? Section { get; init; }
    
    /// <summary>
    /// Optional flag influencing how to treat the ini section. See <see cref="FileFlags"/> for more information.<br/>
    /// <br/> - Defaults to <see cref="FileFlags.Append"/>.
    /// </summary>
    public FileFlags Mode { get; init; } = FileFlags.Append;
    
    /// <summary>
    /// Determines the type according to <see cref="PatchType"/> of the value.<br/>
    /// <br/> - Required for UPK mods.
    /// </summary>
    public PatchType? Type { get; init; }
    
    /// <summary>
    /// The offset relative to the start of the UObject (or file, if a UObject was not passed).<br/>
    /// <br/> - Required for UPK mods.
    /// <br/> - Not required by patch type <see cref="PatchType.Replace"/>.
    /// </summary>
    public int? Offset { get; init; }
    
    /// <summary>
    /// The patch value to apply. <see cref="JsonElement"/> allows to parse all types.<br/>
    /// <br/> - Section patches can use strings for single properties, or string arrays for multiple.
    /// </summary>
    public JsonElement Value { get; init; }
    
    /// <summary>
    /// Optional boolean that, when set to false, will skip the current patch. Defaults to true.
    /// </summary>
    public bool Enabled { get; init; }

    // Private
    
    /// <summary>
    /// Reference to the Section for convenience.
    /// </summary>
    internal Section? _section;
    
    /// <summary>
    /// <see cref="JsonElement"/> converted to byte array.
    /// </summary>
    internal byte[] _value;
}

public class ModObject
{
    /// <summary>
    /// The object to target.<br/>
    /// <br/> - Can be null for UPK mods only, which will then target the file root and act as an Ini mod.
    /// <br/> - For UPK mods, this is the full name of an export object.
    /// <br/> - For Coalesced mods, this is the Ini path within the coalesced file.
    /// </summary>
    public string? Object { get; init; }
    
    /// <summary>
    /// Optional flag influencing how to treat the Ini or Export object. See <see cref="FileFlags"/> for more information.<br/>
    /// <br/> - Defaults to <see cref="FileFlags.Append"/>.
    /// </summary>
    public FileFlags Mode { get; init; } = FileFlags.Append;
    public List<ModPatch> Patches { get; init; } = new();

    // Private
    
    /// <summary>
    /// Reference to the export object for convenience. 
    /// </summary>
    internal FObjectExport? _export;
    
    /// <summary>
    /// Reference to the Ini for convenience. 
    /// </summary>
    internal Ini? _ini;
}

public class ModFile
{
    /// <summary>
    /// Name of the file within the IPA to target.
    /// </summary>
    public string File { get; init; } = string.Empty;
    
    /// <summary>
    /// Flag influencing how to parse <see cref="File"/>. See <see cref="FileType"/> for more information.
    /// </summary>
    public FileType Type { get; init; }
    
    /// <summary>
    /// Optional flag influencing how to treat the file. See <see cref="FileFlags"/> for more information.<br/>
    /// <br/> - Defaults to <see cref="FileFlags.Append"/>.
    /// </summary>
    public FileFlags Mode { get; init; } = FileFlags.Append;
    public Dictionary<string, ModObject> Objects { get; init; } = new();
    
    // Private
    
    /// <summary>
    /// Reference to the UPK/Coalesced stream.
    /// </summary>
    internal IUnrealStreamable Stream;
    
    /// <summary>
    /// The fully-qualified filepath within the IPA. Derived from <see cref="File"/>.
    /// </summary>
    public string? QualifiedPath;
}

public class Mod
{
    /// <summary>
    /// Friendly name of the mod. If null or not specified, the mod's filename will be used.
    /// </summary>
    public string Name { get; init; }
    
    /// <summary>
    /// A description of the mod's function. Unused by IBPatcher CLI.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// The author(s) of the mod. Unused by IBPatcher CLI.
    /// </summary>
    public string? Author { get; init; }
    
    // @TODO Going to need a json format version number.
    /// <summary>
    /// String version of the mod. Unused by IBPatcher CLI.
    /// </summary>
    public string? Version { get; init; }
    
    /// <summary>
    /// The date the mod was created, using the ISO 8601 format. Unused by IBPatcher CLI.
    /// </summary>
    public string? Date { get; init; }

    public Dictionary<string, ModFile> Files = new();
    
    /// <summary>
    /// The <see cref="Game"/> Game of the mod. If the loaded IPA does not match the mod's game, an error will be thrown.
    /// </summary>
    public Game Game { get; set; }

    public string ErrorContext { get; set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorContext);

    // Some things are handled differently for ini mods.
    public bool IsIni = false;
    
    
    /// Validates and applies the mod if no errors occurred.
    public bool Process(ModContext modContext)
    {
        if (HasError) return false;

        // @ERROR: Valid game did not match the loaded IPA.
        if (Game != modContext.Game && !IsIni)
        {
            ErrorContext = $"Mod is for {UnLib.Globals.GameToString(Game)}, but loaded IPA is {modContext.GameString}";
            return false;
        }

        // change to 'for' loop for indexing.
        // todo empty ini
        foreach (var file in Files.Values)
        {
            // @ERROR: Filename was null.
            if (string.IsNullOrEmpty(file.File))
            {
                ErrorContext = "Filename was not specified!";
                return false;
            }
            
            // @ERROR: UPK/Coalesced failed to be read.
            if (file.Stream.InitFailed)
            {
                ErrorContext = $"Failed to parse '{file.File}'!";
                return false;
            }

            foreach (var obj in file.Objects.Values)
            {
                #region UPK
                if (file.Type is FileType.Upk)
                {
                    // If object name was passed, try find UObject.
                    if (!string.IsNullOrEmpty(obj.Object))
                    {
                        var unObject = ((UnrealPackage)file.Stream).FindObject(obj.Object);
                    
                        // @ERROR: UObject was not found.
                        if (unObject is null)
                        {
                            ErrorContext = "UObject was not found!";
                            return false;
                        }

                        // @ERROR: UObject was not an FObjectExport.
                        if (unObject is not FObjectExport export)
                        {
                            ErrorContext = "UObject is not an export!";
                            return false;
                        }

                        obj._export = export;   
                    }
                }
                #endregion

                #region Coalesced
                else
                {
                    // @ERROR: Object name (Ini path) was null.
                    if (string.IsNullOrEmpty(obj.Object))
                    {
                        ErrorContext = $"An object under '{file.File}' did not specify a name";
                        return false;
                    }

                    // Cache Ini.
                    ((Coalesced)file.Stream).Inis.TryGetValue(obj.Object, out obj._ini);

                    switch (obj.Mode)
                    {
                        case FileFlags.Delete:
                            ((Coalesced)file.Stream).Inis.Remove(obj.Object);
                            file.Stream.Modified = true;
                            continue;
                        case FileFlags.Replace:
                            ((Coalesced)file.Stream).Inis[obj.Object] = new Ini();
                            file.Stream.Modified = true;
                            break;
                        case FileFlags.Append:
                            // @ERROR: Ini was null.
                            if (obj._ini is null)
                            {
                                ErrorContext = $"The ini file '{obj.Object}' was not found in '{file.File}'!";
                                return false;
                            }
                            break;
                    }
                }
                #endregion
                
                foreach (var patch in obj.Patches)
                {
                    if (!patch.Enabled) continue;
                    
                    #region UPK
                    if (file.Type is FileType.Upk)
                    {
                        if (patch.Type is PatchType.Replace)
                        {
                            // @ERROR: Used 'Replace' without having an active UObject.
                            if (obj._export is null)
                            {
                                ErrorContext = "Cannot use 'Replace' without specifying a UObject!";
                                return false;
                            }
                        }
                        else
                        {
                            // @ERROR: Offset was null.
                            if (patch.Offset is null)
                            {
                                ErrorContext = "Offset was not specified!";
                                return false;
                            }
                        
                            // @ERROR: Offset was negative.
                            if (patch.Offset < 0)
                            {
                                ErrorContext = "Offset cannot be negative!";
                                return false;
                            }
                        }

                        // Parse UPK patch data in external method. If its returned error isn't null, error.
                        ErrorContext = ParsePatchValue(patch, (UnrealPackage)file.Stream);
                        if (HasError) return false;
                        
                        // Check for overflow
                        if (patch.Type is not PatchType.Replace)
                        {
                            int endPosition = (int)patch.Offset + patch._value.Length;
                            
                            if (obj._export is not null)
                            {
                                // @ERROR: Parsed patch value would exceed the UObject's data.
                                if (endPosition > obj._export.SerialOffset + obj._export.SerialSize)
                                {
                                    ErrorContext = "UObject overflow!";
                                    return false;
                                }
                            }
                            // @ERROR: Patch value would exceed the file's length (.ini only).
                            else if (endPosition > file.Stream.Length)
                            {
                                ErrorContext = "UPK overflow!";
                                return false;
                            }
                        }

                        // Write patch data to UPK
                        if (patch.Type is PatchType.Replace)
                        {
                            ((UnrealPackage)file.Stream).ReplaceExportData(obj._export, patch._value);
                        }
                        else
                        {
                            file.Stream.BaseStream.Position = obj._export?.SerialOffset ?? 0 + (int)patch.Offset;
                            file.Stream.Write(patch._value);
                        }

                        file.Stream.Modified = true;
                    }
                    #endregion

                    #region Coalesced
                    else
                    {
                        // @ERROR: Section name was not specified.
                        if (string.IsNullOrEmpty(patch.Section))
                        {
                            ErrorContext = "Section cannot be null!";
                            return false;
                        }
                        
                        // Cache section
                        obj._ini.Sections.TryGetValue(patch.Section, out patch._section);

                        switch (patch.Mode)
                        {
                            case FileFlags.Delete:
                                obj._ini.Sections.Remove(patch.Section);
                                continue;
                            case FileFlags.Replace:
                                obj._ini.Sections[patch.Section] = new Section();
                                break;
                            case FileFlags.Append:
                                // @ERROR: Section was null.
                                if (patch._section is null)
                                {
                                    ErrorContext = "Section was null!";
                                    return false;
                                }
                                break;
                        }
                        
                        if (patch.Value.ValueKind is JsonValueKind.Array)
                        {
                            foreach (var item in patch.Value.EnumerateArray())
                            {
                                patch._section.UpdateProperty(item.ToString());
                            }
                        }
                        else
                        {
                            patch._section.UpdateProperty(patch.Value.ToString());
                        }

                        file.Stream.Modified = true;
                    }
                    #endregion
                }
            }
        }

        return true;
    }

    #region Helpers
    
    public static bool ParseBoolean(string value) =>
        string.Equals("true", value, StringComparison.OrdinalIgnoreCase) || value == "1";
    
    private static string ParsePatchValue(ModPatch patch, UnrealPackage upk)
    {
        // @TODO: Implement a way to take advantage of JsonElement value
        string value = patch.Value.ToString();
        
        switch (patch.Type)
        {
            case PatchType.Byte:
            case PatchType.Replace:
                // @TODO: Determine whether to keep Name/Object reference support.
                string error = ParseBytes(value, patch, upk);
                if (error.Length != 0) return error;
                break;
            
            case PatchType.Boolean:
                patch._value = new[] { ParseBoolean(value) ? (byte)0 : (byte)1 };
                break;
            
            case PatchType.UInt8:
                if (byte.TryParse(value, out byte ui8))
                {
                    patch._value = new[] { ui8 };
                }
                // @ERROR: Failed to convert to UInt8.
                else return $"could not convert '{value}' to a UInt8!";
                break;
            case PatchType.Int32:
                if (int.TryParse(value, out int i32))
                {
                    patch._value = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(patch._value, i32);
                }
                // @ERROR: Failed to convert to Int32.
                else return $"could not convert '{value}' to an Int32!";
                break;
            case PatchType.Float:
                if (float.TryParse(value, out float f32))
                {
                    patch._value = new byte[4];
                    BinaryPrimitives.WriteSingleLittleEndian(patch._value, f32);
                }
                // @ERROR: Failed to convert to Float32.
                else return $"could not convert '{value}' to a Float32!";
                break;
            
            case PatchType.String:
                // @ERROR: UPK string used Unicode encoding.
                if (!UnrealStream.IsPureAscii(value))
                {
                    return "UPK strings must use ASCII-encoding!";
                }
                patch._value = Encoding.ASCII.GetBytes(value);
                break;
        }

        return string.Empty;
    }

    // @TODO find a better way. Converting a string to a stringbuilder to a byte array is not ideal.
    private static string ParseBytes(string value, ModPatch patch, UnrealPackage pkg)
    {
        var sb = new StringBuilder();
        int index = 0;

        while (index < value.Length)
        {
            char c = value[index];

            if (char.IsAsciiHexDigit(c)) sb.Append(c);
            else if (!char.IsWhiteSpace(c))
            {
                if (c is '{' or '[')
                {
                    var reference = new StringBuilder();

                    while (index < value.Length && c != '}' && c != ']')
                    {
                        c = value[index];

                        reference.Append(c);

                        index++;
                    }

                    // UObject reference
                    if (reference[0] == '[' && reference[^1] == ']')
                    {
                        var objName = reference.ToString(1, reference.Length - 2);
                        var result = pkg.FindObject(objName);

                        if (result is null) return $"REFERENCE could not find a UObject matching '{objName}'!";
                        if (result is not FObjectExport) return $"";

                        sb.Append(IntToHexString(result.SerializedIndex));
                    }

                    // Name reference
                    else if (reference[0] == '{' && reference[^1] == '}')
                    {
                        var name = reference.ToString(1, reference.Length - 2);
                        var result = pkg.FindName(name);

                        if (result is null) return $"NAME could not find a name matching '{name}'";

                        sb.Append(IntToHexString(result.Index));
                        sb.Append(IntToHexString(result.Instance));
                    }
                    else return $"invalid character '{reference.ToString(1, 1)}' in byte value!";
                }
                else return $"invalid character '{c}' in byte value!";
            }

            index++;
        }

        patch._value = Convert.FromHexString(sb.ToString());
        return string.Empty;
    }

    // A "what-if" I didn't need reference support.
    private static string ParseBytesReduced(string value, ModPatch patch, UnrealPackage upk)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsAsciiHexDigit(value[i])) sb.Append(value[i]);
            else if (!char.IsWhiteSpace(value[i])) return $"Invalid character '{value[i]}' in byte value!";
        }

        // @TODO: Verify whether odd lengths are valid.
        patch._value = Convert.FromHexString(sb.ToString());
        return string.Empty;
    }

    /// <summary>
    /// Takes an <see cref="Int32"/> and outputs the corresponding little-endian hex string.
    /// </summary>
    /// <param name="value">The <see cref="Int32"/> to conert.</param>
    /// <returns>A little-endian hex string.</returns>
    public static string IntToHexString(int value) =>
        $"{value & 0xFF:X2}{(value >> 8) & 0xFF:X2}{(value >> 16) & 0xFF:X2}{(value >> 24) & 0xFF:X2}";

    #endregion
    
    #region EnumConverters
    
    public static PatchType? ConvertPatchType(string? value) => value?.ToLower() switch
    {
        "byte" => PatchType.Byte,
        "boolean" => PatchType.Boolean,
        "uint8" => PatchType.UInt8,
        "int32" => PatchType.Int32,
        "float" => PatchType.Float,
        "string" => PatchType.String,
        "replace" => PatchType.Replace,
        _ => null
    };
    
    #endregion
}