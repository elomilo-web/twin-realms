using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// The online server (MMO mode). A headless copy of the game runs the shared world (`godot --headless -- --server`) and players
/// connect over WebSocket (Railway and most hosts only forward HTTP/WebSocket, not UDP). Before a player gets into the world they log in
/// with email and password through Godot's authentication step: no game messages flow until the server accepts them.
/// The server keeps every account's save (three hero slots); the client uploads it on every save. Friend hosting (ENet) is unchanged.
/// Auth messages are small JSON objects: {op: login|register|save|enter, ...}; replies: {op: auth|saved|enter, ok, msg, save}.
/// </summary>
public partial class Net
{
    /// <summary>True in the headless server process. It has no hero of its own in the roster.</summary>
    public static bool Dedicated;
    /// <summary>Client side of Play Online: "" (not online), "connecting", "lobby" (logged in, choosing a hero), "entering", "in".</summary>
    public static string OnlineState = "";
    public static string AuthMsg = "";
    /// <summary>Raised on the client when login state changes, so the title screen can refresh.</summary>
    public static event Action? OnlineChanged;

    readonly Dictionary<long, string> _acct = new();      // server: peer -> account key
    readonly Dictionary<long, int> _authMsgs = new();     // server: auth messages per peer (flood guard)
    string _pendingOp = "", _pendingEmail = "", _pendingPw = "";
    SceneMultiplayer SM => (SceneMultiplayer)Multiplayer;

