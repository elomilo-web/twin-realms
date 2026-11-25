# UI
Purpose: In-game HUD plus every menu/overlay (pack, skills, spellcraft, mods, pause, settings, controls, smith, dialogue), all built in code with vector-drawn icons and a data-driven gothic theme.
## Files
- `scripts/Ui.cs` (827 lines): partial Control owning all menus, notes, banners, dialogue, shop, pack, character. Key: `MakeTheme()` L22, `Note()` L40, `Banner()` L41, `_Ready()` L44, `_UnhandledInput()` L85, `Close()` L122, `Btn()` L125, `Inner()` L132
- `scripts/Hud.cs` (350 lines): top-left info, compass, quest tracker, target bar, hotbar, minimap. Key: `_Process()` L42, `_Draw()` L56, `PlaceName()` L348
- `scripts/Icons.cs` (239 lines): code-drawn vector icons (spells, potions, gear, materials). Key: `Draw()` L30, `ForSkill()` L217, `ForItem()` L225, `Frame()` L232
- `scripts/UiTheme.cs` (65 lines): colours/sizes from ui_theme.json, panel drawing. Key: `Box()` L23, `_Draw()` L36
- `scripts/UiWidgets.cs` (144 lines): mouse tooltip card, small widgets. Key: `Show()` L17, `_GuiInput()` L43, `Pos()` L82
- `scripts/UiSkills.cs` (252 lines): Skills menu (L), tiles with rank pips. Key: `OpenSkills()` L47, `SkillShown()` L33, `_Draw()` L246
- `scripts/UiMods.cs` (81 lines): modifier tree menu (U). Key: `OpenMods()` L16
- `scripts/UiMacros.cs` (90 lines): Macro 1/2 chain builder. Key: `OpenMacros()` L13
- `scripts/UiSystem.cs` (231 lines): pause, settings, quit confirm, unstuck. Key: `OpenPause()` L16, `OpenSettings()` L117, `QuitToTitle()` L99
- `scripts/UiControls.cs` (56 lines): key rebinding. Key: `OpenControls()` L11, `_Input()` L38
- `scripts/MapMarks.cs` (31 lines): party member dots on minimap/map. Key: `Dot()` L26
## Scenes / nodes
Built in code: Main/Hud (Control, custom _Draw), Main/Ui (menu host, windows are children created on open), tooltip card follows the mouse.
## Data
data/ui_theme.json (panel, line, text colours, sizes); data/settings via UiSystem; data/spellcards.json via icons.
## Links
[[loot]] [[spellcraft]] [[skills_vfx]] [[progression]] [[player]] [[combat]] [[world]] [[audio]] [[core]]
## Scores
- visuals 6/10: #1: gothic framed bars + damage ghost + low-HP pulse; inventory/menus still plain
- feel 6/10: clear hotkeys and hints; no open/close animation or sound feedback.
- code 5/10: Ui.cs 827-line god-class with lambdas; Hud draws everything in one _Draw.
## Ideas
- Hotbar: add cooldown sweep and rarity-less ornate frame in `Hud._Draw()` L56, replace flat bars with fraying-edge textured fills.
- Replace minimap pixel tiles with smoothed parchment map plus ring ornament in `Hud._Draw()`.
- Menu open/close fade+slide tween in `Ui.Close()` L122 and window creation.
- Add dark vignette and noise grain panel texture in `UiTheme.Box()` L23.
- Gothic serif font in `Ui.MakeTheme()` L22 (headings Cinzel-like) driven by ui_theme.json.
- Split Ui.cs by window (pack, character, dialogue, shop) into partials like UiSkills.
- Redraw `Icons.Draw()` L30 weapons/spells with engraved metal shading and darker palette instead of bright glyphs.
- Damage-direction and low-HP blood vignette in Hud.
