using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Admin access and commands over the network (focus F-43, F-44). The host decides everything:
///   R_AdminLogin (client -> server): the typed login line; the online server checks it against the hash (Admin.PasswordOk) off the
///     game thread, rate-limited and logged by AdminGuard, stores Admin on the account file and answers with R_AdminState.
///   R_AdminHello (client -> server, once per session): "what am I?"; the server reads the account's flags and answers R_AdminState.
///   R_Cmd (client -> server): one command line (plus the block cell the player aims at); run by Commands.Exec with the SERVER's
///     admin flag for the sender's account; the output comes back as R_CmdOut.
///   R_AdminDo (server -> one client): what a command does to that player's hero (teleport, heal, give, level ...), Admin.ApplyLocal.
/// In a friend's hosted world there are no accounts: only the host (with its own offline admin flag) can run commands.
/// </summary>
public partial class Net
{
    readonly Dictionary<long, string> _flagsOf = new();     // server: peer -> account key whose flags were read
    readonly Dictionary<long, long> _muteUntil = new();     // server: peer -> unix time the mute ends
    readonly Dictionary<long, (ulong sec, int n)> _cmdRate = new();
    long _helloFor;                                         // client: the session (our peer id) we asked the server about

    public string AcctOf(long peer) => _acct.GetValueOrDefault(peer, "");
    public string PeerAddr(long peer) { try { return (_peer as WebSocketMultiplayerPeer)?.GetPeerAddress((int)peer) ?? ""; } catch { return ""; } }

    /// <summary>Server truth: is this peer an admin? Online server: the flag on the account they logged in with. Friend hosting: only the host.</summary>
    public bool PeerIsAdmin(long peer)
    {
        if (!_server) return false;
        if (!Dedicated) return peer == MyId && Admin.IsAdmin;
        if (!_acct.TryGetValue(peer, out var key)) { Admin.AdminPeers.Remove(peer); _flagsOf.Remove(peer); _muteUntil.Remove(peer); return false; }
        if (!_flagsOf.TryGetValue(peer, out var seen) || seen != key) ReadFlags(peer, key);
        return Admin.AdminPeers.Contains(peer);
    }
    void ReadFlags(long peer, string key)
    {
        var a = Accounts.Info(key); _flagsOf[peer] = key;
        if (a != null && a.Admin) Admin.AdminPeers.Add(peer); else Admin.AdminPeers.Remove(peer);
        if (a != null && a.MuteUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) _muteUntil[peer] = a.MuteUntil; else _muteUntil.Remove(peer);
    }
    /// <summary>Server: muted players (for chat, when the game gets one; /whois shows it).</summary>
    public bool IsMuted(long peer) => _muteUntil.TryGetValue(peer, out long t) && t > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public void SetMuted(long peer, long until) { if (until > 0) _muteUntil[peer] = until; else _muteUntil.Remove(peer); }
    public void SetPeerAdmin(long peer, bool on)
    {
        if (on) Admin.AdminPeers.Add(peer); else Admin.AdminPeers.Remove(peer);
        if (peer != MyId && Players.ContainsKey(peer)) RpcId(peer, MethodName.R_AdminState, on, on ? "You are now an admin." : "Your admin access was removed.");
    }

