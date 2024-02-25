# Understanding Coalesced and INI files

## INI file
INI files are configuration files used to store settings and strings, which are organized into sections and key/value pairs.
Refer to [this Wikipedia article](https://en.wikipedia.org/wiki/INI_file) for a comprehensive overview of INI files.

### Arrays
INI files used by Unreal Engine 3 support `arrays` which can use either `implicit` or `explicit` indexing:
```ini
; The below examples are functionally equivalent 

; Implicit array indexing
MyArray=1
MyArray=2

; Explicit array indexing
MyArray[0]=1
MyArray[1]=2
```

### Objects
`Objects` are another type commonly used in Unreal Engine 3 INI files, and represent UnrealScript types in string format:
```ini
MyColor=(B=255, G=255, R=255, A=255)
```

## Coalesced file
Coalesced are used by Unreal Engine 3 and act as containers to group INI files together under a single file, similar to a zip archive.
These files are often encrypted to protect its contents from being tampered with, as is the case with Infinity Blade II and newer.

A coalesced file exists for every language supported by the game, with each storing a copy of the game settings its respective locale.
Unfortunately, this means every coalesced file stores an identical copy of the game settings, which introduces extra hurdles for modders.

# Mod format features

## INI section clearing
Starting with `v1.3.0`, JSON mods can clear INI sections of their properties by specifying a `!` prefix in the section's name.
This can be useful for when you want to remove all original properties, and is used by the graphics mods to reset every graphics preset.

<details open>
<summary>Example</summary>

<br/>In the following example, the JSON patch removes all properties from [SystemSettingsIPhone3GS] and adds two new values:

#### INI section pre-patch:
```ini
[SystemSettingsIPhone3GS]
BasedOn=SystemSettingsMobileTextureBias
LensFlares=False
DetailMode=0
MobileEnableMSAA=True
MobileMaxMemory=100
MemoryDetailMode=0
bMobileUsingHighResolutionTiming=False
MobileLandscapeLodBias=2
StatFontScaleFactor=1.8
ParticleLODBias=1
...
```

#### The JSON mod patch to apply:
```json
{
    "section": "!SystemSettingsIPhone3GS",
    "value": [ "BasedOn=SystemSettings", "MobileContentScaleFactor=2.2" ]
}
```

#### INI section post-patch:
```ini
[SystemSettingsIPhone3GS]
BasedOn=SystemSettings
MobileContentScaleFactor=2.2
```

</details>

## Global Coalesced patches
Starting with `v1.3.0`, JSON mods can be made to automatically target all Coalesced files in the game installation.
This allows modders to write out a single set of changes that can be replicated across all Coalesced files, allowing players to experience the mod regardless of their game language.

To take advantage of this, mod files must target `Coalesced_ALL`. The patcher will recognize this as a template and copy it to every Coalesced file.

INI files are copied unconditionally, but localization files are filtered to the Coalesced of the same language. See the example below for more details.

<!-- Say something about how this renders the old approach mostly obsolete? -->

<details open>
<summary>Example</summary>

The following example mod targets all Coalesced files globally and specifies three INIs to edit.
- `SwordGame/Config/IPhone-SwordGame.ini` is copied to every Coalesced file since it is not language-specific.
- `SwordGame/Localization/INT/SwordGame.int` is copied to `Coalesced_INT.bin`, since it is a locale file targeting American English—`INT`.
- `SwordGame/Localization/DEU/SwordGame.deu` is copied to `Coalesced_DEU.bin`, since it is a locale file targeting German—`DEU`.

```json
{
    "file": "Coalesced_ALL",
    "type": "Coalesced",
    "objects": [
        {
            "object": "SwordGame/Config/IPhone-SwordGame.ini",
            "patches":
        },
        {
            "object": "SwordGame/Localization/INT/SwordGame.int",
            "patches":
        },
        {
            "object": "SwordGame/Localization/DEU/SwordGame.deu",
            "patches":
        }
    ]
}
```

</details>

## INI property parsing

Starting with `v1.3.0`, values within Coalesced patches accept prefixes to modify their behavior.
These allow modders additional control in how properties are applied to INI sections.

> [!NOTE]
> Values without a prefix default to using the `.` operator, which matches the functionality prior to `v1.3.0`.

<br/>

|                 Name                 | Operator | Description                                                                   | Syntax Example |
|:------------------------------------:|:--------:|:------------------------------------------------------------------------------|:--------------:|
|               `Empty`                |   `!`    | Removes all instances of the key. Values after `=` are ignored.               |  `!MyArray=`   |
|               `Remove`               |   `-`    | Remove all matching key/value pairs from the section.                         |  `-MyArray=5`  |
|        `Append (Conditional)`        |   `+`    | Adds the key/value pair to the section, but only if it doesn't already exist. |  `+MyArray=5`  |
|       `Append (Unconditional)`       |   `.`    | Adds the key/value pair to the section unconditionally.                       |  `.MyArray=5`  |

## Usage examples

<!-- Empty -->

<details>
<summary>Empty</summary>

#### INI section pre-patch:
```ini
[SwordGame.SwordPlayer]
SpecialMaxGemList=Gem1_1
SpecialMaxGemList=Gem1_2
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
```

#### JSON patch:
```json
{
    "section": "SwordGame.SwordPlayer",
    "value": [
        "!ConstantStoreGemList=",
        ".ConstantStoreGemList=Gem2_1"
    ]
}
```

#### INI section post-patch:
```ini
[SwordGame.SwordPlayer]
SpecialMaxGemList=Gem1_1
SpecialMaxGemList=Gem1_2
ConstantStoreGemList=Gem2_1
```

</details>

<!-- Remove -->

<details>
<summary>Remove</summary>

#### INI section pre-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
```

#### JSON patch:
```json
{
    "section": "SwordGame.SwordPlayer",
    "value": [
        "-ConstantStoreGemList=Gem3_1",
        "-ConstantStoreGemList=Gem3_3"
    ]
}
```

#### INI section post-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_2
```

</details>

<!-- Append cond. -->

<details>
<summary>Append (Conditional)</summary>

<!-- <br/> I'm really not sure what this one would be useful for. -->

#### INI section pre-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
```

#### JSON patch:
```json
{
    "section": "SwordGame.SwordPlayer",
    "value": [
        "+ConstantStoreGemList=Gem3_3",
        "+ConstantStoreGemList=Gem3_4"
    ]
}
```

#### INI section post-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
ConstantStoreGemList=Gem3_4
```

</details>

<!-- Append uncond. -->

<details>
<summary>Append (Unconditional)</summary>

#### INI section pre-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
```

#### JSON patch:
```jsonc
{
    "section": "SwordGame.SwordPlayer",
    "value": [
        ".ConstantStoreGemList=Gem3_2",
        "ConstantStoreGemList=Gem3_2"   // Values without an operator prefix default to Append Unconditional
    ]
}
```

#### INI section post-patch:
```ini
[SwordGame.SwordPlayer]
ConstantStoreGemList=Gem3_1
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_3
ConstantStoreGemList=Gem3_2
ConstantStoreGemList=Gem3_2
```

</details>
