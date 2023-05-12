# INI format
The .INI format was designed by Niko_KV and released on the 8th of October, 2022. It is a flat structure where multiple patches belong to a single mod file. Metadata exists as comments at the start of a file describing the Mod's name, game, author, and date. It has been superseded by the .JSON format, but remains available for backwards compability and convenience for modders.

## Ini Patch structure
Key         | Explanation
:---------: | ------------
[Name]      | Keyless section header denoting the start of a new patch. Enclosed in square brackets as per the ini standard
File        | A string referencing the file to be patched in the 'Payload/{Game}.app/CookedIPhone/' directory
Offset      | An integer (int or hex) marking where in the file to write 'Value'. Can be followed by additional int offsets<br><br><ul> <li> Int: 3567575 </li> <li> Hex: 0x003f6da </li> <li> Int + int:&nbsp;3567575 + 02 </li> <li> Hex + int:&nbsp;0x003f6da + 125</li> </ul>
Type        | A string influencing how the 'Value' key is interpreted. Types: 'Byte', 'Boolean', 'Integer', 'Float', 'String'
Value       | The patch's value, whose type is determined by the 'Type' key. 'Byte' expects hexadecimal without prefix
Size*       | An optional integer specifying the byte size of an Integer-type value. 1: UInt8, 4: Int32
Original*   | This optional key is used to show the original value prior to patching, and is ignored by the patcher
Enabled*    | An optional boolean which tells the patcher to ignore the patch if set to false. Defaults to true |

<br>

## ExampleMod.ini
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

# JSON format
The .JSON format was designed by myself and follows a hierarchical structure as shown below. It offers several features over the .ini format, such as cross-version compatibility, coalesced patches, and name/object lookups. These mods must be created by hand as there is no GUI creation tool as of yet (I recommend Visual Studio Code for its syntax highlighting and linting. Notepad++ is a close second)
 
 - Root
    - Files
      - Objects
        - Patches
      - Inis
        - Sections

<br> The following tables lists all the various structures of a JSON mod. Properties ending with '\*' are optional and do not need to be passed. JSON does not support comments, so any property beginning with '//' will be treated as a comment and ignored.<br>

## Root
Property       | Type     | Explanation
:------------: | :------: | ------------
Name*          | String   | The internal name of the mod. If the mod cannot be read, or if this value is null, the filename is used instead
Description*   | String   | A summary describing what the mod does
Author*        | String   | The name of the mod's creator
Version*       | String   | The version of the mod, if applicable. Defaults to v1.0.0
Date*          | String   | The date the mod was published
Game           | String   | The game the mod is intended for. If the game does not equal the loaded ipa, the mod is skipped
Files          | File[]   | An array of files this mod is applying patches to |

<br>Root can contain any number of File objects in its 'Files' array. Each File instructs the patcher to find a certain file within the game's files, as well as how to interpret it<br>

## File
Property    | Type       | Explanation
:---------: | :--------: | -----------
fileName    | String     | The name of the file to be patched. All files must be within the CookedIPhone folder
fileType    | String     | The type of file to be patched. Types include: "UPK" and "Coalesced"
Objects*    | Object[]   | The array of Object objects -- if filetype is "UPK"
Inis*       | Ini[]      | The array of Ini objects -- if filetype is "Coalesced" |

<br>

## Object
Property     | Type      | Explanation
:----------: | :-------: | -----------
objectName   | String    | The name of a UObject within an Unreal package
Patches      | Patch[]   | The array of Patch objects belonging to this Object

### Patch
Property    | Type       | Explanation
:---------: | :--------: | -----------
Type        | String     | The data type to interpret 'Value' as. Availble types include: 'Bytes', 'Boolean', 'UInt8', 'Int32', 'Float', 'String'
Offset      | Integer    | Determines the offset from the start of the parent object to write this patch's value
Value       | Variable   | The data to be patched to the file. Number types can be passed by number format or string
Size*       | Integer    | Specifies the maximum length for a string patch. String values shorter than 'Size' will be padded with '0B' tokens. A negative 'Size' value will force unicode encoding
Enabled*    | Boolean    | If set to false, the patch will be ignored. Defaults to true |

<br>

## Ini
Property    | Type        | Explanation
:---------: | :---------: | -----------
iniPath     | String      | The path of the ini as it appears inside a coalesced file, e.g., '..\\..\\SwordGame\\Config\\SwordItems.ini'
Sections    | Section[]   | The array of Section objects to apply to this ini file
Mode*       | String      | Determines how the ini file should be handled: "Delete" deletes the ini file, "Overwrite" wipes the file before writing and "Append" adds to the file as-is. Defaults to "Append"
Enabled*    | Boolean     | If set to false, the ini and all its sections will be ignored. Defaults to true |

### Section
Property      | Type       | Explanation
:-----------: | :--------: | -----------
sectionName   | String     | The section in the ini file to write 'Properties' under
Properties    | String[]   | The array of Key/Value pairs to place under 'sectionName' in the file
Mode*         | String     | Determines how the section should be handled: "Delete" deletes the section and all its properties, "Overwrite" clears the section's existing properties before writing and "Append" adds to the section as-is. Defaults to "Append"
Enabled*      | Boolean    | If set to false, the section will be ignored. Defaults to true |

<br>

# Name and Object references
Starting with v1.2, name and object references can be used inside of byte type patches. Name and object references allow modders to specify an objects or names by name over its compiled bytecode. This makes modding easier since we can use references without finding its internal indicies, and it also allows for cross-compatibility across different versions, as every version stores its names and objects in different positions.

## Object references
An object reference is a string contained within square brackets, e.g. '[PlayerPawn]'. The patcher will attempt to find the object (in this case "PlayerPawn") and convert it into the equivalent bytecode for the current package.

## Name references
A name reference is a string contained within curly braces, e.g. '{Sword}'. Name references can also have name _instances_ which we can take advantage of. For example, if we wanted to reference 'Sword_26' (the Infinity Blade), we can do this by specifying its name, followed by a comma and the instance number. For example, '{Sword,26}'.

### Examples of object and name reference usage inside 'BYTE' values:
- "value": "1B {LoadStartNewBloodline} 26 16 01 [UnlockedNewGamePlus] 01 [UnlockedNewGamePlus] 0B 0B 0B"<br>
- "value": "{FlashItemWon}"<br>
- "value": "{Sword,139}"<br>

<br>

# Bin mods
Bin mods are simple file replacements. If the patcher finds a file with a '.bin' extension, it will add it to the target IPA or output folder. .Bin mods are used to copy *pre-patched* coalesced.bin files into the target destination, but with the addition of .JSON mods in v1.2, the need for bin mods has greatly diminished. They are kept only for backwards compatibility and modders' convenience.

<br>

# Command mods
A command "mod" is also a simple file replacement. Unreal Engine 3 games can take advantage of a file which runs console commands at startup which can be used to control various settings like the fps cap, the state of certain graphics features, configurable options and so on. The patcher is designed to look out for a file named 'Commands.txt' in the mods folder, and if it is found, it will be copied to the target IPA or output folder.<br>

'Commands.txt' needs one other file in order to function, 'UE3CommandLine.txt'. This file instructs the game to execute the contents of 'Commands.txt' into the console at startup. If this file is not present in the mods folder, it will be generated automatically by the patcher.
