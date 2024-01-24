using System;
using System.Globalization;
using UnrealLib.Config;

namespace IBPatcher.Mod;

public static class IniMod
{
    public static ModBase Read(string modPath, ModContext ctx)
    {
        var mod = new ModBase(modPath, ModFormat.Ini, ctx.Game);
        var ini = Ini.FromFile(modPath);

        // Error if the ini contains duplicate sections or no sections at all
        if (ini.Sections.Count == 0)
        {
            mod.SetError(ModError.Ini_HasNoSections);
        }
        else if (ini.ErrorType is IniError.ContainsDuplicateSection)
        {
            mod.SetError(ModError.Ini_HasDuplicateSections, ini.ErrorContext);
        }
        else
        {
            foreach (var section in ini.Sections)
            {
                #region Parse File

                if (!section.GetValue("File", out string fileStr) || string.IsNullOrWhiteSpace(fileStr))
                {
                    mod.SetError(ModError.Generic_UnspecifiedFile, section.Name);
                    break;
                }

                ModFile file = mod.GetFile(fileStr, ctx.QualifyPath(fileStr), FileType.Upk);
                if (file.Objects.Count == 0)
                {
                    file.Objects.Add(new ModObject(""));
                }

                #endregion

                #region Parse Type

                if (!section.GetValue("type", out string type))
                {
                    mod.SetError(ModError.Generic_UnspecifiedPatchType, section.Name);
                    break;
                }

                var patch = new ModPatch { Type = EnumConverters.GetPatchType(type) };
                if (patch.Type == PatchType.Unspecified)
                {
                    mod.SetError(ModError.Generic_BadPatchType, section.Name);
                    break;
                }

                #endregion

                #region Parse Offset

                if (section.GetValue("offset", out string offset))
                {
                    string[] sub = offset.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Parse primary offset.
                    // This can be in either base 10 or base 16.
                    if (sub.Length == 0 || !int.TryParse(sub[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ?
                        sub[0][2..] : sub[0], NumberStyles.AllowHexSpecifier, null, out int result))
                    {
                        mod.SetError(ModError.Generic_BadOffset, section.Name);
                        break;
                    }

                    // Parse tertiary offsets.
                    // These can only be in base 10.
                    for (int i = 1; i < sub.Length; i++)
                    {
                        if (!int.TryParse(sub[i], null, out int tertiary))
                        {
                            mod.SetError(ModError.Generic_BadOffset, section.Name);
                            break;
                        }

                        result += tertiary;
                    }

                    // If we broke out of the tertiary loop, break out of this one too
                    if (mod.HasError) break;

                    patch.Offset = result;
                }
                else
                {
                    mod.SetError(ModError.Generic_UnspecifiedOffset, section.Name);
                    break;
                }

                #endregion

                #region Parse Size

                if (section.GetValue("size", out string size))
                {
                    if (patch.Type is not PatchType.Int32)
                    {
                        mod.SetError(ModError.Ini_UnexpectedSize, section.Name);
                        break;
                    }

                    if (!int.TryParse(size, out int result) || (result != 1 && result != 4))
                    {
                        mod.SetError(ModError.Ini_BadSize, section.Name);
                        break;
                    }

                    // Size has been kept for backwards compatibility with original Niko mods.
                    // We don't keep this variable; instead we infer PatchType from it
                    if (result == 1)
                    {
                        patch.Type = PatchType.UInt8;
                    }
                }

                #endregion

                #region Parse Value

                // We want to parse the value immediately so we can reference the section name for errors
                if (!section.GetValue("value", out string value))
                {
                    mod.SetError(ModError.Generic_UnspecifiedValue, section.Name);
                    break;
                }

                if (!patch.TryParseValue(value))
                {
                    mod.SetError(ModError.Generic_BadValue, section.Name);
                    break;
                }

                #endregion

                #region Parse Enable

                if (section.GetValue("enable", out string enable))
                {
                    if (!bool.TryParse(enable, out patch.Enabled))
                    {
                        mod.SetError(ModError.Generic_BadEnabled, section.Name);
                        break;
                    }
                }

                #endregion

                file.Objects[0].Patches.Add(patch);
            }
        }

        return mod;
    }
}
