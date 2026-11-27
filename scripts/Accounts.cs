using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace TwinRealms;

/// <summary>Settings for the online server and the Play Online screen (data/online.json).</summary>
public static class OnlineCfg
{
    /// <summary>The one online server every player uses (Railway). Players cannot change it.</summary>
    public const string Server = "wss://twin-realms-production.up.railway.app";
    public static int Port = 8080, MaxPlayers = 64, MinPassword = 8, MaxSaveKb = 1024, FailedBeforeLock = 5, Iterations = 120000;
    public static float SaveEvery = 20f, LockSeconds = 60f, LoginTimeout = 600f;
    static bool _loaded;
    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        var r = JsonUtil.Load("res://data/online.json").RootElement;
        Port = (int)JsonUtil.F(r, "port", Port); MaxPlayers = (int)JsonUtil.F(r, "maxPlayers", MaxPlayers);
        SaveEvery = JsonUtil.F(r, "saveEverySec", SaveEvery); MinPassword = (int)JsonUtil.F(r, "minPassword", MinPassword); MaxSaveKb = (int)JsonUtil.F(r, "maxSaveKb", MaxSaveKb);
        FailedBeforeLock = (int)JsonUtil.F(r, "failedLoginsBeforeLock", FailedBeforeLock); LockSeconds = JsonUtil.F(r, "lockSeconds", LockSeconds);
        LoginTimeout = JsonUtil.F(r, "loginTimeoutSec", LoginTimeout); Iterations = (int)JsonUtil.F(r, "hashIterations", Iterations);
    }
}

/// <summary>
/// Server side: player accounts (email + password) and their saves, kept as JSON files in the data folder
/// (TR_DATA, e.g. a Railway volume mounted at /data; otherwise user://server). Passwords are never stored:
/// only a random salt and a PBKDF2-SHA256 hash. Safe to call from worker threads (one lock around the files).
/// </summary>
public static class Accounts
{
    public class Account
    {
        public string Email = "", Salt = "", Hash = ""; public int Iter; public long Created, LastLogin;
        public List<Token> Tokens = new();   // saved logins (launcher "Remember me"): only a hash of each token is stored
        // F-43 / F-42: admin access (set by the server after the admin login), ban and mute (unix time they end; long.MaxValue = for good), the last hero played (to find the account by name)
        public bool Admin; public long BanUntil, MuteUntil; public string BanReason = "", Hero = "";
        // A-32: friends (mutual), friend requests received, and blocked accounts (also the chat's /ignore list): the other account's key and the hero name it had (Friends.cs). Older account files load with none.
        public List<Pal> Friends = new(), FriendIn = new(), Blocked = new();
    }
    public class Pal { public string Key = "", Name = ""; }
    public class Token { public string Hash = ""; public long Expires; }
    public const int TokenDays = 30, MaxTokens = 5;
    public record Result(bool Ok, string Message, string Key = "");

