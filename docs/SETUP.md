# Setup (once)

1. Install: Godot 4.x **.NET** build, .NET 8 SDK, Git, Claude Code.
2. Create a private GitHub repo `twin-realms` and clone it.
3. In Godot, create a new project **in that folder**, named TwinRealms, renderer **Forward+**. Then Project > Tools > C# > Create C# solution.
4. Copy the contents of this kit into the repo root. Keep Godot's own `project.godot`. If it is the only file that clashes, keep Godot's.
5. `git add -A && git commit -m "Starter kit" && git push`
6. Open the folder in Claude Code and say: "Read CLAUDE.md and docs/PORT.md, then do step 0."

# Daily loop
- You play and describe what feels wrong, or paste a screenshot.
- Claude Code makes the change, builds, runs the smoke test and commits.
- Design decisions made in chat get written into `docs/DESIGN.md` so both sides stay in sync.
