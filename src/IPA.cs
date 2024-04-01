using System;
using System.Collections.Generic;
using System.IO;
using UnrealLib;
using UnrealLib.Enums;
using Zip;
using Zip.Core;
using Zip.Core.Events;
using Zip.Core.Exceptions;
using static UnrealLib.Globals;

namespace IBPatcher;

public enum IpaError
{
    // Generic
    None = 0,               // No error
    PathNonexistent,        // A file was not found at the given path
    PathUnreadable,         // IPA file wasn't readable
    PathIsFolder,           // Path represents a folder and not a file

    // Zip-specific
    InvalidZip,             // Not a PKWARE Zip
    UnsupportedCompression, // One or more entries contains an unsupported compression scheme
    Encrypted,              // One or more entries are encrypted
    InvalidGame             // Archive does is not Sword/Vote Game
}

/// <summary>
/// An Apple iOS zip archive representing an Infinity Blade game.
/// </summary>
public class IPA : ErrorHelper<IpaError>
{
    private readonly ZipArchive _archive;
    public readonly Game Game;

    public readonly int PackageVersion;
    public readonly int EngineVersion;
    public readonly bool IsLatestVersion;

    public readonly string AppFolder;
    public readonly string CookedFolder;

    public string Name => _archive.Name;

    #region Constructors

    public IPA(string path)
    {
        var fileHelper = new FileHelper(path);

        // Check path is legally formatted and exists on disk
        if (!fileHelper.IsLegallyFormatted || !fileHelper.Exists)
        {
            SetError(IpaError.PathNonexistent, path);
            return;
        }

        // Make sure we have a file and not a folder
        if (fileHelper.IsDirectory)
        {
            SetError(IpaError.PathIsFolder, path);
            return;
        }

        // Check for read access. We'll never save over the original IPA, so don't check for write access
        if (!fileHelper.IsReadable)
        {
            SetError(IpaError.PathUnreadable, fileHelper.Name);
            return;
        }

        // Preliminary checks passed. Try to initialize our ZipFile
        try
        {
            _archive = ZipArchive.Read(File.Open(fileHelper.FullName, FileMode.Open, FileAccess.Read, FileShare.Read));
            // _archive.TempFileFolder = Globals.CachePath; @TODO
            _archive.ProgressChanged += ProgressChanged;
        }
        catch (ZipException e)
        {
            SetError(e.Type switch
            {
                ZipExceptionType.InvalidZip=> IpaError.InvalidZip,
                ZipExceptionType.UnsupportedCompression => IpaError.UnsupportedCompression,
                ZipExceptionType.EncryptedEntries => IpaError.Encrypted,
                // ZipExceptionType.FailedCrc => IpaError.CrcFailed
            }, fileHelper.Name);

            return;
        }

        // Try to get SwordGame's 'Entry.xxx' file entry
        ZipEntry? entry = _archive.GetEntry("Payload/SwordGame.app/CookedIPhone/Entry.xxx");

        if (entry is null)
        {
            // SwordGame's 'Entry.xxx' entry wasn't found. Try searching for VoteGame's 'Entry.xxx' entry instead
            if ((entry = _archive.GetEntry("Payload/VoteGame.app/CookedIPhone/Entry.xxx")) is null)
            {
                // VoteGame's 'Entry.xxx' entry wasn't found either. Consider this an invalid IPA
                SetError(IpaError.InvalidGame, fileHelper.Name);
                return;
            }

            Game = Game.Vote;
        }

        // Determine version info from 'Entry.xxx'. This file persists across all UE3 games
        string entryName = $"{Globals.CachePath}/{entry.Name}";
        entry.Extract(Globals.CachePath);

        using (var upk = UnrealPackage.FromFile(entryName, FileMode.Open, FileAccess.Read))
        {
            PackageVersion = upk.GetPackageVersion();
            EngineVersion = upk.GetEngineVersion();

            switch (PackageVersion)
            {
                case > PackageVerIB2:
                    Game = Game.IB3;
                    IsLatestVersion = EngineVersion == EngineVerIB3;
                    break;
                case > PackageVerIB1 when Game is Game.Vote:
                    IsLatestVersion = EngineVersion == EngineVerVOTE;
                    break;
                case > PackageVerIB1:
                    Game = Game.IB2;
                    IsLatestVersion = EngineVersion == EngineVerIB2;
                    break;
                default:
                    Game = Game.IB1;
                    IsLatestVersion = EngineVersion == EngineVerIB1;
                    break;
            }
        }

        File.Delete(entryName);

        // Now that we know what game type we've got, cache some common paths for ease of use
        AppFolder = $"/Payload/{(Game is Game.Vote ? "Vote" : "Sword")}Game.app/";
        CookedFolder = $"{AppFolder}CookedIPhone/";
    }