    static WebSocketMultiplayerPeer NewWs() => new() { InboundBufferSize = 8 << 20, OutboundBufferSize = 8 << 20, MaxQueuedPackets = 8192, HandshakeTimeout = 15 };
    static byte[] Msg(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o));
    static Dictionary<string, JsonElement>? Read(byte[] data)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Encoding.UTF8.GetString(data)); } catch { return null; }
    }
    static string Str(Dictionary<string, JsonElement> m, string k) => m.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static bool Flag(Dictionary<string, JsonElement> m, string k) => m.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.True;
    static int Int(Dictionary<string, JsonElement> m, string k) => m.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    void HookAuth() { SM.PeerAuthenticating += OnAuthenticating; SM.PeerAuthenticationFailed += OnAuthFailed; }
    void UnhookAuth() { SM.PeerAuthenticating -= OnAuthenticating; SM.PeerAuthenticationFailed -= OnAuthFailed; }
    void ClearAuth() { SM.AuthCallback = new Callable(); _acct.Clear(); _authMsgs.Clear(); _pendingOp = _pendingPw = ""; }

    // ================================================================== server
    /// <summary>Starts the online server on this (headless) game: WebSocket on the given port, logins required.</summary>
    public bool Serve(int port)
    {
        Shutdown(); OnlineCfg.Load(); MaxPlayers = OnlineCfg.MaxPlayers;
        var p = NewWs(); var err = p.CreateServer(port);
        if (err != Error.Ok) { GD.PrintErr($"[SERVER] could not listen on port {port}: {err}"); return false; }
        SM.AuthCallback = Callable.From<long, byte[]>(ServerAuth); SM.AuthTimeout = OnlineCfg.LoginTimeout;
        Multiplayer.MultiplayerPeer = p; _peer = p; _live = true; _server = true; _myId = 1; Dedicated = true; HostPort = port;
        Players.Clear(); Parties.Clear(); Realm = World.Realm;
        LoadPartyChest();
        GD.Print($"[SERVER] Twin Realms {Game.Version} listening on port {port} (WebSocket), up to {MaxPlayers} players, data in {Accounts.Dir}");
        return true;
    }

    void ServerAuth(long id, byte[] data)
    {
        if (!_server || !Dedicated) return;
        int n = _authMsgs[id] = _authMsgs.GetValueOrDefault(id) + 1;
        if (n > 60) { GD.Print($"[SERVER] peer {id} sent too many login messages"); SM.DisconnectPeer((int)id); return; }
        var m = Read(data); if (m == null) { SM.DisconnectPeer((int)id); return; }
        string op = Str(m, "op");
        switch (op)
        {
            case "login":
            case "register":
            {
                if (_acct.ContainsKey(id)) return;
                if (Int(m, "ver") != Protocol) { Reply(id, new { op = "auth", ok = false, msg = $"The server runs a different version of Twin Realms ({Game.Version}). Please download the newest build." }); return; }
                string email = Str(m, "email"), pw = Str(m, "pw");
                // password hashing is slow on purpose: do it off the game thread, then answer on it
                Task.Run(() =>
                {
                    var res = op == "register" ? Accounts.Register(email, pw) : Accounts.Login(email, pw);
                    string save = res.Ok ? Accounts.LoadSave(res.Key) : "";
                    Callable.From(() => AuthDone(id, Accounts.Normalize(email), res, save)).CallDeferred();
                });
                break;
            }
            case "save":
            {
                if (!_acct.TryGetValue(id, out var key)) return;
                string? e = Accounts.StoreSave(key, Str(m, "data"));
                Reply(id, new { op = "saved", ok = e == null, msg = e ?? "" });
                break;
            }
            case "enter":
            {
                if (!_acct.ContainsKey(id)) return;
                if (Players.Count >= MaxPlayers) { Reply(id, new { op = "enter", ok = false, msg = "The world is full. Try again in a few minutes." }); return; }
                SM.CompleteAuth((int)id);
                break;
            }
        }
    }

    void AuthDone(long id, string email, Accounts.Result res, string save)
    {
        if (Array.IndexOf(SM.GetAuthenticatingPeers(), (int)id) < 0) return;   // they left while the password was checked
        if (!res.Ok) { Reply(id, new { op = "auth", ok = false, msg = res.Message }); return; }
        // one session per account: an older connection with the same account is closed
        foreach (var kv in new List<KeyValuePair<long, string>>(_acct))
            if (kv.Value == res.Key && kv.Key != id)
            {
                GD.Print($"[SERVER] {Accounts.Mask(email)} logged in again; closing the older session {kv.Key}");
                DropSession(kv.Key, "You logged in from somewhere else.");
            }
        _acct[id] = res.Key;
        Reply(id, new { op = "auth", ok = true, msg = res.Message, email, save });
    }

    /// <summary>
    /// Server: end a session ourselves. Godot does not raise "peer disconnected" for peers the server disconnects,
    /// so the player leaves the roster here; the socket is closed a moment later if the old game has not closed it already.
    /// </summary>
    void DropSession(long old, string why)
    {
        if (Players.ContainsKey(old)) { RpcId(old, MethodName.R_Kick, why); OnPeerDisconnected(old); }
        _acct.Remove(old); _authMsgs.Remove(old);
        GetTree().CreateTimer(3f).Timeout += () =>
        {
            if (_peer == null || !Dedicated) return; int o = (int)old;
            if (Array.IndexOf(SM.GetPeers(), o) >= 0 || Array.IndexOf(SM.GetAuthenticatingPeers(), o) >= 0) SM.DisconnectPeer(o);
        };
    }

    void Reply(long id, object o) => SM.SendAuth((int)id, Msg(o));

    void OnAuthFailed(long id) { _acct.Remove(id); _authMsgs.Remove(id); if (Dedicated) GD.Print($"[SERVER] peer {id} left before entering the world"); else if (id == 1 && OnlineState != "") { AuthMsg = AuthMsg == "" ? "The server closed the connection." : AuthMsg; CloseOnline(); } }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    void R_SaveUp(string json)
    {
        if (!Dedicated) return; long id = Sender;
        if (!_acct.TryGetValue(id, out var key)) return;
        string? e = Accounts.StoreSave(key, json);
        if (e != null) { GD.PrintErr($"[SERVER] save from peer {id} rejected: {e}"); RpcId(id, MethodName.R_Note, "The server could not store your save: " + e); }
        else Count("saves");
    }

    // ================================================================== client
    /// <summary>Connects to the online server and logs in (op "login") or creates an account (op "register").</summary>
    public bool Connect(string url, string op, string email, string password)
    {
        Shutdown(); OnlineCfg.Load();
        url = url.Trim(); if (url == "") { AuthMsg = "Type the server address first."; OnlineChanged?.Invoke(); return false; }
        if (!url.Contains("://")) url = (url.StartsWith("localhost") || url.StartsWith("127.") ? "ws://" : "wss://") + url;
        var p = NewWs(); var err = p.CreateClient(url);
        if (err != Error.Ok) { AuthMsg = $"Could not start connecting ({err})."; OnlineChanged?.Invoke(); return false; }
        SM.AuthCallback = Callable.From<long, byte[]>(ClientAuth); SM.AuthTimeout = OnlineCfg.LoginTimeout;
        Multiplayer.MultiplayerPeer = p; _peer = p; _server = false;
        _pendingOp = op; _pendingEmail = email.Trim(); _pendingPw = password;
        OnlineState = "connecting"; AuthMsg = "Connecting to " + url + " ...";
        OnlineChanged?.Invoke();
        return true;
    }

    void OnAuthenticating(long id)
    {
        if (_server || id != 1 || _pendingOp == "") return;
        SM.SendAuth(1, Msg(new { op = _pendingOp, email = _pendingEmail, pw = _pendingPw, ver = Protocol }));
        _pendingOp = ""; _pendingPw = ""; AuthMsg = "Checking your account ..."; OnlineChanged?.Invoke();
    }

    void ClientAuth(long id, byte[] data)
    {
        var m = Read(data); if (m == null || id != 1) return;
        switch (Str(m, "op"))
        {
            case "auth":
                AuthMsg = Str(m, "msg");
                if (Flag(m, "ok")) { Game.UseOnline(Str(m, "email"), Str(m, "save")); OnlineState = "lobby"; GD.Print("[NET] logged in to the online server"); }
                else CloseOnline();
                break;
            case "saved":
                if (!Flag(m, "ok")) { AuthMsg = "The server could not store your save: " + Str(m, "msg"); GD.PrintErr("[NET] " + AuthMsg); }
                break;
            case "enter":
                if (!Flag(m, "ok")) { AuthMsg = Str(m, "msg"); CloseOnline(); }
                break;
        }
        OnlineChanged?.Invoke();
    }

    /// <summary>Logged in and a hero is chosen: finish the login step; the usual hello/welcome follows and onWelcome builds the world.</summary>
    public void EnterOnline(Action onWelcome)
    {
        if (OnlineState != "lobby") return;
        _onWelcome = onWelcome; OnlineState = "entering"; AuthMsg = "Entering the world ...";
        SM.SendAuth(1, Msg(new { op = "enter" })); SM.CompleteAuth(1);
    }

    /// <summary>Client: stores the save file on the server (during hero select through the login channel, in the world as a message).</summary>
    public void UploadSave(string json)
    {
        if (_server || _peer == null || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
        if (_live) RpcId(1, MethodName.R_SaveUp, json);
        else if (OnlineState == "lobby") SM.SendAuth(1, Msg(new { op = "save", data = json }));
    }

    /// <summary>Before closing: send what is still queued (the last save) so quitting right after saving loses nothing. Waits at most 2 s.</summary>
    void FlushOnline()
    {
        if (_server || _peer is not WebSocketMultiplayerPeer ws || ws.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
        var wp = ws.GetPeer(1); if (wp == null) return;
        ulong t0 = Time.GetTicksMsec();
        while (wp.GetCurrentOutboundBufferedAmount() > 0 && Time.GetTicksMsec() - t0 < 2000) { ws.Poll(); OS.DelayMsec(5); }
    }

    /// <summary>Log out: close the connection and go back to the heroes saved on this computer.</summary>
    public void Logout() { AuthMsg = ""; Shutdown(); OnlineChanged?.Invoke(); }

    void CloseOnline() { string keep = AuthMsg; Shutdown(); AuthMsg = keep; OnlineChanged?.Invoke(); }
}
