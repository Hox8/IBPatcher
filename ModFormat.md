# INCOMPLETE

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

## Name and Object references
Starting with v1.2, name and object references can be used inside of 'BYTE' datatypes. These allow mods to specify names and objects exactly rather than hardcoding byte values, which is useful not only for making modding easier, but enabling compatibility across versions where the indexes of names and objects change.

### Object references
Denoted via square bracket encapsulation, e.g. [PlayerPawn]. Object references are looked up in the table of the current package and converted from an int32 index into a byte array

### Name references
Denoted via curly brace encapsulation, e.g. {Sword}. Name references have two parts: The index to the name table, and its instance number (defaults to 0).<br>
Name instances can be specified in a name reference by using a comma delimiter ( , ) and an integer.<br><br>

As an example, equipment items are defined by their type and an instance number. A name reference equivalent to the Infinity Blade (Sword_26) would look like: {Sword,26}. Name references are looked up in the name table of the current package and converted from the int32 index and its int32 instance number (default 0) to a byte array.

### Examples of object and name reference usage inside 'BYTE' values:
- "value": "1B {LoadStartNewBloodline} 26 16 01 [UnlockedNewGamePlus] 01 [UnlockedNewGamePlus] 0B 0B 0B 0B 0B 0B 0B 0B 0B 0B 0B 0B"<br>
- "value": "{FlashItemWon}"<br>
- "value": ""{Sword,139}"<br>

## Bin mods
Bin mods were used to swap vanilla coalesced.bin mods for pre-modded ones. This functionality is no longer required since the addition of the .JSON format, but has been left included in the latest version of IBPatcher for convenience's sake. Json mods should be preferred when releasing public mods due to their interoperability with other coalesced mods.

## Command mods
Command "mods" are used to copy a commands txt file and optionally a UE3CommandLine.txt file to the target destination. If a UE3CommandLine.txt is not provided, it is generated during patching.<br><br>
The commands text file is used to enter console commands to Infinity Blade on startup. Setting the FPS cap to 60, disabling shadows, and removing external links from the options menu are examples of what the commands "mod" can do.
