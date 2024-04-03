using IBPatcher.Mod;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnrealLib;
using UnrealLib.Config;
using UnrealLib.Config.Coalesced;
using UnrealLib.Core;
using UnrealLib.Enums;
using Zip.Core;

namespace IBPatcher;

public enum ModContextError
{
    // Extract
    FailExtract_Space,          // Failed to extract required IPA files to disk due to insufficient disk space

    // Save
    FailSaveIpa_Space,          // Failed to save a modded IPA copy due to insufficient disk space
    FailSaveIpa_Contention,     // Failed to save a modded IPA copy due to destination being in use
    FailSaveFolder_Contention,  // Failed to save modified files to Output due to being in use
}

public class ModContext : ErrorHelper<ModContextError>
{
    private const string Backspace = "\b\b\b\b\b\b\b\b\b\b";
    private const string SuccessString = Backspace + " [SUCCESS]\n";
    private const string FailureString = Backspace + " [FAILURE]\n";
    private const string SkippedString = Backspace + " [SKIPPED]\n";
    private const string CommandsModName = "Commands.txt";

    public readonly IPA Ipa;
    public List<ModBase> Mods = [];
    public List<CachedArchive> ArchiveCache = [];
    public int WarningCount = 0;

    public readonly string ModFolderAbsolute;
    public readonly string ModFolderRelative;
    public readonly string CommandsModPath;

    public Game Game => Ipa.Game;
    public int ModCount => Mods.Count + (File.Exists(CommandsModPath) ? 1 : 0);

    public ModContext(IPA ipa)
    {
        Ipa = ipa;

        ModFolderRelative = $"Mods/{UnrealLib.Globals.GetString(Game, true)}";
        ModFolderAbsolute = Path.Combine(AppContext.BaseDirectory, ModFolderRelative);
        CommandsModPath = Path.Combine(ModFolderAbsolute, CommandsModName);

        Debug.Assert(Directory.Exists(Globals.CachePath), "Global persistent cache path does not exist!");
    }

    /// <summary> Scans and pulls in mods from the mod directory. </summary>
    public void LoadMods()
    {
        // We unconditionally create all mod folders at startup, so this should be true 99% of the time
        if (!Directory.Exists(ModFolderAbsolute))
        {
            // If this check returns false, this means we had a permission error or a file existed in place of the directory.
            // Fail silently and assume the no mods found error. User should be able to quickly figure out the issue
            return;
        }

        // It's important that .bin mods are read before regular mods to ensure coalesced files are not extracted unnecessarily
        foreach (var entry in Directory.EnumerateFiles(ModFolderAbsolute, "*.bin"))
        {
            Mods.Add(BinMod.Read(entry, this));
        }

        foreach (var entry in Directory.EnumerateFiles(ModFolderAbsolute))
        {
            switch (Path.GetExtension(entry).ToLowerInvariant())
            {
                case ".ini":
                    var iniMod = IniMod.Read(entry, this);
                    iniMod.Setup(this);
                    Mods.Add(iniMod);
                    break;
                case ".json" or ".jsonc":
                    var jsonMod = JsonMod.Read(entry, this);
                    jsonMod.Setup(this);
                    Mods.Add(jsonMod);
                    break;
            }
        }
    }

    /// <summary> Extracts and initializes all cached archives. </summary>
    private void LoadCachedArchives()
    {
        long totalSize = 0, currentSize = 0;
        int fileCount = 0;

        foreach (var archive in ArchiveCache)
        {
            // Only tally stats of files that need extracting
            if (!archive.ShouldExtractFile) continue;

            totalSize += archive.Entry.UncompressedSize;
            fileCount++;
        }

        PrintMessage($"Unpacking game data | {fileCount} {(fileCount == 1 ? "file" : "files")} | {FormatSizeString(totalSize)}");

        try
        {
            Parallel.ForEach(ArchiveCache, archive =>
                {
                    if (archive.ShouldExtractFile)
                    {
                        // Extract entry and add size to the counter
                        archive.Entry.Extract(Globals.CachePath);
                        Interlocked.Add(ref currentSize, archive.Entry.UncompressedSize);

                        // Only print if it won't equal "100.00%", otherwise it messes with the formatting
                        if (currentSize != totalSize) PrintPercentage((float)currentSize / totalSize);
                    }

                    // Initialize archive
                    archive.Archive = archive.Type is FileType.Upk
                            ? UnrealPackage.FromFile(Path.Combine(Globals.CachePath, archive.Entry.Name))
                            : Coalesced.FromFile(Path.Combine(Globals.CachePath, archive.Entry.Name), Ipa.Game);

                    archive.Archive.StartSaving();
                });

            Console.WriteLine(fileCount == 0 ? SkippedString : SuccessString);
        }
        catch (Exception e)
        {
            // Handle rare condition where the user does not have enough free disk space
            if (ExceptionIsDiskSpace(e))
            {
                SetError(ModContextError.FailExtract_Space);

                // Clear out archives. This will cause most mods to fail and prevent trying to dispose null streams later on
                ArchiveCache.Clear();

                Globals.PrintColor($"{FailureString}\n", ConsoleColor.Red);
            }
        }
    }

