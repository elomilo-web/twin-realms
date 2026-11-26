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

## Implemented (core) — parts 1-4 (2026-11-25)
- data/pets.json (`_help` lines): rules, rarity table, cubes (iron/silver/runed/abyssal/void "Void Reliquary"), natures, stats
  coefficients (+ `stats.world` floors), skills (world: range/cd/fx; duel: power/acc/cost=focus/priority/effect/chance/turns,
  kinds attack/heal/shield/buff/debuff), archetype learnsets + elemLearn, species overrides, pet gear (rolls + craft).
- Every enemies.json type is catchable (species derived from the def; overrides in pets.json "species" or an enemy's "catch").
  Excluded: shade/shadeArcher/shadeCaster (hero mirror copies, Enemy.ShadeOf) via pets.json "uncatchable". Bosses/raid
  (Scarletta, hp >= raidHp) use cube bossMul and bossMinChance (0.2%): very low, never zero.
- scripts/PetDb.cs: `PetState` (fields only: Id, Species, Nick, Nature, Ot, Cube, Level, Xp, Bond, HpFrac, KoT, Learned,
  Skills[<=4], Gear{pet_collar,pet_armor,pet_charm}, Stay, Aggro, Loot, LootRar; methods Name(), Sp(), Stats() -> PetStats
  {Hp,Atk,Def,Spd,Mag}, Elem() (game damage types: physical/fire/frost/lightning/poison/shadow/holy), GainXp(n), LearnUpTo(),
  ToJson()/FromJson(s) compact JSON for trade). `PetDb`: Skill(id), AllSkills, Cubes, Cube(id), Natures, Species(type),
  New(species, level, cube), CatchChance(enemy, cube), XpNeed, RollGear, GearText, R(rule).
- PlayerStats: Pets, ActivePet, Cubes (tier -> count), CubeSel. Old saves load empty.
- scripts/Cube.cs: `Cubes` (Count/Give/Selected/Cycle/Throw/DrawIcon), `SoulCube` node (arc, pull ribbons, 1-3 shake rolls of
  chance^(1/3), gold burst + new pet / crack + Enemy.Enrage). Enemy.Captured() removes the foe (quests/arena/dungeon boss count).
- scripts/Pet.cs: `Pet` (CharacterBody3D, IDamageable; body = `Enemy { PetBody = true }` model only, PetSwing/PetFlinch/PetDown),
  `PetWorld` (Out, Active, Tick, SetActive, ToggleStay, OnKill xp share, WorldDamage, CanMount/MountSpeed/Riding).
  Hits are IsPlayer HitInfos with Attacker = the pet (no hero bonuses/leech); struck foes may switch aggro to it (Enemy.PetFoe).
- Player: ToggleMount rides the active mountable pet (its model replaces the box horse; horse kept as fallback).
- scripts/UiPets.cs: OpenPets(sel, tab) — list, PetPreview (SubViewport, own world), stats, Skills/Gear/Orders, rename/release/active.
- Keys: P pets (pad LT+RT), V throw cube (pad LT+RB), "," next cube tier, "." follow/stay. Shop sells cubes; craft bench
  (Potions tab) makes cubes + pet gear; cubes drop (3% / 8% dungeon / 60% boss); pet gear 2% of item rolls.
- MP: pets are local only (not shown to friends yet; RemoteHero has no pet). Catching a host's monster as a client lands as a killing blow.
- For the duel/trade agent: build duel stats from `PetState.Stats()`, moves from `PetDb.Skill(id)` (power/acc/cost/priority/
  effect/turns), element wheel on `Elem()`; trade by `ToJson()`/`FromJson()` (give a new Id on receipt). Arena: PetWorld does not
  spawn pets while `Arenas.Active`.

## Implemented (duels + trading) — 2026-11-26
- data/pets.json "duel" (`_help`): damage formula numbers, element wheel (fire>poison>lightning>frost>fire, holy<->shadow; x1.5 /
  x0.67, texts "Searing!"/"Resisted..."), statuses (dot/skip/spdMul/dmgMul/takenMul/noSwap), focus, items (Soul Draught x2, Ember of
  Return x1), trainer names, species pool, 8 ladder ranks (size, lvl offset, valor/gold/petXp, cube+chance, gearChance, potions, smart),
  "trader" (Odile fee/levelSpread/rarityFee), "trade" limits (maxPets 3, maxPetJson 4096, maxGold).
- scripts/SoulDuel.cs: `DuelDb` (data access, `Elem(atk, def)`), `DuelPet`, `DuelSide`, `SoulDuel` (CanvasLayer; `Begin(teamIds, dev)`,
  `Stop()`, `Active`, `Choose(kind, arg)`, `DevChoose(i)`, `Phase`). Turn = queued timed steps (Perform/Land per actor, EndOfTurn,
  AfterKo). Order: swap/item first, skill priority, spd (chill halves). AI: potion/heal when low, swaps when out-matched, scores moves
  by power x acc x element + status value; `smart` = chance it plays its best move. Models: Enemy PetBody per side facing each other,
  own Camera3D (restored at end), hero frozen behind its pet (Player.Frozen), HUD hidden, trainer Npc. UI: cards (HP bar, focus pips,
  statuses, team pips, items), message line, bottom command panel (2x2 skills with element arrows, Swap/Item/Forfeit, sub-lists, B/Esc back;
  buttons take pad focus), result panel (Next duelist / Leave). Rewards (not in dev runs): valor, gold, pet xp (half for KO'd), cube,
  pet gear; PlayerStats `DuelRank` (+1 per win, last rank repeats), `SoulWins`, `SoulLosses`; loss gives 25% xp.
- Arenas.Start(mode, team, dev): mode "souls" (Tick skips it; End stops the duel). Entry: Kael's dialogue, "Soul Duels" button in the
  Pets menu, pause menu entry -> `Ui.OpenSoulTeam()` (team picker, ladder preview). Totals shows the ladder.
- scripts/PetTrade.cs: `PetTrade` (session state, `Pack`/`Unpack` = auction hook for F-37, `StartNpc`, `Toggle`, `SetGold`, `Confirm`,
  `Cancel`, `Prepare` escrow, `Done`, `Cancelled`, `RestoreEscrow`), Net partial (host sessions; RPCs R_TradeReq/Ask/Answer/Open/Offer/
  View/Ok/Prep/Ready/Done/Cancel/End; two-phase: both sides move their offer into PlayerStats.TradeEscrow, then each gets the other's;
  any offer change bumps Rev and clears confirms; payload size-checked on the host), `Ui.OpenPetTrade()` two-sided window.
  Entry: E on a friend -> "Offer a pet trade"; Pets menu shows "Trade: <name>" while one is open. Online: the server is the host, so
  the same RPCs run there (it relays; it does not check offers against the stored account saves). Escrow comes back on cancel,
  partner leaving, disconnect (Net.Shutdown) or on the next tick after a crash (PetWorld.Tick). Protocol 14.
- Offline: Odile, Pet Trader (data/quests.json npc "odile", role "pettrader", by the arena): one pet for a pool pet of the same rarity,
  level +-1, fixed per visit, fee; the given pet's gear returns to the pack.
- Friends' pets: PeerInfo.Pet "species|level|nick" (roster, re-sent when it changes); RemoteHero spawns a PetBody follower behind the
  friend (hidden while mounted/dead, name tag).
- Captures: viewpoints "souls_duel" (DevCapture "souls:start") and "pet_trade" ("ui:pettrade", Odile with a pet offered).
- Not done / untested: MP trade never run (no MP tests by user preference); a disconnect in the instant between "ready" and "done" could
  leave a pet on both sides (escrow is returned on disconnect); auction listing (F-37) only has the Pack/Unpack hook.
