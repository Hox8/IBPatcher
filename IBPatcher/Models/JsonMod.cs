using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnLib.Enums;

namespace IBPatcher.Models;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(JsonModBase))]
public partial class JsonCtx : JsonSerializerContext { }

public static class JsonMod
{
    private static string GetJsonErrorString(JsonException e)
    {
        string context;
        
        // @ERROR: Bad JSON syntax: missing comma.
        if (e.Message.Contains("is invalid after a value.")) context = "Missing comma";

        // @ERROR: Bad JSON syntax: trailing comma.
        else if (e.Message.Contains("contains a trailing comma")) context = "Trailing comma";

        // @ERROR: Bad JSON syntax: unexpected value type.
        else if (e.InnerException?.Message.StartsWith("InvalidCast") == true)
        {
            var sub = e.InnerException.Message.Split(',', StringSplitOptions.TrimEntries);
            return $"Unexpected value type (got {sub[1].ToLower()}, expected {sub[2]})";
        }
        
        // @ERROR: Bad JSON syntax: else.
        else context = "Bad syntax";

        return $"{context} on line {e.LineNumber}";
    }
    
    #region Enum converters
    private static Game? ConvertGame(string? value) => value?.ToLower() switch
    {
        "infinity blade i" or "infinity blade 1" or "ib1" => Game.IB1,
        "infinity blade ii" or "infinity blade 2" or "ib2" => Game.IB2,
        "infinity blade iii" or "infinity blade 3" or "ib3" => Game.IB3,
        "vote" or "vote!" or "vote!!" or "vote!!!" => Game.Vote,
        _ => null
    };
    
    private static FileFlags? ConvertFileFlags(string? value) => value?.ToLower() switch
    {
        "append" => FileFlags.Append,
        "replace" => FileFlags.Replace,
        "delete" => FileFlags.Delete,
        _ => null
    };
    
    private static FileType? ConvertFileType(string? value) => value?.ToLower() switch
    {
        "upk" => FileType.Upk,
        "coalesced" => FileType.Coalesced,
        _ => null
    };
    #endregion
    
