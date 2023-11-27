using Ionic.Zip;
using UnrealLib.Config.Coalesced;

namespace IBPatcher.Mod;

public static class BinMod
{
    public static ModBase ReadBinMod(string modPath, ModContext ctx)
    {
        var mod = new ModBase(modPath, ModFormat.Bin, ctx.Game) { Name = Path.GetFileName(modPath) };

        // Copy ipa-qualified path to the cached path
        string ipaPath = ctx.QualifyPath(mod.Name)[1..];
        string cachePath = Path.Combine(Globals.CachePath, ipaPath);
        File.Copy(modPath, cachePath);

        var coal = new Coalesced(cachePath, ctx.Game, false);

        // Translate UnrealArchive error to ModError
        mod.SetError(coal.Error switch
        {
            UnrealLib.UnrealArchiveError.UnexpectedGame => ModError.CoalescedUnexpectedGame,
            UnrealLib.UnrealArchiveError.DecryptionFailed => ModError.CoalescedDecryptionFailed,
            UnrealLib.UnrealArchiveError.ParseFailed => ModError.ArchiveLoadFailed,
            UnrealLib.UnrealArchiveError.None => ModError.None
        });

        // Add to CachedArchives list. Use a dummy ZipEntry and indicate it should not be extracted normally.
        // Link already-loaded Coalesced file and force Modified
        ctx.ArchiveCache.Add(new CachedArchive(new ZipEntry { FileName = ipaPath }, FileType.Coalesced, false) { Archive = coal, Modified = true });

        return mod;
    }
}
