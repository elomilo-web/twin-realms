using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Playtest flag (F8, rebindable): saves a screenshot and appends a report to user://playtest/flags.log
/// (%APPDATA%\Godot\app_userdata\TwinRealms\playtest\): hero, place, target, nearby monsters, settings and the last events
/// (notes, blows taken and dealt), so a moment that felt wrong can be reviewed later. Works in menus too.
/// </summary>
public partial class Flag : Node
{
    const int Keep = 25;
    static readonly Queue<string> _events = new();
    static int _n; bool _busy;
    /// <summary>Where flags go; the test bots use their own folder so they never mix with a real playtest.</summary>
    public static string Dir => Bot.Wanted() || MpBot.Wanted() != null ? "user://playtest_test/" : "user://playtest/";

    /// <summary>Remember a recent event for the next report (notes, hits).</summary>
    public static void Event(string s)
    {
        _events.Enqueue($"{TimeStamp()}  {s}");
        while (_events.Count > Keep) _events.Dequeue();
    }
    static string TimeStamp() { var t = TimeSpan.FromSeconds(Game.PlayTime); return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}"; }

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }

    public override void _Input(InputEvent e)
    {
        if (_busy || !Ui.InGame || !e.IsActionPressed(Inputs.Flag) || e.IsEcho()) return;
        _ = Capture(); GetViewport().SetInputAsHandled();
    }

    async Task Capture()
    {
        _busy = true;
        try
        {
            string dir = Dir; DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + (++_n);
            // the frame as you see it (the "flagged" note comes after the picture)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage(); string png = $"flag_{stamp}.png";
            img.SavePng(ProjectSettings.GlobalizePath(dir + png));
            string report = Report(png);
            using (var f = FileAccess.Open(dir + "flags.log", FileAccess.FileExists(dir + "flags.log") ? FileAccess.ModeFlags.ReadWrite : FileAccess.ModeFlags.Write))
            { f.SeekEnd(); f.StoreString(report); }
            GD.Print($"[FLAG] {png}");
            Ui.Note($"Flagged for review ({png})", new Color(1f, .85f, .4f)); Sfx.Play("click");
        }
        catch (Exception ex) { GD.PushError("flag failed: " + ex.Message); }
        _busy = false;
    }

    static string Report(string png)
    {
        var d = Game.D; var p = Player.Instance; var b = new StringBuilder();
        b.AppendLine($"===== FLAG {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {png}");
        b.AppendLine($"version {Game.Version}  session {TimeStamp()}  hero played {Game.Played(d)}  fps {Engine.GetFramesPerSecond():0}  quality {Game.Set.Quality} scale {Game.Set.RenderScale:0.00} view {Game.Set.ViewDist:0}m  auto target {Game.Set.AutoTarget}");
        b.AppendLine($"hero {d.Name} ({d.Race} {d.Class}, {d.Body})  Lv {d.Level}  xp {d.Xp}/{d.XpNeed()}  gold {d.Gold}  unspent attr {d.AttrPts} tree {d.SkillPts} skill {d.RankPts}");
        if (p != null) b.AppendLine($"hp {p.Hp:0}/{d.MaxHp()}  mp {p.Mp:0}/{d.MaxMp()}  st {p.St:0}/{d.MaxSt()}  dead {p.Dead}  mounted {p.Mounted}");
        b.AppendLine($"stats: weapon hit {d.WpnDmg():0}  defense {d.Defense()}  crit {d.CritChance() * 100:0}%  spell x{d.SpellPow():0.00}  str {d.Str} dex {d.Dex} vit {d.Vit} mag {d.Mag}");
        var gear = new List<string>(); foreach (var s in PlayerStats.Slots) if (d.Eq.GetValueOrDefault(s) is Item it) gear.Add($"{s}: {it.Name} L{it.Lvl} {Items.RarityName(it.Rar)}{(it.Set != null ? " [set]" : "")}");
        b.AppendLine("gear: " + (gear.Count > 0 ? string.Join(" | ", gear) : "none"));
        // where
        string place = Dungeon.Active ? $"dungeon {Dungeon.Key} depth {Dungeon.Depth} tier {Progression.Tier(Dungeon.Depth) + 1}{(Dungeon.Shared ? " (party)" : "")}"
            : Arenas.Active ? $"arena {Arenas.Mode} {Arenas.Status}" : "overworld";
        if (p != null) { var g = p.GlobalPosition; b.AppendLine($"place {place}  pos ({g.X:0.0}, {g.Y:0.0}, {g.Z:0.0})  area level {Game.LevelAt(g.X, g.Z, World.Realm)}  realm {(World.Realm == 1 ? "shadow" : "light")}  time of day {World.TimeOfDay:0.00}  threat {Threat.Level} ({Threat.Minutes:0} min)"); }
        b.AppendLine($"menu open: {(Ui.AnyOpen ? Ui.I!.PanelName : "none")}  pack {d.Inv.Count}/28  potions hp {d.HpPot} mp {d.MpPot}");
        if (Net.Active) b.AppendLine($"multiplayer: {(Net.IsHost ? "host" : "client")}, {Net.I!.Players.Count} players, party {(Net.I.MyParty != null ? Net.I.MyParty.Members.Count + " members" : "none")}, monster x{Progression.PartyMul():0.0}");
        // target and nearby monsters
        if (Targeting.Aim is Enemy t && GodotObject.IsInstanceValid(t))
            b.AppendLine($"target ({(Targeting.Current != null ? "locked" : "auto")}): {Foe(t, p)}  armor {t.ArmorNow:0} mr {t.MrNow:0}  {t.UpgradeLine().Trim()}");
        if (p != null)
        {
            var near = new List<(float, Enemy)>();
            foreach (var n in p.GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.Active) { float dd = e.GlobalPosition.DistanceTo(p.GlobalPosition); if (dd < 35f) near.Add((dd, e)); }
            near.Sort((a, c) => a.Item1.CompareTo(c.Item1));
            b.AppendLine($"monsters within 35 m: {near.Count}");
            for (int i = 0; i < Math.Min(10, near.Count); i++) b.AppendLine("  " + Foe(near[i].Item2, p));
        }
        b.AppendLine("recent events:");
        foreach (var ev in _events) b.AppendLine("  " + ev);
        b.AppendLine();
        return b.ToString();
    }
    static string Foe(Enemy e, Player? p) =>
        $"{e.Def.N} Lv{e.Level}  hp {e.Hp:0}/{e.MaxHp:0}  hits for {e.Damage:0}  {(p != null ? $"{e.GlobalPosition.DistanceTo(p.GlobalPosition):0.0} m" : "")}{(e.Def.Boss ? "  BOSS" : "")}{(e.Ambusher ? "  ambush" : "")}{(e.Puppet ? "  (host's)" : "")}";
}