    public static Mod ReadJsonMod(string modPath)
    {
        var fileName = Path.GetFileName(modPath);
        var text = File.ReadAllText(modPath);
        JsonModBase jsonMod;

        try
        {
            jsonMod = JsonSerializer.Deserialize(text, typeof(JsonModBase), JsonCtx.Default) as JsonModBase;
        }
        catch (JsonException e)
        {
            return new Mod { Name = fileName, ErrorContext = GetJsonErrorString(e) };
        }
        
        var mod = new Mod
        {
            Name = jsonMod.Name ?? fileName,
            Description = jsonMod.Description,
            Author = jsonMod.Author,
            Version = jsonMod.Version,
            Date = jsonMod.Date
        };
        
        if (ConvertGame(jsonMod.Game) is not Game game)
        {
            // @ERROR: Game was null or invalid.
            mod.ErrorContext = jsonMod.Game is null ? "Game was not specified" : $"Game '{jsonMod.Game}' is invalid";
            return mod;
        }
        mod.Game = game;
        
        if (jsonMod.Files is null || jsonMod.Files.Length == 0)
        {
            // @ERROR: Files was null or empty.
            mod.ErrorContext = "No files were specified";
            return mod;
        }
        
        foreach (var file in jsonMod.Files)
        {
            if (file.File is null)
            {
                // @ERROR: Filename was null.
                mod.ErrorContext = "A file's 'File' field was not specified";
                return mod;
            }
            
            if (ConvertFileType(file.Type) is not FileType fileType)
            {
                // @ERROR: Filetype was null.
                mod.ErrorContext = $"{file.File}: File type was not specified";
                return mod;
            }
            
            if (ConvertFileFlags(file.Mode) is not FileFlags fileFlags)
            {
                if (file.Mode is null) fileFlags = FileFlags.Append;
                else
                {
                    // @ERROR: Passed FileFlags was invalid.
                    mod.ErrorContext = $"{file.File}: File mode '{file.Mode}' is invalid";
                    return mod;
                }
            }

            mod.Files.TryAdd(file.File, new ModFile
            {
                File = file.File,
                Type = fileType,
                Mode = fileFlags,
            });
            
            // CONVERT OBJECT
            if (file.Objects is null || file.Objects.Length == 0)
            {
                // @ERROR: Objects was null or empty.
                mod.ErrorContext = $"{file.File}: No objects were specified";
                return mod;
            }

            foreach (var obj in file.Objects)
            {
                // Object can be an empty string, but error if it's unspecified.
                if (obj.Object is null)
                {
                    mod.ErrorContext = $"{file.File}: An object's 'Object' field was not specified";
                    return mod;
                }
                
                if (ConvertFileFlags(obj.Mode) is not FileFlags objMode)
                {
                    if (file.Mode is null) objMode = FileFlags.Append;
                    else
                    {
                        // @ERROR: Passed FileFlags was invalid.
                        mod.ErrorContext = $"{file.File}: object mode '{obj.Mode}' is invalid";
                        return mod;
                    }
                }

                mod.Files[file.File].Objects.TryAdd(obj.Object, new ModObject
                {
                    Object = obj.Object,
                    Mode = objMode
                });

                // CONVERT PATCH
                if (obj.Patches is null || obj.Patches.Length == 0)
                {
                    // @ERROR: Patches was null or empty.
                    mod.ErrorContext = $"{obj.Object}: No patches were specified";
                    return mod;
                }

                foreach (var patch in obj.Patches)
                {
                    if (ConvertFileFlags(patch.Mode) is not FileFlags patchMode)
                    {
                        if (patch.Mode is null) patchMode = FileFlags.Append;
                        else
                        {
                            // @ERROR: Passed FileFlags was invalid.
                            mod.ErrorContext = $"{obj.Object}: patch mode '{patch.Mode}' is invalid";
                            return mod;
                        }
                    }
                    
                    if (Mod.ConvertPatchType(patch.Type) is not PatchType patchType)
                    {
                        if (fileType is FileType.Upk)
                        {
                            // @ERROR: UPK patch type is null.
                            if (patch.Type is null) mod.ErrorContext = $"{obj.Object}: Patch type was not specified";
                            
                            // @ERROR: UPK patch type not valid.
                            else mod.ErrorContext = $"{obj.Object}: Patch type '{patch.Type}' is invalid";
                            
                            return mod;
                        }

                        // To keep the constructor below happy (this value should never be used)
                        patchType = PatchType.Byte;
                    }
                    
                    if (patch.Value is null)
                    {
                        // @ERROR: Patch value was null.
                        mod.ErrorContext = $"{obj.Object}: Patch value was not specified";
                        return mod;
                    }
                    
                    mod.Files[file.File].Objects[obj.Object].Patches.Add(new ModPatch
                    {
                        Section = patch.Section,
                        Mode = patchMode,
                        Type = patchType,
                        Offset = patch.Offset,
                        Value = (JsonElement)patch.Value,
                        Enabled = patch.Enabled ?? true
                    });
                }
            }
        }
        
        return mod;
    }
}

#region Intermediary classes
// These 'intermediate' classes reflect the ones found in Mod.cs, but with certain fields 'dumbed down'.
// This is done so I don't have to use any JsonConverter overrides, which are especially painful.

public class JsonPatch
{
    public string? Section { get; init; }
    public string? Type { get; init; }
    public string? Mode { get; set; }
    public int? Offset { get; init; }
    public JsonElement? Value { get; init; }
    public bool? Enabled { get; init; }
}

public class JsonObject
{
    public string? Object { get; set; }
    public string? Mode { get; init; }
    public JsonPatch[]? Patches { get; init; }
}

public class JsonFile
{
    public string? File { get; set; }
    public string? Type { get; init; }
    public string? Mode { get; init; }
    public JsonObject[]? Objects { get; set; }
}

public class JsonModBase
{
    public string? Name { get; init; }
    public string? Game { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public string? Version { get; init; }
    public string? Date { get; init; }
    public JsonFile[]? Files { get; set; }
}

#endregion