    // ================================================================== session start
    /// <summary>Client, every frame from the command bar: once we are in someone's world, ask the host what we are.</summary>
    public void AdminHelloTick()
    {
        if (!IsClient) { _helloFor = 0; return; }
        if (_helloFor == _myId || !Players.ContainsKey(_myId)) return;
        _helloFor = _myId; Admin.SetServerFlag(false); RpcId(1, MethodName.R_AdminHello);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_AdminHello()
    {
        if (!_server) return; long id = Sender; if (id == MyId || !Players.TryGetValue(id, out var info)) return;
        bool admin = false;
        if (Dedicated && _acct.TryGetValue(id, out var key))
        {
            ReadFlags(id, key); admin = Admin.AdminPeers.Contains(id);
            string hero = info.Name; Task.Run(() => Accounts.Update(key, a => a.Hero = hero));   // so /ban, /whois and /deadmin find them by hero name when they are offline
        }
        RpcId(id, MethodName.R_AdminState, admin, "");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_AdminState(bool admin, string msg)
    {
        Admin.SetServerFlag(admin);
        if (msg != "") { CommandBar.Print(msg); Ui.Note(msg, UiTheme.Highlight); }
    }

    // ================================================================== the admin login
    /// <summary>Client: sends the typed login line to the host over the game connection (TLS on the online server). It is not kept anywhere here.</summary>
    public void AdminLogin(string line) { if (IsClient) RpcId(1, MethodName.R_AdminLogin, line); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_AdminLogin(string line)
    {
        if (!_server) return; long id = Sender; if (id == MyId || !Players.ContainsKey(id)) return;
        if (!Dedicated || !_acct.TryGetValue(id, out var key)) { RpcId(id, MethodName.R_AdminState, false, "In a friend's world only the host can be an admin."); return; }
        string addr = PeerAddr(id), who = Accounts.Mask(Accounts.Info(key)?.Email ?? "") + " / " + NameOf(id);
        AdminGuard.Configure();
        if (AdminGuard.Blocked(key, addr) is { } wait) { AdminGuard.Log("BLOCKED (locked out)", key, who, addr); RpcId(id, MethodName.R_AdminState, false, wait); return; }
        bool locked = AdminGuard.Fail(key, addr);   // counted as failed until proven right, so attempts in flight count too
        Task.Run(() =>
        {
            bool ok = Admin.PasswordOk(line);   // slow on purpose: off the game thread
            Callable.From(() => AdminLoginDone(id, key, addr, who, ok, locked)).CallDeferred();
        });
    }
    void AdminLoginDone(long id, string key, string addr, string who, bool ok, bool locked)
    {
        if (ok)
        {
            AdminGuard.Ok(key); AdminGuard.Unfail(addr);
            bool saved = Accounts.Update(key, a => a.Admin = true);
            AdminGuard.Log(saved ? "GRANTED" : "GRANTED but the account file could not be written", key, who, addr);
            if (!saved || !_acct.TryGetValue(id, out var k) || k != key) return;   // they left meanwhile: the account has the flag for next time
            _flagsOf[id] = key; Admin.AdminPeers.Add(id);
            RpcId(id, MethodName.R_AdminState, true, "Admin access granted. It is saved on your account. Try /help.");
            return;
        }
        AdminGuard.Log(locked ? "FAILED (now locked out)" : "FAILED", key, who, addr);
        if (_acct.TryGetValue(id, out var k2) && k2 == key)
            RpcId(id, MethodName.R_AdminState, Admin.AdminPeers.Contains(id), locked ? AdminGuard.Blocked(key, addr) ?? "Too many wrong attempts." : "unknown command (try /help)");
    }

    // ================================================================== commands
    /// <summary>Client: asks the host to run a command line. aim = the block cell we are looking at (for /blocklog).</summary>
    public void SendCommand(string line, Vector3I? aim)
    {
        if (!IsClient) return; var a = aim ?? Vector3I.Zero;
        RpcId(1, MethodName.R_Cmd, line, aim != null, a.X, a.Y, a.Z);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_Cmd(string line, bool hasAim, int ax, int ay, int az)
    {
        if (!_server) return; long id = Sender; if (id == MyId || !Players.ContainsKey(id)) return;
        // at most 8 command lines a second from one player
        ulong sec = Time.GetTicksMsec() / 1000; var r = _cmdRate.GetValueOrDefault(id); r = r.sec == sec ? (sec, r.n + 1) : (sec, 1); _cmdRate[id] = r;
        if (r.n > 8) return;
        if (line.Length > 300) { RpcId(id, MethodName.R_CmdOut, "that command line is too long"); return; }
        var x = new CmdCtx { Peer = id, Account = AcctOf(id), Name = NameOf(id), IsAdmin = PeerIsAdmin(id), Remote = true, Pos = PosOf(id), Aim = hasAim ? new Vector3I(ax, ay, az) : null };
        if (!x.IsAdmin)
        {
            // nothing a non-admin may run reaches the server (/help is local): refuse without looking at the line or logging its text
            GD.Print($"[ADMIN] refused a command from {x.Name} (peer {id}): not an admin"); RpcId(id, MethodName.R_CmdOut, Commands.NotAdmin); return;
        }
        string res = Commands.Exec(line, x);
        GD.Print($"[ADMIN] {x.Name} [{(x.Account.Length > 8 ? x.Account[..8] : x.Account)}] ran: /{string.Join(' ', Commands.Words(line))}");
        if (res.Length > 6000) res = res[..6000] + "\n...";
        if (Players.ContainsKey(id)) RpcId(id, MethodName.R_CmdOut, res);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_CmdOut(string text) => CommandBar.Print(text);

    /// <summary>Host: applies a command's effect to a player's hero (on our own machine at once, otherwise on theirs).</summary>
    public void AdminDo(long target, string what, string arg)
    {
        if (target == MyId && !Dedicated) Admin.ApplyLocal(what, arg);
        else if (_server && Players.ContainsKey(target)) RpcId(target, MethodName.R_AdminDo, what, arg);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    void R_AdminDo(string what, string arg) { string res = Admin.ApplyLocal(what, arg); if (res != "" && Admin.IsAdmin && what != "note") CommandBar.Print(res); }

    /// <summary>Host: removes a player from the world with a message.</summary>
    public void AdminKick(long peer, string why)
    {
        if (!_server || peer == MyId || !Players.ContainsKey(peer)) return;
        if (Dedicated) DropSession(peer, why); else RpcId(peer, MethodName.R_Kick, why);
    }
}
