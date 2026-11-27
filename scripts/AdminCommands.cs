using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The admin commands of the command bar (focus F-42), one Commands.Add(new Cmd { ... }) entry each. To add a command: copy an entry.
///   Server = true  : it changes the shared world or another player. Offline it runs here; as a client it is sent to the host / online
///                    server (Net.R_Cmd), which runs this same code after checking ITS admin flag for the sender.
///   x (CmdCtx)     : who runs it (x.Peer, x.Name, x.Account, x.Pos, x.Aim). Never use Player.Instance for "the sender": on the server
///                    there is none. What a command does to a hero goes through Do(peer, what, arg) = Admin.ApplyLocal on that hero's machine.
/// Hero names with spaces are typed with _ ("Sir_Bob"); "me" is yourself; "#12345" is a peer id (see /whois).
/// </summary>
public static class AdminCommands
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static readonly string[] None = Array.Empty<string>();
    static readonly string[] OnOff = { "on", "off" };

    // ---------------------------------------------------------------- helpers
    static string U(string s) => s.Replace(' ', '_');
    static IEnumerable<string> PlayerNames() => Net.Active ? Net.I!.Players.Values.Select(p => U(p.Name)) : new[] { U(Game.D.Name) };
    static string NameOf(long peer) => Net.Active ? Net.I!.NameOf(peer) : Game.D.Name;
    static bool Num(string s, out float v) => float.TryParse(s, NumberStyles.Float, CI, out v);
    static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CI, out v);
    static string F(float v) => v.ToString("0.##", CI);
    const string OnlineOnly = "this works on the online server only (accounts and the block log live there)";

    /// <summary>The peer a typed name means, or an error line.</summary>
    static string? FindPlayer(CmdCtx x, string who, out long peer)
    {
        peer = x.Peer;
        if (who.Equals("me", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Net.Active) return U(Game.D.Name).Equals(U(who), StringComparison.OrdinalIgnoreCase) ? null : $"no player '{who}' (you are offline: there is only {U(Game.D.Name)})";
        var ps = Net.I!.Players;
        if (who.StartsWith('#') && long.TryParse(who[1..], out long id)) { if (ps.ContainsKey(id)) { peer = id; return null; } return $"no player with id {who}"; }
        var exact = ps.Values.Where(p => U(p.Name).Equals(U(who), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 0) exact = ps.Values.Where(p => U(p.Name).StartsWith(U(who), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) { peer = exact[0].Id; return null; }
        if (exact.Count > 1) return $"{exact.Count} players match '{who}': use the id, {string.Join(", ", exact.Select(p => $"{U(p.Name)} = #{p.Id}"))}";
        return Commands.NoSuch("player", who, PlayerNames()) + " (online: " + string.Join(", ", PlayerNames().Take(12)) + ")";
    }
    static bool IsPlayerName(string who) => who.Equals("me", StringComparison.OrdinalIgnoreCase) || who.StartsWith('#') || PlayerNames().Any(n => n.StartsWith(U(who), StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies an effect to a hero: here (offline, or our own hero when hosting) or on that player's machine. Returns what to print.</summary>
    static string Do(CmdCtx x, long peer, string what, string arg, string done)
    {
        if (!Net.Active) return Admin.ApplyLocal(what, arg);
        Net.I!.AdminDo(peer, what, arg);
        return peer == x.Peer ? done : $"{done} ({NameOf(peer)})";
    }

    /// <summary>An account for commands that also work on players who are offline: an online name, else the last hero name on an account, else "@" + the start of an account id.</summary>
    static string? FindAccount(CmdCtx x, string who, out string key, out long peer, out string label)
    {
        key = ""; peer = 0; label = who;
        if (!Net.Dedicated) return OnlineOnly;
        if (!who.StartsWith('@') && FindPlayer(x, who, out long p) == null) { peer = p; key = Net.I!.AcctOf(p); label = NameOf(p); return key != "" ? null : "that player has no account"; }
        var found = Accounts.FindByHero(who.Replace('_', ' '));
        if (found.Count == 0 && !who.StartsWith('@')) found = Accounts.FindByHero(who);
        if (found.Count == 0) return $"no player or account '{who}' (offline players: their last hero name, or @ and the first letters of the account id from /whois)";
        if (found.Count > 1) return $"{found.Count} accounts match '{who}': use one of {string.Join(", ", found.Take(8).Select(f => "@" + f.key[..8] + " (" + f.acc.Hero + ")"))}";
        key = found[0].key; label = (found[0].acc.Hero != "" ? found[0].acc.Hero : "account") + " [" + key[..8] + "]";
        foreach (var kv in Net.I!.Players) if (Net.I.AcctOf(kv.Key) == key) peer = kv.Key;
        return null;
    }

    static IEnumerable<string> SkillNames() { LifeSkills.Load(); return LifeSkills.Order.Concat(Skills.All.Select(s => s.Id)); }
    static readonly (string name, float hour)[] Times = { ("dawn", 6f), ("day", 9f), ("noon", 12f), ("dusk", 18.5f), ("night", 22f), ("midnight", 0f) };

    /// <summary>/debug systems: name -> (is it on?, switch it). Other systems may add their own.</summary>
    public static readonly Dictionary<string, (Func<bool> get, Action<bool> set, string help)> Debug = new(StringComparer.OrdinalIgnoreCase)
    {
        ["casts"] = (() => Player.LogCasts, v => Player.LogCasts = v, "[CAST] lines for every cast path"),
        ["social"] = (() => NpcSocial.Log, v => NpcSocial.Log = v, "NPC chats and greetings in the log"),
        ["sfx"] = (() => Sfx.Log, v => Sfx.Log = v, "every sound played"),
        ["models"] = (() => Enemy.LogModels, v => Enemy.LogModels = v, "monster model loading"),
        ["fps"] = (() => Game.Set.ShowFps, v => Game.Set.ShowFps = v, "the FPS counter (until the game closes)"),
    };
    /// <summary>/debug systems that only print something once.</summary>
    public static readonly Dictionary<string, Func<string>> DebugInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["net"] = () => !Net.Active ? "net: offline" : $"net: {(Net.IsHost ? "host" : "client")}{(Game.Online ? " (online server)" : "")}, my id {Net.MyId}, {Net.I!.Players.Count} players: {string.Join(", ", Net.I.Players.Values.Select(p => $"{p.Name}#{p.Id} L{p.Level} zone {p.Zone}"))}",
        ["blocks"] = () => BlockWorld.I?.Area is { } a ? $"blocks: area {a.Key}, {a.Chunks.Count} chunks, {a.Diff.Count} changed cells, aim {(Admin.AimCell() is { } c ? $"{c.X} {c.Y} {c.Z} = {Blocks.Def(a.Get(c.X, c.Y, c.Z)).Key}" : "nothing")}, online no-mining {OnlineRules.NoMining}" : "blocks: no block area here",
        ["admin"] = () => $"admin: {Admin.IsAdmin} (this computer {Admin.LocalFlag}, online {Game.Online}), god {Admin.God}, fly {Admin.Fly}, noclip {Admin.Noclip}",
        ["world"] = () => $"world: time {World.TimeOfDay * 24f:0.0}h, realm {World.Realm}, rain {Weather.Rain:0.00}, hero at {(Player.Instance is { } p ? $"{p.GlobalPosition.X:0.0} {p.GlobalPosition.Y:0.0} {p.GlobalPosition.Z:0.0}, {Lands.RegionAt(p.GlobalPosition)}, biome {FishDb.Biome(p.GlobalPosition.X, p.GlobalPosition.Z)}" : "-")}",
    };

    /// <summary>heal / god / fly / noclip: "[player] [on|off]" applied to a hero.</summary>
    static Cmd Power(string name, string what, bool toggle, string help) => new()
    {
        Name = name, Usage = $"/{name} [player]" + (toggle ? " [on|off]" : ""), Help = help, Server = true,
        Complete = (i, a) => i == 0 ? PlayerNames().Concat(toggle ? OnOff : None) : i == 1 && toggle ? OnOff : None,
        Run = (x, a) =>
        {
            long peer = x.Peer; var rest = a.ToList();
            if (rest.Count > 0 && !(toggle && OnOff.Contains(rest[0].ToLowerInvariant()))) { if (FindPlayer(x, rest[0], out peer) is { } err) return err; rest.RemoveAt(0); }
            string arg = rest.Count > 0 ? rest[0].ToLowerInvariant() : "";
            if (toggle && arg != "" && !OnOff.Contains(arg)) return $"usage: /{name} [player] [on|off]";
            return Do(x, peer, what, arg, name + (arg != "" ? " " + arg : toggle ? " switched" : " done"));
        }
    };

    static bool _done;
    public static void RegisterAll()
    {
        if (_done) return; _done = true;
        Commands.Register("admintest", "F-43/F-44 self-test of the server-side admin and block-log functions with two fake accounts (PASS / FAIL per step); set TR_ADMIN_TEST_PW to also test the right login", AdminTest.Run);

        Commands.Add(new Cmd
        {
            Name = "spawn", Usage = "/spawn <mob|item|npc> [amount]", Server = true,
            Help = "a monster type (/spawn wolf 3), an item (/spawn weapon, /spawn stone 20: dropped or put in your pack) or an NPC of your world brought to you (/spawn npc <id>)",
            Complete = (i, a) => i == 0 ? EnemyDb.Types.Concat(Admin.GiveNames()).Concat(new[] { "npc" }) : i == 1 && a[0] == "npc" ? NpcDb.All.Select(r => r.Id) : None,
            Run = (x, a) =>
            {
                if (a.Length == 0) return "usage: /spawn <mob|item|npc> [amount]";
                var w = a.ToList(); string kind = "";
                if (w[0] is "mob" or "item" or "npc" && w.Count > 1) { kind = w[0]; w.RemoveAt(0); }
                string name = w[0]; int n = 1;
                if (w.Count > 1 && (!Int(w[1], out n) || n < 1 || n > 50)) return "the amount must be a number from 1 to 50";
                string? mob = kind is "" or "mob" ? EnemyDb.Types.FirstOrDefault(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
                if (mob != null)
                {
                    if (x.Pos is not { } at || World.I == null || Dungeon.Active || Arenas.Active) return "monsters can only be spawned in the overworld (and you must be in it)";
                    n = Math.Min(n, 30); int lvl = Progression.OverworldLevel(Game.LevelAt(at.X, at.Z, World.Realm), false);
                    for (int i = 0; i < n; i++)
                    {
                        float ang = i / (float)n * Mathf.Tau + GD.Randf(), r = 4f + GD.Randf() * 2f + n * .15f;
                        var e = new Enemy { Type = mob, Level = lvl, Ambusher = true, NoLeash = true, AnyRealm = true, RespawnTime = 0, Position = World.OnGround(at.X + Mathf.Cos(ang) * r, at.Z + Mathf.Sin(ang) * r, .3f) };
                        if (Net.IsHost) World.AddNetExtra(e);   // shared: everyone sees and fights it (as ambushes are)
                        World.I.Over.AddChild(e);
                        if (Net.IsHost) Net.I!.AmbushSpawn(e);
                    }
                    return $"spawned {n} x {mob} (level {lvl}) around {at.X:0} {at.Z:0}";
                }
                if (kind is "" or "item" && Admin.GiveKey(name) is { } key) return Do(x, x.Peer, "drop", $"{key} {n}", $"spawned {n} x {key}");
                if (kind == "npc" || (kind == "" && !x.Remote && NpcDb.Get(name) != null))
                {
                    if (!x.Remote && NpcDb.Get(name) == null) return Commands.NoSuch("NPC", name, NpcDb.All.Select(r => r.Id));
                    return Do(x, x.Peer, "npc", name, $"asked your game to bring NPC {name} (NPCs live in each player's own game)");
                }
                return kind == "mob" ? Commands.NoSuch("mob type", name, EnemyDb.Types) : kind == "item" ? Commands.NoSuch("item", name, Admin.GiveNames())
                    : Commands.NoSuch("mob type or item", name, EnemyDb.Types.Concat(Admin.GiveNames())) + " (for an NPC: /spawn npc <id>)";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "give", Usage = "/give [player] <item> [amount]", Server = true,
            Help = "puts things in a pack: gold, xp, hp_potion, mp_potion, valor, skillpoints, attrpoints, rankpoints, a gear slot (weapon, armor ...: rolled at the hero's level), a material or a block (stone, planks ...)",
            Complete = (i, a) => i == 0 ? PlayerNames().Concat(Admin.GiveNames()) : i == 1 && Admin.GiveKey(a[0]) == null ? Admin.GiveNames() : None,
            Run = (x, a) =>
            {
                if (a.Length == 0) return "usage: /give [player] <item> [amount]";
                var w = a.ToList(); long peer = x.Peer;
                if (w.Count >= 2 && Admin.GiveKey(w[0]) == null) { if (FindPlayer(x, w[0], out peer) is { } err) return err; w.RemoveAt(0); }
                if (Admin.GiveKey(w[0]) is not { } key) return Commands.NoSuch("item", w[0], Admin.GiveNames());
                int n = 1; if (w.Count > 1 && (!Int(w[1], out n) || n < 1 || n > 1000000)) return "the amount must be a number from 1 to 1000000";
                return Do(x, peer, "give", $"{key} {n}", $"gave {n} x {key}");
            }
        });

        Commands.Add(new Cmd
        {
            Name = "tp", Usage = "/tp <player | x y z | x z>", Server = true,
            Help = "teleports you to a player or a place (x z: onto the ground there); /tp <player> <other player> moves the first to the second",
            Complete = (i, _) => i <= 1 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                const string usage = "usage: /tp <player | x y z | x z>";
                if (a.Length == 0 || a.Length > 3) return usage;
                if (Num(a[0], out float px))
                {
                    if (a.Length < 2 || !Num(a[1], out float p1) || (a.Length == 3 && !Num(a[2], out _))) return usage;
                    float p2 = 0; if (a.Length == 3) Num(a[2], out p2);
                    if (Mathf.Abs(px) > 60000 || Mathf.Abs(p1) > 60000 || Mathf.Abs(p2) > 60000) return "that is outside the world";
                    return Do(x, x.Peer, "tp", a.Length == 3 ? $"{F(px)} {F(p1)} {F(p2)}" : $"{F(px)} {F(p1)}", "teleported");
                }
                if (a.Length == 3) return usage;
                long who = x.Peer;
                if (a.Length == 2) { if (FindPlayer(x, a[0], out who) is { } e1) return e1; }
                if (FindPlayer(x, a[^1], out long to) is { } e2) return e2;
                if (to == who) return a.Length == 1 ? "that is you" : "they are the same player";
                if (!Net.Active || Net.I!.PosOf(to) is not { } pos) return $"{NameOf(to)} is not in the overworld (a dungeon or the arena): cannot go there";
                return Do(x, who, "tp", $"{F(pos.X + 1.2f)} {F(pos.Y + .3f)} {F(pos.Z)}", $"teleported to {NameOf(to)}");
            }
        });

        Commands.Add(Power("heal", "heal", false, "full health, mana and stamina (you, or the named player)"));
        Commands.Add(Power("god", "god", true, "god mode: nothing damages the hero"));
        Commands.Add(Power("fly", "fly", true, "flight without wings: jump in the air to take off, hold Jump to climb, Roll to dive; never tires"));
        Commands.Add(Power("noclip", "noclip", true, "fly through blocks, walls and the ground"));

        Commands.Add(new Cmd
        {
            Name = "time", Usage = "/time [0-24 | dawn | day | noon | dusk | night | midnight]", Server = true,
            Help = "shows or sets the time of day for the whole world",
            Complete = (i, _) => i == 0 ? Times.Select(t => t.name) : None,
            Run = (x, a) =>
            {
                if (a.Length == 0) return $"it is {World.TimeOfDay * 24f:0.0} h (0-24)";
                float h; var named = Times.Where(t => t.name.Equals(a[0], StringComparison.OrdinalIgnoreCase)).ToList();
                if (named.Count > 0) h = named[0].hour; else if (!Num(a[0], out h) || h < 0 || h > 24) return "usage: /time [0-24 | dawn | day | noon | dusk | night | midnight]";
                World.TimeOfDay = Mathf.PosMod(h / 24f, 1f);   // a host sends the clock to everyone every 2 s
                return $"time set to {h:0.0} h";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "weather", Usage = "/weather <clear | rain | storm | auto>", Server = true,
            Help = "sets the weather for everyone in the world now (auto: back to the changing weather)",
            Complete = (i, _) => i == 0 ? Admin.Weathers : None,
            Run = (x, a) =>
            {
                if (a.Length == 0 || !Admin.Weathers.Contains(a[0].ToLowerInvariant())) return "usage: /weather <clear | rain | storm | auto>" + (a.Length > 0 ? "; " + Commands.NoSuch("weather", a[0], Admin.Weathers) : "");
                string wname = a[0].ToLowerInvariant();
                if (!Net.Active) return Admin.ApplyLocal("weather", wname);
                foreach (var id in Net.I!.Players.Keys.ToList()) Net.I.AdminDo(id, "weather", wname);
                return $"weather {wname} for {Net.I.Players.Count} player(s) (players who join later keep their own weather)";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "setlevel", Usage = "/setlevel [player] <level>", Server = true,
            Help = "sets the hero's level (going up also hands out the attribute, tree and rank points of those levels)",
            Complete = (i, _) => i == 0 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                if (a.Length == 0 || a.Length > 2) return "usage: /setlevel [player] <level>";
                long peer = x.Peer; if (a.Length == 2 && FindPlayer(x, a[0], out peer) is { } err) return err;
                if (!Int(a[^1], out int lv) || lv < 1 || lv > 999) return "the level must be a number from 1 to 999";
                return Do(x, peer, "level", lv.ToString(CI), $"level set to {lv}");
            }
        });

        Commands.Add(new Cmd
        {
            Name = "setskill", Usage = "/setskill [player] <skill> <level>", Server = true,
            Help = "sets a life skill's level (mining, fishing, cooking ...) or an active skill's rank (1-5)",
            Complete = (i, a) => i == 0 ? PlayerNames().Concat(SkillNames()) : i == 1 && !SkillNames().Contains(a[0]) ? SkillNames() : None,
            Run = (x, a) =>
            {
                if (a.Length < 2 || a.Length > 3) return "usage: /setskill [player] <skill> <level>";
                long peer = x.Peer; if (a.Length == 3 && FindPlayer(x, a[0], out peer) is { } err) return err;
                string skill = SkillNames().FirstOrDefault(s => s.Equals(a[^2], StringComparison.OrdinalIgnoreCase)) ?? "";
                if (skill == "") return Commands.NoSuch("skill", a[^2], SkillNames());
                if (!Int(a[^1], out int lv) || lv < 1 || lv > 999) return "the level must be a number (1 or more)";
                return Do(x, peer, "skill", $"{skill} {lv}", $"{skill} set to {lv}");
            }
        });

        Commands.Add(new Cmd
        {
            Name = "kill", Usage = "/kill [radius | player]", Server = true,
            Help = "kills the monsters within the radius of you (default 15 m, at most 300; the usual rewards drop), or the named player's hero",
            Complete = (i, _) => i == 0 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                float r = 15f;
                if (a.Length > 1) return "usage: /kill [radius | player]";
                if (a.Length == 1 && !Num(a[0], out r))
                {
                    if (FindPlayer(x, a[0], out long peer) is { } err) return err;
                    return Do(x, peer, "kill", "", $"killed {NameOf(peer)}");
                }
                if (r <= 0 || r > 300) return "the radius must be between 1 and 300";
                if (x.Pos is not { } at) return "you must be in the overworld (the server runs the monsters there)";
                int n = 0;
                foreach (var node in ((SceneTree)Engine.GetMainLoop()).GetNodesInGroup("enemies"))
                {
                    if (node is not Enemy e || e.Dead || e.Puppet || !e.IsInsideTree() || e.GlobalPosition.DistanceTo(at) > r) continue;
                    e.TakeHit(new HitInfo { Damage = 1e7f, From = at, Type = "true", NoText = x.Remote, Remote = x.Remote, Peer = x.Peer }); n++;
                }
                return $"killed {n} monster(s) within {r:0} m";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "clear", Usage = "/clear [log | inv [player]]", Server = true,
            Help = "/clear or /clear log empties the command bar's output; /clear inv [player] empties a hero's pack (worn gear, materials and blocks stay)",
            Complete = (i, _) => i == 0 ? new[] { "log", "inv" } : i == 1 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                if (a.Length == 0 || a[0].Equals("log", StringComparison.OrdinalIgnoreCase)) return Do(x, x.Peer, "clearlog", "", "");
                if (!a[0].Equals("inv", StringComparison.OrdinalIgnoreCase) || a.Length > 2) return "usage: /clear [log | inv [player]]";
                long peer = x.Peer; if (a.Length == 2 && FindPlayer(x, a[1], out peer) is { } err) return err;
                return Do(x, peer, "clearinv", "", "pack emptied");
            }
        });

        Commands.Add(new Cmd
        {
            Name = "debug", Usage = "/debug [system] [on|off]", Server = false,
            Help = "debug output of one system on this game; /debug alone lists the systems",
            Complete = (i, _) => i == 0 ? Debug.Keys.Concat(DebugInfo.Keys) : i == 1 ? OnOff : None,
            Run = (x, a) =>
            {
                if (a.Length == 0)
                    return "systems: " + string.Join(", ", Debug.Select(kv => $"{kv.Key} ({(kv.Value.get() ? "on" : "off")}: {kv.Value.help})")) + "\ninfo: " + string.Join(", ", DebugInfo.Keys);
                if (DebugInfo.TryGetValue(a[0], out var info)) return info();
                if (!Debug.TryGetValue(a[0], out var sys)) return Commands.NoSuch("debug system", a[0], Debug.Keys.Concat(DebugInfo.Keys));
                bool on = a.Length > 1 ? a[1].Equals("on", StringComparison.OrdinalIgnoreCase) : !sys.get();
                if (a.Length > 1 && !OnOff.Contains(a[1].ToLowerInvariant())) return "usage: /debug [system] [on|off]";
                sys.set(on); return $"debug {a[0].ToLowerInvariant()} {(on ? "on" : "off")}: {sys.help}";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "kick", Usage = "/kick <player> [reason]", Server = true, Help = "removes a player from the world (they can come back)",
            Complete = (i, _) => i == 0 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                if (a.Length == 0) return "usage: /kick <player> [reason]";
                if (!Net.IsHost) return "there is nobody to kick: you are not in a shared world";
                if (FindPlayer(x, a[0], out long peer) is { } err) return err;
                if (peer == x.Peer || peer == Net.MyId) return "you cannot kick yourself or the host";
                string name = NameOf(peer), why = a.Length > 1 ? string.Join(' ', a.Skip(1)) : "";
                Net.I!.AdminKick(peer, "You were kicked by an admin." + (why != "" ? " Reason: " + why : ""));
                return $"kicked {name}";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "ban", Usage = "/ban <player> [time] [reason]", Server = true,
            Help = "bans the account from the online server (time like 30m, 2h, 7d; none = for good); the login is refused with the reason. Offline players: their last hero name or @account",
            Complete = (i, _) => i == 0 ? PlayerNames() : i == 1 ? new[] { "30m", "2h", "1d", "7d" } : None,
            Run = (x, a) =>
            {
                if (a.Length == 0) return "usage: /ban <player> [time] [reason]";
                if (FindAccount(x, a[0], out string key, out long peer, out string label) is { } err) return err;
                if (key == x.Account) return "you cannot ban yourself";
                long span = a.Length > 1 ? BlockAudit.ParseSpan(a[1]) : 0; string why = string.Join(' ', a.Skip(span > 0 ? 2 : 1));
                long until = span > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + span : long.MaxValue;
                if (!Accounts.Update(key, acc => { acc.BanUntil = until; acc.BanReason = why; acc.Tokens?.Clear(); })) return "the account file could not be written";
                if (peer != 0) Net.I!.AdminKick(peer, "You were banned" + (span > 0 ? " for " + a[1] : "") + "." + (why != "" ? " Reason: " + why : ""));
                return $"banned {label}{(span > 0 ? " for " + a[1] : " for good")}{(why != "" ? " (" + why + ")" : "")}{(peer != 0 ? "; kicked" : "")}";
            }
        });
        Commands.Add(new Cmd
        {
            Name = "unban", Usage = "/unban <player | @account>", Server = true, Help = "lifts a ban",
            Run = (x, a) =>
            {
                if (a.Length != 1) return "usage: /unban <player | @account>";
                if (FindAccount(x, a[0], out string key, out _, out string label) is { } err) return err;
                return Accounts.Update(key, acc => { acc.BanUntil = 0; acc.BanReason = ""; }) ? $"unbanned {label}" : "the account file could not be written";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "mute", Usage = "/mute <player> [time]", Server = true,
            Help = "mutes the account (time like 30m, 2h; none = for good). Stored on the account: a muted player cannot send chat (World, Local, Party, whispers) until it ends (NetChat.cs)",
            Complete = (i, _) => i == 0 ? PlayerNames() : i == 1 ? new[] { "10m", "1h", "1d" } : None,
            Run = (x, a) =>
            {
                if (a.Length == 0 || a.Length > 2) return "usage: /mute <player> [time]";
                if (FindAccount(x, a[0], out string key, out long peer, out string label) is { } err) return err;
                long span = a.Length > 1 ? BlockAudit.ParseSpan(a[1]) : 0; if (a.Length > 1 && span == 0) return "the time looks like 30m, 2h or 1d";
                long until = span > 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + span : long.MaxValue;
                if (!Accounts.Update(key, acc => acc.MuteUntil = until)) return "the account file could not be written";
                if (peer != 0) { Net.I!.SetMuted(peer, until); Net.I.AdminDo(peer, "note", "You were muted by an admin" + (span > 0 ? " for " + a[1] : "") + "."); }
                return $"muted {label}{(span > 0 ? " for " + a[1] : " for good")} (they cannot use the chat; stored on the account)";
            }
        });
        Commands.Add(new Cmd
        {
            Name = "unmute", Usage = "/unmute <player | @account>", Server = true, Help = "lifts a mute",
            Complete = (i, _) => i == 0 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                if (a.Length != 1) return "usage: /unmute <player | @account>";
                if (FindAccount(x, a[0], out string key, out long peer, out string label) is { } err) return err;
                if (!Accounts.Update(key, acc => acc.MuteUntil = 0)) return "the account file could not be written";
                if (peer != 0) Net.I!.SetMuted(peer, 0);
                return $"unmuted {label}";
            }
        });

        Commands.Add(new Cmd
        {
            Name = "whois", Usage = "/whois <player | @account>", Server = true, Help = "who a player is: hero, level, where, peer id and (online server) account, admin / ban / mute state, address",
            Complete = (i, _) => i == 0 ? PlayerNames() : None,
            Run = (x, a) =>
            {
                if (a.Length != 1) return "usage: /whois <player | @account>";
                long peer = 0; string key = ""; string? perr = a[0].StartsWith('@') ? "account" : FindPlayer(x, a[0], out peer);
                if (perr != null) { peer = 0; if (!Net.Dedicated) return perr == "account" ? OnlineOnly : perr; if (FindAccount(x, a[0], out key, out peer, out _) is { } aerr) return aerr; }
                var sb = new System.Text.StringBuilder(); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (peer != 0 || !Net.Active)
                {
                    var p = Net.Active ? Net.I!.Players.GetValueOrDefault(peer) : null;
                    if (p != null) { sb.Append($"{p.Name}: level {p.Level} {p.Race} {p.Class}, peer #{p.Id}, {(p.Zone == 0 ? "overworld" : p.Zone == 2 ? "arena" : "dungeon")}"); if (Net.I!.PosOf(peer) is { } at) sb.Append($" at {at.X:0} {at.Y:0} {at.Z:0} ({Lands.RegionAt(at)})"); sb.Append($", health {p.Hp:0}/{p.MaxHp:0}{(p.Party != 0 ? ", party " + p.Party : "")}"); }
                    else sb.Append($"{Game.D.Name}: level {Game.D.Level} {Game.D.Race} {Game.D.Class} (you, offline), admin on this computer: {Admin.LocalFlag}");
                    if (Net.Active && key == "") key = Net.I!.AcctOf(peer);
                    if (Net.Active && !Net.Dedicated) sb.Append(peer == Net.MyId ? ", the host" : ", a guest");
                }
                if (Net.Dedicated && Accounts.Info(key) is { } acc)
                {
                    if (sb.Length == 0) sb.Append($"{(acc.Hero != "" ? acc.Hero : "(no hero yet)")}: offline");
                    sb.Append($"\naccount @{key[..8]} {Accounts.Mask(acc.Email)}, admin {(acc.Admin ? "YES" : "no")}, created {DateTimeOffset.FromUnixTimeSeconds(acc.Created):yyyy-MM-dd}, last login {DateTimeOffset.FromUnixTimeSeconds(acc.LastLogin):yyyy-MM-dd HH:mm} UTC");
                    if (Accounts.BanText(acc) is { } ban) sb.Append("\n" + ban);
                    if (acc.MuteUntil > now) sb.Append("\nmuted" + (acc.MuteUntil == long.MaxValue ? " for good" : $" until {DateTimeOffset.FromUnixTimeSeconds(acc.MuteUntil):yyyy-MM-dd HH:mm} UTC"));
                    if (peer != 0) sb.Append($"\naddress {(Net.I!.PeerAddr(peer) is { Length: > 0 } ad ? ad : "?")} (behind a proxy this is the proxy's address)");
                }
                return sb.ToString();
            }
        });

        Commands.Add(new Cmd
        {
            Name = "blocklog", Usage = "/blocklog [r <radius> | <player> | files] [page]", Server = true,
            Help = "the online server's record of blocks placed and removed: no argument = the block you are aiming at; r <radius> = around you (at most 64 m); <player> = everything by that player (hero name or @account, offline too). Newest first, 8 per page",
            Complete = (i, _) => i == 0 ? PlayerNames().Concat(new[] { "r", "files" }) : None,
            Run = (x, a) =>
            {
                if (!Net.Dedicated) return OnlineOnly;
                var w = a.ToList(); int page = 1;
                if (w.Count > 0 && w[0].Equals("files", StringComparison.OrdinalIgnoreCase))
                    return $"block log: {BlockAudit.Count} entries in memory (newest {BlockAudit.MaxMem} kept); files (rotated at {BlockAudit.MaxBytes / 1024} KB, {BlockAudit.KeepFiles} kept): " + string.Join(", ", BlockAudit.Files().Select(f => $"{f.file} {f.bytes / 1024} KB"));
                if (w.Count > 0 && w[0].Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    if (w.Count < 2 || !Num(w[1], out float r) || r < 1 || r > 64) return "usage: /blocklog r <radius 1-64> [page]";
                    if (w.Count > 2 && (!Int(w[2], out page) || page < 1)) return "the page must be a number (1 or more)";
                    if (x.Pos is not { } at) return "you must be in the overworld";
                    return BlockAudit.QueryRadius(at, r, page);
                }
                if (w.Count > 0 && Int(w[^1], out int pg) && (w.Count > 1 || !IsPlayerName(w[0]))) { if (pg < 1) return "the page must be a number (1 or more)"; page = pg; w.RemoveAt(w.Count - 1); }
                if (w.Count == 0)
                {
                    if (x.Aim is not { } c) return "aim at a block first (within reach), or use /blocklog r <radius> or /blocklog <player>";
                    return BlockAudit.QueryCell(c.X, c.Y, c.Z, page);
                }
                if (w.Count > 1) return "usage: /blocklog [r <radius> | <player> | files] [page]";
                string who = w[0].StartsWith('@') ? w[0][1..] : FindPlayer(x, w[0], out long peer) == null && Net.I!.AcctOf(peer) != "" ? Net.I.AcctOf(peer) : w[0].Replace('_', ' ');
                string res = BlockAudit.QueryPlayer(who, page);
                return res.EndsWith("nothing recorded") && Commands.Near(w[0], BlockAudit.Names().Select(U)) is { Length: > 0 } near && !near.Equals(w[0], StringComparison.OrdinalIgnoreCase) ? res + $"; did you mean {near}?" : res;
            }
        });

        Commands.Add(new Cmd
        {
            Name = "rollback", Usage = "/rollback <player> [time]", Server = true,
            Help = "undoes the blocks a player placed and removed in the last while (30m, 2h, 1d; default 1h), newest first; a spot someone else changed since is left alone. Hero name or @account, offline too. The rollback itself is logged",
            Complete = (i, _) => i == 0 ? PlayerNames() : i == 1 ? new[] { "30m", "1h", "2h", "1d" } : None,
            Run = (x, a) =>
            {
                if (a.Length == 0 || a.Length > 2) return "usage: /rollback <player> [time]";
                if (!Net.Dedicated) return OnlineOnly;
                long span = 3600; if (a.Length == 2 && (span = BlockAudit.ParseSpan(a[1])) == 0) return "the time looks like 30m, 2h or 1d";
                string who = a[0].StartsWith('@') ? a[0][1..] : FindPlayer(x, a[0], out long peer) == null && Net.I!.AcctOf(peer) != "" ? Net.I.AcctOf(peer) : a[0].Replace('_', ' ');
                var set = new List<(int x, int y, int z, byte id, byte meta)>(); var regen = new List<(int x, int y, int z, byte id, byte meta)>();
                var (undone, skipped) = BlockAudit.Rollback(who, span, x.Account, x.Name, set, regen);
                Net.I!.SendBlockTruth(set, regen);
                string t = a.Length == 2 ? a[1] : "1h";
                if (undone == 0 && skipped == 0)
                    return $"nothing by '{a[0]}' in the last {t}" + (Commands.Near(a[0], BlockAudit.Names().Select(U)) is { Length: > 0 } near && !near.Equals(a[0], StringComparison.OrdinalIgnoreCase) ? $"; did you mean {near}?" : "");
                return $"rolled back {undone} block change(s) by {a[0]} from the last {t} ({set.Count + regen.Count} cells)" + (skipped > 0 ? $"; {skipped} left alone (changed again since)" : "");
            }
        });

        Commands.Add(new Cmd
        {
            Name = "deadmin", Usage = "/deadmin <player>", Server = true,
            Help = "removes admin access: online from the account (hero name or @account, offline too) and from that player's computer; offline (/deadmin me) from this computer",
            Complete = (i, _) => i == 0 ? PlayerNames().Concat(new[] { "me" }) : None,
            Run = (x, a) =>
            {
                if (a.Length != 1) return "usage: /deadmin <player>";
                if (!Net.Dedicated)
                {
                    if (FindPlayer(x, a[0], out long p) is { } err) return err;
                    if (p != Net.MyId) return "in a friend's world only the host is an admin";
                    Admin.SetLocal(false); return "admin access removed on this computer";
                }
                if (FindAccount(x, a[0], out string key, out long peer, out string label) is { } aerr) return aerr;
                if (Accounts.Info(key) is not { Admin: true }) return $"{label} is not an admin";
                if (!Accounts.Update(key, acc => acc.Admin = false)) return "the account file could not be written";
                AdminGuard.Log("REMOVED by " + x.Name, key, label, "");
                if (peer != 0) { Net.I!.AdminDo(peer, "deadmin", ""); Net.I.SetPeerAdmin(peer, false); }
                return $"admin access removed from {label}";
            }
        });
    }
}
