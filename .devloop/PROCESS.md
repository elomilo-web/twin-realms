# Dev loop process (living; changed every 5th cycle when progress is slow)

## Fixed rules (never remove)
- Stop check first: .devloop/STOP or the user saying "stop".
- One commit per cycle ("devloop #N: ..."), pushed (the user wants finished work pushed without asking).
- Revert-on-failure: an item that fails verify 3 times has only its own files reverted and is logged as blocked.

## Decisions
- 2026-11-24 ART DIRECTION: the setup prompt asked for "anime cel-shaded fantasy", but the project already committed to
  dark gothic realism (Elden Ring-like; docs/GOTHIC.md, data/look.json toon shaders off) and the Minecraft-style block world
  merged into main today. The loop keeps gothic realism + block world: muted grade, scanned triplanar materials, warm
  torchlight vs cold shadow, readable silhouettes, rim light. Write "anime" (or anything else) in DIRECTIVES.md to switch.
- Verification = tools/verify.bat (build + DevCapture). Bot and multiplayer tests are NOT run unless the user asks
  (standing user preference); capture screenshots + report.json are the check.
- Subagents: one per item, sonnet model for S/M items, briefed with card line ranges. They must not run verify or git
  (the lead does that once per cycle, so parallel builds do not collide).

- Windows PowerShell 5.1 reads BOM-less UTF-8 as ANSI: edit .devloop files with the Edit tool, or in PowerShell use
  [IO.File]::ReadAllText/WriteAllText with UTF8Encoding($false). Never Get-Content | Set-Content them (mangles the separator dots).

## Review #5 (2026-11-24): what changed in the process
- Every subagent brief now carries the API GOTCHAS block below (2 of 4 cycles failed the first build on Godot C# names).
- Features that stills cannot show (dodge, torch, look-at, stagger) get action viewpoints; a feature is "done" only when a picture shows it.
- Full sweeps (no -only) are also copied to screenshots/full/ (capture.ps1), so partial runs never lose the last full baseline.
  Compare partial runs against full/ when previous/ is a partial set.
- Keep 6 items per cycle; lead fixes small look problems found in verify instead of deferring them (worked well in #4).

## Review #10 (2026-11-24)
- Cycles 6-9: 6 items each, every cycle shipped; 1-2 lead compile fixes per cycle (namespace, static vs instance, GPU-only
  particle props, non-const shader globals) -> added to GOTCHAS. Visual verification catches real problems (tufts hidden by
  the grass shader, opaque telegraph disc, pause overflow): keep "lead looks at every changed view".
- Gameplay features pile up untested in play (no bot runs by user preference): keep .devloop/PLAYTEST.md, a short checklist
  for the user, updated each cycle with new mechanics.
- Spell/action views must use distinct spots (block world remembers craters and ice between views).

## Review #15 (2026-11-25)
- Cycles 11-14: all shipped; user directive (all menus) handled in #12-#14 with a capture view per menu (50 views now,
  full sweep ~10 min). Lead still fixes 1-3 compile errors per cycle (GPU-only particle props, name shadowing, missing usings,
  duplicate colour fields across Ui partials).
- Shared-file races: two agents editing Ui.cs in one cycle caused near-misses (whole-file writes). Rule: at most ONE agent per
  shared big file per cycle (Ui.cs, Player.cs, Enemy.cs, WorldEnv.cs); others list line numbers for the lead.
- FPS in captures depends on the preceding views (streamed chunks, tufts): compare a view's fps only against the same view set.
- Order-dependent state leaks between views keep appearing (realm, rain, craters): DevCapture resets realm and rain per view;
  new global states must get a reset there too.

## Rule added #19 (user: "more scrutiny, don't re-fix the same thing")
- Before fixing: build a capture view that SHOWS the problem (gear/pack_fill/mats_fill/probe options), find the root cause,
  grep LOG for earlier attempts, then brief one fix. After: same view, compare.
- Grep every cycle's diff for code swallowed into `//` comments (#18 attacks, 217a461 roll):
  `git diff | Select-String '^\+.*//[^/]*[a-z]\w* (=|\+=) [^;]+;\s*\w'`.

## API GOTCHAS (paste into every brief)
EDIT FILES ONLY WITH THE Edit/Write TOOLS: no PowerShell file rewrites (Set-Content/Add-Content/[IO.File] or helper
functions): #15's Ui.cs split lost 342 lines to a PowerShell alias collision (`R` -> Invoke-History) and was reverted.
CpuParticles3D has no AmountRatio and no Turbulence*. A local variable named like a class (float Dmg) hides it: qualify
TwinRealms.Dmg. A static class holding a nested Godot node class must be partial (GD0002). PlaneMesh.OrientationEnum is X/Y/Z. Ui partials already define Parch, Muted, GoldC, Parchment, ErrRed, Amber, RuneGold: don't redeclare.
New files: `namespace TwinRealms;` after the usings. Ui helpers like Lbl/Btn are instance methods (no `static` callers).
CpuParticles3D has no Turbulence* (GPU only). Shader globals must be `const` (or uniforms). GeometryInstance3D.ShadowCastingSetting
needs the class prefix outside GeometryInstance3D subclasses.
Godot 4.7 C#: CpuParticles3D / GpuParticles3D (not CPUParticles3D); lights have no VisibilityRangeEnd (use
DistanceFadeEnabled/Begin/Length); TorusMesh uses InnerRadius/OuterRadius/Rings/RingSegments; Engine.TimeScale must
always be restored; no `?.` on the left of an assignment; Color.FromHtml for hex; StandardMaterial3D per-instance only if cached.

## How a cycle runs (short)
1 stop check; 2 DIRECTIVES, STATE, INDEX, top of BACKLOG/LOG, tools/reindex.ps1; 3 capture (full sweep every 5th
cycle, else relevant views + 2 random) and contact sheet via tools/sheet.ps1; 4 pick 4-6 items, 3+ categories, 2+ visible;
5 subagents in parallel; 6 verify, compare previous vs current; 7 commit + push; 8 update web; 9 every 5th: review here.
