# DS3 Lua format investigation — 2026-10-13

Evidence: the user-supplied `DS3-Luascripts/script/*.luabnd.dcx` archives and the vendored
SoulsFormatsNEXT readers/writers. Original game archives were read only.

* 29 archives are BND4; archive entry names include `INTERROOT_win64` paths.
* 1,106 AI/event scripts carry Lua 5.0 headers: `1B 4C 75 61 50 01 04 08 04 06 08 09 09 08`.
  They use little endian, 4-byte integers/instructions, 8-byte size_t, and 8-byte numbers.
* 28 LUAINFO files use `LUAI`, version 1, 24-byte goal records: ID int32, battle/logic flags,
  two padding bytes, name offset int64, interrupt-name offset int64. Names are UTF-16.
  Goal IDs stay 32-bit even though offsets are 64-bit.
* 29 LUAGNL files contain int64 offsets ending in zero, followed by UTF-16 names.
* Lua bytecode re-emission at width 8 and goal/global document round trips are byte-identical
  across every supplied entry. All script outputs compile and can be decompiled again.
* Two stock decompiles report a warning: `common_um_Yoshihi.lua` and `021600_battle.lua`, both
  in aicommon. Syntactic compilation is not proof of semantic equivalence.
* 10,480 identifiers were collected from LUAINFO names, LUAGNL global names, and identifier-shaped
  bytecode string constants. Only names are shipped; signatures and inferred argument types are not added.
* Existing `Ds1AiCatalog` goal-name/ID and callback matching applies to the supplied DS3 convention.
  Archive ownership is now retained on individual goals so navigation reaches their actual source.

Relevant upstream: https://github.com/soulsmods/SoulsFormatsNEXT (BND4, LUAINFO, LUAGNL, DCX).
Reproduction: `dotnet run --project tools/DS3Probe -c Release -- --verify`.
No game runtime was tested, no process memory was inspected, and no decompiled script bodies are shipped.
