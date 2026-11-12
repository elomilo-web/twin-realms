using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>
/// --measure-attacks: poses a body through attack clips and finds when the weapon hand moves fastest (the impact),
/// as a fraction of the clip. Prints every speed peak too, so combo clips can be cut into single swings.
/// Writes bot_out/attack_clips.txt. Used to fill data/avatar.json "attackHits".
/// </summary>
public partial class AnimTool : Node
{
    public static bool Wanted() => Array.IndexOf(OS.GetCmdlineUserArgs(), "--measure-attacks") >= 0 || Sheet;
    /// <summary>--attack-sheet: photographs every attack animation (data/avatar.json "attackAnims") at wind-up, impact and follow-through: bot_out/attacks/.</summary>
    static bool Sheet => Array.IndexOf(OS.GetCmdlineUserArgs(), "--attack-sheet") >= 0;

    async System.Threading.Tasks.Task RunSheet()
    {
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(12, 12) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.55f, .57f, .6f) } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 150, 0), ShadowEnabled = true, LightEnergy = 1.2f });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, -30, 0), LightEnergy = .35f });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.66f, .72f, .8f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .55f } });
        var cam = new Camera3D { Fov = 40, Current = true }; AddChild(cam); cam.Position = new Vector3(2.2f, 1.5f, 5.2f); cam.LookAt(new Vector3(0, 1f, 0), Vector3.Up);
        var label = new Label { Position = new Vector2(14, 10) }; label.AddThemeFontSizeOverride("font_size", 26); var layer = new CanvasLayer(); layer.AddChild(label); AddChild(layer);
        var sword = new Item { Name = "Longsword", Slot = "weapon", Wt = "sword", Dmg = 5 };
        var bodies = new List<Avatar>();
        for (int i = 0; i < 3; i++)
        {
            var av = new Avatar { Name = "A" + i, Position = new Vector3((i - 1) * 1.9f, 0, 0) }; AddChild(av);
            av.Build(new Appearance { Skin = new Color(.86f, .66f, .5f), Hair = new Color(.2f, .14f, .1f) }, "sheet");
            av.Dress(s => s == "weapon" ? sword : null, "sword", Classes.Get("warrior").Tunic); bodies.Add(av);
        }
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/attacks/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        for (int f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var (id, d) in Avatar.AttackDefs())
        {
            float hit = d.Reverse ? 1 - d.Hit : d.Hit; var fr = new[] { hit * .4f, hit, Mathf.Min(1f, hit + (1 - hit) * .6f) };
            for (int i = 0; i < 3; i++) bodies[i].PoseAt(Avatar.AtkName(id), fr[i]);
            label.Text = $"{id}  ({d.Clip}{(d.Reverse ? ", reversed" : "")})   wind-up | impact | follow-through";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(dir + id + ".png");
        }
        GD.Print("[ANIMTOOL] sheet written to " + dir); GetTree().Quit();
    }
    static readonly string[] Candidates =
    {
        "Sword_Regular_A", "Sword_Regular_B", "Sword_Regular_C", "Sword_Regular_Combo", "Sword_Attack", "Sword_Dash_RM", "Melee_Hook", "Melee_Hook_Rec",
        "Sword_Regular_A_Rec", "Sword_Regular_B_Rec", "Sword_Regular_C_Rec", "TreeChopping", "OverhandThrow", "Shield_OneShot", "Shield_Dash_RM", "Push",
        "Zombie_Scratch", "Farm_Harvest", "Farm_PlantSeed", "Punch_Jab", "Punch_Cross", "NinjaJump_Start", "NinjaJump_Land", "Slide", "Spell_Simple_Shoot", "Pistol_Shoot", "Hit_Knockback",
    };

    public override async void _Ready()
    {
        if (Sheet) { await RunSheet(); return; }
        Avatar.UseSources = true;
        var av = new Avatar { Name = "Measure" }; AddChild(av);
        av.Build(new Appearance { Skin = new Color(.86f, .66f, .5f), Hair = new Color(.2f, .14f, .1f) }, "measure");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var names = new HashSet<string>(av.ClipNames); var sb = new StringBuilder();
        foreach (var c in Candidates)
        {
            if (!names.Contains(c)) { sb.AppendLine($"{c}: missing"); continue; }
            float len = av.ClipLength(c); const int N = 90; var pts = new List<Vector3>();
            for (int i = 0; i <= N; i++) { av.PoseAt(c, i / (float)N); var h = av.HandPos(false); var root = av.GlobalPosition; pts.Add(h.HasValue ? h.Value - root : Vector3.Zero); }
            var sp = new float[N + 1]; for (int i = 1; i <= N; i++) sp[i] = pts[i].DistanceTo(pts[i - 1]) * N / Mathf.Max(.01f, len);
            // smooth, then peaks above 45% of the strongest
            var sm = new float[N + 1]; for (int i = 0; i <= N; i++) { float s = 0; int n = 0; for (int k = -2; k <= 2; k++) { int j = i + k; if (j >= 1 && j <= N) { s += sp[j]; n++; } } sm[i] = n > 0 ? s / n : 0; }
            float max = sm.Max(); var peaks = new List<(float f, float v)>();
            for (int i = 2; i < N - 1; i++) if (sm[i] >= sm[i - 1] && sm[i] > sm[i + 1] && sm[i] > max * .45f) peaks.Add((i / (float)N, sm[i]));
            int best = Array.IndexOf(sm, max);
            sb.AppendLine($"{c}: len {len:0.00}s  impact {best / (float)N:0.00}  hand speed max {max:0.0} m/s  peaks " + string.Join(", ", peaks.Select(p => $"{p.f:0.00} ({p.v:0.0})")));
        }
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        System.IO.File.WriteAllText(dir + "attack_clips.txt", sb.ToString()); GD.Print(sb.ToString());
        GetTree().Quit();
    }
}
