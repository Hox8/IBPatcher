/*
 * IBPatcher
 * Copyright © 2023 Hox
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using IBPatcher;
using UnrealLib;

namespace JsonTest2
{
    public class JsonModException : JsonException
    {
        public JsonModException(string message) : base(message) { }
    }

    public enum JsonType : byte
    {
        Other = 0,
        UPK = 1,
        Coalesced = 2,
    }

    public enum JsonMode : byte
    {
        // No exclusive create option. How would that be useful?
        Delete = 0,     // If file exists, delete it
        Overwrite,      // Delete + Update combined
        Append          // DEFAULT. Adds to an existing ini file. If doesn't exist, create new one     
    }

    public class JsonSection
    {
        // REQUIRED
        public string SectionName;      // Name of the section within an ini file to update/remove
        public string[] Properties;     // String array of key/values to place under an ini section

        // OPTIONAL
        public JsonMode Mode;           // Influences whether inis are deleted, replaced, or added to. Defaults to Append
        public bool? Enabled;
    }

    public class JsonIni
    {
        // REQUIRED
        public string IniName;                  // The filename of the ini to modify within a coalesced file
        public List<JsonSection> Sections;      // A list of all sections within an ini to update/delete
        public JsonMode Mode;                   // Influences whether inis are deleted, replaced, or added to
        public bool? Enabled;
    }

    public class JsonPatch // A patch to apply to a UObject inside a UPK
    {
        // REQUIRED
        public PatchType Type;      // Data type of the patch's contents. Possible types: Byte, Boolean, UInt8, Int32, Float, String
        public int Offset;          // Integer of relative offset inside object. Will be added to object's offset inside the UPK
        public string Value;        // Patch's contents in string form. Will be interpreted

        // OPTIONAL
        public int? Size;           // Used for string types only?
        public bool? Enabled;       // If false, patch will be skipped during patching

        // INTERNAL
        internal byte[] Bytes;        // Byte buffer to store interpreted string contents
    }

    public class JsonObject
    {
        // REQUIRED
        public string ObjectName;               // Name of the object to look for inside a UPK file
        public List<JsonPatch> Patches;   // List of patches to apply to the specified Object

        // INTERNAL
        internal int UObjectOffsetInPackage;
        internal int UObjectSerialSize;         // Used to check if a mod bleeds into other objects
    }

    public class JsonFile
    {
        // REQUIRED
        public string FileName;         // Name of the file inside the CookedIPhone folder
        public JsonType? FileType;      // Whether the patcher should treat the file as a UPK or a Coalesced file

        public List<JsonObject> Objects;    // List of objects the patch is editing
        public List<JsonIni> Inis;          // List of inis the patch is editing
    }

    public class JsonRoot
    {
        // REQUIRED
        public Shared.GameType Game;               // The game this mod is intended for
        public List<JsonFile> Files;        // List of all files this mod edits

        //OPTIONAL
        public string? Name;                // Name of the mod as it appears in the patcher
        public string? Description;
        public string? Author;
        public string? Date;
        public string? Version;
    }

    public class JsonError
    {
        public ModError Error;
        public string? Message;
    }

    public class JsonMod
    {
        public string ModName;
        public JsonRoot Mod;
        public JsonError Error;

        public JsonMod(string jsonPath, IPA ipa, Mods mods)
        {
            try
            {
                Mod = JsonSerializer.Deserialize(File.ReadAllText(jsonPath), typeof(JsonRoot), PersistentJsonContext.Context) as JsonRoot;
                ModName = Mod.Name ??= Path.GetFileName(jsonPath);

                if (Mod.Game != ipa.Game)
                {
                    Error = new JsonError() { Error = ModError.WrongGame, Message = $"only compatible with {Mod.Game}" };
                    return;
                }

                // Iterate over files to test if any are missing. Ideally this would be checked DURING deserialization but I'm not sure how to pass variables to converters effectively
                for (int i = 0; i < Mod.Files.Count; i++)
                {
                    if (!mods.RequiredFiles.Contains(Mod.Files[i].FileName))
                    {
                        if (!ipa.Archive.ContainsEntry($"{ipa.AppFolder}CookedIPhone/{Mod.Files[i].FileName}"))
                        {
                            Mod.Files.RemoveRange(i, Mod.Files.Count - 1);
                            Error = new() { Error = ModError.BadFile };
                            return;
                        }
                        else mods.RequiredFiles.Add(Mod.Files[i].FileName);
                    }
                }
            }
            catch (JsonException e)
            {
                ModName = Path.GetFileName(jsonPath);
                Error = new JsonError();

                if (e is JsonModException)
                {
                    Error.Message = e.Message;
                }
                else
                {
                    // Trailing comma exception
                    if (e.Message.StartsWith("The JSON object contains a trailing"))
                    {
                        Error.Message = $"Trailing comma, line {e.LineNumber}";
                    }
                    // Expected comma exception
                    else if (e.Message.Contains("is invalid after a value."))
                    {
                        // Console.WriteLine(e.Message);
                        Error.Message = $"Missing value delimiter, line {e.LineNumber}";
                    }
                    // Incorrect value type exception
                    else if (e.Message.Contains("The JSON value could not be converted"))
                    {
                        Error.Message = $"Unexpected data type, line {e.LineNumber + 1}";
                    }
                    // Invalid literal exception
                    else if (e.Message.Contains("is an invalid JSON literal."))
                    {
                        Error.Message = $"Invalid literal, line {e.LineNumber}";
                    }
                    else
                    {
                        Error.Message = "Unhandled exception!";
                    }
                }
            }
        }
    }

    [JsonSerializable(typeof(JsonRoot))]
    [JsonSerializable(typeof(JsonFile))]
    [JsonSerializable(typeof(JsonObject))]
    [JsonSerializable(typeof(JsonPatch))]
    [JsonSerializable(typeof(JsonIni))]
    [JsonSerializable(typeof(JsonSection))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    public partial class CustomJsonCtx : JsonSerializerContext { }

    public static class PersistentJsonContext
    {
        public static JsonSerializerOptions Options { get; }
        public static JsonSerializerContext Context { get; }

        static PersistentJsonContext()
        {
            Options = new JsonSerializerOptions
            {
                Converters = {
                    new JsonModConverter(),
                    new JsonFileConverter(),
                    new JsonObjectConverter(),
                    new JsonPatchConverter(),
                    new JsonIniConverter(),
                    new JsonSectionConverter()
                }
            };

            Context = new CustomJsonCtx(Options);
        }
    }

    #region Converters

    public abstract class CustomConverter<T> : JsonConverter<T>
    {
        public static string TryGetString(ref Utf8JsonReader reader, string propertyName, string location)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonModException($"Bad value type for property '{propertyName}' in {location}. Expected '{JsonTokenType.String}', got '{reader.TokenType}'.");
            return reader.GetString();
        }
        public static int TryGetInt(ref Utf8JsonReader reader, string propertyName, string location)
        {
            if (reader.TokenType != JsonTokenType.Number) throw new JsonModException($"Bad value type for property '{propertyName}' in {location}. Expected '{JsonTokenType.Number}', got '{reader.TokenType}'.");
            return reader.GetInt32();
        }
        public static bool TryGetBool(ref Utf8JsonReader reader, string propertyName, string location)
        {
            if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
            {
                throw new JsonModException($"Bad value type for property '{propertyName}' in {location}. Expected 'Boolean', got '{reader.TokenType}'.");
            }
            return reader.GetBoolean();
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }

    public class JsonSectionConverter : CustomConverter<JsonSection>
    {
        public override JsonSection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonSection section = new() { Mode = JsonMode.Append, Enabled = true };

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "SECTIONNAME":
                            section.SectionName = TryGetString(ref reader, "SectionName", "Section");
                            break;
                        case "PROPERTIES":
                            section.Properties = JsonSerializer.Deserialize(ref reader, typeof(string[]), PersistentJsonContext.Context) as string[];
                            break;
                        case "MODE":
                            string value = TryGetString(ref reader, "Mode", "Ini");
                            section.Mode = value.ToUpper() switch
                            {
                                "APPEND" or "DEFAULT" => JsonMode.Append,
                                "DELETE" => JsonMode.Delete,
                                "OVERWRITE" => JsonMode.Overwrite,
                                _ => throw new JsonModException($"'{value}' is not a valid mode"),
                            };
                            break;
                        case "ENABLED":
                            section.Enabled = TryGetBool(ref reader, "Enabled", "Section");
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonSection property");
                    }
                }
            }

            if (section.SectionName is null) throw new JsonModException("Required property 'SectionName' was never passed");
            return section;
        }
    }

    public class JsonIniConverter : CustomConverter<JsonIni>
    {
        public override JsonIni? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonIni ini = new() { Mode = JsonMode.Append, Enabled = true };

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "ININAME":
                            ini.IniName = TryGetString(ref reader, "IniName", "Ini");
                            break;
                        case "SECTIONS":
                            ini.Sections = JsonSerializer.Deserialize(ref reader, typeof(List<JsonSection>), PersistentJsonContext.Context) as List<JsonSection>;
                            break;
                        case "MODE":
                            string value = TryGetString(ref reader, "Mode", "Ini");
                            ini.Mode = value.ToUpper() switch
                            {
                                "APPEND" or "DEFAULT" => JsonMode.Append,
                                "DELETE" => JsonMode.Delete,
                                "OVERWRITE" => JsonMode.Overwrite,
                                _ => throw new JsonModException($"'{value}' is not a valid mode"),
                            };
                            break;
                        case "ENABLED":
                            ini.Enabled = TryGetBool(ref reader, "Enabled", "Ini");
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonIni property");
                    }
                }
            }

            if (ini.IniName is null) throw new JsonModException("Required property 'IniName' was never passed");
            return ini;
        }
    }

    public class JsonPatchConverter : CustomConverter<JsonPatch>
    {
        public override JsonPatch? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            PatchType? type = null;
            int? offset = null;
            JsonElement? patchValue = null;
            int? size = null;
            bool enabled = true;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "TYPE":
                            string value = TryGetString(ref reader, "Type", "Patch");

                            type = value.ToUpper() switch
                            {
                                "BYTE" or "BYTES" => PatchType.Byte,
                                "BOOL" or "BOOLEAN" => PatchType.Boolean,
                                "UINT8" or "INT8" => PatchType.UInt8,
                                "INT32" or "INT" or "INTEGER" => PatchType.Int32,
                                "FLOAT" => PatchType.Float,
                                "STRING" => PatchType.String,
                                _ => throw new JsonModException($"'{value}' is not a valid patch type")
                            };
                            break;
                        case "OFFSET":
                            offset = TryGetInt(ref reader, "Offset", "Patch");
                            break;
                        case "VALUE":
                            patchValue = JsonDocument.ParseValue(ref reader).RootElement;   // process once finished reading
                            break;
                        case "SIZE":
                            size = TryGetInt(ref reader, "Size", "Patch");
                            break;
                        case "ENABLED":
                            enabled = TryGetBool(ref reader, "Enabled", "Patch");
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonPatch property");
                    }
                }
            }

            if (type is null) throw new JsonModException("Required property 'Type' was never passed");
            if (offset is null) throw new JsonModException("Required property 'Offset' was never passed");
            if (patchValue is null) throw new JsonModException("Required property 'Value' was never passed");
            if (size is not null && type != PatchType.String) throw new JsonModException("Property 'Size' is only applicable for 'String' types");

            return new JsonPatch()
            {
                Type = (PatchType)type,
                Offset = (int)offset,
                Value = patchValue.Value.ToString(),
                Size = size,
                Enabled = enabled
            };
        }
    }

    public class JsonObjectConverter : CustomConverter<JsonObject>
    {
        public override JsonObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonObject uobject = new();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "OBJECTNAME":
                            uobject.ObjectName = TryGetString(ref reader, "ObjectName", "Object");
                            break;
                        case "PATCHES":
                            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonModException("Property 'Patches' in JsonObject must be an array of JsonPatch objects");
                            uobject.Patches = JsonSerializer.Deserialize(ref reader, typeof(List<JsonPatch>), PersistentJsonContext.Context) as List<JsonPatch>;
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonObject property");
                    }
                }
            }

            if (uobject.ObjectName is null) throw new JsonModException("Required property 'ObjectName' was never passed");
            if (uobject.Patches is null) throw new JsonModException("Required property 'Patches' was never passed");
            return uobject;
        }
    }

    public class JsonFileConverter : CustomConverter<JsonFile>
    {
        public override JsonFile? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonFile file = new();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "FILENAME":
                            // Immediately check if file exists from ref IPA
                            file.FileName = TryGetString(ref reader, "Filename", "File");
                            if (file.FileName.Length == 0) file.FileName = null;
                            break;

                        case "FILETYPE":
                            string value = TryGetString(ref reader, "Filetype", "File");

                            file.FileType = value.ToUpper() switch
                            {
                                "UPK" => JsonType.UPK,
                                "COALESCED" => JsonType.Coalesced,
                                _ => throw new JsonModException($"'{value}' is not a valid value for 'Filetype'"),
                            };
                            break;

                        case "OBJECTS":
                            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonModException("Property 'Objects' in File must be an array of JsonObject objects");
                            file.Objects = JsonSerializer.Deserialize(ref reader, typeof(List<JsonObject>), PersistentJsonContext.Context) as List<JsonObject>;
                            break;

                        case "INIS":
                            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonModException("Property 'Inis' in File must be an array of JsonIni objects");
                            file.Inis = JsonSerializer.Deserialize(ref reader, typeof(List<JsonIni>), PersistentJsonContext.Context) as List<JsonIni>;
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonFile property");
                    }
                }
            }

            if (file.FileName is null) throw new JsonModException("Required property 'FileName' was never passed");
            if (file.FileType is null) throw new JsonModException("Required property 'FileType' was never passed");
            if (file.FileType == JsonType.UPK)
            {
                if (file.Objects is null) throw new JsonModException($"No UObjects were provided for {file.FileName}");
                if (file.Inis is not null) throw new JsonModException($"Inis cannot be passed if FileType is 'UPK'");
            }
            else  // JsonType.Coalesced
            {
                if (file.Inis is null) throw new JsonModException($"No Inis were provided for {file.FileName}");
                if (file.Objects is not null) throw new JsonModException("UObjects cannot be passed if FileType is 'Coalesced'");
            }
            return file;
        }
    }

    public class JsonModConverter : CustomConverter<JsonRoot>
    {
        public override JsonRoot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("[DEBUG] Expected StartObject (JsonBase)");
            }

            JsonRoot root = new();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName.ToUpper())
                    {
                        case "NAME":
                            root.Name = TryGetString(ref reader, "Name", "Root");
                            break;
                        case "DESCRIPTION":
                            root.Description = TryGetString(ref reader, "Description", "Root");
                            break;
                        case "GAME":
                            string value = TryGetString(ref reader, "Game", "Root");

                            root.Game = Mods.RemoveWhitespace(value.ToUpper()) switch
                            {
                                "INFINITYBLADEIII" or "INFINITYBLADE3" or "IB3" => Shared.GameType.IB3,
                                "INFINITYBLADEII" or "INFINITYBLADE2" or "IB2" => Shared.GameType.IB2,
                                "INFINITYBLADEI" or "INFINITYBLADE1" or "IB1" => Shared.GameType.IB1,
                                "VOTE!!!" or "VOTE" => Shared.GameType.VOTE,
                                _ => throw new JsonModException($"'{value}' is not a valid IB-series game"),
                            };
                            break;
                        case "AUTHOR":
                            root.Author = TryGetString(ref reader, "Author", "Root");
                            break;
                        case "DATE":
                            root.Date = TryGetString(ref reader, "Date", "Root");
                            break;
                        case "VERSION":
                            root.Version = TryGetString(ref reader, "Version", "Root");
                            break;
                        case "FILES":
                            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonModException("Property 'Files' in Root must be an array of JsonFile objects");
                            root.Files = JsonSerializer.Deserialize(ref reader, typeof(List<JsonFile>), PersistentJsonContext.Context) as List<JsonFile>;
                            break;
                        default:
                            if (propertyName.StartsWith("//") || propertyName.StartsWith(';')) continue;
                            throw new JsonModException($"'{propertyName}' is not a valid JsonRoot property");
                    }
                }
            }
            return root;
        }
    }
    #endregion
}