using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Two-machine self-test, run as two game windows on one computer (tools/run.ps1 mp):
///   godot -- --mp-host     hosts, stands in the village, accepts every invite and duel, and checks what the friend did
///   godot -- --mp-join     joins 127.0.0.1, fights a shared enemy, collects loot, forms a party, uses the party chest, duels the host, shifts the realm
/// Each writes bot_out/mp_host.txt or bot_out/mp_join.txt; screenshots show each side seeing the other.
/// </summary>
public partial class MpBot : Node
{
    public const int Port = 24571;
    public string Mode = "host";
    public static string? Wanted() { foreach (var a in OS.GetCmdlineUserArgs()) { if (a == "--mp-host") return "host"; if (a == "--mp-join") return "join"; } return null; }

    readonly StringBuilder _log = new(); int _fail, _pass; string _dir = "";
    void Say(string s) { GD.Print("[MPBOT " + Mode + "] " + s); _log.AppendLine(s); }
    void Check(string name, bool ok, string detail = "") { if (ok) _pass++; else _fail++; Say((ok ? "PASS  " : "FAIL  ") + name + (detail != "" ? "  (" + detail + ")" : "")); }
    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task<bool> Until(Func<bool> f, float timeout) { float t = 0; while (t < timeout) { if (f()) return true; await Wait(.2f); t += .2f; } return f(); }
    async Task Shot(string name) { await Wait(.3f); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng(_dir + name + ".png"); Say("screenshot " + name); }
    static Player P => Player.Instance!;
    static Net N => Net.I!;
    static void Mouse(bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = new Vector2(640, 360) });
    void Face(Vector3 pos) { var d = pos - P.GlobalPosition; if (P.Rig != null) P.Rig.Rotation = new Vector3(-.15f, Util.YawTo(d.X, d.Z), 0); }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; Net.AutoAccept = true;
        _dir = ProjectSettings.GlobalizePath("res://") + "bot_out/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Mode == "host" ? RunHost() : RunJoin();
    }
    bool _written;
    void Write()
    {
        if (_written) return; _written = true;
        Say($"=== {_pass} passed, {_fail} failed");
        using var f = FileAccess.Open(_dir + "mp_" + Mode + ".txt", FileAccess.ModeFlags.Write); f.StoreString(_log.ToString());
    }
    void Finish() { Write(); GetTree().Quit(_fail > 0 ? 1 : 0); }

    // ================================================================== host
    bool _joinDone;
    async Task RunHost()
    {
        try
        {
            await Wait(1.5f);
            P.Teleport(World.OnGround(0, 9, .6f), 0f);
            Check("host starts", N.Host(Port), Net.Status);
            bool joined = await Until(() => N.Players.Count >= 2, 60f);
            Check("a friend joins", joined, $"{N.Players.Count} players");
            if (!joined) { Finish(); return; }
            long friend = 0; foreach (var id in N.Players.Keys) if (id != 1) friend = id;
            bool seen = await Until(() => N.HeroOf(friend) is { HasPose: true } h && h.Visible, 15f);
            Check("the friend's hero is drawn in our world", seen);
            var fh = N.HeroOf(friend);
            int meshes = 0; if (fh != null) foreach (var _ in fh.FindChildren("*", "MeshInstance3D", true, false)) meshes++;
            Check("the friend's hero has a body and gear", meshes > 15, $"{meshes} mesh parts");
            if (fh != null) { P.Teleport(fh.GlobalPosition + new Vector3(3.5f, .5f, 3.5f), 0f); Face(fh.GlobalPosition + Vector3.Up); await Wait(1f); await Shot("mp_host_sees_friend"); P.Teleport(World.OnGround(0, 9, .6f), 0f); }
            // the lobby with the join codes to send to friends
            await Until(() => Net.PublicAddress != "", 8f);
            Ui.I!.OpenMultiplayer(); await Wait(.5f); await Shot("mp_host_lobby"); Ui.I.Close();
            Check("the lobby shows join codes", Net.LocalAddresses().Count > 0 && JoinCode.Make(Net.LocalAddresses()[0], Port) != "", Net.PublicAddress != "" ? "internet address found" : "no internet address (offline?)");
            // the client drives the rest; keep our hero alive until the duel, then let the duel play out
            float t = 0;
            while ((!_joinDone || (_dunStarted && !_hostDunDone)) && t < 300f)
            {
                if (N.Duel == null) { P.Hp = Game.D.MaxHp(); }
                await Wait(.25f); t += .25f;
            }
            Check("the friend finished their tests", _joinDone);
            Check("the party dungeon tests ran", _dunStarted && _hostDunDone);
            Check("the friend's hits reached our enemies", N.Stats.GetValueOrDefault("remoteHits") > 0, $"{N.Stats.GetValueOrDefault("remoteHits")} hits");
            Check("the friend's kill was credited by the host", N.Stats.GetValueOrDefault("remoteKills") > 0);
            Check("a party was formed", N.Stats.GetValueOrDefault("parties") > 0);
            Check("the party chest received an item", N.Stats.GetValueOrDefault("chestPuts") > 0 && N.Stats.GetValueOrDefault("chestTakes") > 0, $"{N.Stats.GetValueOrDefault("chestPuts")} in, {N.Stats.GetValueOrDefault("chestTakes")} out");
            Check("a duel was fought to the end", N.Stats.GetValueOrDefault("duels") > 0);
            Check("nobody died in the duel", !P.Dead);
            Check("the friend's realm shift changed our world", N.Stats.GetValueOrDefault("shifts") > 0);
            Check("loot was claimed through the host", N.Stats.GetValueOrDefault("claims") > 0, $"{N.Stats.GetValueOrDefault("claims")} claims");
        }
        catch (Exception e) { Check("host script threw", false, e.Message); }
        Finish();
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_Done() { _joinDone = true; }

    // ---- party dungeon (the joiner asks us to follow it in)
    bool _hostDunFought, _hostDunDone, _dunStarted;
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_HostDungeon() { if (!_dunStarted) { _dunStarted = true; _ = HostDungeon(); } }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_HostDunFought() { _hostDunFought = true; }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)] void R_HostDunDone() { _hostDunDone = true; }
    async Task HostDungeon()
    {
        try
        {
            Game.D.Eq["weapon"] = new Item { Name = "Bot Blade", Slot = "weapon", Wt = "sword", Dmg = 140 };
            int xp0 = Game.D.Xp + Game.D.Level * 100000, kills0 = Game.D.Kills;
            Dungeon.Enter("warrens", 1);   // a different door on purpose: we still join the party's dungeon
            bool sh = await Until(() => Dungeon.Shared, 8f);
            Check("we join the friend's party dungeon (any door leads to it)", sh && !Dungeon.IsOwner && Dungeon.Key == "barrow", $"key {Dungeon.Key}, owner {Dungeon.Owner}");
            int pup = 0; foreach (var e in Dungeon.NetList) if (e != null && e.Puppet) pup++;
            Check("its monsters mirror the friend's", pup > 0 && pup == Dungeon.NetList.Count, $"{pup}/{Dungeon.NetList.Count}");
            Check("monster snapshots arrive from the friend", await Until(() => N.Stats.GetValueOrDefault("dunSnaps") > 0, 6f));
            await Shot("mp_host_party_dungeon");
            Enemy? m = null; float md = 1e9f; foreach (var e in Dungeon.NetList) if (e != null && !e.Dead && !e.Def.Boss) { float d = e.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < md) { md = d; m = e; } }
            if (m != null)
            {
                float t = 0; while (t < 25f && !m.Dead) { var d = m.GlobalPosition - P.GlobalPosition; d.Y = 0; if (d.Length() > 2.4f) P.Teleport(m.GlobalPosition - d.Normalized() * 1.7f + new Vector3(0, .5f, 0), Util.YawTo(d.X, d.Z)); Face(m.GlobalPosition + Vector3.Up); Mouse(true); await Wait(.08f); Mouse(false); await Wait(.15f); t += .23f; }
                Check("our hits kill a monster the friend runs", m.Dead, $"{m.Def.N}, {t:0.0} s");
            }
            await Until(() => Game.D.Kills > kills0 && Game.D.Xp + Game.D.Level * 100000 > xp0, 5f);
            Check("we get kill credit and an experience share in the party dungeon", Game.D.Kills > kills0 && Game.D.Xp + Game.D.Level * 100000 > xp0, $"kills {kills0} -> {Game.D.Kills}");
            RpcId(Net.I!.Players.Count > 1 ? FriendId() : 1, MethodName.R_HostDunFought);
            bool took = await Until(() => Dungeon.IsOwner, 30f);
            int live = 0; foreach (var e in Dungeon.NetList) if (e != null && !e.Puppet) live++;
            Check("when the friend leaves we take over the monsters", took && live == Dungeon.NetList.Count, $"owner {Dungeon.Owner}, {live} running here");
            Dungeon.Leave(false);
        }
        catch (Exception e) { Check("host dungeon script threw", false, e.Message); }
        _hostDunDone = true; if (Net.Active) RpcId(FriendId(), MethodName.R_HostDunDone);
    }
    long FriendId() { foreach (var id in N.Players.Keys) if (id != Net.MyId) return id; return 1; }
    /// <summary>Waits for a condition while keeping our hero alive (dungeon monsters keep fighting while we wait).</summary>
    async Task<bool> Heal(Func<bool> f, float timeout) { float t = 0; while (t < timeout) { if (Player.Instance != null) P.Hp = Game.D.MaxHp(); if (f()) return true; await Wait(.2f); t += .2f; } return f(); }

    // ================================================================== client
    async Task RunJoin()
    {
        try
        {
            await Wait(2.5f);
            Game.NewGame("ranger", "JoinBot", "elf"); Game.D.Eq["weapon"] = new Item { Name = "Bot Blade", Slot = "weapon", Wt = "sword", Dmg = 140 };
            Game.D.Eq["head"] = Items.Roll(5, 1, "head"); Game.D.Eq["armor"] = Items.Roll(5, 1, "armor");
            bool started = false;
            // join the way a friend does: with a join code (it carries the address and port)
            string code = JoinCode.Make("127.0.0.1", Port); bool decoded = JoinCode.TryRead(code.ToLowerInvariant(), out var ip, out var jport);
            Check("a join code carries the address and port", decoded && ip == "127.0.0.1" && jport == Port, $"{code} -> {ip}:{jport}");
            N.Join(ip, jport, () => { ((Main)Game.Root!).JoinWorld(); started = true; });
            bool ok = await Until(() => started && Player.Instance != null, 40f);
            Check("joins the host's world", ok, Net.Status);
            if (!ok) { Finish(); return; }
            await Wait(2f);
            var host = N.HeroOf(1);
            Check("the host's hero is drawn", host != null && host.HasPose && host.Visible);
            int copies = GetTree().GetNodesInGroup("remote_heroes").Count; var tags = new List<string>();
            foreach (var n in GetTree().GetNodesInGroup("remote_heroes")) if (n is RemoteHero rh) tags.Add($"{rh.Name} at {rh.GlobalPosition.DistanceTo(P.GlobalPosition):0.0} m");
            Check("exactly one drawn hero per friend", copies == 1, string.Join(", ", tags));
            Check("we spawn next to the host", host != null && host.GlobalPosition.DistanceTo(P.GlobalPosition) < 12f, host == null ? "" : $"{host.GlobalPosition.DistanceTo(P.GlobalPosition):0.0} m");
            int puppets = 0; foreach (var e in World.NetEnemies) if (e.Puppet) puppets++;
            Check("our overworld enemies mirror the host", puppets > 20 && puppets == World.NetEnemies.Count, $"{puppets} puppets");
            if (host != null) { P.Teleport(host.GlobalPosition + new Vector3(-3.5f, .5f, 3f), 0f); Face(host.GlobalPosition + Vector3.Up); await Wait(1f); await Shot("mp_join_sees_host"); }

            // ---- a shared enemy: hits go to the host, its death and our experience come back
            Enemy? target = null; float bd = 1e9f;
            foreach (var e in World.NetEnemies) if (e.Active && !e.Dead && (e.Type == "wolf" || e.Type == "goblin" || e.Type == "bandit")) { float d = e.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < bd) { bd = d; target = e; } }
            Check("a shared enemy to fight", target != null);
            if (target != null)
            {
                int xp0 = Game.D.Xp + Game.D.Level * 100000, gold0 = Game.D.Gold, kills0 = Game.D.Kills;
                P.Teleport(target.GlobalPosition + new Vector3(0, .6f, 2.2f), 0f); await Wait(.6f);
                float t = 0;
                while (t < 30f && !target.Dead)
                {
                    P.Hp = Game.D.MaxHp(); Face(target.GlobalPosition + Vector3.Up); var d = target.GlobalPosition - P.GlobalPosition; d.Y = 0;
                    if (d.Length() > 2.6f) P.Teleport(target.GlobalPosition - d.Normalized() * 1.8f + new Vector3(0, .5f, 0), Util.YawTo(d.X, d.Z));
                    Mouse(true); await Wait(.08f); Mouse(false); await Wait(.15f); t += .23f;
                    if ((int)(t / 5f) != (int)((t - .23f) / 5f)) Say($"  target #{target.NetIdx} hp {target.Hp:0}/{target.MaxHp:0} at {target.GlobalPosition.DistanceTo(P.GlobalPosition):0.0} m, state dead={target.Dead} visible={target.Visible}");
                }
                Check("the shared enemy dies (host decides, we see it)", target.Dead, $"{target.Def.N}, {t:0.0} s");
                await Until(() => Game.D.Kills > kills0, 4f);
                Check("kill credit and experience come from the host", Game.D.Kills > kills0 && Game.D.Xp + Game.D.Level * 100000 > xp0, $"kills {kills0} -> {Game.D.Kills}");
                await Shot("mp_join_after_kill");
                // walk over the drops: each pickup is claimed through the host
                for (int i = 0; i < 6; i++)
                {
                    Loot? l = null; float ld = 1e9f; foreach (var n in GetTree().GetNodesInGroup("loot")) if (n is Loot x && x.NetId > 0) { float dd = x.GlobalPosition.DistanceTo(P.GlobalPosition); if (dd < ld) { ld = dd; l = x; } }
                    if (l == null) break; P.Teleport(l.GlobalPosition + new Vector3(.4f, 0, 0), 0f); await Wait(1.2f);
                }
                Check("loot is claimed through the host", N.Stats.GetValueOrDefault("gotLoot") > 0 && Game.D.Gold > gold0, $"gold {gold0} -> {Game.D.Gold}");
            }

            // ---- party
            P.Teleport(N.HeroOf(1)!.GlobalPosition + new Vector3(-2.5f, .5f, 2.5f), 0f);
            N.Invite(1);
            bool party = await Until(() => N.MyParty != null && N.MyParty.Members.Count == 2, 8f);
            Check("party invite accepted, party of two", party);
            N.SetLootMode("roundrobin");
            Check("the leader can change the loot rule", await Until(() => N.MyParty?.Loot == "roundrobin", 5f), N.MyParty?.Loot ?? "none");
            await Wait(.5f); await Shot("mp_join_party_hud");

            // ---- party chest
            var gift = Items.Roll(4, 1); Game.D.Inv.Add(gift); int inv0 = Game.D.Inv.Count;
            N.ChestPut(Game.D.Inv.Count - 1);
            bool inChest = await Until(() => N.PartyChest.Exists(i => i.Name == gift.Name), 5f);
            Check("an item put in the party chest arrives in the host's chest", inChest && Game.D.Inv.Count == inv0 - 1);
            Ui.I!.OpenPartyChest(); await Wait(.6f); await Shot("mp_join_party_chest"); Ui.I.Close();
            int idx = N.PartyChest.FindIndex(i => i.Name == gift.Name); N.ChestTake(idx);
            Check("and can be taken back out", await Until(() => Game.D.Inv.Exists(i => i.Name == gift.Name), 5f));

            // ---- duel: nobody dies; the loser stays on 1 health
            var hh = N.HeroOf(1)!; P.Teleport(hh.GlobalPosition + new Vector3(0, .5f, 2.4f), 0f); await Wait(.5f);
            N.Challenge(1);
            bool started2 = await Until(() => N.Duel != null, 6f);
            Check("duel challenge accepted", started2);
            bool live = await Until(() => N.Duel is { Live: true }, 6f);
            Enemy? proxy = null; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.DuelPeer == 1) proxy = e;
            Check("the opponent can be targeted like an enemy", live && proxy != null);
            await Shot("mp_join_duel");
            float dt = 0; int wins0 = Game.D.DuelWins;
            while (N.Duel != null && dt < 60f && proxy != null && IsInstanceValid(proxy))
            {
                P.Hp = Game.D.MaxHp(); var d = proxy.GlobalPosition - P.GlobalPosition; d.Y = 0;
                if (d.Length() > 2.4f) P.Teleport(proxy.GlobalPosition - d.Normalized() * 1.7f + new Vector3(0, .5f, 0), Util.YawTo(d.X, d.Z));
                Face(proxy.GlobalPosition + Vector3.Up); Mouse(true); await Wait(.08f); Mouse(false); await Wait(.17f); dt += .25f;
            }
            Check("the duel ends with us as the winner", Game.D.DuelWins > wins0, $"{dt:0.0} s");

            // ---- realm shift is shared
            World.ShiftCd = 0; World.TryShiftRealm();
            Check("our shift request turns the whole world", await Until(() => World.Realm == 1, 5f));
            await Wait(2.5f); World.ShiftCd = 0; World.TryShiftRealm(); await Until(() => World.Realm == 0, 6f);

            // ---- party dungeon: we open it (we run its monsters), the host follows us in, both fight, we leave and the host takes over
            N.Stats.Clear();
            Dungeon.Enter("barrow", 1);
            bool sh = await Until(() => Dungeon.Shared, 8f);
            Check("a party member opens a shared party dungeon", sh && Dungeon.IsOwner, $"shared {Dungeon.Shared}, zone {Net.MyZone}");
            if (sh)
            {
                int hostLvl = N.Players.GetValueOrDefault(1)?.Level ?? 1, want = Progression.PartyDungeonLevel((Game.D.Level + hostLvl) / 2f, 1), wrong = 0;
                foreach (var e in Dungeon.NetList) if (e != null && !e.Def.Boss && e.Level != want && e.Level != want + 1) wrong++;
                Check("party dungeon level = average party level + 3", wrong == 0 && Dungeon.NetList.Count > 0, $"levels {Game.D.Level} and {hostLvl}, monsters {want}, wrong {wrong}");
                RpcId(1, MethodName.R_HostDungeon);
                bool hostIn = await Heal(() => N.HeroOf(1) is { Visible: true } h && h.Zone == Net.MyZone, 15f);
                Check("the host follows us into the same dungeon and is drawn there", hostIn);
                if (hostIn) { var hp = N.HeroOf(1)!.GlobalPosition; P.Teleport(hp + new Vector3(2.5f, .5f, 3f), 0f); Face(hp + Vector3.Up); await Wait(.8f); await Shot("mp_join_party_dungeon"); }
                // we kill a monster: it is ours to decide, the host gets an equal share
                Enemy? m = null; float md = 1e9f; foreach (var e in Dungeon.NetList) if (e != null && !e.Dead && !e.Def.Boss) { float d = e.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < md) { md = d; m = e; } }
                if (m != null)
                {
                    float t = 0; while (t < 25f && !m.Dead) { P.Hp = Game.D.MaxHp(); var d = m.GlobalPosition - P.GlobalPosition; d.Y = 0; if (d.Length() > 2.4f) P.Teleport(m.GlobalPosition - d.Normalized() * 1.7f + new Vector3(0, .5f, 0), Util.YawTo(d.X, d.Z)); Face(m.GlobalPosition + Vector3.Up); Mouse(true); await Wait(.08f); Mouse(false); await Wait(.15f); t += .23f; }
                    Check("we kill a party dungeon monster and everyone inside is paid", m.Dead && N.Stats.GetValueOrDefault("dunKills") > 0, $"{m.Def.N} lv {m.Level}, {t:0.0} s");
                }
                Check("the host's hits reach the monsters we run", await Heal(() => N.Stats.GetValueOrDefault("dunRemoteHits") > 0, 25f), $"{N.Stats.GetValueOrDefault("dunRemoteHits")} hits");
                await Heal(() => _hostDunFought, 30f);
                Dungeon.Leave(false); Check("we leave; the host stays inside", !Dungeon.Active);
                await Until(() => _hostDunDone, 30f);   // the host takes over the monsters, then reports
            }
            Game.Save();
        }
        catch (Exception e) { Check("join script threw", false, e.Message + " " + e.StackTrace); }
        Write();   // the report first: the host quits as soon as it hears we are done
        if (Net.Active) { RpcId(1, MethodName.R_Done); await Wait(1.5f); }
        Finish();
    }
}
