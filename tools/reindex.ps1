# Dev loop info web: refreshes .devloop/files.json (every .cs/.tscn/.tres/.gdshader -> path, lines, hash8, system, summary)
# and prints the files whose hash changed (or are new / gone) so only those need a fresh look.
#   powershell -File tools/reindex.ps1          -> update files.json, list changed files
#   powershell -File tools/reindex.ps1 -check   -> only list changes, write nothing
# A summary written by hand (field "manual": true) is kept while the file is unchanged; otherwise the summary is the
# file's first /// <summary> line (or first comment), cut to 15 words.
param([switch]$check)
Set-Location (Join-Path $PSScriptRoot "..")
$out = ".devloop/files.json"
$old = @{}
if (Test-Path $out) { foreach ($e in (Get-Content $out -Raw | ConvertFrom-Json)) { $old[$e.path] = $e } }

# file name -> system card (first match wins); cards live in .devloop/systems/<system>.md
$map = [ordered]@{
  '^scripts/dev/' = 'devtools'; 'Bot|Shots|Showcase|Observer|FitBot|Perf|AnimTool|Flag\.cs|OnlineBot|MpBot' = 'devtools'
  'Block' = 'blockworld'; 'Spellcraft|spellcard' = 'spellcraft'; 'Dungeon|LairIntro' = 'dungeons'; 'Arena' = 'arena'
  'Net|Online|Updater|Server|RemoteHero|Accounts' = 'network'; 'Enemy|Threat|Targeting|Wildlife|Npc' = 'enemies'
  'Camera' = 'camera'; 'Avatar|Anim|HeroRig|Hair|Outfit|Kit\.cs|FootIK|Wings' = 'animation'
  'Sfx|Music|Audio' = 'audio'
  'PlayerSkill|/Skills\.cs|Vfx|SpellFx|Projectile|SwordTrail|GearFx|/Fx\.cs' = 'skills_vfx'
  'Combat|Feel|Status|Attacks' = 'combat'; 'Quests|Progression|Tree|ModTree|Valor|Titles|Affixes|Classes|Races' = 'progression'
  'Player|Inputs|Mount|Downed' = 'player'; 'Weapon|Item|Loot|Craft|Shop|Stash|Home|Smith' = 'loot'
  'Hud|Icons|/Ui|Widget|Theme|MapMarks' = 'ui'
  'Lands|World|Builder|Terrain|Flora|Grass|Water|Sky|Ambient|Gothic|Gfx|Toon|Genshin|Look|Props|Gather|Interact|gdshader' = 'world'
  'Game|Save|Stats|Json|Tune|Util|Main|Setup' = 'core'
}
function Sys($p) { foreach ($k in $map.Keys) { if ($p -cmatch $k) { return $map[$k] } }; if ($p -match '\.tres$') { return 'world' }; return 'core' }
function Summ($full) {
  $lines = Get-Content $full -TotalCount 40 -ErrorAction SilentlyContinue
  $t = ""
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i].Trim()
    if ($l -match '^///\s*<summary>\s*(.*)$') { $t = $Matches[1]; if (-not $t -and $i + 1 -lt $lines.Count) { $t = ($lines[$i + 1].Trim() -replace '^///\s*', '') }; break }
    if (-not $t -and $l -match '^(//+|#)\s*(\S.*)$' -and $l -notmatch '^#\s*(include|define)') { $t = $Matches[2] }
  }
  $t = ($t -replace '</?\w+[^>]*>', '' -replace '\s+', ' ').Trim()
  $w = $t -split ' '; if ($w.Count -gt 15) { $t = ($w[0..14] -join ' ') + '...' }
  return $t
}

$files = Get-ChildItem -Recurse -File -Include *.cs, *.tscn, *.tres, *.gdshader | Where-Object { $_.FullName -notmatch '\\(\.godot|bin|obj|build|build_stage|build_setup|installer\\obj|addons)\\' }
$root = (Get-Location).Path + '\'
$new = @(); $changed = @(); $seen = @{}
$md5 = [Security.Cryptography.MD5]::Create()
foreach ($f in $files) {
  $p = $f.FullName.Substring($root.Length) -replace '\\', '/'; $seen[$p] = 1
  $bytes = [IO.File]::ReadAllBytes($f.FullName)
  $h = ([BitConverter]::ToString($md5.ComputeHash($bytes)) -replace '-', '').Substring(0, 8).ToLower()
  $n = ([Text.Encoding]::UTF8.GetString($bytes) -split "`n").Count
  $o = $old[$p]
  if ($o -and $o.hash8 -eq $h) { $new += $o; continue }
  $changed += ($(if ($o) { "changed  " } else { "new      " }) + "$p ($n lines)")
  $keepSys = if ($o) { $o.system } else { Sys $p }
  $new += [pscustomobject]@{ path = $p; lines = $n; hash8 = $h; system = $keepSys; summary = (Summ $f.FullName) }
}
foreach ($k in $old.Keys) { if (-not $seen[$k]) { $changed += "gone     $k" } }
if ($changed.Count -eq 0) { "REINDEX: no changes ($($new.Count) files)" } else { "REINDEX: $($changed.Count) changes"; $changed }
if (-not $check) { $new | Sort-Object path | ConvertTo-Json -Depth 3 | Out-File -Encoding utf8 $out }
