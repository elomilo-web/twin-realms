using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>The arena gate from the inside: during a run, interact to leave (Arenas.Leave). A banner over the door lights up while a run is on.</summary>
public partial class ArenaExit : Node3D, IInteractable
{
    public string Prompt => Arenas.Mode == "waves" ? $"Leave the arena (keep {Arenas.RunValor} valor)" : "Leave the arena";
    public bool CanInteract => Arenas.Active && Player.Instance is { Dead: false };
    Label3D _sign = null!;
    public override void _Ready()
    {
        AddToGroup("interact");
        _sign = new Label3D { Text = "EXIT", FontSize = 64, OutlineSize = 12, PixelSize = .01f, Position = new Vector3(-.4f, 4.2f, 0), Rotation = new Vector3(0, Mathf.Pi / 2, 0), Modulate = new Color(1f, .8f, .4f), Visible = false };
        AddChild(_sign);
    }
    public override void _Process(double delta) { _sign.Visible = Arenas.Active; }
    public void Interact() => Arenas.Leave();
}

/// <summary>Crimson Arena: Wave Survival and Shadow Duel (a clone of the hero). Ticked from Main.</summary>
public static class Arenas
{
    public static bool Active; public static string Mode = "waves"; public static int Wave = 1, Streak = 1, Alive, RunValor; static string _phase = "prep"; static float _delay;
    /// <summary>Leave by the gate (ArenaExit): ends the run where it stands; valor already earned is kept.</summary>
    public static void Leave()
    {
        if (!Active) return;
        string what = Mode == "waves" ? $"Left after wave {Wave - 1}" + (RunValor > 0 ? $", {RunValor} valor earned" : "") : $"Left the duel at round {Streak}";
        End(false); Ui.Banner("Crimson Arena", what);
    }
    static readonly Random Rn = new();
    static Vector3 Center => Builder.ArenaPos;
    public static string Status => !Active ? "" : Mode == "waves" ? "Wave " + Wave + (_phase == "fight" ? "  -  foes left: " + Alive : "  -  get ready") : "Duel round " + Streak;

    public static void Start(string mode)
    {
        var p = Player.Instance; if (p == null || Dungeon.Active) return;
        World.ForceRealm(0); if (p.Mounted) p.ToggleMount(true);
        Active = true; Mode = mode; Wave = 1; Streak = 1; Alive = 0; RunValor = 0; _phase = "prep"; _delay = 3f;
        Builder.SetGate(false);
        var c = Center; p.Teleport(World.OnGround(c.X - 12, c.Z, 0.5f), -Mathf.Pi / 2);
        p.Revive(p.GlobalPosition);
        Ui.Banner(mode == "waves" ? "Wave Survival" : "Shadow Duel", "the crowd roars"); Sfx.Play("roar"); Ui.Flash(Colors.White);
    }

    public static void End(bool died)
    {
        if (!Active) return;
        foreach (var n in Game.Root!.GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.Arena) e.QueueFree();
        foreach (var n in Game.Root.GetTree().GetNodesInGroup("projectiles")) n.QueueFree();
        Active = false; Alive = 0; Builder.SetGate(true);
        if (!died && Player.Instance != null) { var c = Center; Player.Instance.Teleport(World.OnGround(c.X - 40, c.Z, 0.5f), Mathf.Pi / 2); }
        Game.Save();
    }

    public static void OnPlayerDeath() { Ui.Banner("Defeated", "Wave " + Wave); Game.D.BestWave = Math.Max(Game.D.BestWave, Wave - 1); GetTree()?.CreateTimer(2.5).Connect("timeout", Callable.From(() => { var p = Player.Instance; End(true); p?.Revive(World.OnGround(Center.X - 40, Center.Z, 0.5f)); p?.Teleport(World.OnGround(Center.X - 40, Center.Z, 0.5f), Mathf.Pi / 2); })); }
    static SceneTree? GetTree() => Game.Root?.GetTree();

    public static void OnEnemyDown(Enemy e) { Alive = Math.Max(0, Alive - 1); }

