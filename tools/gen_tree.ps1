# Generates data/skilltree.json: a large passive tree in the spirit of Path of Exile.
#   powershell -File tools/gen_tree.ps1
# Layout: 13 class wedges around a centre. Each wedge has the class start (free for that class), an outward path to a notable
# and a keystone, two notable clusters (wheels), an inward path to a shared inner ring, and a bridge to the next class with a
# hybrid notable in the middle, so builds can mix any neighbouring classes and reach far ones through the ring and the centre.
# Every stat key used here must be read by the game (TreeDb.Known lists them; the bot checks it).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$classes = @("warrior","berserker","lancer","rogue","assassin","warlock","necromancer","mage","druid","cleric","paladin","spellblade","warden")
$names = @{ warrior="Warrior"; berserker="Berserker"; lancer="Lancer"; rogue="Ranger"; assassin="Assassin"; warlock="Warlock"; necromancer="Necromancer"; mage="Mage"; druid="Druid"; cleric="Cleric"; paladin="Paladin"; spellblade="Spellblade"; warden="Warden" }
$Theme = @{
 warrior     = @{ col="#e0584a"; small=@(@{meleeP=4},@{hp=14},@{def=4},@{block=3}); nots=@(@("Shield Wall",@{block=10;def=10}),@("Veteran",@{hpP=8;def=6}),@("Sword Master",@{swordP=15;aspd=5})); key=@("Unbreakable",@{def=25;block=15;tenacity=30;move=-5}) }
 berserker   = @{ col="#c23a1e"; small=@(@{meleeP=5},@{aspd=3},@{hp=10},@{greatswordP=6}); nots=@(@("Bloodlust",@{leech=1.5;meleeP=10}),@("Savage Edge",@{critD=20;greatswordP=10}),@("Battle Trance",@{aspd=8;powerP=20})); key=@("Blood Rage",@{rage=1.5;meleeP=15;hpP=-10}) }
 lancer      = @{ col="#4a7ac8"; small=@(@{reach=4},@{knockP=6},@{spearP=6},@{meleeP=3}); nots=@(@("Long Reach",@{reach=12;spearP=10}),@("Launcher",@{finisherUp=3;finisherP=15}),@("Skirmisher",@{move=6;evade=4})); key=@("Skewer",@{reach=20;finisherUp=4;knockP=30;finisherP=20}) }
 rogue       = @{ col="#4ad070"; small=@(@{rangedP=5},@{crit=1},@{move=2},@{evade=1.5}); nots=@(@("Eagle Eye",@{rangedP=12;crit=3}),@("Fleet Foot",@{move=8;evade=5}),@("Piercing Shots",@{pierce=1;armorPen=6})); key=@("Hawk Eye",@{multi=1;rangedP=10}) }
 assassin    = @{ col="#8a8aa8"; small=@(@{crit=1.2},@{critD=6},@{daggerP=6},@{poisonP=5}); nots=@(@("Ambush",@{critD=25;crit=3}),@("Venomous",@{poisonChance=10;poisonP=15}),@("Shadow Dance",@{evade=6;aspd=6})); key=@("Death Mark",@{critD=50;crit=6;hpP=-10}) }
 warlock     = @{ col="#b04ad0"; small=@(@{statusP=5},@{shadowP=5},@{fireP=5},@{magicPen=2}); nots=@(@("Hexer",@{statusP=15;burnChance=8}),@("Withering",@{shadowP=12;magicPenP=8}),@("Soul Fire",@{fireP=12;burnP=20})); key=@("Plague Lord",@{statusP=40;poisonP=30;poisonChance=10}) }
 necromancer = @{ col="#8a3a8a"; small=@(@{shadowP=5},@{leech=0.4},@{mp=10},@{mr=3}); nots=@(@("Grave Pact",@{leech=2;shadowP=10}),@("Bone Armor",@{def=10;mr=8}),@("Dark Ritual",@{cdr=6;shadowP=8})); key=@("Soul Eater",@{leech=3;shadowP=20;regenHp=-1}) }
 mage        = @{ col="#5a86ff"; small=@(@{spellP=5},@{mp=12},@{regenMp=0.4},@{cdr=1.5}); nots=@(@("Arcane Mind",@{spellP=12;mpP=8}),@("Elemental Focus",@{fireP=8;frostP=8;lightningP=8}),@("Quick Casting",@{castSpd=10;cdr=5})); key=@("Mana Shield",@{manaShield=1;mpP=20}) }
 druid       = @{ col="#3aa04a"; small=@(@{poisonP=5},@{regenHp=0.5},@{thorns=2},@{hp=10}); nots=@(@("Thornskin",@{thorns=10;def=6}),@("Regrowth",@{regenHp=2;healP=10}),@("Wild Venom",@{poisonChance=8;poisonP=12})); key=@("Wild Growth",@{regenHp=3;thorns=15;poisonP=15;aoeP=10}) }
 cleric      = @{ col="#e8d880"; small=@(@{healP=5},@{holyP=5},@{mp=10},@{res_shadow=3}); nots=@(@("Blessed",@{healP=15;regenHp=1}),@("Radiance",@{holyP=12;aoeP=8}),@("Sanctuary",@{res_shadow=10;res_fire=6;res_frost=6})); key=@("Divine Grace",@{healP=40;holyP=10;mpP=10}) }
 paladin     = @{ col="#f0c040"; small=@(@{holyP=4},@{def=4},@{maceP=6},@{block=2}); nots=@(@("Righteous",@{holyP=10;maceP=10}),@("Aegis",@{block=10;mr=10}),@("Zealot",@{finisherStun=0.3;finisherP=15})); key=@("Undying",@{undying=1;holyP=10}) }
 spellblade  = @{ col="#9a6ae0"; small=@(@{magToDmg=0.08},@{spellP=3},@{meleeP=3},@{lightningP=4}); nots=@(@("Runeblade",@{magToDmg=0.3;swordP=8}),@("Storm Edge",@{lightningP=12;chain=1}),@("Battle Mage",@{spellP=8;meleeP=8})); key=@("Spellsword",@{magToDmg=0.8;cleave=1;mcost=10}) }
 warden      = @{ col="#8a9a4a"; small=@(@{hp=12},@{axeP=6},@{bleedP=6},@{armorPen=2}); nots=@(@("Iron Hide",@{def=12;hpP=6}),@("Hamstring",@{bleedChance=10;bleedP=15}),@("Sunder",@{armorPenP=12;axeP=8})); key=@("Iron Fortress",@{def=30;res_fire=10;res_frost=10;res_lightning=10;res_poison=10;res_shadow=10;move=-6}) }
}
$generic = @(@{hp=10},@{allP=2},@{mp=8},@{def=3},@{st=8},@{mr=3},@{res_fire=4},@{res_frost=4},@{res_lightning=4},@{res_poison=4},@{res_shadow=4},@{critD=4},@{xpP=2},@{skillP=3},@{aoeP=3},@{armorPen=1},@{magicPen=1},@{tenacity=3},@{cdr=1})
$keyName = @{ meleeP="Might"; hp="Vigor"; def="Toughness"; block="Guard"; aspd="Haste"; greatswordP="Greatblade"; reach="Reach"; knockP="Impact"; spearP="Spearwork"; rangedP="Marksman"; crit="Precision"; move="Swiftness"; evade="Evasion"; critD="Cruelty"; daggerP="Knifework"; poisonP="Toxins"; statusP="Affliction"; shadowP="Darkness"; fireP="Flame"; magicPen="Arcane Edge"; leech="Thirst"; mp="Wisdom"; mr="Warding"; spellP="Sorcery"; regenMp="Clarity"; cdr="Focus"; regenHp="Renewal"; thorns="Brambles"; healP="Mercy"; holyP="Light"; res_shadow="Shadow Ward"; maceP="Maceplay"; magToDmg="Rune Edge"; lightningP="Storm"; axeP="Axemanship"; bleedP="Laceration"; armorPen="Piercing"; allP="Power"; st="Endurance"; res_fire="Fire Ward"; res_frost="Frost Ward"; res_lightning="Storm Ward"; res_poison="Poison Ward"; xpP="Learning"; skillP="Technique"; aoeP="Reach of Spells"; tenacity="Tenacity" }

