using Ionic.Zip;
using UnrealLib;
using UnrealLib.Enums;

namespace IBPatcher;

public enum IpaError : byte
{
    None,
    FileNotFound,
    InvalidZip,
    InvalidGame
}

public class IPA : ErrorHelper<IpaError>
{
    private readonly ZipFile Archive;
    public readonly Game Game;

    public readonly string OriginalPath;

    public readonly string MainFolder;
    public readonly string CookedFolder;

    public readonly int EngineVersion;
    public readonly int EngineBuild;
    public readonly bool IsLatestVersion;

    public override bool HasError => Error is not IpaError.None;

    // @TODO: Comments!
    public IPA(string filePath)
    {
        OriginalPath = filePath;

        if (!File.Exists(OriginalPath))
        {
            // Context = filePath;
            SetError(IpaError.FileNotFound);
            return;
        }

        try
        {
            Archive = new ZipFile(OriginalPath)
            {
                CompressionLevel = Ionic.Zlib.CompressionLevel.Level3,
                TempFileFolder = Globals.CachePath,
                BufferSize = 16384 * 8,     // @TODO: See if increasing buffer sizes makes a meaningful difference
                CodecBufferSize = 16384 * 8
            };

            Archive.SaveProgress += SaveProgress;
        }
        catch (ZipException)
        {
            // Context = filePath;
            SetError(IpaError.InvalidZip);
            return;
        }

        // Try get SwordGame's Entry.xxx file
        ZipEntry? entry = Archive["/Payload/SwordGame.app/CookedIPhone/Entry.xxx"];

        if (entry is null)
        {
            // Try get Vote's Entry.xxx file
            if ((entry = Archive["/Payload/VoteGame.app/CookedIPhone/Entry.xxx"]) is null)
            {
                SetError(IpaError.InvalidGame);
                return;
            }

            Game = Game.Vote;
        }

        // Determine version info from Entry.xxx file
        entry.Extract(Globals.CachePath);
        using (var upk = new UnrealPackage(Globals.CachePath + '/' + entry.FileName, false))
        {
            switch (upk.EngineVersion)
            {
                case > 864:
                    Game = Game.IB3;
                    IsLatestVersion = upk.EngineBuild == 13249;
                    break;
                case > 788 when Game is Game.Vote:
                    IsLatestVersion = upk.EngineBuild == 9711;
                    break;
                case > 788:
                    Game = Game.IB2;
                    IsLatestVersion = upk.EngineBuild == 9714;
                    break;
                default:
                    Game = Game.IB1;
                    IsLatestVersion = upk.EngineBuild == 7982;
                    break;
            }

            EngineVersion = upk.EngineVersion;
            EngineBuild = upk.EngineBuild;
        }

        File.Delete(Globals.CachePath + '/' + entry.FileName);

        MainFolder = $"/Payload/{(Game is Game.Vote ? "Vote" : "Sword")}Game.app/";
        CookedFolder = MainFolder + "CookedIPhone/";
    }

    public void Save(string outputDirectory) => Archive.Save(outputDirectory);

    public void SaveDirectory(string directoryPath)
    {
        foreach (string file in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
        {
            Archive.UpdateFile(file, Path.GetDirectoryName(file[directoryPath.Length..]));
        }
    }

    public ZipEntry? GetEntry(string path) => Archive[path];

    public string QualifyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

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
    /// Returns path relative to the CookedIPhone folder in the IPA.
    /// </summary>
    /// <param name="path">Absolute path within the IPA to be made relative.</param>
    /// <remarks>Opposite of <see cref="QualifyPath"/></remarks>
    public string GetRelativePath(string path) => Path.GetRelativePath(CookedFolder, path);

    public override string GetString(IpaError error) => error switch
    {
#if UNIX
        IpaError.FileNotFound => "IPA file path is not valid. Restart the patcher, and try drag-and-dropping the IPA into the terminal window.",
#else
        IpaError.FileNotFound => "IPA file path is not valid.\n   Double-check the path exists, or try drag-and-dropping instead.",
#endif
        IpaError.InvalidZip => "IPA is not a valid zip archive. Try downloading a fresh IPA and try again.",
        IpaError.InvalidGame => "IPA is not a valid Infinity Blade archive."
    };

    private static void SaveProgress(object sender, SaveProgressEventArgs e)
    {
        if (e.EventType is ZipProgressEventType.Saving_BeforeWriteEntry)
        {
            ModContext.PrintPercentage((float)e.EntriesSaved / e.EntriesTotal);
        }
    }
}
