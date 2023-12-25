using IBPatcher.Mod;
using Ionic.Zip;
using UnrealLib;
using UnrealLib.Config;
using UnrealLib.Config.Coalesced;
using UnrealLib.Core;
using UnrealLib.Enums;

namespace IBPatcher;

public class ModContext
{
    private const string Backspace = "\b\b\b\b\b\b\b\b\b\b";
    private const string SuccessString = Backspace + " [SUCCESS]\n";
    private const string FailureString = Backspace + " [FAILURE]\n";
    private const string SkippedString = Backspace + " [SKIPPED]\n";
    public const string CommandsModName = "Commands.txt";

    public readonly IPA Ipa;
    public List<ModBase> Mods = new();
    public List<CachedArchive> ArchiveCache = new();

    public readonly string ModFolder;
    public string SaveErrorTitle;
    public string SaveErrorMessage;

    public Game Game => Ipa.Game;
    public string CommandsModPath => Path.Combine(ModFolder, CommandsModName);
    public int ModCount => Mods.Count + (File.Exists(CommandsModPath) ? 1 : 0);

    public ModContext(IPA ipa)
    {
        Ipa = ipa;
        ModFolder = Path.Combine(AppContext.BaseDirectory, "Mods", UnrealLib.Globals.GetString(Ipa.Game, true));
    }

    public void LoadMods()
    {
        var directory = Directory.CreateDirectory(ModFolder);

        // Read bin mods separately before everything else
        foreach (var entry in directory.GetFiles("*.bin"))
        {
            Mods.Add(BinMod.ReadBinMod(entry.FullName, this));
        }

        foreach (var entry in directory.GetFiles())
        {    
            switch (entry.Extension.ToLowerInvariant())
            {
                case ".ini":
                    Mods.Add(IniMod.ReadIniMod(entry.FullName, this));
                    Mods[^1].Setup(this);
                    break;
                case ".json":
                    Mods.Add(JsonMod.ReadJsonMod(entry.FullName, this));
                    Mods[^1].Setup(this);
                    break;
            }
        }
    }

    /// <summary>
    /// Extracts and initializes all cached archives.
    /// </summary>
    private void LoadCachedArchives()
    {
        long totalSize = 0, currentSize = 0;
        int fileCount = 0;

        foreach (var archive in ArchiveCache)
        {
            // Only tally stats of files that need extracting
            if (archive.ShouldExtractFile)
            {
                totalSize += archive.Entry.UncompressedSize;
                fileCount++;
            }
        }

        PrintMessage($"{Locale.ExtractingFiles} | {fileCount} {(fileCount == 1 ? Locale.WordFile : Locale.WordFiles)} | {GetSizeInMB(totalSize)} MB");

        foreach (var archive in ArchiveCache)
        {
            if (archive.ShouldExtractFile)
            {
                PrintPercentage((float)currentSize / totalSize);

                archive.Entry.Extract(Globals.CachePath);
                currentSize += archive.Entry.UncompressedSize;
            }

            archive.Archive = archive.Type is FileType.Upk
                ? new UnrealPackage(Path.Combine(Globals.CachePath, archive.Entry.FileName), false)
                : new Coalesced(Path.Combine(Globals.CachePath, archive.Entry.FileName), Ipa.Game, false);
        }

        Console.WriteLine(SuccessString);
    }

    public void ApplyMods()
    {
        int modCount = 0;               // Increments with every processed mod. Used for printing
        int failCount = 0;              // Increments with every failed mod
        int fileCount = 0;              // Increments with every modified archive. Used for printing
        long bytesWritten = 0;
        bool requiresTOCPatch = false;  // TOC patching is only necessary if bytes are added to a UPK

        LoadCachedArchives();

        // Commands mod
        if (File.Exists(CommandsModPath))
        {
            // Create Binaries folder in cache directory
            string binariesFolder = Globals.CachePath + Ipa.QualifyPath("../Binaries/");
            Directory.CreateDirectory(binariesFolder);

            File.Copy(CommandsModPath, binariesFolder + CommandsModName);
            PrintMessage(CommandsModName, ++modCount);
            Console.Write(SuccessString);

            // Create a UE3CommandLine.txt file in the cache's CookedIPhone folder
            File.WriteAllText(Globals.CachePath + Ipa.QualifyPath("UE3CommandLine.txt"), $"-exec=\"{CommandsModName}\"");

            // Don't bother trying to poll file sizes
            fileCount += 2;
        }

        foreach (var mod in Mods)
        {
            PrintMessage(mod.Name, ++modCount);

            // Bin mods are processed on read, so check whether they've errored or not.
            // No harm in calling Write() on file-less bin mods.
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
        }

        // Close / clean up archives
        foreach (var archive in ArchiveCache)
        {
            // If we've modified the archive, save changes
            if (archive.Modified)
            {
                var intermediate = archive.Archive.Save();

                if (archive.Type is FileType.Upk && archive.Archive.OriginalLength != intermediate)
                {
                    requiresTOCPatch = true;
                }

                bytesWritten += intermediate;
                fileCount++;
            }

            // Close stream
            archive.Archive.Dispose();

            // If archive wasn't modified, delete the file to prevent
            // copying it over to the output destination
            if (!archive.Modified)
            {
                File.Delete(archive.Archive.QualifiedPath);
            }
        }

        Console.WriteLine();

        PatchTOCs(requiresTOCPatch, ref fileCount);
        Save(fileCount, bytesWritten, ref failCount);

        HandleWarnings(/*failCount*/);
        HandleErrors(failCount);
        PrintConflicts();

        // Clean up after ourselves
        Directory.Delete(Globals.CachePath, true);
    }

