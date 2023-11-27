using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBPatcher.Mod;

public static class JsonMod
{
    public static ModBase ReadJsonMod(string modPath, ModContext ctx)
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
            var modFile = mod.GetFile(jsonFile.Filename, ctx.QualifyPath(jsonFile.Filename), EnumConverters.GetFileType(jsonFile.Filetype));

            if (jsonFile.Filename is null)
            {
                mod.SetError(ModError.UnspecifiedFile, modFile);
                return mod;
            }

            if (jsonFile.Filetype is null)
            {
                mod.SetError(ModError.UnspecifiedFileType, modFile);
                return mod;
            }

            if (jsonFile.Objects is null) return mod;
            foreach (var jsonObj in jsonFile.Objects)
            {
                var modObj = modFile.GetObject(jsonObj.ObjectName ?? "");

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
                        mod.SetError(ModError.UnspecifiedValue, modFile, modObj, modObj.Patches[^1]);
                        return mod;
                    }

                    // A little hacky. We'll reference this when loading the mod "for real" during ModBase::Setup()
                    modObj.Patches[^1].Value.String = jsonPatch.Value.ToString();
                }
            }
        }

        return mod;
    }

    private static void ParseJsonError(JsonException e, ModBase mod)
    {
        mod.ErrorContext = $"Line: {e.LineNumber + 1}";

        if (e.Message.Contains("is invalid after a value."))
        {
            mod.SetError(ModError.JsonMissingComma);
        }
        else if (e.Message.StartsWith("The JSON object contains a trailing comma"))
        {
            mod.SetError(ModError.JsonTrailingComma);
        }
        else if (e.InnerException.Message.StartsWith("Cannot get the value of a token type") == true)
        {
            mod.SetError(ModError.JsonBadCast);
        }
        else
        {
            mod.SetError(ModError.JsonUnhandled);
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JsonModBase))]
public partial class Ctx : JsonSerializerContext;

public record JsonModPatch(string? Section, string? Type, int? Offset, JsonElement? Value, bool? Enabled);
public record JsonModObject(string? ObjectName, JsonModPatch[] Patches);
public record JsonModFile(string? Filename, string? Filetype, JsonModObject[] Objects);
public record JsonModBase(string? Name, string? Game, JsonModFile[] Files);