    /// <summary> Writes mods to their respective files, patches TOCs, and repackages the game IPA. </summary>
    public void ApplyMods()
    {
        int modCount = 0;               // Increments with every processed mod. Used for printing
        int failCount = 0;              // Increments with every failed mod
        int fileCount = 0;              // Increments with every modified archive. Used for printing
        long bytesWritten = 0;          // Increments with every modified archive. Used for printing
        bool requiresTocPatch = false;  // TOC patching is only necessary if bytes are added to a UPK

        LoadCachedArchives();

        // Write commands mod
        if (File.Exists(CommandsModPath))
        {
            // Store file contents in an intermediate buffer so we can poll their sizes later
            var commandsContent = File.ReadAllBytes(CommandsModPath);
            var ue3CommandLineContent = $"-exec=\"{CommandsModName}\"";

            // Create Binaries folder in cache directory
            string binariesFolder = Globals.CachePath + Ipa.QualifyPath("../Binaries/");
            Directory.CreateDirectory(binariesFolder);

            // Copy "Commands.txt" from mod folder to cache folder
            File.WriteAllBytes($"{binariesFolder}{CommandsModName}", commandsContent);

            // Create a UE3CommandLine.txt file in the cache's CookedIPhone folder
            File.WriteAllText(Globals.CachePath + Ipa.QualifyPath("UE3CommandLine.txt"), ue3CommandLineContent);

            bytesWritten += commandsContent.Length + ue3CommandLineContent.Length;
            fileCount += 2;

            PrintMessage(CommandsModName, ++modCount);
            Console.Write(SuccessString);
        }

        // Write .INI, .JSON, and .BIN mods
        foreach (var mod in Mods)
        {
            PrintMessage(mod.Name, ++modCount);

            // Write mod and print success string if:
            // - INI or JSON mod passes Link() stage without error
            // - BIN mod didn't have any issues. Bin mods will skip the Write() stage
            if ((mod.ModType is ModFormat.Bin && !mod.HasError) || mod.Link())
            {
                mod.Write();
                Console.Write(SuccessString);
            }
            else
            {
                failCount++;
                Globals.PrintColor(FailureString, ConsoleColor.Red);
            }

            // If a mod has at least one unrecognized key, increment warning count
            if (mod.UnrecognizedKeys is not null) WarningCount++;
        }

        // Clean up archives
        foreach (var archive in ArchiveCache)
        {
            // If we've modified the archive, save changes
            if (archive.Modified)
            {
                archive.FinalLength = archive.Archive.SaveToFile();

                // If any archives have changed length, let us know we need to update the TOCs
                if (archive.Archive.StartingLength != archive.FinalLength)
                {
                    requiresTocPatch = true;
                }

                bytesWritten += archive.FinalLength;
                fileCount++;
            }

            // Close archive stream
            archive.Archive.Dispose();

            // If the archive wasn't modified, delete it from the cache directory
            if (!archive.Modified)
            {
                File.Delete(archive.Archive.FullName);
            }
        }

        Console.WriteLine();

        if (requiresTocPatch)
        {
            PatchTOCs(ref fileCount, ref bytesWritten);
        }

        Save(fileCount, bytesWritten, ref failCount);

        HandleErrors(failCount);
        HandleWarnings();
        PrintConflicts();
    }

