# What are Coalesced / ini files? (background knowledge)

### Ini file
Ini files store settings and strings in the form of sections and key/value pairs.
Read [the article on Wikipedia](https://en.wikipedia.org/wiki/INI_file) for a thorough explanation on ini files.

Infinity Blade uses ini files for the following:
- Expose UnrealScript variables
- Graphics settings
- UI elements, including UVs, texture pages and more
- Localized strings
- Boss orders, item stats
- And many more

### Coalesced file
Coalesced files are basic containers used by Unreal Engine 3 to group ini files together under a single file, similar to a zip archive.
These files are often encrypted to protect its contents from being tampered with, as is the case with Infinity Blade II and newer.

A coalesced file exists for each language supported by the game, with each storing a copy of the game settings and the localized strings relevant to the language.
Unfortunately this means every coalesced file stores an identical copy of the game settings, which makes it not only needlessly larger, but also introduces additional challenges for modders.

# Mod stuff

### INI section clearing
JSON mods can clear ini sections of their properties when accompanied with a '!' prefix.
This can be useful for X or Y, and is used by the graphics mod to do Z.

```json
{
    "section": "!SomeSection",
    "value": "BasedOn=SystemSettings"
}
```

### Coalesced_ALL (rename this). Also all written text is garbage rewrite that too
As explained under PUT SECTION HERE, all coalesced files store identical copies of the game settings.
What this means for modders, is, when patching a Coalesced, it will only affect a single coalesced file, and depending on the player's language, they may not end up playing with the tweaks.
Rather than specifying the same patches for multiple files, 'Coalesced_ALL' is a macro that automatically copies the coalesced changes over to all other files supported by the game.

You should be using this unless changing

Just realized this makes the old way completely redundant. Just need to filter out locale files and we're golden. BIG TODO

This will not do X Y Z
```json
{
    "file": "Coalesced_INT.bin",
    "type": "Coalesced",
    "objects": [
        ...
    ]
}
```

This will do X Y Z
```json
{
    "file": "Coalesced_ALL.bin
    "type": "Coalesced",
    "objects": [
        ...
    ]
}
```

### INI property parsing (put this on the end since it's huge)
what how why

Dropdown-able examples!
Stub. Don't forget to mention adding `!` before a section name to clear all its properties.

Some string giving a brief overview of what these are and why they shouldbe used. Don't forget to mention defaulting to `.` if not specified. 

|       Name       | Operator | Description                                                                                                                            |     Example      |
|:----------------:|:--------:|:---------------------------------------------------------------------------------------------------------------------------------------|:----------------:|
|      Empty       |   `!`    | Removes all instances of the key. Any value after `=` is ignored.<br/><br/>Suitable for emptying arrays.                               |   `!MyArray=`    |
|      Remove      |   `-`    | Remove the matching key/value pair.<br/><br/>Suitable for removing array entries or individual keys.                                   | `-MyArray=Value` |
|      Append      |   `+`    | Adds the key/value pair if it doesn't already exist.                                                                                   | `+MyArray=Value` |
| Append Duplicate |   `.`    | Append the value to the array, even if the value already exists in the array.                                                          | `.MyArray=Value` |

Example used within a JSON mod:

What you need to do is make some coalesced mods yourself and figure which are useful and which aren't.
I'm really struggling to see how '+' is going to be useful...

```json
{
    "section": "Armor_100 SwordInventoryItem",
    "value": [
        "!Socket=",     // Clears the Socket[] array
        "",
    ]
}
```