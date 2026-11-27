using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>
/// Admin access (focus F-43). The admin password is never in the game: only its PBKDF2-SHA256 hash (fixed salt, 210,000 rounds) is.
/// Offline: typing the password line into the command bar once writes user://admin.dat (kept apart from the hero saves and tied to this
/// computer), and this machine's player is an admin from then on. Online: the bar sends the line to the server, which checks it against
/// the same hash, stores Admin on the account file and tells the client; the flag the client holds only decides what the UI offers,
/// every command is checked again on the server (NetAdmin.cs). In a friend's hosted world only the host can be an admin.
/// Admins bypass town zones (TownZones) and the online no-mining rule (BlockAudit.cs).
/// </summary>
public static class Admin
{
    // PBKDF2-SHA256 of the admin login line; an online server may replace it with the environment variable TR_ADMIN_HASH (base64, same salt and rounds)
    const string HashB64 = "vizGdbNBc8iTU+fDZhG5wfUveMpAbVku21xP+M7GpnQ=";
    const string SaltText = "TwinRealms/admin/v1"; const int Rounds = 210000;
    const string LocalPath = "user://admin.dat";
    static bool _local, _server, _loaded; static bool? _forced;

    /// <summary>
    /// This machine's player is an admin: offline and when hosting, the flag saved on this computer; as a client of the online server,
    /// what the server last told us; in a friend's world, never. Setting it forces a value (dev captures).
    /// </summary>
    public static bool IsAdmin
    {
        get => _forced ?? (Net.IsClient ? Game.Online && _server : _local);
        set => _forced = value;
    }
    /// <summary>Host side: multiplayer peer ids the server knows as admins (filled from the account files, NetAdmin.cs).</summary>
    public static readonly HashSet<long> AdminPeers = new();
    public static bool IsPeerAdmin(long peer) => Net.I != null && Net.I.PeerIsAdmin(peer);

    /// <summary>/god, /fly, /noclip on this machine's hero (switched by the server's answer when online).</summary>
    public static bool God, Fly, Noclip;
    /// <summary>Wings or /fly: may take off (Player.cs).</summary>
    public static bool CanFly => Fly || Noclip;

