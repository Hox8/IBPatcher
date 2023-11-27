using System.Globalization;
using UnrealLib.Config;

namespace IBPatcher.Mod;

public static class IniMod
{
    public static ModBase ReadIniMod(string modPath, ModContext ctx)
    {
        var mod = new ModBase(modPath, ModFormat.Ini, ctx.Game);
        var ini = new Ini(modPath);

        if (ini.HasDuplicateSections)
        {
            mod.SetError(ModError.DuplicateSection);
            mod.ErrorContext = ini.Context;
        }
        else
        {
            foreach (var section in ini.Sections)
            {
                #region Parse File

                if (!section.GetValue("File", out string fileStr))
                {
                    mod.SetError(ModError.UnspecifiedFile, section);
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
                    mod.SetError(ModError.UnspecifiedType, section);
                    break;
                }

                var patch = new ModPatch { Type = EnumConverters.GetPatchType(type) };
                if (patch.Type == PatchType.Unspecified)
                {
                    mod.SetError(ModError.InvalidType, section);
                    break;
                }

                #endregion

                #region Parse Offset

                if (section.GetValue("offset", out string offset))
                {
                    string[] sub = offset.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Parse primary offset.
                    // This can be in either base 10 or base 16.
                    if (!int.TryParse(sub[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ?
                        sub[0][2..] : sub[0], NumberStyles.AllowHexSpecifier, null, out int result))
                    {
                        mod.SetError(ModError.InvalidOffsetPrimary, section);
                        break;
                    }

                    // Parse tertiary offsets.
                    // These can only be in base 10.
                    for (int i = 1; i < sub.Length; i++)
                    {
                        if (!int.TryParse(sub[i], null, out int tertiary))
                        {
                            mod.SetError(ModError.InvalidOffsetTertiary, section);
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
                    mod.SetError(ModError.UnspecifiedOffset, section);
                    break;
                }

                #endregion

                #region Parse Size

                if (section.GetValue("size", out string size))
                {
                    if (patch.Type is not PatchType.Int32)
                    {
                        mod.SetError(ModError.InappropriateSize, section);
                        break;
                    }

                    if (!int.TryParse(size, out int result))
                    {
                        mod.SetError(ModError.InvalidSize, section);
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
                    mod.SetError(ModError.UnspecifiedValue, section);
                    break;
                }

                if (!patch.TryParseValue(value))
                {
                    mod.SetError(ModError.InvalidValue, section);
                    break;
                }

                #endregion

                #region Parse Enable

                if (section.GetValue("enable", out string enable))
                {
                    if (!bool.TryParse(enable, out patch.Enabled))
                    {
                        mod.SetError(ModError.InvalidEnabled, section);
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