$nodes = New-Object System.Collections.ArrayList
$adj = @{}
function Add-Node($x, $y, $type, $name, $mods, $col, $arm) {
  $id = $nodes.Count
  [void]$nodes.Add([ordered]@{ id=$id; x=[math]::Round($x,1); y=[math]::Round($y,1); type=$type; name=$name; mods=$mods; col=$col; arm=$arm })
  $adj[$id] = New-Object System.Collections.Generic.HashSet[int]
  return $id
}
function Link($a, $b) { [void]$adj[$a].Add($b); [void]$adj[$b].Add($a) }
function P($r, $deg) { $rad = $deg * [math]::PI / 180; return @(([math]::Cos($rad) * $r), ([math]::Sin($rad) * $r)) }
function SmallName($mods) { $k = @($mods.Keys)[0]; if ($keyName.ContainsKey($k)) { return $keyName[$k] } return $k }

$n = $classes.Count; $d = 360.0 / $n
$origin = Add-Node 0 0 "hub" "Heart of the Rift" @{allP=6;hpP=4} "#e3b04b" ""
$starts = [ordered]@{}; $ring = @{}; $path1 = @{}; $gi = 0
for ($i = 0; $i -lt $n; $i++) {
  $c = $classes[$i]; $th = $Theme[$c]; $a = -90 + $i * $d; $col = $th.col; $si = 0
  $p = P 900 $a; $s = Add-Node $p[0] $p[1] "start" $names[$c] @{} $col $c; $starts[$c] = $s
  # inward path to the inner ring, with a spoke to the centre
  $prev = $s
  foreach ($r in 780, 660, 540) { $m = $generic[$gi++ % $generic.Count]; $p = P $r $a; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" $c; Link $prev $x; $prev = $x }
  $ring[$i] = @()
  foreach ($k in 0, 1, 2) { $m = $generic[$gi++ % $generic.Count]; $p = P 400 ($a + $k * $d / 3); $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" ""; $ring[$i] += $x }
  Link $prev $ring[$i][0]; Link $ring[$i][0] $ring[$i][1]; Link $ring[$i][1] $ring[$i][2]
  $m = $generic[$gi++ % $generic.Count]; $p = P 200 $a; $sp = Add-Node $p[0] $p[1] "small" (SmallName $m) $m "#b8b0a0" ""; Link $ring[$i][0] $sp; Link $sp $origin
  # outward path: five class smalls, a notable, the keystone
  $prev = $s; $out = @()
  foreach ($r in 1000, 1100, 1200, 1300, 1400) { $m = $th.small[$si++ % 4]; $p = P $r $a; $x = Add-Node $p[0] $p[1] "small" (SmallName $m) $m $col $c; Link $prev $x; $prev = $x; $out += $x }
  $path1[$i] = $out[0]
  $p = P 1520 $a; $nt = Add-Node $p[0] $p[1] "notable" $th.nots[0][0] $th.nots[0][1] $col $c; Link $prev $nt
  $p = P 1660 $a; $ks = Add-Node $p[0] $p[1] "key" $th.key[0] $th.key[1] $col $c; Link $nt $ks
  # two wheels: an entry small, four smalls around a notable
  foreach ($w in 0, 1) {
    $from = $out[1 + 2 * $w]; $ang = $a + ($(if ($w -eq 0) { -1 } else { 1 })) * $d * 0.3; $cr = 1150 + 200 * $w
    $cp = P $cr $ang; $m = $th.small[$si++ % 4]; $ep = P ($cr - 40) ($a + ($ang - $a) * 0.45)
    $entry = Add-Node $ep[0] $ep[1] "small" (SmallName $m) $m $col $c; Link $from $entry
    $center = Add-Node $cp[0] $cp[1] "notable" $th.nots[1 + $w][0] $th.nots[1 + $w][1] $col $c
    $wheel = @()
    foreach ($k in 0..3) { $wt = ($k * 90 + $ang) * [math]::PI / 180; $m = $th.small[$si++ % 4]; $x = Add-Node ($cp[0] + [math]::Cos($wt) * 75) ($cp[1] + [math]::Sin($wt) * 75) "small" (SmallName $m) $m $col $c; $wheel += $x; Link $x $center }
    Link $entry $wheel[2]; foreach ($k in 0..3) { Link $wheel[$k] $wheel[($k + 1) % 4] }
  }
}
# inner ring closes around; bridges between neighbouring classes with a hybrid notable in the middle
for ($i = 0; $i -lt $n; $i++) {
  $j = ($i + 1) % $n; Link $ring[$i][2] $ring[$j][0]
  $ca = $classes[$i]; $cb = $classes[$j]; $a = -90 + $i * $d; $prev = $path1[$i]
  foreach ($k in 1..5) {
    $p = P (1000 + [math]::Sin($k / 6 * [math]::PI) * 70) ($a + $d * $k / 6)
    if ($k -eq 3) {
      $mods = @{}; foreach ($src in $Theme[$ca].small[0], $Theme[$cb].small[0], $Theme[$ca].small[1]) { foreach ($kk in $src.Keys) { $mods[$kk] = [math]::Round(([double]$mods[$kk] + 2 * $src[$kk]), 2) } }
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
$out = [ordered]@{ _help = "Generated by tools/gen_tree.ps1 (edit that, then run it). starts = class -> start node (free for that class). Types: start, small, notable, key (keystone), hub. Mods use the stat keys in docs/STATS.md."; version = 2; starts = $starts; flags = $flags; nodes = $nodes }
$json = $out | ConvertTo-Json -Depth 6 -Compress
[IO.File]::WriteAllText((Join-Path $root "data\skilltree.json"), $json, (New-Object Text.UTF8Encoding $false))
"nodes: $($nodes.Count)"
