# Dark Souls Lua Studio 1.2

Windows x64 editor for Dark Souls: Prepare to Die Edition and Dark Souls III AI scripts, goal tables, global names and archives.

## Changes

- DS3 mode with Lua 5.0 64-bit output, BND4/DCX archive support, and format-preserving goal/global table editing.
- Game-specific autocomplete and highlighting, including 10,480 DS3 identifiers.
- Goal links and F12 navigation across scripts and archives.
- Function drafts with previews, safe renaming, insertion and persistent storage.
- Add drafts and custom templates manually with names, notes and editable Lua source.
- Six starter templates for attacks, combos, enemies and bosses.
- Function folding and improved autocomplete behavior.

## Installation

Extract the Windows x64 ZIP and run `DS1LuaStudio.exe`. Keep all files and the `LuaNative` folder together.

The framework-dependent package requires the .NET 8 Desktop Runtime (x64).

Back up your game files before editing. Compilation and file round trips have been verified; in-game behavior has not been tested. Two supplied DS3 scripts produce decompiler warnings; details are in the README.

Includes the upstream license files. No original game archives or optional proprietary Havok Script compiler are included.