    static EnemyDef MakeClone()
    {
        var d = Game.D; var w = d.Weapon;
        return new EnemyDef { N = "Your Shadow", Kind = "hum", Hp = d.MaxHp() * 0.9f, Dmg = Mathf.Max(6, d.WpnDmg() * 0.55f), Spd = 5.4f, Aggro = 60, Reach = w?.Wt == "bow" || w?.Wt == "wand" || w?.Wt == "staff" ? 2.4f : 2.4f, Wind = 0.4f, Cd = 0.85f, Xp = 0, R = 0.45f, Scale = 1f, Color = 0x5a2a8a, Realm = 0 };
    }

    public static void Tick(float dt)
    {
        if (!Active) return; var p = Player.Instance; if (p == null || p.Dead) return;
        if (_phase == "prep")
        {
            _delay -= dt; if (_delay > 0) return;
            if (Mode == "waves")
            {
                int w = Wave, n = Math.Min(10, 2 + w); string[] pool = w < 3 ? new[] { "wolf", "goblin", "bandit" } : w < 5 ? new[] { "goblin", "bandit", "archer", "skeleton", "wolf" } : w < 8 ? new[] { "bandit", "archer", "skeleton", "wraith", "hound", "goblin" } : new[] { "skeleton", "wraith", "hound", "archer", "bandit", "revenant" };
                int lvl = Math.Max(1, Game.D.Level + w / 3 - 1); var list = new List<string>(); for (int i = 0; i < n; i++) list.Add(pool[Rn.Next(pool.Length)]);
                if (w % 5 == 0) list.Add(w >= 10 ? "revenant" : "brute");
                for (int i = 0; i < list.Count; i++) SpawnOne(list[i], lvl, i, list.Count, null);
                Ui.Banner("Wave " + w, list.Count + " foes"); Sfx.Play("roar");
            }
            else { var d = MakeClone(); SpawnOne("clone", 1, 0, 1, d); Ui.Banner("Round " + Streak, "your shadow rises"); Sfx.Play("roar"); }
            _phase = "fight";
        }
        else if (Alive <= 0)
        {
            _phase = "prep"; _delay = 6f;
            if (Mode == "waves")
            {
                // no gold or items in Wave Survival: milestone waves pay valor (data/valor.json); leave through the gate whenever you like
                int v = Valor.ForWave(Wave); Game.D.BestWave = Math.Max(Game.D.BestWave, Wave); Quests.OnWave(Wave);
                int nx = Valor.NextMilestone(Wave);
                Ui.Banner("Wave " + Wave + " cleared", v > 0 ? $"+{v} valor" : nx > 0 ? $"valor at wave {nx}" : "");
                Valor.Give(v); RunValor += v; Wave++;
            }
            else { int g = 45 * Streak; Game.D.Gold += g; Game.D.BestDuel = Math.Max(Game.D.BestDuel, Streak); Quests.OnDuel(Streak); Ui.Banner("Victory!", "+" + g + " gold. Streak " + Streak); Loot.DropFrom(Center + new Vector3(0, 1, 0), Game.D.Level + 1, .6f, 2f, 0, .3f); Streak++; }
            p.Hp = Mathf.Min(Game.D.MaxHp(), p.Hp + Game.D.MaxHp() * .35f); p.Mp = Game.D.MaxMp(); Sfx.Play("level"); Game.Save();
        }
    }

    /// <summary>Wave Survival pays only valor: its foes drop no items, gold or materials.</summary>
    public static bool NoDrops(Enemy e) => e.Arena && Mode == "waves";

    static void SpawnOne(string type, int lvl, int i, int n, EnemyDef? over)
    {
        float a = i / (float)n * Mathf.Tau + (float)Rn.NextDouble(), rr = type == "clone" ? 10f : 15f; var c = Center;
        var e = new Enemy { Type = type == "clone" ? "wolf" : type, DefOverride = over, Level = lvl, Arena = true, NoLeash = true, AnyRealm = true, Position = World.OnGround(c.X + Mathf.Cos(a) * rr, c.Z + Mathf.Sin(a) * rr, 0.4f) };
        Game.Root!.AddChild(e); Alive++; Fx.Burst(e.Position + Vector3.Up, new Color(1f, .5f, .3f), 14, 4f, .6f, .1f);
        e.Wake();
    }
}
