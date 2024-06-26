﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace IBPatcher.Mod;

public static class JsonMod
{
    // String error codes for use with SetJsonModError()
    private const string RequiresArray = "0";
    private const string BadArrayValue = "1";
    private const string UnexpectedArrayValue = "2";
    private const string UnsupportedJsonVersion = "3";

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

        int offset = 0;
        if (bytes.Length >= 3)
        {
            // If file is encoded via UTF8 with BOM, skip over BOM
            if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                offset = 3;
            }
            // If file is UTF16-encoded, fail
            else if ((bytes[0] == 0xFE && bytes[1] == 0xFF) || (bytes[0] == 0xFF && bytes[1] == 0xFE))
            {
                mod.SetError(ModError.Json_BadEncoding);
                return mod;
            }
        }

        var reader = new Utf8JsonReader(bytes[offset..], new JsonReaderOptions() { CommentHandling = JsonCommentHandling.Skip });

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
                    else if (reader.CurrentDepth == 4)
                    {
                        // Add object to current file
                        mod.Files[^1].Objects.Add(new ModObject());
                    }

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
        else if (e.Message == BadArrayValue)
        {
            mod.SetError(ModError.Json_HasBadArrayValue, $"Line: {lineNumber + 1}");
        }
        else if (e.Message == UnexpectedArrayValue)
        {
            mod.SetError(ModError.Json_UnexpectedArrayValue, $"Line: {lineNumber + 1}");
        }
        else if (e.Message == UnsupportedJsonVersion)
        {
            mod.SetError(ModError.Json_UnsupportedVersion);
        }
        else
        {
            mod.SetError(ModError.Json_UnhandledException, $"Line: {lineNumber}");
        }
    }

    private static void ParsePropertyValue(ModBase mod, string _key, ref Utf8JsonReader reader, ModContext context)
    {
        string key = _key.ToUpperInvariant();

        // ModBase
        if (reader.CurrentDepth == 1)
        {
            switch (key)
            {
                case "NAME": mod.Name = reader.GetString(); break;
                case "GAME": mod.Game = EnumConverters.GetGame(reader.GetString()); break;
                case "DESCRIPTION" or "AUTHOR" or "VERSION" or "DATE": break;
                case "FILES": if (reader.TokenType is not JsonTokenType.StartArray) Throw(RequiresArray); break;
                case "JSONVERSION": mod.JsonVersion = reader.GetInt32(); if (mod.JsonVersion > ModBase.CurrentJsonVersion) Throw(UnsupportedJsonVersion); break;
                default: AddModWarning(mod, _key); break;
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
                default: AddModWarning(mod, _key); break;
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
                default: AddModWarning(mod, _key); break;
            }
        }
        // ModPatch
        else
        {
            Debug.Assert(reader.CurrentDepth == 7, "JSON mod reached unreocgnized depth. This should NEVER happen!");

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
                        // If this isn't for a Coalesced patch, throw
                        if (mod.Files[^1].FileType != FileType.Coalesced)
                        {
                            Throw(UnexpectedArrayValue);
                        }

                        // Prepare for List<string> type
                        patch.Type = PatchType.Strings;
                        patch.Value.Strings = [];

                        while (true)
                        {
                            reader.Read();
                            if (reader.TokenType != JsonTokenType.String)
                            {
                                if (reader.TokenType == JsonTokenType.EndArray) break;

                                // Array-type values must consist of only strings! Throw on anything different
                                Throw(BadArrayValue);
                            }

                            patch.Value.Strings.Add(reader.GetString());
                        }
                    }
                    else
                    {
                        patch.Value.String = Encoding.UTF8.GetString(reader.ValueSpan);
                        reader.Skip();
                    }
                    break;
                default: AddModWarning(mod, _key); break;
            }
        }
    }

    /// <summary> Adds an unrecognized JSON key as a mod warning. </summary>
    /// <param name="mod"> The mod to add the warning to. </param>
    /// <param name="key"> The unrecognized JSON key. </param>
    private static void AddModWarning(ModBase mod, string key)
    {
        // Keys starting with '//' are treated as comments and should be ignored
        if (!key.StartsWith("//"))
        {
            mod.UnrecognizedKeys ??= [];

            // Add key only if it isn't already present
            if (!mod.UnrecognizedKeys.Contains(key))
            {
                mod.UnrecognizedKeys.Add(key);
            }
        }
    }
}
