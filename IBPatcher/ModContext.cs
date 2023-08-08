using System;
using System.Collections.Generic;
using System.IO;
using IBPatcher.Models;
using UnLib;
using UnLib.Coalesced;
using UnLib.Enums;
using UnLib.Interfaces;

namespace IBPatcher;

public class ModContext : IDisposable
{
    private readonly IPA Ipa;
    private readonly List<Mod> Mods = new();
    private readonly List<string> CopyMods = new();
    private readonly Dictionary<string, FileStream> TOCs = new();
    private readonly Dictionary<string, IUnrealStreamable> Streams = new();

    public Game Game => Ipa.Game;
    public string GameString => UnLib.Globals.GameToString(Game);
    public string ModDirectory => Path.Combine("Mods", GameString);
    private string WorkingDir => Path.Combine(Globals.TempPath, GameString);
    private static string TempDir => Globals.TempPath;
    public const string Backspace = "\b\b\b\b\b\b\b";
    public int ModCount => Mods.Count + CopyMods.Count;
    public int FailedCount = 0;

    private static bool ShouldOutputIpa => !Directory.Exists("Output");

    public ModContext(IPA ipa)
    {
        Ipa = ipa;
        Directory.CreateDirectory(WorkingDir);
        
        ReadMods();
    }

    private void ReadMods()
    {
        if (!Directory.Exists(ModDirectory)) Directory.CreateDirectory(ModDirectory);

        foreach (var modDir in Directory.GetFiles(ModDirectory))
        {
            var fileName = Path.GetFileName(modDir);

            switch (Path.GetExtension(fileName).ToLower())
            {
                case ".json":
                    Mods.Add(JsonMod.ReadJsonMod(modDir));
                    break;
                
                case ".ini":
                    Mods.Add(IniMod.ReadIniMod(modDir));
                    break;
                
                case ".bin":
                    CopyMods.Add(Ipa.ZipPath(fileName));
                    break;
                
                case ".txt":
                    if (string.Equals(fileName, "Commands.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        CopyMods.Add(Ipa.ZipPath("../Binaries/Commands.txt"));
                        CopyMods.Add(Ipa.ZipPath("UE3Commandline.txt"));
                        File.WriteAllText(Path.Combine(ModDirectory, "UE3Commandline.txt"), "-exec=\"Commands.txt\"");
                    }
                    break;
            }
        }
    }
    public void PrepareFiles()
    {
        foreach (var mod in Mods)
        {
            foreach (var file in mod.Files.Values)
            {
                file.QualifiedPath = Ipa.ZipPath(file.File);
                string tempFilePath = Path.Combine(WorkingDir, file.QualifiedPath);
                
                // @ERROR: File not found within IPA.
                if (!Ipa.HasEntry(file.QualifiedPath))
                {
                    mod.ErrorContext = $"File '{file.QualifiedPath}' not found in the IPA";
                    break;
                }

                Streams.TryAdd(file.QualifiedPath, file.Type is FileType.Upk
                    ? new UnrealPackage(tempFilePath)
                    : new Coalesced(tempFilePath, Ipa.Game));
                file.Stream = Streams[file.QualifiedPath];
            }
        }

        // Copy all CopyMods to the temp directory.
        foreach (var file in CopyMods)
        {
            string fileName = Path.GetFileName(file);
            string outputFolder = Path.Combine(WorkingDir, file[..^fileName.Length]);
            string modPath = Path.Combine(ModDirectory, fileName);

            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            File.Copy(modPath, Path.Combine(outputFolder, fileName));
        }
        
        // Go over all UPK/Coalesced files used by mods and extract/initialize them.
        Console.Write(GetStatusString("Unpacking game data") + "[ 00.0% ]");
        int count = 0;
        
        foreach (var stream in Streams)
        {
            // If file has been copied, do not extract it from the IPA.
            if (!CopyMods.Contains(stream.Key)) Ipa.ExtractEntry(stream.Key, WorkingDir);
            
            stream.Value.Init();
            
            string percentage = ((float)count++ / Streams.Count * 100).ToString("00.0");
            Console.Write(string.Join(percentage, Backspace, "% ]"));
        }
        Console.WriteLine(new string('\b', 9) + "[SUCCESS]\n");
    }

    public void ApplyMods()
    {
        // Cache the result for better performance + ensure remains constant.
        bool shouldOutputIpaCached = ShouldOutputIpa;
        
        // Process CopyMods
        for (var i = 0; i < CopyMods.Count; i++)
        {
            string fileName = Path.GetFileName(CopyMods[i]);
            string outputPath = Path.Combine(ModDirectory, fileName);

            if (shouldOutputIpaCached)
            {
                Ipa.UpdateEntry(CopyMods[i], File.Open(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read));   
            }
            
            Console.WriteLine(GetStatusString(fileName, i + 1) + "[SUCCESS]");
        }
        
        // Process Mods
        for (var i = 0; i < Mods.Count; i++)
        {
            var mod = Mods[i];
            Console.Write(GetStatusString(mod.Name, CopyMods.Count + i + 1));

            // If mod has error before processing (usually only missing file)
            if (mod.HasError)
            {
                FailedCount++;
                Globals.PrintColor("[FAILED!]\n", ConsoleColor.Red);
                continue;
            }
            
            CreateFallbackFiles(mod);

            if (!mod.Process(this))
            {
                FailedCount++;
                RestoreFallbackFiles(mod);
                Globals.PrintColor("[FAILED!]\n", ConsoleColor.Red);
            }
            else
            {
                ClearFallbackFiles(mod);
                Console.WriteLine("[SUCCESS]");
            }
        }
        
        Console.WriteLine();
        
        // Finalize streams
        foreach (var stream in Streams)
        {
            // If the file wasn't modified, discard it.
            if (!stream.Value.Modified)
            {
                stream.Value.Dispose();
                File.Delete(stream.Value.FilePath);
                Streams.Remove(stream.Key);
                continue;
            }
            
            stream.Value.Save();
            
            // If ShouldOutputIpa, update the IPA entry.
            // Otherwise, we need to explicitly dispose of here. IPA automatically disposes on save.
            if (shouldOutputIpaCached)
            {
                Ipa.UpdateEntry(stream.Key, stream.Value.BaseStream);
            }
        }
        
        // TOC Fixup
        // Ensure the TOC output path exists
        string baseFolder = Path.Combine(WorkingDir, Ipa.CookedFolder);
        if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
        
        foreach (var tocFile in GetTOCs(Game))
        {
            TOCs[tocFile] = File.Create(Path.Combine(WorkingDir, tocFile));
        
            // If ShouldOutputIpa, update the IPA entry
            if (shouldOutputIpaCached)
            {
                Ipa.UpdateEntry(tocFile, TOCs[tocFile]);
            }
        }
        Console.WriteLine(GetStatusString($"Fixing TOCs") + "[SUCCESS]");
    }
    
    public void Output()
    {
        string outputError = string.Empty;
        int fileCount = Streams.Count + CopyMods.Count /* + TOCs.Count*/;
        
        Console.Write(GetStatusString(
            $"Saving {fileCount} file{(fileCount == 1 ? "" : "s")} to {(ShouldOutputIpa ? "IPA" : "output folder")}") + "[ 00.0% ]");

        // Don't want to save if no files that need saving (ignore TOCs)
        if (fileCount == 0)
        {
        }
        else if (ShouldOutputIpa)
        {
            // If the IPA could not be saved (likely because output IPA was open)
            if (!Ipa.SaveModdedIpa())
            {
                // @ERROR: Remind user not to have the output IPA open.
                outputError = $"The output IPA '{Ipa.ModdedName}' is in use!\nEnsure it is closed and try running the patcher again.";
            }
        }
        else
        {
            // Close streams manually so temp dir can be moved.
            foreach (var stream in Streams.Values) stream.Dispose();
            foreach (var stream in TOCs.Values) stream.Dispose();
            
            string outputPath = Path.Combine("Output", GameString);

            try
            {
                if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                Directory.Move(WorkingDir, outputPath);
            }
            // If the output folder could not be deleted (likely due to an open file)
            catch (IOException) when (Directory.Exists(outputPath))
            {
                // @ERROR: Remind user to not have any relevant files open.
                outputError = $"One or more files were open inside '{outputPath}'!\nEnsure all files are closed and try running the patcher again.";
            }
            // If the working directory could not be moved (likely a stream that wasn't closed)
            catch (IOException)
            {
                // @ERROR: This should not happen. Uh-oh.
                outputError = "Issues with internal folder!\nIf this persists after restarting your PC, please reach out for assistance.";
            }
        }
        
        if (fileCount == 0) Console.WriteLine(Backspace + "\b\b[SKIPPED]\n");
        else if (outputError.Length == 0) Console.WriteLine(Backspace + "\b\b[SUCCESS]\n");
        else
        {
            FailedCount++;
            Globals.PrintColor(Backspace + "\b\b[FAILED!]\n\n", ConsoleColor.Red);
        }
        
        // Warnings + conflict detection - yellow font
        var conflicts = GetConflicts();
        if (conflicts.Count > 0)
        {
            Globals.PrintColor($"Warnings ({conflicts.Count})\n\n", ConsoleColor.Yellow);

            foreach (var conflict in conflicts)
            {
                Globals.PrintColor("  Conflict ", ConsoleColor.Yellow);
                Console.WriteLine($"{conflict.Key}\n   - {string.Join("\n   - ", conflict.Value)}");
                Console.WriteLine();
            }
        }
        
        // Error report - red font
        if (FailedCount > 0)
        {
            Globals.PrintColor($"Errors ({FailedCount})\n\n", ConsoleColor.Red);

            // Save error.
            if (outputError.Length != 0)
            {
                Console.WriteLine(outputError + '\n');
            }
            
            // Mod errors.
            foreach (var mod in Mods)
            {
                if (!mod.HasError) continue;
                
                Console.WriteLine($"  {mod.Name}:");
                Globals.PrintColor($"  {mod.ErrorContext}\n\n", ConsoleColor.Red);
            }
        }
    }

    #region Helpers
    
    /// Create copies of the files being worked on in case the mod errors so we can restore them.
    private void CreateFallbackFiles(Mod mod)
    {
        foreach (var file in mod.Files.Values)
        {
            string filePath = Path.Combine(WorkingDir, file.QualifiedPath);
            File.Copy(filePath, filePath + "_");
        }
    }

    /// Restores the existing fallback files when a mod encounters an error.
    private void RestoreFallbackFiles(Mod mod)
    {
        foreach (var file in mod.Files.Values)
        {
            file.Stream.Dispose();
            
            string filePath = Path.Combine(WorkingDir, file.QualifiedPath);
            
            File.Delete(filePath);
            File.Move(filePath + "_", filePath);
            
            file.Stream.Init();
        }
    }

    /// Deletes the fallback files as the mod was successful.
    private void ClearFallbackFiles(Mod mod)
    {
        foreach (var file in mod.Files.Values)
        {
            File.Delete(Path.Combine(WorkingDir, file.QualifiedPath) + "_");
        }
    }

    private Dictionary<string, List<string>> GetConflicts()
    {
        // Create a dictionary of all UObjects being used and the mod that uses them
        // + Coalesced ini.sections
        Dictionary<string, List<string>> usage = new();
        
        foreach (var mod in Mods)
        {
            if (mod.HasError || mod.IsIni) continue;

            foreach (var file in mod.Files.Values)
            {
                foreach (var obj in file.Objects.Values)
                {
                    if (string.IsNullOrEmpty(obj.Object)) continue;
                    if (file.Type is FileType.Upk)
                    {
                        usage.TryAdd(obj.Object, new List<string>());
                        if (!usage[obj.Object].Contains(mod.Name)) usage[obj.Object].Add(mod.Name);
                    }
                    // SPECIAL! Do combination of ini + section
                    else
                    {
                        foreach (var patch in obj.Patches)
                        {
                            string qualifiedSection = $"{obj.Object}.{patch.Section}";
                            
                            usage.TryAdd(qualifiedSection, new List<string>());
                            if (!usage[qualifiedSection].Contains(mod.Name)) usage[qualifiedSection].Add(mod.Name);
                        }
                    }
                }
            }
        }

        foreach (var entry in usage)
        {
            if (entry.Value.Count < 2) usage.Remove(entry.Key);
        }

        return usage;
    }

    /// <summary>
    /// Returns a list of TOC filepaths according to the passed Game. 
    /// </summary>
    private static List<string> GetTOCs(Game game)
    {
        string folder = $"Payload/{(game is Game.Vote ? "Vote" : "Sword")}Game.app/";

        var tocs = GetLanguages(game)[1..];
        for (int i = 0; i < tocs.Count; i++)
        {
            tocs[i] = $"{folder}IPhoneTOC_{tocs[i]}.txt";
        }
        tocs.Add($"{folder}IPhoneTOC.txt");

        return tocs;
    }

    public static List<string> GetLanguages(Game game)
    {
        // INT will always be the first element.
        List<string> langs = new() { "INT" };

        // VOTE only has INT.
        if (game is not Game.Vote) langs.AddRange(new List<string>
        {
            "BRA", "CHN", "DEU", "DUT", "ESN", "FRA",
            "ITA", "JPN", "KOR", "POR", "RUS", "SWE"
        });
        
        // IB3 has a few additional languages.
        if (game is Game.IB3)langs.AddRange(new List<string>
        {
            "ESM", "IND", "THA"
        });

        return langs;
    }

    private static string GetStatusString(string modName, int? digit = null)
    {
        // Ternary so we can use this method for non-mod strings.
        string status = digit is null ? modName : $"  {digit:00} - {modName}";
        
        if (status.Length > Globals.MaxStrLength)
            status = status[..(Globals.MaxStrLength - 3)];
        
        return $"{status} {new string('.', Globals.MaxStrLength - status.Length - 1)} ";
    }
    
    #endregion

    public void Dispose()
    {
        foreach (var stream in Streams.Values)
        {
            stream.Dispose();
        }

        foreach (var toc in TOCs.Values)
        {
            toc.Dispose();
        }
        
        Directory.Delete(TempDir, true);
        
        GC.SuppressFinalize(this);
    }
}