using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace IBPatcher.Mod;

public static class JsonMod
{
    private const string RequiresArray = "REQUIRES_ARRAY";

    // Microsoft does not expose this as an accessible (readonly) property, so we're doing it ourselves here.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_lineNumber")]
    private static extern ref long GetLineNumber(in Utf8JsonReader reader);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(string? message = null) => throw new Exception(message);

    public static ModBase Read(string path, ModContext context)
    {
        var mod = new ModBase(path, ModFormat.Json);

        ReadOnlySpan<byte> bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions() { CommentHandling = JsonCommentHandling.Skip });

        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.PropertyName)
                {
                    // Read in the key and advance reader
                    string propertyName = reader.GetString();
                    reader.Read();

                    ParsePropertyValue(mod, propertyName, ref reader, context);
                }
                else if (reader.TokenType is JsonTokenType.StartObject)
                {
                    // Add new ModFile
                    if (reader.CurrentDepth == 2) mod.Files.Add(new ModFile());

                    // Add new ModObject
                    else if (reader.CurrentDepth == 4) mod.Files[^1].Objects.Add(new ModObject());

                    // Add new ModPatch
                    else if (reader.CurrentDepth == 6) mod.Files[^1].Objects[^1].Patches.Add(new ModPatch());
                }
            }
        }
        catch (Exception e)
        {
            SetJsonModError(e, mod, in reader);
        }

        return mod;
    }

    // Ugly hacky method to discern JsonException type from string message
    private static void SetJsonModError(Exception e, ModBase mod, in Utf8JsonReader reader)
    {
        long lineNumber = GetLineNumber(in reader);

        if (e.Message.StartsWith("Cannot get the value of a token type"))
        {
            mod.SetError(ModError.Json_HasUnexpectedValueType, $"Line: {lineNumber + 1}");
        }
        else if (e.Message.Contains("is invalid after a value."))
        {
            mod.SetError(ModError.Json_HasMissingComma, $"Line: {lineNumber}");
        }
        else if (e.Message.Contains("contains a trailing"))
        {
            mod.SetError(ModError.Json_HasTrailingComma, $"Line: {lineNumber}");
        }
        else if (e.Message.Contains("is an invalid "))
        {
            mod.SetError(ModError.Json_HasBadValue, $"Line: {lineNumber + 1}");
        }
        else if (e.Message == RequiresArray)
        {
            mod.SetError(ModError.Json_HasUnexpectedValueType, $"Line: {lineNumber + 1}");
        }
        else
        {
            mod.SetError(ModError.Json_UnhandledException, $"Line: {lineNumber}");
        }
    }

    private static void ParsePropertyValue(ModBase mod, string _key, ref Utf8JsonReader reader, ModContext context)
    {
        string key = _key.ToUpperInvariant();

        // @TODO unrecognized properties need to trigger warnings.
        // @TODO only Coalesced patches can use string[] value.

        // ModBase
        if (reader.CurrentDepth == 1)
        {
            switch (key)
            {
                case "NAME": mod.Name = reader.GetString(); break;
                case "GAME": mod.Game = EnumConverters.GetGame(reader.GetString()); break;
                case "DESCRIPTION" or "AUTHOR" or "VERSION" or "DATE": break;
                case "FILES": if (reader.TokenType is not JsonTokenType.StartArray) Throw(RequiresArray); break;
                // default: throw new Exception($"Unsupported key '{_key}'");
            }
        }
        // ModFile
        else if (reader.CurrentDepth == 3)
        {
            var file = mod.Files[^1];

            switch (key)
            {
                case "FILE": file.FileName = reader.GetString(); file.QualifiedIpaPath = context.QualifyPath(file.FileName); break;
                case "TYPE": file.FileType = EnumConverters.GetFileType(reader.GetString()); break;
                case "OBJECTS": if (reader.TokenType is not JsonTokenType.StartArray) Throw(RequiresArray); break;
                // default: throw new Exception($"Unsupported key '{_key}'");
            }
        }
        // ModObject
        else if (reader.CurrentDepth == 5)
        {
            var obj = mod.Files[^1].Objects[^1];

            switch (key)
            {
                case "OBJECT": obj.ObjectName = reader.GetString(); break;
                case "PATCHES": if (reader.TokenType is not JsonTokenType.StartArray) Throw(RequiresArray); break;
                // default: throw new Exception($"Unsupported key '{_key}'");
            }
        }
        // ModPatch
        else
        {
            Debug.Assert(reader.CurrentDepth == 7);

            var patch = mod.Files[^1].Objects[^1].Patches[^1];

            switch (key)
            {
                case "SECTION": patch.SectionName = reader.GetString(); break;
                case "TYPE": patch.Type = EnumConverters.GetPatchType(reader.GetString()); break;
                case "OFFSET": patch.Offset = reader.GetInt32(); break;
                case "ENABLED": patch.Enabled = reader.GetBoolean(); break;
                case "VALUE":
                    // Coalesced patches are allowed to use string[] type for its value property
                    if (reader.TokenType is JsonTokenType.StartArray)
                    {
                        List<string> strings = [];

                        while (true)
                        {
                            reader.Read();
                            if (reader.TokenType is JsonTokenType.EndArray) break;

                            // Only strings are supported
                            strings.Add(reader.GetString());
                        }

                        patch.Value.Strings = strings.ToArray();
                    }
                    else
                    {
                        patch.Value.String = Encoding.UTF8.GetString(reader.ValueSpan);
                        reader.Skip();
                    }
                    break;
                // default: if (key.Length < 2 || key[0] != '/' && key[1] != '/') throw new Exception($"Unsupported key '{_key}'"); break;
            }
        }
    }
}
