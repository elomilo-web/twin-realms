# Pets ("Bound Souls") — design (user directive 2026-11-25; build in cycle 21+)
Purpose: catch monsters in Soul Cubes, keep them as companions (follow, fight, loot, mount), level and gear them, and fight
turn-based pet duels in the Arena. Original gothic take: a cube is a rune-carved iron reliquary that binds a weakened soul.

## Catching
- Item: Soul Cube (tiers Iron / Silver / Runed / Abyssal; data/pets.json "cubes": catch bonus, cost). Bought from the shop,
  crafted (Iron Scrap + Arcane Dust), dropped in dungeons. Hotbar-usable (throw with an arc, like a potion key; gamepad LT+...).
- Catch chance = base(species rarity) x (1 - hp%)^1.5 x cube bonus x status bonus (stun/sleep/chill +) x level gap; bosses,
  humanoid NPCs and raid foes uncatchable. Throw -> cube flies, opens, soul ribbons pull the foe in, cube shakes 1-3 times
  (each shake is one roll), then gold burst (caught) or crack + foe breaks free angry.
- Catchable = enemies.json types with "catch": {rarity, skills, mountable, petScale}.

## Pets
- PetState in PlayerStats: id, species, nickname, level, xp, stats (hp/atk/def/spd/mag from species base x level x nature),
  gear (collar, armor barding, charm: 3 slots, item slot types "pet_collar/pet_armor/pet_charm"), skills (4 equipped of
  learned, from species list by level), bond (0-100: grows with time out), mountable flag.
- Active party: up to 1 companion out in the world (2 at a later tier); the rest in the "Soul Box" (stash).
- World behaviour (Pet.cs, reuses Enemy rig/models): follow (leash 4-8 m, teleports if > 40 m), aggro setting
  Passive / Defensive (attacks what hits you or it) / Aggressive (attacks foes in 12 m) / Assist (attacks your target);
  auto-loot setting Off / Gold only / Rarity >= X / Everything (walks to drops within 15 m and brings them).
- Pets gain xp from kills nearby and arena duels; level up -> stats + new skills (choose to learn/replace).
- Mount: mountable species (wolf, boar, drake, golem...) can be ridden (replaces the box-horse: Player.Mounted uses the
  pet model; speed from pet spd; dismount T).
- Death in the world: knocked out, returns to its cube, revives after 60 s or with a potion.

## Pets menu (UiPets.cs, key P / pad LT+Y?)
- Left: party + Soul Box list (portrait, name, level, species, HP bar). Centre: 3D preview (SubViewport) of the pet, stats,
  bond, nature. Right tabs: Skills (equip 4), Gear (3 slots from pack), Orders (follow / stay, aggro mode, auto-loot,
  mount). Rename, release, set active. Fixed-size window, scrolls (same rules as the pack).

## Arena: Soul Duels (turn-based)
- Arenas.Start("souls"): pick 3 pets; opponents are generated trainers ("Duelists of the Pit") with ladders of rank.
- Turn order by spd; each turn choose: one of 4 skills (power, accuracy, element, cost in focus points, effects:
  burn/chill/poison/stun/buff/heal/shield), swap pet, use item (limited), forfeit. Element wheel from GearFx elements
  (fire > venom > storm > frost > fire; holy <-> shadow).
- Presentation: arena camera framing both pets, command panel bottom (gamepad friendly), damage numbers, element
  effectiveness text, skill VFX reuse SpellFx, KO + swap. Rewards: valor, pet xp, cubes, gear. Ladder rank in Totals.

## Build plan (agents, cycle 21)
1. Data + core: data/pets.json (species from enemies.json, cubes, skills, natures, gear), PetDb.cs, PlayerStats.Pets,
   save/load, catch math. 2. Soul Cube item + throw + capture VFX/SFX (Cube.cs). 3. Pet.cs world companion (follow, aggro
   modes, auto-loot, KO, xp, mount). 4. UiPets.cs menu + capture view. 5. SoulDuel.cs turn-based arena mode + UI.
Links: [[enemies]] [[combat]] [[arena]] [[ui]] [[loot]] [[player]]