    static readonly object Gate = new();
    static readonly Dictionary<string, (int fails, DateTime until)> Fails = new();
    static readonly Regex EmailRx = new(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled);
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };
    static string _dir = "";

    /// <summary>The data folder. Call once on the main thread before using the rest.</summary>
    public static string Dir
    {
        get
        {
            if (_dir != "") return _dir;
            string env = System.Environment.GetEnvironmentVariable("TR_DATA") ?? "";
            _dir = env != "" ? env : ProjectSettings.GlobalizePath("user://server");
            Directory.CreateDirectory(Path.Combine(_dir, "accounts")); Directory.CreateDirectory(Path.Combine(_dir, "saves"));
            return _dir;
        }
        set => _dir = value;   // the self-test points this at a scratch folder
    }

    public static string Normalize(string email) => (email ?? "").Trim().ToLowerInvariant();
    /// <summary>File name for an account: a hash of the email, so any address is a safe file name.</summary>
    public static string KeyOf(string email) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(email)))).ToLowerInvariant()[..32];
    static string AccPath(string key) => Path.Combine(Dir, "accounts", key + ".json");
    static string SavePath(string key) => Path.Combine(Dir, "saves", key + ".json");

    public static string? CheckNew(string email, string password)
    {
        email = Normalize(email);
        if (email.Length > 254 || !EmailRx.IsMatch(email)) return "That does not look like an email address.";
        if ((password ?? "").Length < OnlineCfg.MinPassword) return $"The password needs at least {OnlineCfg.MinPassword} characters.";
        if (password!.Length > 200) return "That password is too long.";
        return null;
    }

    public static Result Register(string email, string password)
    {
        string? bad = CheckNew(email, password); if (bad != null) return new(false, bad);
        email = Normalize(email); string key = KeyOf(email);
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16); int iter = OnlineCfg.Iterations;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, 32);
        var acc = new Account { Email = email, Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash), Iter = iter, Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        lock (Gate)
        {
            if (File.Exists(AccPath(key))) return new(false, "An account with that email already exists. Log in instead.");
            WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt));
        }
        GD.Print($"[ACCOUNTS] registered {Mask(email)}");
        return new(true, "Account created.", key);
    }

    public static Result Login(string email, string password)
    {
        email = Normalize(email); string key = KeyOf(email);
        lock (Gate)
        {
            if (Fails.TryGetValue(key, out var f) && f.until > DateTime.UtcNow)
                return new(false, $"Too many wrong passwords. Try again in {(int)Math.Ceiling((f.until - DateTime.UtcNow).TotalSeconds)} seconds.");
        }
        Account? acc = null;
        lock (Gate) { try { if (File.Exists(AccPath(key))) acc = JsonSerializer.Deserialize<Account>(File.ReadAllText(AccPath(key)), Opt); } catch (Exception e) { GD.PushError("account unreadable: " + e.Message); } }
        bool ok = false;
        if (acc != null)
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(password ?? "", Convert.FromBase64String(acc.Salt), acc.Iter, HashAlgorithmName.SHA256, 32);
            ok = CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(acc.Hash));
        }
        else Rfc2898DeriveBytes.Pbkdf2(password ?? "", new byte[16], OnlineCfg.Iterations, HashAlgorithmName.SHA256, 32);   // same time whether or not the email exists
        lock (Gate)
        {
            if (!ok)
            {
                var f = Fails.GetValueOrDefault(key); f.fails++;
                if (f.fails >= OnlineCfg.FailedBeforeLock) { f.until = DateTime.UtcNow.AddSeconds(OnlineCfg.LockSeconds); f.fails = 0; }
                Fails[key] = f;
                return new(false, "Wrong email or password.");
            }
            Fails.Remove(key);
            acc = ReadAcc(key) ?? acc!;   // again, under the lock: a flag set while the password was being checked (ban, admin) must not be written over
            if (BanText(acc) is { } banned) return new(false, banned);
            acc.LastLogin = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt));
        }
        GD.Print($"[ACCOUNTS] login {Mask(email)}");
        return new(true, "Logged in.", key);
    }

    static string TokHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""))).ToLowerInvariant();
    // ---- admin, ban, mute (F-42 / F-43)
    /// <summary>The refusal shown at login while the account is banned, or null.</summary>
    public static string? BanText(Account a)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); if (a.BanUntil <= now) return null;
        string until = a.BanUntil == long.MaxValue ? "" : $" until {DateTimeOffset.FromUnixTimeSeconds(a.BanUntil):yyyy-MM-dd HH:mm} UTC";
        return $"This account is banned{until}." + (a.BanReason != "" ? " Reason: " + a.BanReason : "");
    }
    /// <summary>The account file as it is now (null if there is none).</summary>
    public static Account? Info(string key) { if (string.IsNullOrEmpty(key)) return null; lock (Gate) return ReadAcc(key); }
    /// <summary>Reads the account, lets `change` edit it and writes it back, all under the file lock. False if there is no such account.</summary>
    public static bool Update(string key, Action<Account> change)
    {
        if (string.IsNullOrEmpty(key)) return false;
        lock (Gate)
        {
            var acc = ReadAcc(key); if (acc == null) return false;
            try { change(acc); WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt)); return true; }
            catch (Exception e) { GD.PushError("account not updated: " + e.Message); return false; }
        }
    }
    /// <summary>Accounts whose last hero had this name (for commands about players who are offline). Reads every account file: admin commands only.</summary>
    public static List<(string key, Account acc)> FindByHero(string hero)
    {
        var l = new List<(string, Account)>();
        lock (Gate)
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(Dir, "accounts"), "*.json"))
                {
                    string key = Path.GetFileNameWithoutExtension(f); var a = ReadAcc(key);
                    if (a != null && (hero.StartsWith('@') ? hero.Length >= 5 && key.StartsWith(hero[1..], StringComparison.OrdinalIgnoreCase) : (a.Hero ?? "").Equals(hero, StringComparison.OrdinalIgnoreCase))) l.Add((key, a));
                }
            }
            catch (Exception e) { GD.PushError("accounts not searched: " + e.Message); }
        }
        return l;
    }

    static Account? ReadAcc(string key) { try { return File.Exists(AccPath(key)) ? JsonSerializer.Deserialize<Account>(File.ReadAllText(AccPath(key)), Opt) : null; } catch (Exception e) { GD.PushError("account unreadable: " + e.Message); return null; } }

    /// <summary>A new login token for the account (valid TokenDays; the oldest is dropped beyond MaxTokens). Lets the launcher log in again without the password.</summary>
    public static string IssueToken(string key)
    {
        string token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (Gate)
        {
            var acc = ReadAcc(key); if (acc == null) return "";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            acc.Tokens ??= new(); acc.Tokens.RemoveAll(t => t.Expires < now);
            acc.Tokens.Add(new Token { Hash = TokHash(token), Expires = now + TokenDays * 86400L });
            while (acc.Tokens.Count > MaxTokens) acc.Tokens.RemoveAt(0);
            WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt));
        }
        return token;
    }

    /// <summary>Login with a saved token instead of the password.</summary>
    public static Result LoginToken(string email, string token)
    {
        email = Normalize(email); string key = KeyOf(email);
        lock (Gate)
        {
            var acc = ReadAcc(key); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); string h = TokHash(token);
            if (acc == null || string.IsNullOrEmpty(token) || acc.Tokens == null || !acc.Tokens.Exists(t => t.Expires >= now && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(t.Hash), Encoding.UTF8.GetBytes(h))))
                return new(false, "Your saved login has expired. Log in with your password.");
            if (BanText(acc) is { } banned) return new(false, banned);
            acc.LastLogin = now; WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt));
        }
        GD.Print($"[ACCOUNTS] login (saved) {Mask(email)}");
        return new(true, "Logged in.", key);
    }

    /// <summary>Forget one saved login (launcher "Log out").</summary>
    public static void DropToken(string key, string token)
    {
        lock (Gate)
        {
            var acc = ReadAcc(key); if (acc?.Tokens == null) return;
            string h = TokHash(token); if (acc.Tokens.RemoveAll(t => t.Hash == h) > 0) WriteAtomic(AccPath(key), JsonSerializer.Serialize(acc, Opt));
        }
    }

    /// <summary>The account's save file (three hero slots, the same format as the local save), or "" for a new account.</summary>
    public static string LoadSave(string key) { lock (Gate) { try { return File.Exists(SavePath(key)) ? File.ReadAllText(SavePath(key)) : ""; } catch { return ""; } } }

    /// <summary>Stores an uploaded save after checking that it parses and is not too big. Keeps the previous copy as .bak.</summary>
    public static string? StoreSave(string key, string json)
    {
        if (json.Length > OnlineCfg.MaxSaveKb * 1024) return "save too large";
        try
        {
            var f = JsonSerializer.Deserialize<Game.SaveFile>(json, Opt);
            if (f == null || f.Slots == null || f.Slots.Length != Game.MaxSlots) return "save has the wrong shape";
        }
        catch (Exception e) { return "save unreadable: " + e.Message; }
        json = AuctionServer.Reconcile(key, json);   // F-37/F-38: auction and trade changes the server made that this upload does not contain yet (NetAuction.cs)
        lock (Gate)
        {
            string p = SavePath(key); if (File.Exists(p)) File.Copy(p, p + ".bak", true);
            WriteAtomic(p, json);
        }
        return null;
    }

    static void WriteAtomic(string path, string text) { string tmp = path + ".tmp"; File.WriteAllText(tmp, text); File.Move(tmp, path, true); }
    /// <summary>Logs show "pl***@example.com", never the full address.</summary>
    public static string Mask(string email) { int at = email.IndexOf('@'); return at <= 2 ? "***" + email[Math.Max(0, at)..] : email[..2] + "***" + email[at..]; }
}
