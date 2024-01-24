using System.IO;
using UnrealLib.Config.Coalesced;
using Zip.Core;

namespace IBPatcher.Mod;

public static class BinMod
{
    public static ModBase Read(string modPath, ModContext context)
    {
        var mod = new ModBase(modPath, ModFormat.Bin, context.Game);

        string fileName = Path.GetFileName(modPath);
        string ipaPath = context.QualifyPath(fileName)[1..];

        var coal = Coalesced.FromFile(modPath, context.Game);

        // Check for Coalesced errors
        if (coal.HasError)
        {
            string modPathFormatted = $"./{context.ModFolderRelative}/{fileName}";

            ModError error = coal.ErrorType switch
            {
                UnrealLib.ArchiveError.UnexpectedGame => ModError.Coalesced_WrongGame,
                _ => ModError.Coalesced_InvalidFile  // Consider any other errors to be an invalid file
            };

            mod.SetError(error, modPathFormatted);
        }

        // Copy to cached path
        File.Copy(modPath, Path.Combine(Globals.CachePath, ipaPath));

        // Add to cached archives so other mods don't pull a copy from the IPA

        // Dummy ZipEntry to conform with existing systems. Won't be used
        var entry = ZipEntry.CreateNew(null, ipaPath);

        context.ArchiveCache.Add(new CachedArchive(entry, FileType.Coalesced, false) { Archive = coal, Modified = true });

        return mod;
    }
}
