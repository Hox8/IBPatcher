using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using UnLib.Enums;

namespace IBPatcher.Models;

public static class IniMod
{
    public static Mod ReadIniMod(string modPath)
    {
        var fileName = Path.GetFileName(modPath);
        string[] lines = File.ReadAllLines(modPath);
        IniModSection? curSection = null;

        var mod = new Mod
        {
            Name = fileName,
            Game = Game.Unknown,
            IsIni = true
        };
        
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed[0] == ';') continue;
            
            // Start of new IniModSection.
            if (trimmed[0] == '[')
            {
                if (trimmed[^1] != ']')
                {
                    // @ERROR: Bad ini syntax (malformed name header).
                    mod.ErrorContext = $"Bad ini syntax, line {i + 1}";
                    return mod;
                }
                
                if (curSection is not null && !curSection.MapToMod(mod)) return mod;
                curSection = new IniModSection { Name = trimmed[1..^1] };
                continue;
            }

            if (curSection is null)
            {
                // @ERROR: Bad ini syntax (properties appeared before section).
                mod.ErrorContext = $"Bad ini syntax, line {i + 1}";
                return mod;
            }
            
            // Remember that there will only ever be TWO items in this array!
            var sub = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);

            switch (sub[0].ToLower())
            {
                case "file":
                    curSection.File = sub[1];
                    break;
                
                case "offset":
                    var offset = sub[1].Split('+', StringSplitOptions.TrimEntries);
                    
                    curSection.Offset = TryParseInt(offset[0]);
                    if (curSection.Offset is null)
                    {
                        // @ERROR: Bad primary offset.
                        mod.ErrorContext = $"Offset '{sub[1]}' is invalid - line {i + 1}";
                        return mod;
                    }

                    for (int j = 1; j < offset.Length; j++)
                    {
                        if (!int.TryParse(offset[j], out int tertiary))
                        {
                            // @ERROR: Bad tertiary offset.
                            mod.ErrorContext = $"Offset '{sub[1]}' is invalid - line {i + 1}";
                            return mod;
                        }
                        
                        curSection.Offset += tertiary;
                    }
                    break;
                
                case "type":
                    if (Mod.ConvertPatchType(sub[1]) is not PatchType type)
                    {
                        // @ERROR: Invalid patch type.
                        mod.ErrorContext = $"Type '{sub[1]}' is invalid - line {i + 1}";
                        return mod;
                    }

                    curSection.Type = type;
                    break;
                
                case "value":
                    curSection.Value = JsonDocument.Parse($"\"{sub[1]}\"").RootElement;
                    break;
                
                case "size":
                    if (!int.TryParse(sub[1], out int size))
                    {
                        mod.ErrorContext = $"Size '{sub[1]}' is invalid - line {i + 1}";
                        return mod;
                    }

                    curSection.Size = size;
                    break;
                
                case "enable":
                    curSection.Enabled = Mod.ParseBoolean(sub[1]);
                    break;
                
                case "original":
                    break;
                
                default:
                    // @ERROR: Invalid ini key.
                    mod.ErrorContext = $"Ini parameter '{sub[0]}' is invalid - line {i + 1}";
                    return mod;
            }
        }

        // Add the final section.
        curSection?.MapToMod(mod);
        return mod;
    }

    #region Helpers
    
    private static int? TryParseInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value[2..], NumberStyles.HexNumber, null, out int hex)) return hex;
        }
        
        else if (int.TryParse(value, out int output))
        {
            return output;
        }

        return null;
    }

    #endregion
}

public class IniModSection
{
    public string? Name;
    public string? File;
    public int? Offset;
    public PatchType? Type;
    public JsonElement? Value;
    public int? Size;
    public bool? Enabled;

    public bool MapToMod(Mod mod)
    {
        if (File is null)
        {
            // @ERROR: File was null.
            mod.ErrorContext = $"File parameter not specified - {Name}";
            return false;
        }
        
        if (Offset is null)
        {
            // @ERROR: Offset was null.
            mod.ErrorContext = $"Offset parameter not specified - {Name}";
            return false;
        }

        if (Type is null)
        {
            // @ERROR: Patch type was null.
            mod.ErrorContext = $"Type parameter not specified - {Name}";
            return false;
        }
        
        if (Value is null)
        {
            // @ERROR: Patch value was null.
            mod.ErrorContext = $"Value parameter not specified - {Name}";
            return false;
        }

        if (Size is not null)
        {
            // If size is set to 1 and type is integer, set type to UInt8 (Ini only has generic Int type).
            if (Size == 1)
            {
                if (Type is PatchType.Int32) Type = PatchType.UInt8;
            }
            else if (Size != 4)
            {
                // @ERROR: Ini size was invalid.
                mod.ErrorContext = $"Size value must be either 1 or 4 - {Name}";
                return false;
            }
        }

        mod.Files.TryAdd(File, new ModFile { File = File });
        mod.Files[File].Objects.TryAdd(string.Empty, new ModObject());
        mod.Files[File].Objects[string.Empty].Patches.Add(new ModPatch
        {
            Type = (PatchType)Type,
            Offset = Offset,
            Value = (JsonElement)Value,
            Enabled = Enabled ?? true,
        });

        return true;
    }
}