    /// <summary>
    /// Nulls the contents of every IPhoneTOC.txt file, which seems to have the same effect as recalculating them.
    /// <br/>Deleting the TOC files outright will cause crashes for IB1.
    /// </summary>
    private void PatchTOCs(bool requiresTOCPatch, ref int fileCount)
    {
        PrintMessage("Patching TOCs");

        if (!requiresTOCPatch)
        {
            Console.Write(SkippedString);
            return;
        }

        string cookedPath = Ipa.CookedFolder[1..];  // Remove the prepending '/'
        string tocPathPrefix = Ipa.Game is Game.Vote ? "..\\VoteGame\\CookedIPhone\\" : "..\\SwordGame\\CookedIPhone\\";
            string basePath = Globals.CachePath + Ipa.MainFolder;
        FTableOfContents masterTOC = new(Ipa.Game);

        // Recalculate master TOC from scratch. Include all files from CookedIPhone directory
        // LOC TOCs aren't required, so we'll simply include all of their exclusive content in here. Likely no observable difference during runtime.
        foreach (var file in Ipa.EntriesSorted)
        {
            // We aren't interested in folders or files outside the cooked folder.
            // While some files outside the cooked folder are valid, excluding them from the TOC makes no difference.
            if (file.IsDirectory || !file.FileName.StartsWith(cookedPath)) continue;

            masterTOC.AddEntry($"{tocPathPrefix}{file.FileName[cookedPath.Length..].Replace('/', '\\')}", (int)file.UncompressedSize, 0);
        }

        foreach (var archive in ArchiveCache)
        {
            // If the archive's length has changed, reflect this in the TOC
            if (archive.Modified && archive.Archive.InitialLength != archive.Archive.LastLength)
            {
                string archivePath = $"{tocPathPrefix}{archive.Archive.QualifiedPath[(archive.Archive.DirectoryName.Length + 1)..].Replace('/', '\\')}";
                masterTOC.UpdateEntry(archivePath, (int)archive.Archive.LastLength, 0);
            }
        }

        // Write out master TOC file
        masterTOC.Save($"{basePath}IPhoneTOC.txt");

        // Zero-out all of the LOC TOCs. All LOC-specific files have been included in the master above.
        var LOCs = UnrealLib.Globals.GetLanguages(Ipa.Game)[1..];
        foreach (var loc in LOCs)
        {
            File.WriteAllText($"{basePath}IPhoneTOC_{loc}.txt", null);
        }

        fileCount += LOCs.Length + 1;
        Console.Write(SuccessString);
    }