    static byte[] Wanted()
    {
        string env = Net.Dedicated ? System.Environment.GetEnvironmentVariable("TR_ADMIN_HASH") ?? "" : "";
        try { if (env != "") return Convert.FromBase64String(env); } catch { }
        return Convert.FromBase64String(HashB64);
    }
    /// <summary>True if this typed line is the admin login. Slow on purpose (about 0.1 s): never call it every frame.</summary>
    public static bool PasswordOk(string? line)
    {
        line = (line ?? "").Trim(); if (line.Length == 0 || line.Length > 128) return false;
        var h = Rfc2898DeriveBytes.Pbkdf2(line, Encoding.UTF8.GetBytes(SaltText), Rounds, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(h, Wanted());
    }

    // ---------------------------------------------------------------- the flag saved on this computer
    static string LocalToken() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HashB64 + "|" + OS.GetUniqueId() + "|admin")));
    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        try { using var f = Godot.FileAccess.Open(LocalPath, Godot.FileAccess.ModeFlags.Read); _local = f != null && f.GetAsText().Trim() == LocalToken(); } catch { _local = false; }
    }
    /// <summary>Offline admin access on this computer, on or off (permanent: every hero, every session).</summary>
    public static void SetLocal(bool on)
    {
        _loaded = true; _local = on;
        try
        {
            if (on) { using var f = Godot.FileAccess.Open(LocalPath, Godot.FileAccess.ModeFlags.Write); f?.StoreString(LocalToken()); }
            else if (Godot.FileAccess.FileExists(LocalPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(LocalPath));
        }
        catch (Exception e) { GD.PushWarning("[ADMIN] could not save the admin flag: " + e.Message); }
        if (!on) ClearPowers();
    }
    public static bool LocalFlag { get { Load(); return _local; } }
    /// <summary>Client: what the online server says about our account.</summary>
    public static void SetServerFlag(bool on) { _server = on; if (!on && Net.IsClient) ClearPowers(); }
    static void ClearPowers() { God = false; Fly = false; SetNoclip(false); }

    // ---------------------------------------------------------------- helpers for commands
    /// <summary>The block cell the hero is aiming at (build-mode reach), for /blocklog.</summary>
    public static Vector3I? AimCell()
    {
        var p = Player.Instance; if (p == null || !GodotObject.IsInstanceValid(p) || !p.IsInsideTree() || !BlockWorld.Enabled) return null;
        try { return BlockAim.Target(p.GetViewport(), p.BlockEye, Blocks.F("world.reach", 6f) + 4f) is { } t ? t.b : null; } catch { return null; }
    }
    static uint _maskBefore;
    static void SetNoclip(bool on)
    {
        var p = Player.Instance; bool was = Noclip; Noclip = on;
        if (p == null || !GodotObject.IsInstanceValid(p) || was == on) return;
        if (on) { _maskBefore = p.CollisionMask; p.CollisionMask = 0; p.Flying = true; }
        else p.CollisionMask = _maskBefore != 0 ? _maskBefore : 3;
    }
    /// <summary>Every physics frame (CommandBar): a no-clip hero must stay in flight (it would fall through the world) and never tires.</summary>
    public static void Tick()
    {
        if (!Noclip && !Fly) return;
        var p = Player.Instance; if (p == null || !GodotObject.IsInstanceValid(p)) { Noclip = false; return; }
        if (p.Dead) return;
        if (Noclip) { if (p.CollisionMask != 0) { _maskBefore = p.CollisionMask; p.CollisionMask = 0; } p.Flying = true; }
        p.St = Game.D.MaxSt();
    }
    static bool Toggle(bool cur, string arg) => arg switch { "1" or "on" => true, "0" or "off" => false, _ => !cur };

    public static readonly string[] Weathers = { "clear", "rain", "storm", "auto" };
    /// <summary>Things /give and /spawn know besides materials and blocks.</summary>
    public static readonly string[] GiveBasics = { "gold", "xp", "hp_potion", "mp_potion", "valor", "skillpoints", "attrpoints", "rankpoints" };
    public static IEnumerable<string> GiveNames()
    {
        GatherDb.Load(); Blocks.Load();
        return GiveBasics.Concat(PlayerStats.Slots).Concat(GatherDb.Mats.Keys).Concat(Blocks.Defs.Where(d => d.Id != 0 && !d.IsFluid && d.Key != "bedrock").Select(d => d.Key));
    }
    /// <summary>The name as /give understands it ("stone" -> "blk_stone"), or null.</summary>
    public static string? GiveKey(string name)
    {
        string n = name.ToLowerInvariant(); GatherDb.Load();
        if (GiveBasics.Contains(n) || PlayerStats.Slots.Contains(n)) return n;
        foreach (var k in GatherDb.Mats.Keys) if (k.Equals(name, StringComparison.OrdinalIgnoreCase)) return k;
        if (n.StartsWith("blk_")) n = n[4..];
        return Blocks.Exists(n) ? Blocks.Item(n) : null;
    }

    /// <summary>
    /// What an admin command does to a hero, applied on that hero's own machine (offline: at once; online: the server sends it after its
    /// own admin check, Net.R_AdminDo). Returns a line for the log.
    /// </summary>
    public static string ApplyLocal(string what, string arg)
    {
        var p = Player.Instance; var d = Game.D; var ci = CultureInfo.InvariantCulture;
        if (p == null || !GodotObject.IsInstanceValid(p)) return "no hero in the world";
        var w = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float Num(int i, float def = 0) => i < w.Length && float.TryParse(w[i], NumberStyles.Float, ci, out float v) ? v : def;
        switch (what)
        {
            case "tp":
            {
                Vector3 to = w.Length >= 3 ? new Vector3(Num(0), Num(1), Num(2)) : World.OnGround(Num(0), Num(1), .6f);
                p.Teleport(to); return $"teleported to {to.X:0} {to.Y:0} {to.Z:0}";
            }
            case "heal": p.Hp = d.MaxHp(); p.Mp = d.MaxMp(); p.St = d.MaxSt(); Ui.Note("Healed", new Color(.5f, 1f, .6f)); return "healed";
            case "god": God = Toggle(God, arg); Ui.Note("God mode " + (God ? "on" : "off"), UiTheme.Highlight); return "god mode " + (God ? "on" : "off");
            case "fly":
                Fly = Toggle(Fly, arg); if (Fly && !p.IsOnFloor()) p.Flying = true; if (!Fly && !Noclip && !Wings.Worn) p.Flying = false;
                Ui.Note(Fly ? "Flight on: jump in the air to take off, hold Jump to climb, Roll to dive" : "Flight off", UiTheme.Highlight); return "fly " + (Fly ? "on" : "off");
            case "noclip":
                SetNoclip(Toggle(Noclip, arg)); if (!Noclip && !Fly && !Wings.Worn) p.Flying = false;
                Ui.Note(Noclip ? "No-clip on: you pass through everything (flying)" : "No-clip off", UiTheme.Highlight); return "noclip " + (Noclip ? "on" : "off");
            case "weather":
            {
                float r = w.Length == 0 ? -1 : w[0] switch { "clear" => 0f, "cloud" => .12f, "rain" => .6f, "storm" => 1f, _ => -1f };
                Weather.Force(r); return "weather " + (r < 0 ? "auto" : w[0]);
            }
            case "time": World.TimeOfDay = Mathf.PosMod(Num(0, 12f) / 24f, 1f); return $"time {Num(0, 12f):0.#}h";
            case "give":
            case "drop":
            {
                if (w.Length == 0) return "nothing given"; int n = Math.Max(1, (int)Num(1, 1)); string k = w[0]; var at = p.GlobalPosition;
                if (PlayerStats.Slots.Contains(k))
                {
                    for (int i = 0; i < Math.Min(n, 20); i++)
                    {
                        var it = Items.Roll(d.Level, 1f, k);
                        if (what == "drop") Loot.Drop(at + new Vector3(GD.Randf() - .5f, .3f, GD.Randf() - .5f) * 2f, 0, it); else { d.Inv.Add(it); Ui.Note(it.Describe(), it.Color); }
                    }
                    Game.Notify(); return $"{Math.Min(n, 20)} x {k}";
                }
                switch (k)
                {
                    case "gold": if (what == "drop") Loot.Drop(at + Vector3.Forward, n); else { d.Gold += n; Ui.Note($"+{n} gold", UiTheme.Highlight); } break;
                    case "xp": p.AddXp(n, at); break;
                    case "hp_potion": d.HpPot += n; Ui.Note($"+{n} health potion", Colors.White); break;
                    case "mp_potion": d.MpPot += n; Ui.Note($"+{n} mana potion", Colors.White); break;
                    case "valor": Valor.Give(n); break;
                    case "skillpoints": d.SkillPts += n; break;
                    case "attrpoints": d.AttrPts += n; break;
                    case "rankpoints": d.RankPts += n; break;
                    default: GatherDb.Give(k, n, at); break;   // a material or a block ("blk_stone")
                }
                Game.Notify(); return $"{n} x {k}";
            }
            case "npc":
            {
                if (w.Length == 0 || NpcSchedule.LiveOf(w[0]) is not { } l) return "that NPC is not in your world right now";
                var f = -p.GlobalTransform.Basis.Z; l.N.GlobalPosition = World.OnGround(p.GlobalPosition.X + f.X * 2.5f, p.GlobalPosition.Z + f.Z * 2.5f, .1f);
                return "brought " + l.Rec.Name;
            }
            case "level":
            {
                int n = Math.Clamp((int)Num(0, d.Level), 1, 999), was = d.Level;
                if (n > was) { d.AttrPts += (n - was) * Progression.AttrPerLevel; d.SkillPts += (n - was) * Progression.TreePerLevel; d.RankPts += (n - was) * Progression.RankPerLevel; }
                d.Level = n; d.Xp = 0; Game.Notify(); p.Hp = d.MaxHp(); p.Mp = d.MaxMp();
                Ui.Note($"Level set to {n}", UiTheme.Highlight); return $"level {was} -> {n}";
            }
            case "skill":
            {
                if (w.Length < 2) return "skill and level needed"; string id = w[0]; int n = (int)Num(1, 1); LifeSkills.Load();
                if (LifeSkills.Defs.ContainsKey(id))
                {
                    n = Math.Clamp(n, 1, LifeSkills.Max(id)); int xp = 0; for (int lv = 1; lv < n; lv++) xp += LifeSkills.Need(id, lv);
                    d.Life ??= new(); d.Life[id] = xp; Game.Notify(); Ui.Note($"{LifeSkills.Defs[id].Name} set to level {n}", UiTheme.Highlight); return $"{id} level {n}";
                }
                if (Skills.Get(id) != null)
                {
                    n = Math.Clamp(n, 1, Skills.MaxRank);
                    if (d.Skills.TryGetValue(id, out var st)) st.Rank = n; else d.Skills[id] = new SkillState { Rank = n };
                    Game.Notify(); Ui.Note($"{id} set to rank {n}", UiTheme.Highlight); return $"{id} rank {n}";
                }
                return $"no skill '{id}'";
            }
            case "kill":
            {
                bool g = God; God = false;
                try { p.TakeHit(new HitInfo { Damage = 1e7f, From = p.GlobalPosition + Vector3.Up, NoBlock = true, Type = "true" }); } finally { God = g; }
                return "killed";
            }
            case "clearinv": { int n = d.Inv.Count; d.Inv.Clear(); Game.Notify(); Ui.Note("An admin emptied your pack", new Color(1, .6f, .5f)); return $"{n} items removed"; }
            case "note": Ui.Note(arg, UiTheme.Highlight); return arg;
            case "clearlog": CommandBar.ClearLog(); return "";
            case "deadmin": SetLocal(false); return "admin access removed";
        }
        return "unknown effect " + what;
    }
}