    /// <summary> Recalculates TOC files. </summary>
    /// <remarks>
    /// All LOC files are combined into the master, and all LOC TOCs will have their contents nulled.<br/>
    /// This is done because UE3 loads the master TOC (IPhoneTOC.txt) first, and all LOC TOCs are optional and additive.
    /// </remarks>
    private void PatchTOCs(ref int fileCount, ref long bytesWritten)
    {
        PrintMessage("Patching TOCs");

        string cookedPath = Ipa.CookedFolder[1..];  // Remove the prepending '/'
        string tocPathPrefix = Ipa.Game is Game.Vote ? @"..\VoteGame\CookedIPhone\" : @"..\SwordGame\CookedIPhone\";
        string basePath = Globals.CachePath + Ipa.AppFolder;
        var masterToc = new FTableOfContents(Ipa.Game);

        // Recalculate master TOC from scratch. Include only files from CookedIPhone directory.
        // LOC TOCs aren't required, so we'll simply include all of their exclusive content in here
        foreach (var file in Ipa.Entries)
        {
            // We aren't interested in folders or files outside the cooked folder.
            // While some files outside the cooked folder are valid, excluding them from the TOC makes no difference.
            if (file.IsDirectory || !file.Name.StartsWith(cookedPath)) continue;

            masterToc.AddEntry($"{tocPathPrefix}{file.Name[cookedPath.Length..].Replace('/', '\\')}", (int)file.UncompressedSize, 0);
        }

        // Update TOC entries with those from our cache directory
        foreach (var archive in ArchiveCache)
        {
            // If the archive's size has changed, reflect this in the TOC
            if (archive.Modified && archive.OriginalLength != archive.FinalLength)
            {
                string archivePath = $"{tocPathPrefix}{archive.Archive.FullName[(archive.Archive.DirectoryName.Length + 1)..].Replace('/', '\\')}";
                masterToc.UpdateEntry(archivePath, (int)archive.FinalLength, 0);
            }
        }

        // Write out our master TOC file
        bytesWritten += masterToc.Save($"{basePath}IPhoneTOC.txt");

        // Zero-out all of the LOC TOCs. All LOC-specific files have been included in the master above, so they are no longer required.
        // We're writing out empty files instead of deleting them outright to make it easier for loose file output mode, and to make the changes more obivous.
        ReadOnlySpan<string> LOCs = UnrealLib.Globals.GetLanguages(Ipa.Game)[1..];
        foreach (var loc in LOCs)
        {
            File.WriteAllText($"{basePath}IPhoneTOC_{loc}.txt", null);
        }

        // Increment fileCount by the number of LOC TOCs + the master
        fileCount += LOCs.Length + 1;
        Console.Write(SuccessString);
    }

