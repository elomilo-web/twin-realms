# Generates data/skilltree.json (the base passive tree) and data/skilltree_adv.json (the advanced tree, A-15).
#   powershell -File tools/gen_tree.ps1
# BASE: 23 wedges around a centre: one per class (18, each with its own start, free for that class) and five themed branches open
# to everyone (their first node is a "branch" node that costs a point). Each wedge has an outward path to a notable and a keystone,
# two notable clusters (wheels), an inward path to a shared inner ring, and a bridge to the next wedge with a hybrid notable in the
# middle, so builds can mix any neighbours and reach far ones through the ring and the centre.
# ADVANCED: one path of 13 nodes per base wedge (Mage -> Archmage ...): a gate node, a spine of smalls, three notables (class skill
# upgrades, a passive, a pact with a price) and a capstone. Rules for opening it are in the file's "unlock" (read by TreeDb).
# Every stat key used here must be read by the game (TreeDb.Known lists them; skill_*, school_* and form_* keys are added on load).
# Changing the layout changes node ids: bump $version, and every hero gets all tree points back on load (Game.Load).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$version = 3
$RS = 1.75   # 23 wedges instead of 13: every radius grows so the wedges keep their width

$classes = @("warrior","gladiator","berserker","samurai","lancer","monk","rogue","wayfarer","assassin","fortune","warlock","necromancer","chronomancer","mage","elementalist","shaman","druid","shapeshifter","bard","cleric","paladin","spellblade","warden")
$themed = @("gladiator","wayfarer","fortune","elementalist","shapeshifter")   # not classes: open to all, no free start
$names = @{ warrior="Warrior"; berserker="Berserker"; lancer="Lancer"; rogue="Ranger"; assassin="Assassin"; warlock="Warlock"; necromancer="Necromancer"; mage="Mage"; druid="Druid"; cleric="Cleric"; paladin="Paladin"; spellblade="Spellblade"; warden="Warden"
  monk="Monk"; bard="Bard"; shaman="Shaman"; samurai="Samurai"; chronomancer="Chronomancer"; gladiator="Gladiator"; wayfarer="Wayfarer"; fortune="Fortune Seeker"; elementalist="Elementalist"; shapeshifter="Shapeshifter" }
$branchDesc = @{ gladiator="A themed path open to every class: power attacks, finishers and grit."; wayfarer="A themed path open to every class: speed, stamina and the road."
  fortune="A themed path open to every class: luck, gold and lucky strikes."; elementalist="A themed path open to every class: fire, frost and lightning together."; shapeshifter="A themed path open to every class: stronger, longer animal and spirit forms." }
