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

        try
        { 
            json = JsonSerializer.Deserialize(File.ReadAllText(modPath), Ctx.Default.JsonModBase);
        }
        catch (JsonException e)
        {
            mod.Name = Path.GetFileName(modPath);
            ParseJsonError(e, mod);
            return mod;
        }

        mod.Name = string.IsNullOrWhiteSpace(json.Name) ? Path.GetFileName(modPath) : json.Name;

        if (json.Game is null)
        {
            mod.SetError(ModError.UnspecifiedGame);
            return mod;
        }

        mod.Game = EnumConverters.GetGame(json.Game);

        if (json.Files is null) return mod;
        foreach (var jsonFile in json.Files)
        {
            var modFile = mod.GetFile(jsonFile.File, ctx.QualifyPath(jsonFile.File), EnumConverters.GetFileType(jsonFile.Type));

            if (jsonFile.File is null)
            {
                mod.SetError(ModError.UnspecifiedFile, mod.GetErrorLocation(modFile));
                return mod;
            }

            if (jsonFile.Type is null)
            {
                mod.SetError(ModError.UnspecifiedType, mod.GetErrorLocation(modFile));
                return mod;
            }

            if (jsonFile.Objects is null) return mod;
            foreach (var jsonObj in jsonFile.Objects)
            {
                // Only check if the key wasn't passed. A value of "" is fine; we'll forgo using an object (UPK only)
                if (jsonObj.Object is null)
                {
                    mod.SetError(ModError.UnspecifiedObject);
                    return mod;
                }

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
                        mod.SetError(ModError.UnspecifiedValue, mod.GetErrorLocation(modFile, modObj, modObj.Patches[^1]));
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
        string ctx = $"Line: {e.LineNumber + 1}";

        if (e.Message.Contains("is invalid after a value."))
        {
            mod.SetError(ModError.JsonMissingComma, ctx);
        }
        else if (e.Message.StartsWith("The JSON object contains a trailing comma"))
        {
            mod.SetError(ModError.JsonTrailingComma, ctx);
        }
        else if (e.InnerException is not null && e.InnerException.Message.StartsWith("Cannot get the value of a token type") == true)
        {
            mod.SetError(ModError.JsonBadCast, ctx);
        }
        else
        {
            mod.SetError(ModError.JsonUnhandled, ctx);
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