    private void Save(int fileCount, long bytesWritten, ref int failCount)
    {
        bool outputFolder = Directory.Exists("Output");
        string destinationString = outputFolder ? "Output Folder" : "IPA";

        // In case we encounter an error
        SaveErrorTitle = outputFolder ? "Save to Output Folder" : "Save to IPA";

        PrintMessage($"Saving {fileCount} {(fileCount == 1 ? "file" : "files")} | {GetSizeInMB(bytesWritten)} MB | to {destinationString}");

        // Don't save if even a single mod fails
        if (failCount > 0)
        {
            Console.Write(SkippedString);
            return;
        }

        string outputPath = outputFolder
            ? Path.Combine("Output", UnrealLib.Globals.GetString(Ipa.Game, true))
            : Path.ChangeExtension(Ipa.OriginalPath, null) + " - Modded.ipa";

        if (outputFolder)
        {
            try
            {
                // Try move cache folder to output folder. If output folder already exists, try delete it

                if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                Directory.Move(Globals.PayloadPath, outputPath);

                Console.Write(SuccessString);
            }
            catch  // Failed to delete existing Output folder (likely file inside was open)
            {
                SaveErrorMessage = $"Failed to delete '{outputPath}'. Close any open files and retry.";
                Globals.PrintColor(FailureString, ConsoleColor.Red);
                failCount++;
            }
        }
        else
        {
            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);

                Ipa.SaveDirectory(Globals.CachePath);
                Ipa.Save(outputPath);

                Console.Write(SuccessString);
            }
            catch  // Failed to delete existing output IPA (likely was open)
            {
                SaveErrorMessage = $"IPA '{outputPath}' is in use. Close any apps using it and retry.";
                Globals.PrintColor(FailureString, ConsoleColor.Red);
                failCount++;
            }
        }
    }

    private void HandleWarnings(/*int failCount*/)
    {
        int warningCount = (Ipa.IsLatestVersion ? 0 : 1) /*+ (failCount > 0 ? 1 : 0)*/ ;

        if (warningCount > 0)
        {
            Globals.PrintColor($"\nWarnings ({warningCount})\n\n", ConsoleColor.Yellow);

            if (!Ipa.IsLatestVersion)
            {
                Globals.PrintColor($" - You are not using the latest version of {UnrealLib.Globals.GetString(Ipa.Game, true)}. Mods may not work correctly!\n", ConsoleColor.Yellow);
            }

            // I'm finding this annoying.
            //if (failCount > 0)
            //{
            //    Globals.PrintColor(" - All mods must parse successfully in order to perform a save\n\n", ConsoleColor.Yellow);
            //}
        }
    }

    /// <summary>
    /// Collate and print any errors to the console.
    /// </summary>
    /// <param name="failCount">The number of mods which failed.</param>
    private void HandleErrors(int failCount)
    {
        if (failCount > 0)
        {
            Globals.PrintColor($"\nErrors ({failCount})\n", ConsoleColor.Red);

            if (SaveErrorMessage is not null)
            {
                Console.WriteLine($"\n - {SaveErrorTitle}");
                Globals.PrintColor($"   {SaveErrorMessage}\n", ConsoleColor.Red);
            }

            // Print mod errors
            foreach (var mod in Mods)
            {
                if (mod.HasError)
                {
                    Console.Write($"\n - {mod.Name}");
                    // Console.WriteLine($"{(mod.ErrorContext != "" ? $" | {mod.ErrorContext}" : "")}");
                    Globals.PrintColor($"{(mod.ErrorContext != "" ? $" | {mod.ErrorContext}" : "")}\n", ConsoleColor.DarkGray);
                    Globals.PrintColor($"   {mod.ErrorString}\n", ConsoleColor.Red);
                }
            }
        }
    }

    // @TODO: Ideally do this behind the scenes during ModBase::Link(). Also use to influence mod load order
    private void PrintConflicts()
    {
        Dictionary<FObjectExport, ConflictHelper> exports = new();
        Dictionary<Section, ConflictHelper> sections = new();
        int conflictCount = 0;

        // Map out Exports and Sections and track which mods have made changes
        foreach (var mod in Mods)
        {
            if (mod.HasError) continue;

            foreach (var file in mod.Files)
            {
                foreach (var obj in file.Objects)
                {
                    if (file.FileType is FileType.Upk)
                    {
                        if (obj.Export is not null)
                        {
                            exports.TryAdd(obj.Export, new ConflictHelper($"{file.Archive.Archive.Filename}, {obj.Export}"));
                            exports[obj.Export].Mods.Add(mod);
                            if (exports[obj.Export].Mods.Count > 1) conflictCount++;
                        }

                        continue;
                    }

                    foreach (var patch in obj.Patches)
                    {
                        sections.TryAdd(patch.Section, new ConflictHelper($"{file.Archive.Archive.Filename}, {patch.Section}"));
                        sections[patch.Section].Mods.Add(mod);
                        if (sections[patch.Section].Mods.Count > 1) conflictCount++;
                    }
                }
            }
        }

        if (conflictCount > 0)
        {
            Globals.PrintColor($"\nMod Conflicts ({conflictCount})\n", ConsoleColor.Yellow);

            foreach (var export in exports)
            {
                export.Value.PrintConflicts();
            }

            // Do we care about these?
            foreach (var section in sections)
            {
                section.Value.PrintConflicts();
            }
        }
    }

    #region Helpers

    public string QualifyPath(string path) => Ipa.QualifyPath(path);

    public static string GetSizeInMB(long bytes) => $"{bytes / 1000000f:F2}";

    public static void PrintPercentage(float percentage) => Console.Write($"{Backspace} [ {percentage * 100:00.0}% ]");

    /// <summary>
    /// Prints a message to the console. Used during mod patching process.
    /// </summary>
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
            if (archive.Entry.FileName.Equals(qualifiedPath, StringComparison.OrdinalIgnoreCase))
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

    #endregion

    private class ConflictHelper(string uri)
    {
        public readonly string URI = uri;
        public List<ModBase> Mods = new();

        public void PrintConflicts()
        {
            if (Mods.Count > 1)
            {
                Globals.PrintColor($"\n - {URI} ({Mods.Count})\n", ConsoleColor.Yellow);
                foreach (var mod in Mods)
                {
                    Console.WriteLine($"     - {mod.Name}");
                }
            }
        }
    }
}

public class CachedArchive(ZipEntry entry, FileType type, bool shouldExtractFile = true)
{
    public UnrealArchive? Archive = null;
    public readonly ZipEntry Entry = entry;
    public readonly FileType Type = type;
    public bool ShouldExtractFile = shouldExtractFile;
    public bool Modified = false;

    public UnrealPackage Upk => (UnrealPackage)Archive;
    public Coalesced Coalesced => (Coalesced)Archive;
    public bool HasError => Archive?.HasError ?? true;
}