function M { $o = [ordered]@{}; for ($i = 0; $i -lt $args.Count; $i += 2) { $v = $args[$i + 1]; if ($v -is [string]) { $v = [double]::Parse($v, [Globalization.CultureInfo]::InvariantCulture) }; $o[[string]$args[$i]] = $v }; return $o }   # (M def 25 move -5): negative numbers arrive as text
$Theme = @{
 warrior     = @{ col="#e0584a"; small=@((M meleeP 4),(M hp 14),(M def 4),(M block 3)); nots=@(@("Shield Wall",(M block 10 def 10)),@("Veteran",(M hpP 8 def 6)),@("Sword Master",(M swordP 15 aspd 5))); key=@("Unbreakable",(M def 25 block 15 tenacity 30 move -5)) }
 berserker   = @{ col="#c23a1e"; small=@((M meleeP 5),(M aspd 3),(M hp 10),(M greatswordP 6)); nots=@(@("Bloodlust",(M leech 1.5 meleeP 10)),@("Savage Edge",(M critD 20 greatswordP 10)),@("Battle Trance",(M aspd 8 powerP 20))); key=@("Blood Rage",(M rage 1.5 meleeP 15 hpP -10)) }
 lancer      = @{ col="#4a7ac8"; small=@((M reach 4),(M knockP 6),(M spearP 6),(M meleeP 3)); nots=@(@("Long Reach",(M reach 12 spearP 10)),@("Launcher",(M finisherUp 3 finisherP 15)),@("Skirmisher",(M move 6 evade 4))); key=@("Skewer",(M reach 20 finisherUp 4 knockP 30 finisherP 20)) }
 rogue       = @{ col="#4ad070"; small=@((M rangedP 5),(M crit 1),(M move 2),(M evade 1.5)); nots=@(@("Eagle Eye",(M rangedP 12 crit 3)),@("Fleet Foot",(M move 8 evade 5)),@("Piercing Shots",(M pierce 1 armorPen 6))); key=@("Hawk Eye",(M multi 1 rangedP 10)) }
 assassin    = @{ col="#8a8aa8"; small=@((M crit 1.2),(M critD 6),(M daggerP 6),(M poisonP 5)); nots=@(@("Ambush",(M critD 25 crit 3)),@("Venomous",(M poisonChance 10 poisonP 15)),@("Shadow Dance",(M evade 6 aspd 6))); key=@("Death Mark",(M critD 50 crit 6 hpP -10)) }
 warlock     = @{ col="#b04ad0"; small=@((M statusP 5),(M shadowP 5),(M fireP 5),(M magicPen 2)); nots=@(@("Hexer",(M statusP 15 burnChance 8)),@("Withering",(M shadowP 12 magicPenP 8)),@("Soul Fire",(M fireP 12 burnP 20))); key=@("Plague Lord",(M statusP 40 poisonP 30 poisonChance 10)) }
 necromancer = @{ col="#8a3a8a"; small=@((M shadowP 5),(M leech 0.4),(M mp 10),(M mr 3)); nots=@(@("Grave Pact",(M leech 2 shadowP 10)),@("Bone Armor",(M def 10 mr 8)),@("Dark Ritual",(M cdr 6 shadowP 8))); key=@("Soul Eater",(M leech 3 shadowP 20 regenHp -1)) }
 mage        = @{ col="#5a86ff"; small=@((M spellP 5),(M mp 12),(M regenMp 0.4),(M cdr 1.5)); nots=@(@("Arcane Mind",(M spellP 12 mpP 8)),@("Elemental Focus",(M fireP 8 frostP 8 lightningP 8)),@("Quick Casting",(M castSpd 10 cdr 5))); key=@("Mana Shield",(M manaShield 1 mpP 20)) }
 druid       = @{ col="#3aa04a"; small=@((M poisonP 5),(M regenHp 0.5),(M thorns 2),(M hp 10)); nots=@(@("Call of the Pack",(M form_wolf 1 thorns 8 def 6)),@("Tusk and Hide",(M form_boar 1 regenHp 2 healP 10)),@("Wild Venom",(M poisonChance 8 poisonP 12))); key=@("Wild Growth",(M regenHp 3 thorns 15 poisonP 15 aoeP 10)) }
 cleric      = @{ col="#e8d880"; small=@((M healP 5),(M holyP 5),(M mp 10),(M res_shadow 3)); nots=@(@("Blessed",(M healP 15 regenHp 1)),@("Radiance",(M holyP 12 aoeP 8)),@("Sanctuary",(M res_shadow 10 res_fire 6 res_frost 6))); key=@("Divine Grace",(M healP 40 holyP 10 mpP 10)) }
 paladin     = @{ col="#f0c040"; small=@((M holyP 4),(M def 4),(M maceP 6),(M block 2)); nots=@(@("Righteous",(M holyP 10 maceP 10)),@("Aegis",(M block 10 mr 10)),@("Zealot",(M finisherStun 0.3 finisherP 15))); key=@("Undying",(M undying 1 holyP 10)) }
 spellblade  = @{ col="#9a6ae0"; small=@((M magToDmg 0.08),(M spellP 3),(M meleeP 3),(M lightningP 4)); nots=@(@("Runeblade",(M magToDmg 0.3 swordP 8)),@("Storm Edge",(M lightningP 12 chain 1)),@("Battle Mage",(M spellP 8 meleeP 8))); key=@("Spellsword",(M magToDmg 0.8 cleave 1 mcost 10)) }
 warden      = @{ col="#8a9a4a"; small=@((M hp 12),(M axeP 6),(M bleedP 6),(M armorPen 2)); nots=@(@("Iron Hide",(M def 12 hpP 6)),@("Hamstring",(M bleedChance 10 bleedP 15)),@("Sunder",(M armorPenP 12 axeP 8))); key=@("Iron Fortress",(M def 30 res_fire 10 res_frost 10 res_lightning 10 res_poison 10 res_shadow 10 move -6)) }
 # ---- A-15: the five classes that had no path of their own
 monk        = @{ col="#e09040"; small=@((M unarmedP 6),(M evade 1.2),(M aspd 2.5),(M regenMp 0.4)); nots=@(@("Iron Palm",(M unarmedP 18 finisherStun 0.3)),@("Flowing Water",(M evade 6 move 5)),@("Inner Fire",(M regenMp 1.5 regenHp 1.5 tenacity 10))); key=@("Empty Hand",(M unarmedP 45 aspd 12 evade 6 def -15 block -20)) }
 bard        = @{ col="#e060a0"; small=@((M castSpd 3),(M healP 4),(M allP 1.5),(M goldP 4)); nots=@(@("Rousing Chorus",(M allP 5 aspd 5)),@("Soothing Verse",(M healP 15 regenHp 1)),@("Perfect Pitch",(M castSpd 10 cdr 5))); key=@("Crescendo",(M allP 10 castSpd 20 healP 20 hpP -10)) }
 shaman      = @{ col="#4ac0c0"; small=@((M lightningP 5),(M mp 10),(M healP 4),(M regenMp 0.4)); nots=@(@("Spirit Walk",(M form_wisp 1 healP 10 regenMp 1)),@("Storm Totem",(M lightningP 12 chain 1)),@("Ember Spirit",(M form_ember 1 fireP 10 burnChance 6))); key=@("Spirit Conduit",(M lightningP 25 chain 2 healP 20 mcost 18)) }
 samurai     = @{ col="#c8323c"; small=@((M katanaP 6),(M critD 5),(M aspd 2),(M tenacity 3)); nots=@(@("Iaijutsu",(M katanaP 15 crit 3)),@("Zanshin",(M tenacity 15 evade 4)),@("Tameshigiri",(M critD 25 armorPenP 8))); key=@("One Cut",(M katanaP 25 critD 60 crit 5 aspd -12)) }
 chronomancer= @{ col="#70c8e8"; small=@((M cdr 1.5),(M castSpd 3),(M mp 10),(M move 1.5)); nots=@(@("Borrowed Seconds",(M cdr 6 castSpd 8)),@("Slow Field",(M chillChance 10 statusP 15)),@("Foresight",(M evade 5 crit 3))); key=@("Stolen Hours",(M cdr 15 castSpd 20 move 8 mpP -20)) }
 # ---- A-15: five themed branches
 gladiator   = @{ col="#d08a3a"; small=@((M powerP 5),(M finisherP 5),(M tenacity 3),(M st 8)); nots=@(@("Showman",(M powerP 20 knockP 15)),@("Arena Veteran",(M tenacity 15 hpP 6)),@("Finishing Blow",(M finisherP 25 finisherStun 0.3))); key=@("Crowd Favourite",(M powerP 40 finisherP 30 tenacity 20 healIn -25)) }
 wayfarer    = @{ col="#5ab0a0"; small=@((M move 1.5),(M st 10),(M jumpP 4),(M swimP 6)); nots=@(@("Trailblazer",(M move 6 climbP 20 st 20)),@("Deep Lungs",(M breathP 40 swimP 25)),@("Horse Lord",(M rideP 20 xpP 6))); key=@("Wanderer",(M move 12 st 40 jumpP 25 xpP 10 hpP -8)) }
 fortune     = @{ col="#e8c84a"; small=@((M goldP 5),(M luck 1),(M crit 1),(M xpP 2)); nots=@(@("Lucky Find",(M luck 4 goldP 15)),@("Sharp Eye",(M crit 4 critD 15)),@("Quick Study",(M xpP 12 cdr 3))); key=@("Gambler's Luck",(M luck 10 goldP 40 crit 8 def -12 mr -12)) }
 elementalist= @{ col="#f07838"; small=@((M fireP 4),(M frostP 4),(M lightningP 4),(M magicPen 2)); nots=@(@("Pyre",(M fireP 12 burnChance 10)),@("Rime",(M frostP 12 chillChance 12)),@("Tempest",(M lightningP 12 shockChance 10))); key=@("Prismatic Core",(M fireP 22 frostP 22 lightningP 22 magicPenP 10 res_fire -15 res_frost -15 res_lightning -15)) }
 shapeshifter= @{ col="#b08a50"; small=@((M formP 5),(M formDurP 6),(M hp 12),(M move 1.5)); nots=@(@("Stag's Grace",(M form_stag 1 move 4 jumpP 15)),@("Second Skin",(M formDurP 25 formCdr 15)),@("Predator",(M formP 15 crit 3))); key=@("Feral Heart",(M formP 40 formDurP 50 formCdr 20 spellP -20 mcost 15)) }
}
$generic = @((M hp 10),(M allP 2),(M mp 8),(M def 3),(M st 8),(M mr 3),(M res_fire 4),(M res_frost 4),(M res_lightning 4),(M res_poison 4),(M res_shadow 4),(M critD 4),(M xpP 2),(M skillP 3),(M aoeP 3),(M armorPen 1),(M magicPen 1),(M tenacity 3),(M cdr 1))
$keyName = @{ meleeP="Might"; hp="Vigor"; def="Toughness"; block="Guard"; aspd="Haste"; greatswordP="Greatblade"; reach="Reach"; knockP="Impact"; spearP="Spearwork"; rangedP="Marksman"; crit="Precision"; move="Swiftness"; evade="Evasion"; critD="Cruelty"; daggerP="Knifework"; poisonP="Toxins"; statusP="Affliction"; shadowP="Darkness"; fireP="Flame"; magicPen="Arcane Edge"; leech="Thirst"; mp="Wisdom"; mr="Warding"; spellP="Sorcery"; regenMp="Clarity"; cdr="Focus"; regenHp="Renewal"; thorns="Brambles"; healP="Mercy"; holyP="Light"; res_shadow="Shadow Ward"; maceP="Maceplay"; magToDmg="Rune Edge"; lightningP="Storm"; axeP="Axemanship"; bleedP="Laceration"; armorPen="Piercing"; allP="Power"; st="Endurance"; res_fire="Fire Ward"; res_frost="Frost Ward"; res_lightning="Storm Ward"; res_poison="Poison Ward"; xpP="Learning"; skillP="Technique"; aoeP="Reach of Spells"; tenacity="Tenacity"
  unarmedP="Open Palm"; castSpd="Tempo"; goldP="Coin"; katanaP="Bladework"; powerP="Heavy Hand"; finisherP="Flourish"; jumpP="Spring"; swimP="Stroke"; luck="Fortune"; frostP="Frost"; formP="Fang and Claw"; formDurP="Deep Change"; bowP="Bowcraft" }

