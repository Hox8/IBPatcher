# Ini Format
The INI format was designed by Niko_KV and shared on the 8th of October, 2022.

Ini mods use a flat structure where ini mod files are comprised of multiple patches. Metadata exists as comments at the start of a file describing the mod's name, game, author, and date.

## Comments
Lines starting with a semicolon ';' are treated as comments and ignored by the patcher.
Trailing comments, where a semi colon appears on the same line after a value, **are not supported**.

```ini
; This is a comment
bDisable = true ; Trailing comments are not supported!
```

## Structure
INI mods follow the standard INI syntax. Additionally, all patch sections must be uniquely-named.

### Ini Patch
|    Key    | Description                                                                                                                                                                                                                                                        | Required |
|:---------:|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:--------:|
|  `File`   | Specifies which file relative to the CookedIPhone folder the patch is modifying.                                                                                                                                                                                   |  `Yes`   |
| `Offset`  | The byte offset within the file where the patch data should be written. Offset value can be either base 10 or 16, followed by any number of optional integer offsets.<ul><li>Hexadecimal offset: `0x56DF27`<li>Integer offset: `5693223 + 44`                      |  `Yes`   |
|  `Type`   | Indicates the [data type](#patch-types) `Value` should be interpreted as.                                                                                                                                                                                          |  `Yes`   |
|  `Value`  | The value, interpreted as `Type`, to paste within `File` at `Offset`.                                                                                                                                                                                              |  `Yes`   |
|  `Size`   | A **deprecated optional** parameter used in conjunction with the `Int32` `Type`. When set to `1`, `Type` is converted from `Int32` to `UInt8`.<br><br>This parameter is kept for backwards compatibility only—newer ini mods should use the `UInt8` type directly. |   `No`   |
| `Enabled` | An **optional** parameter used to tell the patcher to skip over the current patch.<br/><br/>`Enabled` defaults to `True` when not specified.                                                                                                                       |   `No`   |

<br/>

<details>
<summary>Example</summary>

```ini
; *****************************
; Mod's game:       Infinity Blade II
; Mod's name:       Example Mod
; Mod's details:    This mod prevents the game from doing a thing, and reduces the cooldown of another thing.
;
; Mod's author:     Some Author (2023.10.27)
; *****************************

[A patch with a self-explanatory section header]
file     = SwordGame.xxx
offset   = 0x0022BE94
type     = byte
value    = DE AD BE EF

[Another example patch]
file    = 03_P_Forest.xxx
offset  = 0x0022BE94 + 66 + 5
type    = float
value   = 0.5

[One final patch]
file    = 03_P_Forest.xxx
offset  = 50
type    = int32
value   = -500
```

</details>

# JSON Format
The JSON format was created following a need for greater control, such as the ability to patch coalesced files and manipulate UObjects within Unreal packages.

JSON mods currently must be created by hand as there is no GUI creation tool as of yet.
JSON mod creation is possible with text editors like Notepad or Vim, but plugins or applications that offer linting (such as Visual Studio Code) will lead to a much better experience.

## Comments
JSON files do not support comments the same way ini files do. JSON comments are typically written as key/value pairs like so:
```json
{
  "//": "This is a comment!",
  "//": "This is another comment!"
}
```

## Structure
JSON mods follow standard JSON syntax. Keys are not case-sensitive, and whitespace is ignored.

### Hierarchy visualization
```
+ ModBase             - Every JSON mod file has a single ModBase           
  + ModFile           - ModFiles inherit from the root ModBase 
    + ModObject       - ModObjects inherit from their parent ModFile
      + ModPatch      - ModPatches inherit from their parent ModObject
```

### ModBase
ModBase is the root element of the JSON which describes the core data of a mod, including metadata, target game, and required files.

|      Key      | Description                                                                                                                     | Required |   Type   |
|:-------------:|:--------------------------------------------------------------------------------------------------------------------------------|:--------:|:--------:|
|    `Name`     | A string name the mod can use instead of its filename.                                                                          |   `No`   | `String` |
| `Description` | A string providing a brief summary on what the mod does.                                                                        |   `No`   | `String` |
|   `Author`    | A string indicating the author(s) of the mod.                                                                                   |   `No`   | `String` |
|    `Game`     | A string indicating the target game, e.g. `Infinity Blade II` or `IB2`.                                                         |  `Yes`   | `String` |
| `JsonVersion` | An integer representing the JSON syntax version used. Must be less than or equal to what the patcher supports. Defaults to `1`. |  `Yes`   | `String` |
|    `Files`    | An array of `ModFile` files.                                                                                                    |  `Yes`   | `Array`  |

### ModFile
ModFile describes a file within the IPA to be used by the mod.

|    Key    | Description                                                                                                                                                                    | Required |   Type   |
|:---------:|:-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:--------:|:--------:|
|  `File`   | The path of the target file. Path is relative to the CookedIPhone folder.                                                                                                      |  `Yes`   | `String` |
|  `Type`   | Tells the patcher how to interpret `File`, and influences what fields should be used across `ModObject` and `ModPatch`. Current possible values include `UPK` and `Coalesced`. |   `No`   | `String` |
| `Objects` | An array of `ModObject` objects.                                                                                                                                               |  `Yes`   | `Array`  |

### ModObject
For UPK files, ModObject describes the `UObject` within the UPK to target.<br/>
For Coalesced files, ModObject describes the `Ini` within the Coalesced file to target.

|    Key    | Description                                                                                                            | Required |   Type   |
|:---------:|:-----------------------------------------------------------------------------------------------------------------------|:--------:|:--------:|
| `Object`  | <ul><li>`UPK:` The UObject within the UPK file to target.<li>`Coalesced:` The Ini within the Coalesced file to target. |  `Yes`   | `String` |
| `Patches` | An array of `ModPatch` patches.                                                                                        |  `Yes`   | `Array`  |

### ModPatch
Responsible for patching in information, using the parent ModObject and ModFile.<br/>
Some fields are exclusive to UPK/Coalesced files, but most are used by both.

|    Key    | Description                                                                                                                                                                                                               |  Used With  | Required |    Type    |
|:---------:|:--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:-----------:|:--------:|:----------:|
| `Section` | The section to target within the Ini. A section will be created if one is not found.                                                                                                                                      | `Coalesced` |  `Yes`   |  `String`  |
|  `Type`   | Indicates the [data type](#patch-types) `Value` should be interpreted as.                                                                                                                                                 |    `UPK`    |  `Yes`   |  `String`  |
| `Offset`  | An offset to use in conjunction with the UObject. `Offset` should not be used for [replace type values](#patch-types).                                                                                                    |    `UPK`    |  `Yes`   |  `Number`  |
|  `Value`  | <ul><li>`UPK:` The value to write in the file at the start of `Object` + `Offset`<li>`Coalesced:` A `string` or `string[]` to insert under `Section`. See [this page](CoalescedPatching.md) for special formatting rules. |   `Both`    |  `Yes`   | `Variable` |
| `Enabled` | An **optional** parameter used to tell the patcher to skip applying the current patch.<br/><br/>Defaults to `True` when not specified.                                                                                    |   `Both`    |   `No`   | `Boolean`  |

<br/>

<details>
<summary>Example</summary>

```json
{
  "name": "Example Mod",
  "description": "Forces the fast forward button to appear for all cinematic sequences and removes its 1s delayed start",
  "game": "Infinity Blade II",
  "author": "Some Author",
  "files": [
    {
      "file": "SwordGame.xxx",
      "type": "UPK",
      "objects": [
        {
          "object": "SwordPC.OnStartCinematicMode",
          "patches": [
            {
              "//": "Globally force the fast forward button to appear",
              "type": "byte",
              "offset": 72,
              "value": "28 16 00 73 09 00 00 00 73 09 00 00 00 73 09 00 00 0B 0B 0B"
            }
          ]
        },
        {
          "object": "SwordHudBase.OnMobileInputZoneAdded",
          "patches": [
            {
              "//": "Modifies the delay (in seconds) before fast forward button appears. Disabled by default",
              "type": "float",
              "offset": 334,
              "value": 0,
              "enabled": false
            }
          ]
        }
      ]
    },
    {
      "file": "Coalesced_INT.bin",
      "type": "Coalesced",
      "objects": [
        {
          "object": "SwordGame/Config/IPhone-SwordEngine.ini",
          "patches": [
            {
              "//": "Adds a single value to the SwordGame.SwordBasePC section within IPhone-SwordEngine.ini",
              "section": "SwordGame.SwordBasePC",
              "value": "bCanBuyGoldFromStore=false"
            },
            {
              "//": "Clears the SystemSettingsIPhone3GS section and adds a few lines to it",
              "section": "!SystemSettingsIPhone3GS",
              "value": [
                "BasedOn=SystemSettingsMobile",
                "MobileContentScaleFactor=2.0",
                "DynamicShadows=False"
              ]
            }
          ]
        }
      ]
    }
  ]
}
```
</details>

# Patch Types
Here is a table describing each of the available patch types used by both INI and JSON mods.

|   Type    | Description                                                                                                                                                 | Number of bytes | Expected value type |
|:---------:|:------------------------------------------------------------------------------------------------------------------------------------------------------------|:---------------:|:-------------------:|
|  `Bool`   | A standard Boolean. `True` is mapped to `0x01`, and `False` is mapped to `0x00`.                                                                            |       `1`       |      `Boolean`      |
|  `UBool`  | An UnrealScript Boolean. `True` is mapped to `0x27` and `False` is mapped to `0x28`.                                                                        |       `1`       |      `Boolean`      |
|  `UInt8`  | An unsigned byte. Value must range from `0` to `255`.                                                                                                       |       `1`       |      `Number`       |
|  `Int32`  | A signed integer. Value must range from `-2147483648` to `2147483647`.                                                                                      |       `4`       |      `Number`       |
|  `Float`  | A standard float. Value must range from `-3.4e38` to `3.4e38`.                                                                                              |       `4`       |      `Decimal`      |
| `String`  | An ANSI-only string which doesn't append a null terminator.                                                                                                 |   `Variable`    |      `String`       |
|  `Byte`   | A hexadecimal string with no hex suffixes. Case-insensitive and whitespace tolerant. For decimal byte values, use the `UInt8` type.                         |   `Variable`    |      `String`       |
| `Replace` | A special type available to JSON mods. Identical to the `Byte` type, but instructs the patcher to replace the UObject's data entirely with that of `Value`. |   `Variable`    |      `String`       |

# Miscellaneous
### Bin mods
Bin mods are Coalesced files exported from the Coalesced Editor, and offer mod creators a way
to quickly test Coalesced edits without needing to create a JSON mod.

As bin mods are file replacements, multiple bin mods cannot be combined. Therefore, mod creaters should prefer to distribute
Coalesced edits in the JSON format so they can be combined with other Coalesced mods.

### Commands mod
IBPatcher looks for a file named 'Commands.txt', which applies ini tweaks at runtime.
The Google Drive folder has a version tailored to each game which is fully documented and configurable.

### TOC patching
The patcher will automatically recalculate the `TOC` files when it detects a file has changed size.

`TOC` stands for `Table of Contents`, and it is a manifest telling the game what sizes it should expect its files to be.
If one of the script packages changes length, the game will notice and exit on startup.

`TOC` files consist of a single master file, and one for each language the game supports.
When the patcher recalculates the `TOC` files, it merges all of the locale `TOCs` into the master.
This way, the master `TOC` file will contain all the necessary information and the others can be safely deleted, or kept empty.