/// <summary>
/// Server side of the admin login: failed attempts are counted per account and per address inside a time window; too many lock that key
/// out, longer each time (10 min, 20 min, 40 min ... up to a day). Every attempt is written to admin_log.txt in the server's data folder
/// (time, account, address, outcome), never the typed text. Numbers: data/tuning.json game.online.admin* (defaults below).
/// </summary>
public static class AdminGuard
{
    sealed class Rec { public readonly List<long> Fails = new(); public int Locks; public long Until; }
    static readonly Dictionary<string, Rec> _r = new();
    static readonly object Gate = new();
    public static int MaxFails = 5, AddrMaxFails = 20, WindowSec = 600, LockSec = 600, MaxLockSec = 86400;
    /// <summary>Seconds since 1970 (the self-test moves the clock).</summary>
    public static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>Where the log goes ("" = the server data folder).</summary>
    public static string LogDir = "";
    static bool _cfg;
    public static void Configure()
    {
        if (_cfg) return; _cfg = true;
        MaxFails = Tune.I("online.adminFails", MaxFails); AddrMaxFails = Tune.I("online.adminFailsPerAddress", AddrMaxFails);
        WindowSec = Tune.I("online.adminWindowSec", WindowSec); LockSec = Tune.I("online.adminLockSec", LockSec);
    }
    public static void Reset() { lock (Gate) _r.Clear(); }