$nodes = New-Object System.Collections.ArrayList
$adj = @{}
function Add-Node($x, $y, $type, $name, $mods, $col, $arm, $desc = "") {
  $id = $nodes.Count
  $n = [ordered]@{ id=$id; x=[math]::Round($x,1); y=[math]::Round($y,1); type=$type; name=$name; mods=$mods; col=$col; arm=$arm }
  if ($desc -ne "") { $n.desc = $desc }
  [void]$nodes.Add($n)
  $adj[$id] = New-Object System.Collections.Generic.HashSet[int]
  return $id
}
function Link($a, $b) { [void]$adj[$a].Add($b); [void]$adj[$b].Add($a) }
function P($r, $deg) { $rad = $deg * [math]::PI / 180; return @(([math]::Cos($rad) * $r), ([math]::Sin($rad) * $r)) }
function SmallName($mods) { $k = @($mods.Keys)[0]; if ($keyName.ContainsKey($k)) { return $keyName[$k] } return $k }

# ------------------------------------------------------------------ base tree
$n = $classes.Count; $d = 360.0 / $n
$origin = Add-Node 0 0 "hub" "Heart of the Rift" (M allP 6 hpP 4) "#e3b04b" ""
$starts = [ordered]@{}; $ring = @{}; $path1 = @{}; $gi = 0
for ($i = 0; $i -lt $n; $i++) {
  $c = $classes[$i]; $th = $Theme[$c]; $a = -90 + $i * $d; $col = $th.col; $si = 0
  $p = P (900 * $RS) $a
  if ($themed -contains $c) { $s = Add-Node $p[0] $p[1] "branch" $names[$c] (M allP 2) $col $c $branchDesc[$c] }
  else { $s = Add-Node $p[0] $p[1] "start" $names[$c] ([ordered]@{}) $col $c; $starts[$c] = $s }
  # inward path to the inner ring, with a spoke to the centre
  $prev = $s
  foreach ($r in 780, 660, 540) { $m = $generic[$gi++ % $generic.Count]; $p = P ($r * $RS) $a; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" $c; Link $prev $x; $prev = $x }
  $ring[$i] = @()
  foreach ($k in 0, 1, 2) { $m = $generic[$gi++ % $generic.Count]; $p = P (400 * $RS) ($a + $k * $d / 3); $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" ""; $ring[$i] += $x }
  Link $prev $ring[$i][0]; Link $ring[$i][0] $ring[$i][1]; Link $ring[$i][1] $ring[$i][2]
  $m = $generic[$gi++ % $generic.Count]; $p = P (200 * $RS) $a; $sp = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" ""; Link $ring[$i][0] $sp; Link $sp $origin
  # outward path: five class smalls, a notable, the keystone
  $prev = $s; $out = @()
  foreach ($r in 1000, 1100, 1200, 1300, 1400) { $m = $th.small[$si++ % 4]; $p = P ($r * $RS) $a; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m $col $c; Link $prev $x; $prev = $x; $out += $x }
  $path1[$i] = $out[0]
  $p = P (1520 * $RS) $a; $nt = Add-Node $p[0] $p[1] "notable" $th.nots[0][0] $th.nots[0][1] $col $c; Link $prev $nt
  $p = P (1660 * $RS) $a; $ks = Add-Node $p[0] $p[1] "key" $th.key[0] $th.key[1] $col $c; Link $nt $ks
  # two wheels: an entry small, four smalls around a notable
  foreach ($w in 0, 1) {
    $from = $out[1 + 2 * $w]; $ang = $a + ($(if ($w -eq 0) { -1 } else { 1 })) * $d * 0.3; $cr = (1150 + 200 * $w) * $RS
    $cp = P $cr $ang; $m = $th.small[$si++ % 4]; $ep = P ($cr - 40) ($a + ($ang - $a) * 0.45)
    $entry = Add-Node $ep[0] $ep[1] "small" (SmallName $m) $m $col $c; Link $from $entry
    $center = Add-Node $cp[0] $cp[1] "notable" $th.nots[1 + $w][0] $th.nots[1 + $w][1] $col $c
    $wheel = @()
    foreach ($k in 0..3) { $wt = ($k * 90 + $ang) * [math]::PI / 180; $m = $th.small[$si++ % 4]; $x = Add-Node ($cp[0] + [math]::Cos($wt) * 75) ($cp[1] + [math]::Sin($wt) * 75) "small" (SmallName $m) $m $col $c; $wheel += $x; Link $x $center }
    Link $entry $wheel[2]; foreach ($k in 0..3) { Link $wheel[$k] $wheel[($k + 1) % 4] }
  }
}
# inner ring closes around; bridges between neighbouring wedges with a hybrid notable in the middle
for ($i = 0; $i -lt $n; $i++) {
  $j = ($i + 1) % $n; Link $ring[$i][2] $ring[$j][0]
  $ca = $classes[$i]; $cb = $classes[$j]; $a = -90 + $i * $d; $prev = $path1[$i]
  foreach ($k in 1..5) {
    $p = P ((1000 + [math]::Sin($k / 6 * [math]::PI) * 70) * $RS) ($a + $d * $k / 6)
    if ($k -eq 3) {
      $mods = [ordered]@{}; foreach ($src in $Theme[$ca].small[0], $Theme[$cb].small[0], $Theme[$ca].small[1]) { foreach ($kk in @($src.Keys)) { $mods[$kk] = [math]::Round(([double]$mods[$kk] + 2 * $src[$kk]), 2) } }
      $x = Add-Node $p[0] $p[1] "notable" ("Path of " + $names[$ca] + " and " + $names[$cb]) $mods "#e3b04b" "hybrid"
    } else { $th = $(if ($k -lt 3) { $Theme[$ca] } else { $Theme[$cb] }); $m = $th.small[($k + $i) % 4]; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#d8c890" "hybrid" }
    Link $prev $x; $prev = $x
  }
  Link $prev $path1[$j]
}
foreach ($nd in $nodes) { $nd.adj = @($adj[$nd.id] | Sort-Object) }
$flags = [ordered]@{
  cleave = "Power attacks and combo finishers strike every enemy around you."; multi = "Bows loose three arrows at once, each for 55% damage."; pierce = "Arrows and magic bolts pass through enemies."
  manaShield = "35% of damage you take is paid from your mana first."; undying = "Once every 3 minutes, a killing blow leaves you at 30% health instead."; rage = "You deal up to 50% more damage per point the more health you are missing."
}
$out = [ordered]@{ _help = "Generated by tools/gen_tree.ps1 (edit that, then run it). starts = class -> start node (free for that class). Types: start, branch (a themed path's first node: costs a point), small, notable, key (keystone), hub. Mods use the stat keys in docs/STATS.md. rings = radii of the faint background circles. The advanced tree is data/skilltree_adv.json."
  version = $version; rings = @((400 * $RS), (900 * $RS), (1520 * $RS)); starts = $starts; flags = $flags; nodes = $nodes }
$json = $out | ConvertTo-Json -Depth 6 -Compress
[IO.File]::WriteAllText((Join-Path $root "data\skilltree.json"), $json, (New-Object Text.UTF8Encoding $false))
"base nodes: $($nodes.Count)"

# ------------------------------------------------------------------ advanced tree (A-15)
# id, name, base wedge, description, gate mods, three small kinds, then four named nodes: skills (class skill upgrades), passive, pact (a
# price), capstone. Small names come from $keyName.
$Adv = @(
 @{ id="warlord"; n="Warlord"; base="warrior"; d="The Warrior who leads the line: heavier blows that sweep the whole fight."; gate=(M meleeP 6 def 6); small=@((M meleeP 5),(M hp 20),(M block 4))
    a=@("Veteran's Drill",(M skill_cleave 1 skill_shieldbash 1 skill_taunt 1)); b=@("Banner of Iron",(M def 20 hpP 8 tenacity 15)); c=@("No Retreat",(M meleeP 25 powerP 25 move -8)); k=@("Warlord",(M cleave 1 meleeP 20 hpP 12 block 15 aspd -8)) }
 @{ id="champion"; n="Champion"; base="gladiator"; d="The Gladiator the crowd came for: crushing power attacks and finishers nothing interrupts."; gate=(M powerP 10 st 15); small=@((M powerP 6),(M finisherP 6),(M tenacity 4))
    a=@("Signature Moves",(M school_attack 1)); b=@("Roar of the Crowd",(M finisherStun 0.5 finisherUp 2 knockP 25)); c=@("Glory or Death",(M critD 40 powerP 30 def -20)); k=@("Champion of the Sands",(M powerP 50 finisherP 40 ccImmune 1 healIn -30)) }
 @{ id="juggernaut"; n="Juggernaut"; base="berserker"; d="The Berserker who cannot be stopped: fury that feeds on wounds."; gate=(M meleeP 8 hp 20); small=@((M meleeP 6),(M aspd 3),(M greatswordP 8))
    a=@("Endless Whirl",(M skill_whirlwind 2 skill_leap 1 skill_warcry 1)); b=@("Blood Frenzy",(M leech 2.5 aspd 10)); c=@("Pain Is Fuel",(M rage 0.5 meleeP 15 healIn -25)); k=@("Juggernaut",(M rage 1 ccImmune 1 meleeP 25 def -25)) }
 @{ id="kensei"; n="Kensei"; base="samurai"; d="The Samurai as sword saint: one perfect cut at a time."; gate=(M katanaP 8 crit 2); small=@((M katanaP 6),(M critD 6),(M aspd 3))
    a=@("Sword Saint's Forms",(M skill_iaido 2 skill_crescentslash 1 skill_thousandcuts 1)); b=@("Mind Like Water",(M tenacity 20 evade 6 cdr 5)); c=@("Glass Edge",(M critD 50 crit 6 hpP -12)); k=@("Kensei",(M katanaP 35 crit 10 armorPenP 25 finisherP 30 block -30)) }
 @{ id="dragoon"; n="Dragoon"; base="lancer"; d="The Lancer who fights from the air: longer reach, higher leaps, harder landings."; gate=(M reach 8 spearP 8); small=@((M spearP 6),(M reach 4),(M knockP 8))
    a=@("Dragoon Drills",(M skill_lunge 2 skill_leap 2)); b=@("Sky Piercer",(M finisherUp 4 finisherP 25 jumpP 30)); c=@("Overextend",(M reach 25 spearP 20 def -15)); k=@("Dragoon",(M reach 30 spearP 30 armorPenP 25 powerP 30 aspd -10)) }
 @{ id="grandmaster"; n="Grandmaster"; base="monk"; d="The Monk at the end of the road: fists that need no weapon and a body that needs no armor."; gate=(M unarmedP 10 evade 2); small=@((M unarmedP 6),(M aspd 3),(M evade 1.5))
    a=@("Thousand Forms",(M skill_palmstrike 2 skill_cyclonekick 1 skill_chiwave 1)); b=@("Diamond Body",(M tenacity 25 regenHp 3 res_poison 15 res_fire 10)); c=@("Vow of Poverty",(M unarmedP 30 evade 8 goldP -50)); k=@("Grandmaster",(M unarmedP 50 aspd 15 critD 30 finisherStun 0.6 def -20)) }
 @{ id="sharpshooter"; n="Sharpshooter"; base="rogue"; d="The Ranger who never misses: arrows that pass through everything in line."; gate=(M rangedP 8 crit 2); small=@((M rangedP 6),(M crit 1.5),(M critD 6))
    a=@("Marksman's Drills",(M skill_volley 2 skill_rain 2)); b=@("Killing Wind",(M armorPenP 20 bowP 15 aspd 6)); c=@("Deadeye",(M critD 60 rangedP 15 move -10)); k=@("Sharpshooter",(M pierce 1 rangedP 30 crit 10 critD 30 hpP -10)) }
 @{ id="pathfinder"; n="Pathfinder"; base="wayfarer"; d="The Wayfarer who has walked every road: nothing slows you and everything teaches you."; gate=(M move 3 st 20); small=@((M move 2),(M st 12),(M xpP 3))
    a=@("Fleet Techniques",(M school_movement 1 cdr 4)); b=@("Mountain Goat",(M jumpP 40 climbP 40 swimP 40 breathP 60)); c=@("Travel Light",(M move 10 evade 6 hpP -10)); k=@("Pathfinder",(M move 15 xpP 20 rideP 30 st 60 evade 8 def -15)) }
 @{ id="shadowblade"; n="Shadowblade"; base="assassin"; d="The Assassin become a shadow: killing crits, and a shade's shape to walk unseen."; gate=(M crit 3 daggerP 8); small=@((M critD 7),(M daggerP 6),(M poisonP 6))
    a=@("Assassin's Arts",(M skill_shadowstep 2 skill_poisonblade 1 skill_fan 1)); b=@("Living Shadow",(M form_shade 1 evade 8 stealthP 40 sneakP 30)); c=@("Bleed Them Dry",(M poisonChance 20 bleedChance 20 statusP 30 hpP -10)); k=@("Shadowblade",(M critD 75 crit 8 armorPenP 20 leech 2 def -25 hpP -10)) }
 @{ id="fatespinner"; n="Fatespinner"; base="fortune"; d="The Fortune Seeker who bends luck itself: richer drops, luckier strikes, thinner armor."; gate=(M luck 3 goldP 10); small=@((M luck 1.5),(M goldP 6),(M crit 1.5))
    a=@("Tricks of the Trade",(M school_trap 1 school_projectile 1)); b=@("Midas Touch",(M goldP 40 luck 8)); c=@("Double or Nothing",(M critD 60 crit 5 hpP -15)); k=@("Fatespinner",(M luck 20 xpP 15 crit 10 evade 8 def -20 mr -20)) }
 @{ id="doomcaller"; n="Doomcaller"; base="warlock"; d="The Warlock whose curses do not end: every affliction burns longer and deeper."; gate=(M statusP 8 shadowP 6); small=@((M statusP 6),(M shadowP 6),(M fireP 6))
    a=@("Blackest Arts",(M skill_hex 2 skill_shadowbolt 1 skill_drain 1)); b=@("Hellfire",(M burnChance 15 burnP 30 fireP 15)); c=@("Soul Bargain",(M spellP 25 mcost -15 hpP -15)); k=@("Doomcaller",(M statusP 60 shadowP 25 fireP 25 weakenChance 15 healIn -30)) }
 @{ id="lichlord"; n="Lich Lord"; base="necromancer"; d="The Necromancer past death: you drain the living, wear a wraith's shape, and healing barely reaches you."; gate=(M shadowP 8 leech 1); small=@((M shadowP 6),(M mp 15),(M leech 0.5))
    a=@("Rites of Undeath",(M skill_shadowbolt 2 skill_drain 2)); b=@("Shroud of the Grave",(M form_wraith 1 res_shadow 20 mr 10)); c=@("Phylactery",(M undying 1 hpP -20)); k=@("Lich Lord",(M shadowP 40 leech 4 mleech 2 magicPenP 20 healIn -40 res_holy -30)) }
 @{ id="timelord"; n="Timelord"; base="chronomancer"; d="The Chronomancer who owns the clock: everything of yours is ready sooner."; gate=(M cdr 3 castSpd 5); small=@((M cdr 1.5),(M castSpd 4),(M mp 15))
    a=@("Mastery of Hours",(M skill_chronobolt 2 skill_stasis 1 skill_temporalrift 1)); b=@("Time Dilation",(M move 8 aspd 8 evade 5)); c=@("Borrowed Tomorrow",(M cdr 10 mcost 20)); k=@("Timelord",(M cdr 15 castSpd 25 statusP 30 freezeChance 6 hpP -12)) }
 @{ id="archmage"; n="Archmage"; base="mage"; d="The Mage at full power: stronger spells, wider spells, and nothing between you and a blade."; gate=(M spellP 8 mp 20); small=@((M spellP 6),(M mp 15),(M regenMp 0.6))
    a=@("Arch-Spells",(M skill_fire 2 skill_nova 1 skill_zap 1)); b=@("Spell Weaving",(M castSpd 15 cdr 8 aoeP 15)); c=@("Overchannel",(M spellP 30 mcost 25)); k=@("Archmage",(M spellP 40 magicPenP 25 mpP 25 aoeP 20 def -20 hpP -10)) }
 @{ id="sovereign"; n="Elemental Sovereign"; base="elementalist"; d="The Elementalist who rules all three: fire, frost and lightning as one, and little use for steel."; gate=(M fireP 6 frostP 6 lightningP 6); small=@((M fireP 5),(M frostP 5),(M lightningP 5))
    a=@("Elemental Schools",(M school_fire 1 school_frost 1 school_lightning 1)); b=@("Conflux",(M burnChance 12 chillChance 12 shockChance 12 statusP 20)); c=@("Unstable Core",(M aoeP 30 skillP 15 mcost 20)); k=@("Elemental Sovereign",(M fireP 35 frostP 35 lightningP 35 magicPenP 20 chain 1 physicalP -40 res_shadow -20)) }
 @{ id="stormcaller"; n="Stormcaller"; base="shaman"; d="The Shaman the sky answers: lightning that leaps and leaps again, and a spirit of stone to wear."; gate=(M lightningP 8 healP 6); small=@((M lightningP 6),(M healP 5),(M mp 15))
    a=@("Voice of the Storm",(M skill_stormtotem 2 skill_lavaburst 1 skill_healingspring 1)); b=@("Stone Spirit",(M form_golem 1 def 12 res_lightning 15)); c=@("Lightning Rod",(M lightningP 30 shockChance 20 res_lightning -30)); k=@("Stormcaller",(M lightningP 40 chain 3 shockChance 15 aoeP 20 mcost 20)) }
 @{ id="archdruid"; n="Archdruid"; base="druid"; d="The Druid as the wild itself: deeper regrowth, stronger forms, and wings. Fire is your enemy."; gate=(M poisonP 8 regenHp 1); small=@((M poisonP 6),(M regenHp 0.8),(M thorns 3))
    a=@("Old Growth",(M skill_thornvolley 2 skill_entangle 2)); b=@("Sky Shape",(M form_drake 1 formDurP 25 jumpP 20)); c=@("Bark Skin",(M def 30 thorns 25 move -8 aspd -5)); k=@("Archdruid",(M formP 30 regenHp 5 healP 30 poisonP 30 rootChance 10 fireP -30 res_fire -25)) }
 @{ id="beastlord"; n="Beast Lord"; base="shapeshifter"; d="The Shapeshifter who would rather not change back: forms that last, hit hard and return fast."; gate=(M formP 8 formDurP 10); small=@((M formP 6),(M formDurP 8),(M hp 20))
    a=@("Many Skins",(M form_fox 1 formCdr 20)); b=@("Apex Predator",(M formP 25 crit 5 leech 1.5)); c=@("Lost to the Wild",(M formP 30 formDurP 50 spellP -25 mpP -15)); k=@("Beast Lord",(M formP 50 formDurP 100 formCdr 30 hpP 10 skillP -30)) }
 @{ id="maestro"; n="Maestro"; base="bard"; d="The Bard at the height of the art: every song faster, wider and stronger."; gate=(M castSpd 5 healP 6); small=@((M castSpd 4),(M healP 5),(M allP 2))
    a=@("Master Works",(M skill_sonicriff 2 skill_balladheroes 1 skill_mendingverse 1)); b=@("Standing Ovation",(M allP 8 aoeP 20 goldP 20)); c=@("Encore",(M cdr 12 mcost 20)); k=@("Maestro",(M allP 12 castSpd 25 healP 30 aoeP 25 hpP -15)) }
 @{ id="highpriest"; n="High Priest"; base="cleric"; d="The Cleric as the voice of the light: the strongest healing there is, and no hand for weapons."; gate=(M healP 10 holyP 6); small=@((M healP 6),(M holyP 6),(M mp 15))
    a=@("Greater Miracles",(M skill_heal 2 skill_smite 1 skill_consecrate 1)); b=@("Halo",(M regenHp 3 res_shadow 20 mr 12 healIn 15)); c=@("Martyr",(M healP 40 hpP -15)); k=@("High Priest",(M healP 60 holyP 30 mpP 20 judgedChance 15 meleeP -30 rangedP -30)) }
 @{ id="crusader"; n="Crusader"; base="paladin"; d="The Paladin on the march: judgement on every blow, behind a wall of faith."; gate=(M holyP 8 def 6); small=@((M holyP 6),(M def 5),(M maceP 8))
    a=@("Holy Orders",(M skill_smite 2 skill_consecrate 1 skill_shieldbash 1)); b=@("Bulwark of Faith",(M block 15 mr 15 hpP 8)); c=@("Zeal",(M holyP 25 aspd 10 mcost 20)); k=@("Crusader",(M holyP 40 maceP 30 judgedChance 20 finisherStun 0.5 thorns 20 move -8)) }
 @{ id="runelord"; n="Runelord"; base="spellblade"; d="The Spellblade whose sword is a spell: Magic pours into every swing."; gate=(M magToDmg 0.2 spellP 5); small=@((M magToDmg 0.1),(M spellP 4),(M meleeP 4))
    a=@("Rune Mastery",(M skill_arcanestrike 2 skill_zap 1 skill_cleave 1)); b=@("Storm Blade",(M lightningP 20 shockChance 12 chain 1)); c=@("Arcane Overload",(M magToDmg 0.6 mcost 25)); k=@("Runelord",(M magToDmg 1 spellP 20 meleeP 20 mleech 2 hpP -12 def -15)) }
 @{ id="ironclad"; n="Ironclad"; base="warden"; d="The Warden as a walking wall: nothing gets through, and nothing hurries you."; gate=(M hp 30 def 8); small=@((M hp 20),(M axeP 7),(M bleedP 8))
    a=@("Line Holder",(M skill_warcry 2 skill_shieldbash 1 skill_taunt 1)); b=@("Rend",(M bleedChance 20 bleedP 30 armorPenP 15)); c=@("Rooted",(M def 30 tenacity 25 move -10)); k=@("Ironclad",(M def 45 hpP 20 thorns 30 res_fire 15 res_frost 15 res_lightning 15 aspd -12 move -8)) }
)
$nodes = New-Object System.Collections.ArrayList; $adj = @{}
$paths = [ordered]@{}
foreach ($ap in $Adv) {
  $i = [array]::IndexOf($classes, $ap.base); if ($i -lt 0) { throw "advanced path $($ap.id): no base wedge $($ap.base)" }
  $a = -90 + $i * $d; $col = $Theme[$ap.base].col; $si = 0
  $p = P 700 $a; $g = Add-Node $p[0] $p[1] "gate" $ap.n $ap.gate $col $ap.id $ap.d
  $paths[$ap.id] = [ordered]@{ n = $ap.n; base = $ap.base; baseName = $names[$ap.base]; root = $g; d = $ap.d; col = $col }
  $prev = $g; $sp = @()
  foreach ($r in 850, 1000, 1150, 1300, 1450, 1600) { $m = $ap.small[$si++ % 3]; $p = P $r $a; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m $col $ap.id; Link $prev $x; $prev = $x; $sp += $x }
  $p = P 1780 $a; $ks = Add-Node $p[0] $p[1] "key" $ap.k[0] $ap.k[1] $col $ap.id; Link $prev $ks
  # skills notable off the second small; passive behind a small off the third; pact behind a small off the fifth
  $p = P 1040 ($a - $d * 0.33); $x = Add-Node $p[0] $p[1] "notable" $ap.a[0] $ap.a[1] $col $ap.id; Link $sp[1] $x
  $m = $ap.small[$si++ % 3]; $p = P 1200 ($a + $d * 0.28); $s1 = Add-Node $p[0] $p[1] "small" (SmallName $m) $m $col $ap.id; Link $sp[2] $s1
  $p = P 1330 ($a + $d * 0.36); $x = Add-Node $p[0] $p[1] "notable" $ap.b[0] $ap.b[1] $col $ap.id; Link $s1 $x
  $m = $ap.small[$si++ % 3]; $p = P 1500 ($a - $d * 0.24); $s2 = Add-Node $p[0] $p[1] "small" (SmallName $m) $m $col $ap.id; Link $sp[4] $s2
  $p = P 1640 ($a - $d * 0.3); $x = Add-Node $p[0] $p[1] "notable" $ap.c[0] $ap.c[1] $col $ap.id "A pact: power with a price."; Link $s2 $x
}
foreach ($c in $classes) { if (-not ($Adv | Where-Object { $_.base -eq $c })) { throw "no advanced path for $c" } }
foreach ($nd in $nodes) { $nd.adj = @($adj[$nd.id] | Sort-Object) }
$out = [ordered]@{ _help = "Generated by tools/gen_tree.ps1 (edit that, then run it). The ADVANCED tree (A-15): a second layer of the skill tree window. unlock.points = points a hero must have invested in the base tree before any advanced node can be taken; a path's gate also needs its base path's keystone OR unlock.armNodes nodes of that base path. Advanced nodes cost the same skill tree points (data/progression.json levelUp.tree per level). paths: advanced id -> name, base path (arm in data/skilltree.json), root = its gate node (ids here are local; the game adds the base tree's node count). Node types: gate, small, notable, key (capstone)."
  version = $version; unlock = [ordered]@{ points = 100; armNodes = 8 }; rings = @(700, 1780); paths = $paths; nodes = $nodes }
$json = $out | ConvertTo-Json -Depth 6 -Compress
[IO.File]::WriteAllText((Join-Path $root "data\skilltree_adv.json"), $json, (New-Object Text.UTF8Encoding $false))
"advanced nodes: $($nodes.Count), paths: $($paths.Count)"