    #endregion

    /// <summary>
    /// Saves the IPA file to the specified path.
    /// </summary>
    public void Save(string outputDirectory) => _archive.Save(outputDirectory);

    /// <summary>
    /// Updates all entries in the specified directory.
    /// </summary>
    /// <remarks> Passed directory should mimic the structure of the IPA. </remarks>
    public void UpdateEntries(string directoryPath, string basePath) => _archive.UpdateEntries(directoryPath, basePath);

    #region Helpers

    public void ExtractEntries(List<ZipEntry> entries, string basePath) => _archive.ExtractEntriesParallel(entries, basePath);
    public ZipEntry? GetEntry(string path) => _archive.GetEntry(path);
    public void RemoveEntry(string path) => _archive.RemoveEntry(path);
    public List<ZipEntry> Entries => _archive.Entries;

    /// <summary>
    /// Takes a file path and expands it to a fully-qualified path corresponding to the IPA.
    /// </summary>
    /// <param name="path"> The path to expand; e.g. "SwordGame.xxx", "../Binaries/Commands.txt". </param>
    /// <remarks> Relative paths originate from the IPA's CookedIPhone folder. </remarks>
    public string QualifyPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int relativeSegmentCount = 0;
        Span<char> chars = path.ToCharArray();

        // Ensure path is Unix/Zip-compliant
        chars.Replace('\\', '/');

        // If path is absolute, return path as-is.
        if (chars[0] == '/') return chars.ToString();

        // Take note of the number of relative parts at the beginning of the path
        for (int i = 0; i <= chars.Length - 3; i += 3)
        {
            if (chars[i] == '.' && chars[i + 1] == '.' && chars[i + 2] == '/') relativeSegmentCount++;
            else break;
        }

        // Iterate up 'Folder' by 'relativeSegmentCount' directories
        int idx;
        int relativeCount = 0;
        for (idx = CookedFolder.Length - 1; idx > 0; idx--)
        {
            if (CookedFolder[idx] == '/' && relativeCount++ == relativeSegmentCount) break;
        }

        return CookedFolder[..(idx + 1)] + chars.Slice(relativeSegmentCount * 3).ToString();
    }

    /// <summary>
    /// Returns a path relative to the CookedIPhone folder in the IPA.
    /// </summary>
    /// <param name="path"> Absolute path within the IPA to be made relative. </param>
    /// <remarks> This method is the opposite of <see cref="QualifyPath"/> </remarks>
    public string GetRelativePath(string path) => Path.GetRelativePath(CookedFolder, path);

    /// <summary>
    /// An event which is fired each time the ZipArchive saves an entry.
    /// </summary>
    /// <remarks> Used to print a self-updating percentage box. </remarks>>
    private static void ProgressChanged(object sender, ZipProgressEventArgs e)
    {
        // We don't want to print 100% because the extra digit breaks the formatting
        if (e.TotalBytes != e.ProcessedBytes)
        {
            // Saving uses a 'weighted' progress biased toward bytes requiring compression (1:25)
            // This is because compression involves much more work than simply writing bytes, so
            // multiplying their contribution to the total by x20 seems like a fair approximation.
            ModContext.PrintPercentage(e is SaveProgressEventArgs saveArgs ? saveArgs.ProgressWeighted : e.Progress);
        }
    }

    #endregion

    #region Error messages

    public override string GetErrorString() => ErrorType switch
    {
        // Generic
        IpaError.None => "No errors.",
        IpaError.PathNonexistent => $"'{ErrorContext}' does not exist.",
        IpaError.PathUnreadable => $"'{ErrorContext}' could not be read. Close any files using it any try again.",
        IpaError.PathIsFolder => $"'{ErrorContext}' is a directory; please pass a zip file instead.",

        // Zip-specific
        IpaError.InvalidZip => $"{ErrorContext} is not a valid zip archive.",
        IpaError.UnsupportedCompression => $"{ErrorContext} contains entries stored with an unsupported compression scheme.\n   Apple's IPAs support only 'None' and 'Deflate'.",
        IpaError.Encrypted => $"{ErrorContext} contains one or more encrypted entries.",
        IpaError.InvalidGame => $"{ErrorContext} is not an Infinity Blade archive.",
    };

    #endregion
}
