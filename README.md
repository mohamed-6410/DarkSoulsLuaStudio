# Dark Souls Lua Studio (DS1 / DS3)

A desktop editor for **Dark Souls: Prepare to Die Edition** and **Dark Souls III** AI data — Lua scripts, `.luainfo`
goal tables, `.luagnl` global names, and their `.luabnd` / `.luabnd.dcx` archives.

## DS3 mode

Select **DS3** in the toolbar's **Game** selector. The selection is remembered. New scripts compile
with a **64-bit size_t**, new goal/global tables use **64-bit offsets and UTF-16**. DS1 creates
32-bit goal/global tables and defaults to 32-bit Lua output. Existing bytecode always preserves its
own width, including when switching modes; existing tables retain their original format.

The updated packaged app is `dist\DarkSoulsLuaStudio\DS1LuaStudio.exe`; the executable keeps its
original filename. `publish.ps1` now writes to this folder so an older running build can remain open.

The supplied DS3 AI archives contain **Lua 5.0**, not Havok Script. BND4 and the original DCX
compression are preserved through SoulsFormatsNEXT. Select a Lua, goal table, or globals entry and
click **Open entry**. Save writes the edited entry back into its archive; Save As for compiled scripts
and tables creates a standalone file. Reopening an already open file or entry selects its tab.

Goal links resolve first against the script's own archive, then nearby tables and `aicommon.luabnd.dcx`.
Click the GOAL link to open and select the actual originating table row. **F12** on an identifier follows
script definitions in open scripts, the current archive and the common archive. Native engine calls
have no Lua definition. AI naming does not replace unsaved script edits during relinking.

Enable **Autocomplete while typing** in the sidebar. **Ctrl+Space** opens suggestions; **Tab/Enter**
accept a choice, **Escape** dismisses it. Suggestions include the current script's identifiers and
the selected game's symbols. DS3 includes **10,480 identifiers** collected from the supplied tables
and bytecode constants; signatures are not guessed for harvested symbols. Highlighting updates
when the mode changes. New globals tables are available under **New → AI globals table**.

Verification against all 29 supplied DS3 archives: **1,106 scripts parsed, decompiled, compiled and
decompiled again**, and **28 LUAINFO + 29 LUAGNL tables round-tripped byte-for-byte**. All scripts also
round-tripped through the chunk serializer byte-for-byte. Two scripts (`common_um_Yoshihi.lua` and
`021600_battle.lua` in aicommon) produce one decompiler warning each; these are displayed in the
editor. Successful recompilation checks syntax and chunk readability, not equivalent in-game behavior.
No game runtime was supplied for behavioral testing.

Reproduce the data audit with `dotnet run --project tools/DS3Probe -c Release -- --verify` from this folder.
Without `--verify`, the tool regenerates the embedded DS3 symbol list. Game script bodies are not exported.
`tools/UiProbe` renders the DS3 editor, linked goal table and globals view for UI checks.

## Function drafts, templates and folding

Place the cursor inside an action such as `Enemy_Act04` and choose **Edit → Save function as draft**
or press **Ctrl+Shift+D**. The library previews the complete function, including nested blocks and
anonymous helpers; click **Save draft** to keep it. **Ctrl+Shift+C** copies the whole function directly.
These actions are also available from the script's right-click menu.

Open **Drafts / templates** on the toolbar or press **Ctrl+Shift+L** to find saved drafts.
Use **Add draft** or **Add template** to create your own entry manually: enter a name, optional notes,
and type or paste Lua source, then click **Save draft** or **Save template**. Custom templates are
saved source snippets, editable and reusable like drafts, and remain available after restarting.

Search by
name, notes or source, preview/edit the source, rename the callback, then insert it or open a new
script. Insertion is one undo step and occurs after the enclosing function when the cursor is inside
one. Duplicate function names are rejected. Drafts retain their game mode, source location and notes,
and persist in `%APPDATA%\DS1LuaStudio\Drafts` as individual JSON files. Referenced outer-local names
are listed for review; a captured function does not automatically bring along its enclosing locals
or helper functions. Rename preserves strings, comments and member calls, and rejects an existing
identifier that would capture a local or helper.

