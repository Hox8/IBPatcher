using System;
using System.IO;
using Ionic.Zip;
using Ionic.Zlib;
using UnLib;
using UnLib.Enums;

namespace IBPatcher;

public class IPA : IDisposable
{
    private readonly ZipFile _archive;
    public string ErrorContext;

    public Game Game;
    public readonly string FileName;
    public string ModdedName;
    public readonly string ParentPath;

    public IPA(string filePath)
    {
        FileName = Path.GetFileName(filePath);
        ParentPath = filePath[..^FileName.Length];
        ModdedName = $"{Path.ChangeExtension(FileName, null)} - Modded.ipa";

        if (!File.Exists(filePath))
        {
            ErrorContext = $"\n'{filePath}' is not a valid path.\nDouble-check the file exists, or try drag-and-dropping instead.\n";
            return;
        }

        try
        {
            _archive = new ZipFile(filePath);
            _archive.CompressionLevel = CompressionLevel.Level3; // Faster zipping at the expense of a small size increase.
            _archive.SaveProgress += SaveProgress;  // Subscribe the 'SaveProgress' event.
        }
        catch (ZipException)
        {
            ErrorContext = $"\n{FileName} is not a valid zip archive.\nTry downloading a fresh IPA and try again.\n";
            return;
        }

        var entry = _archive["Payload/SwordGame.app/CookedIPhone/Entry.xxx"];
        if (entry is null) // Not SwordGame
        {
            if (_archive["Payload/VoteGame.app/CookedIPhone/Entry.xxx"] is not null)
                Game = Game.Vote;
            else ErrorContext = $"\n{FileName} is not a valid Infinity Blade archive!\n";

            return;
        }

        // Determine what Infinity Blade game we've got by extracting a small UPK file
        using var ms = new MemoryStream();
        entry.Extract(ms);
        using var upk = new UnrealPackage(ms);
        upk.Init();

        Game = upk.Version switch
        {
            > 864 => Game.IB3,
            > 788 => Game.IB2,
            _ => Game.IB1
        };
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorContext);
    public string CookedFolder => $"Payload/{(Game is Game.Vote ? "Vote" : "Sword")}Game.app/CookedIPhone/";

    public void Dispose()
    {
        if (_archive is not null) _archive.Dispose();
        GC.SuppressFinalize(this);
    }

    /// Returns a boolean based on whether 'filePath' exists in the IPA.
    public bool HasEntry(string filePath) => _archive[filePath] is not null;
    
    /// Extracts a file from the IPA to disk.
    public void ExtractEntry(string filePath, string outputPath) => _archive[filePath].Extract(outputPath);
    
    /// Adds/Updates an entry in the IPA using a stream.
    public void UpdateEntry(string filePath, Stream stream) => _archive.UpdateEntry(filePath, stream);

    public bool SaveModdedIpa()
    {
        var outputPath = Path.Combine(ParentPath, ModdedName);
        var tempPath = Path.Combine(Globals.TempPath, ModdedName);

        try
        {
            File.Delete(outputPath);
            _archive.Save(tempPath);
            File.Move(tempPath, outputPath);
        }
        catch
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Constructs a fully-qualified path from a filepath inside the IPA.<br /><br />Paths are relative from the
    /// CookedIPhone folder.
    /// </summary>
    /// <param name="path">The filepath to construct a qualified path from.</param>
    /// <returns></returns>
    public string ZipPath(string path)
    {
        // Absolute path. Take off the '/' suffix.
        if (path.StartsWith('/')) return path[1..];

        // Normal path; lives in CookedIPhone folder.
        if (!path.StartsWith("../")) return $"{CookedFolder}{path}";

        // Relative path. Navigate up from CookedIPhone.
        var sub = path.Split("../");
        var basePath = IterateDirectory(CookedFolder, sub.Length - 1);

        return $"{basePath}{sub[^1]}";
    }

    /// Travels up a directory by 'iterateAmount' and returns the final path.
    private static string IterateDirectory(string basePath, int iterateAmount)
    {
        var dirCount = -1;

        for (var i = basePath.Length - 1; i >= 0; i--)
            if (basePath[i] == '/')
                if (++dirCount == iterateAmount)
                    return $"{basePath[..i]}/";

        // Iteration went too far; return empty string instead of raising exception.
        return string.Empty;
    }

    /// Event during zip save. Used for outputting progress to the console.
    private static void SaveProgress(object? sender, SaveProgressEventArgs e)
    {
        if (e.EventType is ZipProgressEventType.Saving_BeforeWriteEntry)
        {
            var percentage = ((float)e.EntriesSaved / e.EntriesTotal * 100).ToString("00.0");
            Console.Write(ModContext.Backspace + percentage + "% ]");
        }
    }
}