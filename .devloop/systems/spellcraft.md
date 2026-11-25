# Spellcraft
Purpose: Player builds spells from cards (carrier + effect + modifiers, optional second chained section), forges them into skills; spell tree points spent in the same system.
## Files
- `scripts/Spellcraft.cs` (424 lines): crafted-spell model, card data, validation, spell tree. Key: `Text()` L32, `Load()` L52, `Rank()` L103, `TreeFree()` L107, `CanBuy()` L112, `Buy()` L121, `ResetTree()` L127
- `scripts/PlayerSpellcraft.cs` (66 lines): runtime casting of crafted spells, second section chaining (Two Worlds II style). Partial of the Player class.
- `scripts/UiSpellcraft.cs` (282 lines): Spellcraft menu (B). Key: `ScLoad()` L20, `OpenSpellcraft()` L28, `CardTile()` L248, `PreviewNums()` L265
## Scenes / nodes
Menu built in code under Main/Ui; casts spawn projectile/area nodes via skills_vfx.
## Data
data/spellcards.json, data/spellfx.json, data/skills.json (forged spells become skills).
## Links
[[skills_vfx]] [[ui]] [[player]] [[combat]] [[progression]] [[loot]]
## Scores
- visuals 4/10: three weak categories of identical tiny icons (many duplicate fireballs/swords), flat navy grid, text-heavy, no card art or preview of the spell effect.
- feel 6/10: deep and clear rules text; no live preview or cast feedback, no card-add animation.
- code 6/10: Spellcraft.cs cleanly data-driven, but UI is one 282-line builder.
## Ideas
- Unique art per card in `Icons.ForSkill()` (Icons.cs L217); stop reusing sword/fireball glyphs.
- Live spell preview (damage, mana, cooldown) already in `UiSpellcraft.PreviewNums()` L265: show as bar chart.
- Card-shaped tiles with rarity frames in `CardTile()` L248.
- Animated forge effect (embers, flash) on Forge in `OpenSpellcraft()` L28.
- Fix the label typo "Cards per spell spell" in `OpenSpellcraft()`.
- Drag cards into section slots instead of click-only.
- Test-cast dummy button in the menu.
- Card synergy hints (carrier compatibility highlight) when picking the second section.