    /// <summary> Saves the patched files to an IPA / folder. </summary>
    private void Save(int fileCount, long bytesWritten, ref int failCount)
    {
        bool outputToFolder = Directory.Exists("Output");
        string destinationString = outputToFolder ? "Output Folder" : "IPA";

        PrintMessage($"Saving {fileCount} {(fileCount == 1 ? "file" : "files")} | {FormatSizeString(bytesWritten)} | to {destinationString}");

        // Don't save if even a single mod fails
        if (failCount > 0)
        {
            Console.Write(SkippedString);
            return;
        }

        string outputPath = outputToFolder
            ? Path.Combine("Output", UnrealLib.Globals.GetString(Ipa.Game, true))
            : Path.ChangeExtension(Ipa.Name, null) + " - Modded.ipa";

        if (outputToFolder)
        {
            try
            {
                // Try move cache folder to output folder. If output folder already exists, try delete it
                // @TODO this does not include the root Payload path or any files inside. Issue? Probably.

                if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                Directory.Move($"{Globals.CachePath}/Payload/", outputPath);

                Console.Write(SuccessString);
            }
            catch  // Failed to delete existing Output folder (likely file inside was open)
            {
                SetError(ModContextError.FailSaveFolder_Contention, outputPath);
                Globals.PrintColor(FailureString, ConsoleColor.Red);
                failCount++;
            }
        }
        else // Save to IPA
        {
            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);

                // Ipa.SaveDirectory(Globals.CachePath);
                Ipa.UpdateEntries(Globals.CachePath, "");
                Ipa.Save(outputPath);

                Console.Write(SuccessString);
            }
            catch (Exception e) // Failed to delete existing output IPA (likely was open)
            {
                // Handle rare condition where the user does not have enough free space
                SetError(ExceptionIsDiskSpace(e) ? ModContextError.FailSaveIpa_Space : ModContextError.FailSaveIpa_Contention, outputPath);

                Globals.PrintColor(FailureString, ConsoleColor.Red);
                failCount++;
            }
        }
    }

    /// <summary> Prints all occurred IPA/mod warnings. </summary>
    private void HandleWarnings()
    {
        if (!Ipa.IsLatestVersion) WarningCount++;
        if (WarningCount == 0) return;

        Globals.PrintColor($"\nWarnings ({WarningCount})\n", ConsoleColor.Yellow);

        if (!Ipa.IsLatestVersion)
        {
            Globals.PrintColor($"\n - You are not using the latest version of {UnrealLib.Globals.GetString(Ipa.Game, true)}. Mods may not work correctly!\n", ConsoleColor.Yellow);
        }

        // Check for JSON mods with unrecognized keys
        foreach (var mod in Mods)
        {
            // Only JSON mods with non-null UnrecognizedKeys lists contain warnings
            if (mod.ModType != ModFormat.Json || mod.UnrecognizedKeys is null) continue;

            Globals.PrintColor($"\n - {mod.Name}\n", ConsoleColor.Yellow);
            foreach (var key in mod.UnrecognizedKeys)
            {
                Console.WriteLine($"    - Unrecognized key '{key}'");
            }
        }
    }

    /// <summary> Collates and prints all errors to the console. </summary>
    /// <param name="failCount">The number of mods which failed.</param>
    private void HandleErrors(int failCount)
    {
        if (failCount > 0)
        {
            Globals.PrintColor($"\nErrors ({failCount})\n", ConsoleColor.Red);

            // If we as the ModContext encountered an error, print it here
            if (HasError)
            {
                Console.WriteLine($"\n - {GetErrorTitle()}");
                Globals.PrintColor($"   {GetErrorString()}\n", ConsoleColor.Red);
            }

            // Print mod errors
            foreach (var mod in Mods)
            {
                if (mod.HasError)
                {
                    // Print the mod's name
                    Console.Write($"\n - {mod.Name}");

                    // Print the context of the error if it exists
                    if (mod.ErrorContext is not null)
                    {
                        Globals.PrintColor($" | {mod.ErrorContext}\n", ConsoleColor.DarkGray);
                    }
                    else Console.WriteLine();

                    // Print the error string
                    Globals.PrintColor($"    - {mod.GetErrorString()}\n", ConsoleColor.Red);
                }
            }
        }
    }

    // @TODO: Ideally do this behind the scenes during ModBase::Link(). Also use to influence mod load order
    // @TODO: (UPK) conflict detection can be improved by checking not just if edit same object, but if edits overlap
    // This method is disgusting. Works though.
    private void PrintConflicts()
    {
        Dictionary<FObjectExport, ConflictHelper> exports = [];
        Dictionary<Section, ConflictHelper> sections = [];
        int conflictCount = 0;

        // Map out Exports and Sections and track which mods have made changes
        foreach (var mod in Mods)
        {
            if (mod.HasError) continue;

            foreach (var file in mod.Files)
            {
                foreach (var obj in file.Objects)
                {
                    // Only checking conflicts for UPK mods currently
                    if (file.FileType is FileType.Upk)
                    {
                        // Ini patches need to be "found" in order to detect conflicts
                        if (mod.ModType is ModFormat.Ini)
                        {
                            foreach (var patch in obj.Patches)
                            {
                                if (!patch.Enabled) continue;

                                if (file.Archive.Upk.GetObjectAtOffset((int)patch.Offset) is FObjectExport export)
                                {
                                    exports.TryAdd(export, new ConflictHelper($"{file.Archive.Archive.Name} | {export}"));
                                    if (!exports[export].Mods.Contains(mod))
                                    {
                                        exports[export].Mods.Add(mod);
                                        if (exports[export].Mods.Count > 1) conflictCount++;
                                    }
                                }
                            }
                        }
                        else if (obj.Export is not null)
                        {
                            bool hasAtLeastOneActivePatch = false;
                            foreach (var patch in obj.Patches)
                            {
                                if (patch.Enabled) hasAtLeastOneActivePatch = true;
                            }
                            if (!hasAtLeastOneActivePatch) continue;

                            exports.TryAdd(obj.Export, new ConflictHelper($"{file.Archive.Archive.Name} | {obj.Export}"));
                            exports[obj.Export].Mods.Add(mod);
                            if (exports[obj.Export].Mods.Count > 1) conflictCount++;
                        }
                    }
                    else
                    {
                        foreach (var patch in obj.Patches)
                        {
                            if (!patch.Enabled) continue;

                            sections.TryAdd(patch._sectionReference, new ConflictHelper($"{file.Archive.Archive.Name}, {obj.Ini.FriendlyName}, {patch._sectionReference.Name}"));
                            sections[patch._sectionReference].Mods.Add(mod);
                            if (sections[patch._sectionReference].Mods.Count > 1) conflictCount++;
                        }
                    }
                }
            }
        }

        if (conflictCount > 0)
        {
            Globals.PrintColor($"\nConflict Warnings ({conflictCount})\n", ConsoleColor.Yellow);

            // UPK UObject conflicts
            foreach (var export in exports)
            {
                export.Value.PrintConflicts();
            }

            // Coalesced conflicts (do we care about these?)
            foreach (var section in sections)
            {
                section.Value.PrintConflicts();
            }
        }
    }

    #region Helpers

    public string QualifyPath(string path) => Ipa.QualifyPath(path);

    /// <summary>
    /// Formats a count of bytes into a human-readable string. Automatically converts between KB, MB, and GB.
    /// </summary>
    /// <remarks> Appends the unit onto the end of the string, for example: "4.13 KB". </remarks>
    public static string FormatSizeString(long numBytes)
    {
        const int Kilobyte = 1024;
        const int Megabyte = Kilobyte * 1024;
        const int Gigabyte = Megabyte * 1024;

        return numBytes switch
        {
            >= Gigabyte => $"{(float)numBytes / Gigabyte:N2} GB",
            >= Megabyte => $"{(float)numBytes / Megabyte:N2} MB",
            _ => $"{(float)numBytes / Kilobyte:N2} KB"
        };
    }

    public static void PrintPercentage(float percentage) => Console.Write($"{Backspace} [ {percentage * 100:00.0}% ]");

    /// <summary> Prints a formatted message to the console. </summary>
    /// <remarks> Used during mod patching process. </remarks>
    public static void PrintMessage(string modName, int? digit = null)
    {
        string status = digit is null ? modName : $"  {digit:00} - {modName}";

        // Globals.MaxStringLength - 14 to guarantee at least three period chars are shown before the success string
        status = status[..Math.Min(status.Length, Globals.MaxStringLength - 14)];

        Console.Write($"{status} ".PadRight(Globals.MaxStringLength, '.'));
    }

    /// <summary>
    /// Attempts to retrieve a <see cref="CachedArchive"/> from within CachedArchives.
    /// If not found, and file exists within IPA, one will be added.
    /// </summary>
    public bool TryGetCachedArchive(ref readonly ModFile file, out CachedArchive outArchive)
    {
        string qualifiedPath = Ipa.QualifyPath(file.FileName)[1..]; // [1..] to remove leading '/'

        foreach (var archive in ArchiveCache)
        {
            if (archive.Entry.Name.Equals(qualifiedPath, StringComparison.OrdinalIgnoreCase))
            {
                outArchive = archive;
                return true;
            }
        }

        if (Ipa.GetEntry(qualifiedPath) is not ZipEntry entry)
        {
            outArchive = default;
            return false;
        }

        outArchive = new CachedArchive(entry, file.FileType);
        ArchiveCache.Add(outArchive);
        return true;
    }

    private static bool ExceptionIsDiskSpace(Exception e) => e.Message.StartsWith("There is not enough space");

    #endregion

    #region Error messages

    public override string GetErrorString() => ErrorType switch
    {
        ModContextError.FailExtract_Space => "There is not enough free disk space to extract the required files.",
        ModContextError.FailSaveIpa_Space => "There is not enough free disk space to save the IPA.",
        ModContextError.FailSaveIpa_Contention => $"Output IPA '{ErrorContext}' is in use. Close any apps using it and retry.",
        ModContextError.FailSaveFolder_Contention => $"Failed to delete '{ErrorContext}'. Close any open files and retry."
    };

    public string GetErrorTitle() => ErrorType switch
    {
        ModContextError.FailExtract_Space => "Extract files",
        ModContextError.FailSaveIpa_Space => "Save to IPA",
        ModContextError.FailSaveIpa_Contention => "Save to IPA",
        ModContextError.FailSaveFolder_Contention => "Save to Output Folder"
    };

    #endregion

    // @TODO documentation
    private class ConflictHelper(string uri)
    {
        public readonly string URI = uri;
        public List<ModBase> Mods = [];

        public void PrintConflicts()
        {
            if (Mods.Count > 1)
            {
                Globals.PrintColor($"\n - {URI}\n", ConsoleColor.Yellow);
                foreach (var mod in Mods)
                {
                    Console.WriteLine($"     - {mod.Name}");
                }
            }
        }
    }
}

// Wrapper for UnrealArchive classes with added functionality
// @TODO documentation
public record CachedArchive
{
    public UnrealArchive? Archive;
    public readonly ZipEntry Entry;
    public readonly FileType Type;
    public bool ShouldExtractFile;
    public bool Modified;

    public CachedArchive(ZipEntry entry, FileType type, bool shouldExtractFile = true)
    {
        Entry = entry;
        Type = type;
        ShouldExtractFile = shouldExtractFile;
    }

    public long OriginalLength => Archive.StartingLength;
    public long FinalLength;

    public UnrealPackage Upk => (UnrealPackage)Archive;
    public Coalesced Coalesced => (Coalesced)Archive;
    public bool HasError => Archive?.HasError ?? true;

    public override string ToString() => Entry.Name;
}