    /// <summary>Null if this account / address may try now, else the refusal with the wait.</summary>
    public static string? Blocked(string account, string address)
    {
        long now = Clock();
        lock (Gate)
            foreach (var k in Keys(account, address))
                if (_r.TryGetValue(k, out var r) && r.Until > now) return $"Too many wrong attempts. Try again in {Wait(r.Until - now)}.";
        return null;
    }
    static string Wait(long s) => s >= 3600 ? $"{(s + 3599) / 3600} h" : s >= 60 ? $"{(s + 59) / 60} min" : $"{s} s";
    static IEnumerable<string> Keys(string account, string address) { if (account != "") yield return "a:" + account; if (address != "") yield return "ip:" + address; }

    /// <summary>Counts an attempt as failed (call BEFORE checking, so attempts in flight count too). True if this one caused a lockout.</summary>
    public static bool Fail(string account, string address)
    {
        long now = Clock(); bool locked = false;
        lock (Gate)
            foreach (var k in Keys(account, address))
            {
                if (!_r.TryGetValue(k, out var r)) _r[k] = r = new Rec();
                r.Fails.RemoveAll(t => now - t > WindowSec); r.Fails.Add(now);
                if (r.Fails.Count >= (k[0] == 'a' ? MaxFails : AddrMaxFails))
                {
                    r.Until = now + Math.Min(MaxLockSec, (long)LockSec << Math.Min(r.Locks, 10)); r.Locks++; r.Fails.Clear(); locked = true;
                }
            }
        return locked;
    }
    /// <summary>The password was right: the account's count starts over (the address keeps its own).</summary>
    public static void Ok(string account) { lock (Gate) _r.Remove("a:" + account); }
    /// <summary>Takes back the attempt counted by Fail for a correct password, for the address key.</summary>
    public static void Unfail(string address)
    {
        lock (Gate)
        {
            if (!_r.TryGetValue("ip:" + address, out var r)) return;
            if (r.Fails.Count > 0) r.Fails.RemoveAt(r.Fails.Count - 1);
            else if (r.Until > Clock()) { r.Until = 0; r.Locks = Math.Max(0, r.Locks - 1); }   // the right password was the attempt that tripped the lock: undo it
        }
    }

    /// <summary>One line in admin_log.txt (rotated at 512 KB, one old file kept) and the server log. Never pass the typed text.</summary>
    public static void Log(string outcome, string account, string who, string address)
    {
        string line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z\t{outcome}\taccount {account}\t{who}\taddress {(address == "" ? "?" : address)}";
        GD.Print("[ADMIN] " + line.Replace('\t', ' '));
        try
        {
            lock (Gate)
            {
                string path = Path.Combine(LogDir != "" ? LogDir : Accounts.Dir, "admin_log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, line + "\n");
            }
        }
        catch (Exception e) { GD.PushWarning("[ADMIN] log not written: " + e.Message); }
    }
}
