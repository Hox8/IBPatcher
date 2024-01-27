![IBPatcher banner](https://user-images.githubusercontent.com/125164507/236659387-e1ac1787-c639-4c6d-bf2a-3090b3a6dd68.png)

# IBPatcher
IBPatcher is a modding utility designed to apply community-made mods to the Infinity Blade series on iOS.

<p align="center">
   <img src="https://user-images.githubusercontent.com/125164507/236659991-b90a322b-eeff-4a46-9915-5f62ca9cc2c8.png" width=1024 alt="IBPatcher in use">
</p>

## Features
- Automatically extracts, modifies, and packages game files—no manual input required.
- Supports the latest versions of _Infinity Blade I_, _II_, _III_, and _VOTE: The Game_.
- Support for compression, encryption, and parsing of Unreal UPK and Coalesced files.
- Implements its own [mod formats](docs/ModFormat.md).

## Install
Download a copy of IBPatcher from the [releases tab](https://github.com/Hox8/IBPatcher/releases), extract to your computer, and run.<br/>
IBPatcher is fully self-contained and doesn't depend on any preinstalled libraries or frameworks.

## Usage
### Video guide
https://www.youtube.com/watch?v=iBrwtwaMYkk

### Written guide
1. Download and extract the latest version of IBPatcher for your device from the [releases tab](https://github.com/Hox8/IBPatcher/releases). Consider placing it in its own directory as it generates folders for each game.

2. Download a copy of Infinity Blade to use with the patcher. These can be downloaded as IPAs from the [modding server](https://discord.gg/DjpqJHvcJY) or [Archive.org](https://archive.org/details/software?query=Infinity+Blade).

3. Seek out mods from our [shared Google Drive archive](https://drive.google.com/drive/folders/1796Y97dCVlQMZpSiXQ1xh4ejHlNv50VO), and place them in the mod folders which are generated after running the patcher for the first time.

4. Run the patcher and follow the on-screen instructions. By default, a new IPA will be created in the same folder as the IPA which was loaded.

5. If you’d prefer the patcher to output loose files instead, you can do that! Create a folder named ‘Output’ within IBPatcher’s directory and all changed files will be saved to the directory.

## Credits
- The team at ChAIR for creating Infinity Blade.
- Epic Games for Unreal Engine.
- Niko_KV for kick-starting the Infinity Blade modding scene, and whose work inspired the creation of this tool.
- The Infinity Blade community:
   - [Reddit](https://www.reddit.com/r/infinityblade/)
   - [Discord](https://discord.gg/S7jCh9N)
