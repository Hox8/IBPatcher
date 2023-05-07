# Mod format documentation

Brief overview of what this document's purpose is, how its data is structured
<br> Something stating keys ending in '&#42;' are optional

## INI format
The .INI format was designed by Niko_KV and released on the 8th of October, 2022. It is a flat structure where multiple patches belong to a single mod file. Metadata exists as comments at the start of a file describing the Mod's name, game, author, and date.

### Ini Patch
| Key       | Explanation   |
| :---------: | :------------ |
| [Name]      | Keyless section header denoting the start of a new patch. Enclosed in square brackets as per the ini standard |
| File      | A string referencing the file to be patched in the 'Payload/{Game}.app/CookedIPhone/' directory |
| Offset    | An integer (int or hex) marking where in the file to write 'Value'. Can be followed by additional int offsets<br><br><ul> <li> Int: 3567575 </li> <li> Hex: 0x003f6da </li> <li> Int + int:&nbsp;3567575 + 02 </li> <li> Hex + int:&nbsp;0x003f6da + 125</li> </ul>
| Type      | A string influencing how the 'Value' key is interpreted. Types: 'Byte', 'Boolean', 'Integer', 'Float', 'String' |
| Value     | The patch's value, whose type is determined by the 'Type' key. 'Byte' expects hexadecimal without prefix |
| Size&#42;      | An optional integer specifying the byte size of an Integer-type value. 1: UInt8, 4: Int32 |
| Original&#42;  | This optional key is used to show the original value prior to patching, and is ignored by the patcher |
| Enabled&#42;   | An optional boolean which tells the patcher to ignore the patch if set to false. Defaults to true |

<br>

### ExampleMod.ini
> [BytePatch]<br>
> file    = SwordGame.xxx<br>
> offset  = 0x00263473 + 9281<br>
> type    = BYTE<br>
> value   = 0B 0B C0

>[FloatPatch]<br>
>file     = SwordGame.xxx<br>
>offset   = 0x00263473<br>
>type     = FLOAT<br>
>value    = 0.0625<br>
>original = 3.0<br>
>enabled  = false
<br>

## JSON format
The .JSON format was designed by myself and follows a hierarchical structure as shown below. Additional space filler is needed, what to fill it with...<br>
 
 - Root
    - Files
      - Objects
        - Patches
      - Inis
        - Sections

<br>Every JSON mod has a Root object which contains metadata and a list of files being modified.<br>
### Root
Parameter     | Explanation
------------- | -------------
Name*         | The internal name of the mod. If the mod cannot be read, or if this value is null, the filename is used instead
Description*  | A summary describing what the mod does
Author*       | The name of the mod's creator
Version*      | The version of the mod, if applicable. Defaults to v1.0.0
Date*         | The date the mod was published
Game          | The game the mod is intended for. If the game does not equal the loaded ipa, the mod is skipped
Files         | An array of files this mod is applying patches to |

<br>

### File
Parameter     | Explanation
------------- | -------------
Filename      | The name of the file to be patched in the 'Payload/{Game}.app/CookedIPhone/' directory
Filetype      | The type of file to be patched. Types include: "UPK" and "Coalesced"
Objects*      | Contains the array of objects each containing patches. Only if filetype is "UPK"
Inis*         | Contains the array of inis each containing sections. Only if filetype is "Coalesced" |

<br>

## Bin mods
Placeholder

## Command mods
Placeholder