Six original starter templates cover **single attacks, approach then attack, two-hit combos, ranged
spacing, melee enemies, and bosses with two HP phases**. Set the AI base name, attack IDs, range and
phase HP ratio, then click **Generate preview**. The preview remains editable and can be saved as a
personal draft. Example attack IDs must be replaced with animations supported by your character;
register battle callbacks in the relevant `.luainfo` and globals in `.luagnl`. These starters compile
for both DS1's 32-bit and DS3's 64-bit Lua layouts, but need in-game tuning.

Function fold controls appear beside multi-line declarations. Click to collapse/expand, or use
**Edit → Collapse all functions / Expand all functions**. Fold state follows each tab. Folding uses
Lua tokens, so `end` inside a comment or string does not prematurely close a function.

Autocomplete now uses a wider dark popup with function/constant/AI labels, prefix-first matching,
substring matches, and explicit left-to-right text/list layout. **Tab/Enter** accepts a choice;
space dismisses suggestions without inserting an unintended symbol.

Built on two upstream projects:

| Purpose | Source |
|---|---|
| File formats (`LUAINFO`, `LUAGNL`, `BND3`/`BND4`, DCX) | [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) |
| Lua decompiler / compiler | [ElaDiDu/DSLuaDecompiler](https://github.com/ElaDiDu/DSLuaDecompiler/tree/ast-fix) (`ast-fix`) |

Licences for both are kept in `LICENSE-SoulsFormats.txt` and `LICENSE-DSLuaDecompiler.txt`.

---

## Which Lua does DS1 PTDE use?

This matters, because it decides what can be read and written:

- **AI scripts in DS1 PTDE are Lua 5.0 bytecode**, stored inside `aistub.luabnd` (a BND3 binder).
  DSLuaDecompiler's own README states Lua 5.0 "is used for AI scripts in almost all FromSoft games".
- **Havok Script (Lua 5.1)** is the dialect used for *character control logic* in Bloodborne,
  DS3, Sekiro and Elden Ring — **not** DS1's AI. It is still supported here for reading, and for
  compiling if you supply the external DLL (see *Bytecode support*).

So for DS1 modding the whole loop is available: **decompile → edit → recompile**, using the
bundled Lua 5.0 compiler.

## Bytecode support

| Dialect | Decompile | Compile |
|---|---|---|
| Lua 5.0 (DS1 PTDE AI) | yes | yes (bundled `lua502.dll`) |
| Havok Script (BB / DS3 / Sekiro / ER) | yes | only if you add `HavokScript_FinalRelease.dll` |
| Lua 5.3 (Smash UI) | yes | no |

To compile Havok Script, place `HavokScript_FinalRelease.dll` (from Civilization VI) in a
`LuaNative` folder next to `DS1LuaStudio.exe`. It is not redistributable, so it is not included.
Without it the app still decompiles Havok Script normally and simply reports that compiling is
unavailable.

## Features

**Goal tables (`.luainfo`)**
- Spreadsheet-style editor: goal ID, name, battle/logic interrupt flags, interrupt function.
- Add a single goal, or **add a battle + logic pair** in one action — DS1 runs a battle script and a
  logic script together for the same enemy, so the two goals must share one ID.
- Duplicate / delete / reorder / sort by ID, plus a filter over ID, name and interrupt name.
- **Validation**, tuned against the stock tables so it does not cry wolf:
  - A goal ID shared by exactly one battle goal + one logic goal is **normal** and reported as a
    note, not an error. All three stock files (`aiCommon`, `m10_02_00_00`, `m14_01_00_00`) contain
    exactly this pattern and validate with zero errors.
  - A repeated ID that is *not* a clean battle+logic pair **is** an error.
  - Duplicate goal **names** are an error: names identify goals and are unique in all 140 stock goals.
  - Also flagged: empty names, logic-interrupt flag with no function name, negative IDs.
- Import / export as CSV or JSON for spreadsheet or diff workflows.
- Saves byte-faithfully: endianness, 32- vs 64-bit offsets, Shift-JIS vs UTF-16 and any DCX
  compression are all preserved from the file you opened.

**Script editor (`.lua`, and any bytecode file)**
- Syntax highlighting tuned for DS1: goal constants (`GOAL_COMMON_*`), engine calls
  (`GetDist`, `TurnTarget`…), AI callbacks (`GlobalActivate`, `GlobalUpdate`…) and AI globals are
  coloured from `VariableNamePatterns.json`.
- **Real AI argument names, taken from the goal tables.** See below.
- A **goal link** for battle and logic scripts: the `GOAL` row in the editor header names the goal
  this script implements, and clicking it opens the `.luainfo` beside the script and selects that
  row. `251000_battle.lua` links to the battle goal and `251000_logic.lua` to the logic goal —
  never across to the other half.
- Find and replace, line numbers, optional word wrap and several editor fonts.
- An **AI symbol palette** (284 symbols) with a filter — double click to insert at the caret.

**Script editor (`.lua`, and any bytecode file)**
- While a Lua script is open, **New goal table** is greyed out. Goals live in a `.luainfo`, not in a
  script, so the action is only offered where it applies.

### Using `.luainfo` to name the Lua

Dark Souls names every AI callback `<goalName>_<suffix>`, and `<goalName>` is exactly the `Name`
column of a `.luainfo` row. Across the 27 stock PTDE scripts that gave **274 named functions**, all of
which resolve to a goal — for example goal 6080 `Knight_of_HolyWomanA6080Battle` owns
`Knight_of_HolyWomanA6080Battle_Activate`, `_Update`, `_Terminate` and `_Interupt`.

So the editor loads the goal table it finds near the script and rewrites the decompiler's generated
parameters into the real ones:

```lua
-- before
function Knight_of_HolyWomanA6080Battle_Activate(f1_arg0, f1_arg1)
    local f1_local3 = f1_arg0:GetDist(TARGET_ENE_0)

-- after
function Knight_of_HolyWomanA6080Battle_Activate(ai, goal)
    local f1_local3 = ai:GetDist(TARGET_ENE_0)
```

Parameter names come from the callback's suffix and its **actual arity**, measured from the shipped
scripts rather than assumed:

| Suffix | Arity | Parameters |
|---|---|---|
| `Activate`, `Update`, `Terminate`, `Interupt` | 2 (always) | `ai, goal` |
| `Logic` | 1 | `ai` |
| `Act01`…`Act20`, `ActAfter_AdjustSpace` | 3 | `ai, goal, paramTbl` |

Safety rules, because a wrong rename would change behaviour:

- **Arity must match.** An `_Activate` that takes three arguments is an unrecognised signature, so
  its parameters keep their generated names even though the goal still links.
- **No shadowing.** If `ai` or `goal` already appears as an identifier in the file, nothing is
  renamed — the goal table is not allowed to silently capture an existing name.
- **Only resolved callbacks change.** Unrelated helpers such as `OnIf_254002` keep `f2_arg0`.

Toggle: **"Name AI arguments from .luainfo"** in the sidebar *AI Script Options*, and in
Tools → Appearance. It is on by default; switch it off to see the raw decompiler output.

Measured against your stock scripts: **261 of 274** callbacks resolved, **3559** parameters renamed,
and **0** functions given more names than they actually have parameters. The 13 that stayed as-is are
correct: `BlackKnight_Sword279000*` (goal 279000 is not in *this* area's table) and two `OnIf_*`
helpers that are not goal callbacks. The editor header shows what was linked, e.g.
`26 goals linked (m10_02_00_00.luainfo) | 174 arguments named`.

**Archive browser (`aistub.luabnd`)**
- Lists entries and marks which ones are Lua bytecode or goal tables.
- Decompile one entry into a tab, or extract every script in the archive to a folder of `.lua` files.
- Saving a script recompiles it and writes it back into the binder entry.

**Appearance**
- All interface text is light by default; nothing renders black-on-dark.
- `Tools → Appearance` changes code text colour (white, silver, light silver, warm white, pale blue,
  …), editor background, interface text and accent colour, plus font, size, line numbers and wrap.
- Changes apply immediately and persist to `%APPDATA%\DS1LuaStudio\settings.json`.

## Running

```powershell
# one clean folder with everything needed to run it
.\publish.ps1                 # framework dependent, ~13 MB, needs .NET 8 Desktop Runtime
.\publish.ps1 -SelfContained  # no .NET install needed on the target machine

# or run straight from the build output
dotnet build DS1LuaStudio.sln -c Release
.\src\DS1LuaStudio.App\bin\Release\net8.0-windows\DS1LuaStudio.exe

# open files from the command line / Explorer
.\src\DS1LuaStudio.App\bin\Release\net8.0-windows\DS1LuaStudio.exe aistub.luabnd m10_02_00_00.luainfo
```

Requires .NET 8 (x64 — the bundled Lua 5.0 compiler is a 64-bit native library).

### About the build output

`dotnet build` prints one line per project and **always names the `.dll`**, because that is the
assembly name:

```
DS1LuaStudio.App -> ...\net8.0-windows\DS1LuaStudio.dll
```

That line does not mean an executable is missing. The `.exe` is the *apphost* — a small launcher
that MSBuild generates alongside the `.dll` in the same folder, with an identical timestamp:

```
DS1LuaStudio.exe    152064 bytes   11:18:37 PM   <- launcher
DS1LuaStudio.dll     93184 bytes   11:18:37 PM   <- the actual app
```

Each of the six projects also has its own `bin\Release\...` folder, and only the App project
contains an exe — the rest are class libraries and legitimately ship DLLs only. The runnable app
is always:

```
src\DS1LuaStudio.App\bin\Release\net8.0-windows\DS1LuaStudio.exe
```

`publish.ps1` gathers that plus every dependency (including `LuaNative\lua502.dll`) into
`dist\DarkSoulsLuaStudio\`, so there is a single obvious entry point. It fails loudly if the exe or the
Lua 5.0 native library is missing.

Error details, if any, are appended to `%APPDATA%\DS1LuaStudio\studio.log`.

## Layout

```
DS1LuaStudio.sln
src/
  SoulsFormats/          vendored from SoulsFormatsNEXT (retargeted net9.0 -> net8.0)
  LuaDecompilerCore/     vendored from DSLuaDecompiler  (retargeted net7.0  -> net8.0)
  LuaCompiler/           vendored from DSLuaDecompiler  (retargeted net7.0  -> net8.0)
  DS1LuaStudio.Core/     document model, decompile/compile, archive + goal table handling
  DS1LuaStudio.App/      WPF front end
tests/
  DS1LuaStudio.Tests/    130 tests over formats, round trips and validation
```

Vendored projects are kept in the tree with their upstream code unmodified apart from the target
framework, so they can be refreshed by re-copying from upstream.

## Notes and limitations

### Switching between a script tab and a goal table tab

Each document type gets its own `DataTemplate`, so moving between a Lua tab and a `.luainfo` tab
tears one template down and builds the other. AvalonEdit 6 does not survive that: clearing the
`TextEditor`'s template chain invalidates `TextArea.Options`, and its handler for that invalidation
dereferences a `TextView` that has already been destroyed. The result is a `NullReferenceException`
raised **inside** the selection change, so the tab silently refuses to switch — and, because the
throw happened once per visual line, the log grew to 146 MB before this was tracked down.

The fix is in `MainWindow.xaml.cs`: `TextArea.Options` is assigned a **local** value rather than
being set through `TextEditor.Options` in XAML. A local value wins over the invalidated template
value, so the effective value never changes and AvalonEdit is never asked to react to a change.

Two supporting details are worth keeping:

- `App.xaml.cs` treats a `NullReferenceException` whose stack contains an AvalonEdit frame and
  **none** of ours as a rendering fault: logged once at WARN, never shown as a crash dialog. Anything
  with our code in the stack is still reported in full.
- `Log.cs` collapses identical consecutive entries and trims at 4 MB, so a fault that repeats per
  mouse move cannot fill the disk.

### `.lua` files that are actually bytecode

Dark Souls PTDE ships its AI scripts **compiled**, with a `.lua` extension — every file in
`...\script\m10_02_00_00-luabnd\ai\out\bin\` is bytecode, not text. The editor sniffs content
before extensions, so these are decompiled rather than rendered as gibberish.

### DS1 uses a 32-bit `size_t`

DS1's Lua was built with a 32-bit `size_t`, so its chunks store string lengths as 32-bit values.
Upstream DSLuaDecompiler read those lengths as a hard-coded 64-bit value, which desynchronised the
parse and made **every** DS1 AI script fail to decompile. The vendored `LuaFile.cs` now takes the
width from the chunk header (`LongSize`), which fixes all 27 stock scripts in that folder.

### Recompiling DS1 scripts — the 32-bit option

DS1's Lua uses a 32-bit `size_t`; the bundled compiler (`lua502.dll`) natively emits **64-bit**, so
raw compiler output would not load in-game. The editor converts the chunk after compiling:

**Tools → Appearance → "Compile Lua 5.0 with a 32-bit size_t"** (on by default).

Behaviour:

- **On** — new DS1 scripts emit 32-bit chunks. Existing compiled files preserve their own width.
- **Off** — new DS1 scripts use the compiler's native 64-bit layout, which PTDE cannot load.
- **DS3 mode** — new scripts always use 64-bit; this DS1 option is disabled in Appearance.

The status bar shows the layout of the open script and what a recompile will produce
(`size_t 32-bit`, or `size_t 32-bit -> 64-bit` when they differ), and saving appends a warning if
the output would stop matching the game.

How this was validated:

- Re-emitting all **27 stock DS1 chunks at their existing width is byte-identical**, which proves the
  parser and serialiser match the real on-disk layout exactly.
- Converting a compiled chunk 8 → 4 → 8 is byte-identical, and the 32-bit form decompiles to
  exactly the same source.
- Recompiling a shipped script produces a header identical to the original
  (`size_t=4, int=4, instr=4, op=6, A=8, B=9, C=9, num=8`) and decompiles to the same 91 lines, with
  only local variable numbering differing (`f1_local5` vs `f1_local0`) — a register-allocation
  difference, not a behavioural one.

### `f0_local0` and friends

Those names are **generated by the decompiler**, not present in the game:

- `f0_local0` = *function* 0, *local* 0. `f1_arg0` / `f1_arg1` are function 1's first and second
  arguments.
- DS1's chunks keep each function's **name** (`Knight_of_HolyWomanA6080Battle_Activate`) but not its
  local variable names, so anything that needs a name is invented.
- Function names come from the chunk header, which is why they line up with your goal table entries
  (`Knight_of_HolyWomanA6080Battle` = goal ID 6080).
- Renaming them is safe: they are ordinary locals in the output, and the names are regenerated on
  every decompile.

### Other notes

- `LUAINFO` cannot express a file with **zero** goals — SoulsFormats cannot detect the format in
  that case, so the validator flags an empty table as an error. Keep at least one goal.
- The Lua 5.0 compiler does not emit debug info, so local variables decompile as `f0_local0`
  style names. Function names are preserved (e.g. `Knight_of_HolyWomanA6080Battle_Activate`),
  and the names in `VariableNamePatterns.json` drive highlighting and the symbol palette.
- Round trip is verified byte-for-byte at the instruction-stream level: compiling, decompiling and
  recompiling a Lua 5.0 chunk reproduces identical bytecode.
