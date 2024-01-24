using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBPatcher.Mod;

public static class JsonMod
{
    // Reads a JSON object into the correspond JsonMod classes.
    // See the comments at the bottom of this page for a thorough explanation.
    public static ModBase Read(string modPath, ModContext ctx)
    {
        var mod = new ModBase(modPath, ModFormat.Json, ctx.Game);
        JsonModBase json;

        byte[] utf8Bytes = File.ReadAllBytes(modPath);

        try
        { 
            json = JsonSerializer.Deserialize(utf8Bytes, Ctx.Default.JsonModBase);
        }
        catch (JsonException e)
        {
            ParseJsonError(e, mod);

            return mod;
        }

        mod.Name = string.IsNullOrWhiteSpace(json.Name) ? Path.GetFileName(modPath) : json.Name;

        if (json.Game is null)
        {
            mod.SetError(ModError.Generic_UnspecifiedGame);
            return mod;
        }

        mod.Game = EnumConverters.GetGame(json.Game);

        if (json.Files is null) return mod;
        for (int i = 0; i < json.Files.Length; i++)
        {
            var jsonFile = json.Files[i];

            if (string.IsNullOrWhiteSpace(jsonFile.File))
            {
                mod.SetError(ModError.Generic_UnspecifiedFile, $"File: {i}");
                return mod;
            }

            var modFile = mod.GetFile(jsonFile.File, ctx.QualifyPath(jsonFile.File), EnumConverters.GetFileType(jsonFile.Type));

            if (string.IsNullOrWhiteSpace(jsonFile.Type))
            {
                mod.SetError(ModError.Generic_UnspecifiedFileType, mod.GetErrorLocation(modFile));
                return mod;
            }

            if (jsonFile.Objects is null) return mod;
            foreach (var jsonObj in jsonFile.Objects)
            {
                var modObj = modFile.GetObject(jsonObj.Object);

                if (jsonObj.Patches is null) return mod;
                foreach (var jsonPatch in jsonObj.Patches)
                {
                    modObj.Patches.Add(new ModPatch
                    {
                        Enabled = jsonPatch.Enabled ?? true,
                        Value = new ModPatchValue(),
                        SectionName = jsonPatch.Section,
                        Type = EnumConverters.GetPatchType(jsonPatch.Type),
                        Offset = jsonPatch.Offset
                    });

                    if (jsonPatch.Value is null)
                    {
                        mod.SetError(ModError.Generic_UnspecifiedValue, mod.GetErrorLocation(modFile, modObj, modObj.Patches[^1]));
                        return mod;
                    }

                    // A little hacky. We'll reference this when loading the mod "for real" during ModBase::Setup()
                    modObj.Patches[^1].Value.String = jsonPatch.Value.ToString();
                }
            }
        }

        return mod;
    }

    // Ugly hacky method to discern JsonException type from string message
    private static void ParseJsonError(JsonException e, ModBase mod)
    {
        if (e.Message.Contains("is invalid after a value."))
        {
            mod.SetError(ModError.Json_HasMissingComma, $"Line: {e.LineNumber}");
        }
        else if (e.Message.StartsWith("The JSON object contains a trailing comma"))
        {
            mod.SetError(ModError.Json_HasTrailingComma, $"Line: {e.LineNumber}");
        }
        else if (e.Message.StartsWith("The JSON value could not be converted"))
        {
            mod.SetError(ModError.Json_HasUnexpectedValue, $"Line: {e.LineNumber + 1}");
        }
        else
        {
            mod.SetError(ModError.Json_UnhandledException, $"Line: {e.LineNumber}");
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JsonModBase))]
public partial class Ctx : JsonSerializerContext;

// These are dumber intermediate classes used exclusively by the JsonSerializer source generator.
// The above code manually converts these classes to the equivalent Mod classes (ModBase, ModFile etc)
// This is done to avoid generating heavy/bloated Json serializer methods.
public record JsonModPatch(string? Section, string? Type, int? Offset, JsonElement? Value, bool? Enabled);  // 'sectionName' -> 'section'
public record JsonModObject(string? Object, JsonModPatch[] Patches);    // 'objectName' -> 'object'
public record JsonModFile(string? File, string? Type, JsonModObject[] Objects); // 'fileName' -> 'file', 'fileType' -> 'type'
public record JsonModBase(string? Name, string? Game, JsonModFile[] Files);
