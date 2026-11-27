using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dev command "admintest" (focus F-43 / F-44): drives the SERVER-side functions in this process, with two throw-away accounts in a
/// scratch folder (user://admintest), and prints PASS / FAIL per step: the admin login hash, the rate limit and its log, the admin /
/// ban flags on account files, the command permission check, the no-mining rule, the block log (append, rotate, look up) and rollback.
/// The right password is only tested when the environment variable TR_ADMIN_TEST_PW holds it (it is never written anywhere).
/// It does not test the network path: that needs two connected clients (see the F-44 note).
/// </summary>
public static class AdminTest
{
    public static string Run(string[] _)
    {
        var sb = new StringBuilder(); int fails = 0;
        void Check(string what, bool ok, string detail = "") { if (!ok) fails++; sb.Append(ok ? "PASS  " : "FAIL  ").Append(what).Append(detail != "" ? "  [" + detail + "]" : "").Append('\n'); }

        string scratch = ProjectSettings.GlobalizePath("user://admintest");
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch { }
        Directory.CreateDirectory(Path.Combine(scratch, "accounts")); Directory.CreateDirectory(Path.Combine(scratch, "saves"));
        string oldDir = Accounts.Dir; var oldGuardClock = AdminGuard.Clock; var oldAuditClock = BlockAudit.Clock;
        int oldBytes = BlockAudit.MaxBytes, oldKeep = BlockAudit.KeepFiles, oldMem = BlockAudit.MaxMem;
        var cells = new List<(int x, int y, int z)>();
        try
        {
            Accounts.Dir = scratch; OnlineCfg.Load();

            // ---- 1. the login hash
            string pw = System.Environment.GetEnvironmentVariable("TR_ADMIN_TEST_PW") ?? "";
            Check("login: a wrong line is refused", !Admin.PasswordOk("/not-the-admin-login") && !Admin.PasswordOk("") && !Admin.PasswordOk(new string('x', 500)));
            if (pw != "") Check("login: the right line is accepted (from TR_ADMIN_TEST_PW)", Admin.PasswordOk(pw));
            else sb.Append("SKIP  login: the right line (set the environment variable TR_ADMIN_TEST_PW to test it)\n");

            // ---- 2. account flags
            var ra = Accounts.Register("admintest_a@example.com", "admintest-pass-1"); var rb = Accounts.Register("admintest_b@example.com", "admintest-pass-2");
            Check("accounts: two test accounts created", ra.Ok && rb.Ok, ra.Message + " / " + rb.Message);
            string A = ra.Key, B = rb.Key;
            Check("accounts: nobody is an admin to begin with", Accounts.Info(A) is { Admin: false } && Accounts.Info(B) is { Admin: false });
            Check("accounts: the admin flag is stored on the account file", Accounts.Update(A, a => a.Admin = true) && Accounts.Info(A) is { Admin: true } && Accounts.Info(B) is { Admin: false }
                && File.ReadAllText(Path.Combine(scratch, "accounts", A + ".json")).Contains("\"Admin\":true"));
            Check("accounts: the flag survives a login", Accounts.Login("admintest_a@example.com", "admintest-pass-1").Ok && Accounts.Info(A) is { Admin: true });
            Accounts.Update(B, a => { a.BanUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; a.BanReason = "test"; a.Hero = "Bob Test"; });
            var banned = Accounts.Login("admintest_b@example.com", "admintest-pass-2");
            Check("accounts: a banned account cannot log in and is told why", !banned.Ok && banned.Message.Contains("banned") && banned.Message.Contains("test"), banned.Message);
            Check("accounts: found by hero name and by @account", Accounts.FindByHero("bob test").Count == 1 && Accounts.FindByHero("@" + B[..8]).Count == 1 && Accounts.FindByHero("nobody").Count == 0);
            Accounts.Update(B, a => a.BanUntil = 0);
            Check("accounts: unbanned, the login works again", Accounts.Login("admintest_b@example.com", "admintest-pass-2").Ok);
            Check("accounts: /deadmin's update clears the flag", Accounts.Update(A, a => a.Admin = false) && Accounts.Info(A) is { Admin: false });
            Accounts.Update(A, a => a.Admin = true);

            // ---- 3. rate limit and log
            long now = 1_000_000; AdminGuard.Clock = () => now; AdminGuard.Reset(); AdminGuard.LogDir = scratch;
            int max = AdminGuard.MaxFails; bool lockedEarly = false, lockedAtMax = false;
            for (int i = 1; i <= max; i++) { bool l = AdminGuard.Fail(B, "10.0.0.1"); if (i < max && l) lockedEarly = true; if (i == max) lockedAtMax = l; AdminGuard.Log("FAILED (admintest)", B, "admintest", "10.0.0.1"); }
            Check($"rate limit: {max} wrong attempts lock the account out", !lockedEarly && lockedAtMax && AdminGuard.Blocked(B, "10.0.0.9") != null, AdminGuard.Blocked(B, "10.0.0.9") ?? "");
            Check("rate limit: another account is not locked by it", AdminGuard.Blocked(A, "10.0.0.2") == null);
            now += AdminGuard.LockSec + 1;
            Check("rate limit: the lock ends after the wait", AdminGuard.Blocked(B, "10.0.0.9") == null);
            for (int i = 0; i < max; i++) AdminGuard.Fail(B, "10.0.0.1");
            now += AdminGuard.LockSec + 1;
            Check("rate limit: the second lockout is longer", AdminGuard.Blocked(B, "10.0.0.9") != null);
            now += AdminGuard.LockSec + 1;
            Check("rate limit: and ends too", AdminGuard.Blocked(B, "10.0.0.9") == null);
            AdminGuard.Reset(); bool addrLocked = false;
            for (int i = 0; i < AdminGuard.AddrMaxFails; i++) addrLocked = AdminGuard.Fail("acct" + i, "10.0.0.7");
            Check($"rate limit: {AdminGuard.AddrMaxFails} wrong attempts from one address lock that address", addrLocked && AdminGuard.Blocked(A, "10.0.0.7") != null && AdminGuard.Blocked(A, "10.0.0.8") == null);
            AdminGuard.Reset(); AdminGuard.Fail(A, "10.0.0.3"); AdminGuard.Ok(A); AdminGuard.Unfail("10.0.0.3");
            Check("rate limit: a right password clears the count", AdminGuard.Blocked(A, "10.0.0.3") == null);
            string logPath = Path.Combine(scratch, "admin_log.txt"); string logText = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            Check("log: failed attempts are written with time, account and address", logText.Split('\n').Count(l => l.Contains("FAILED") && l.Contains(B) && l.Contains("10.0.0.1")) == max);
            if (pw != "") Check("log: the login line is not in it", !logText.Contains(pw));

            // ---- 4. command permissions as the server applies them
            var guest = new CmdCtx { Peer = 2002, Account = B, Name = "Bob Test", IsAdmin = false, Remote = true, Pos = new Vector3(0, 5, 0) };
            var adm = new CmdCtx { Peer = 2001, Account = A, Name = "Alice Test", IsAdmin = true, Remote = true, Pos = new Vector3(0, 5, 0) };
            Check("commands: a non-admin is refused", new[] { "/heal", "/god on", "/time 12", "/give gold 5", "/rollback x", "/blocklog", "/deadmin x", "/kick x", "/ban x", "stats dump" }.All(c => Commands.Exec(c, guest) == Commands.NotAdmin));
            Check("commands: /help is for everyone and lists only what they may use", Commands.Exec("/help", guest) is var h && h.Contains("/help") && !h.Contains("/rollback") && Commands.Exec("/help", adm).Contains("/rollback"));
            Check("commands: an admin's request runs", Commands.Exec("/time", adm).StartsWith("it is"), Commands.Exec("/time", adm));
            Check("commands: debug commands never run for another machine", Commands.Exec("/admintest", adm) == Commands.NotAdmin && Commands.Exec("/debug", adm).Contains("your own game"));
            Check("commands: clear errors", Commands.Exec("/tp", adm).StartsWith("usage: /tp") && Commands.Exec("/spawn wolff", adm).Contains("did you mean wolf") && Commands.Exec("/hea", adm).Contains("did you mean /heal"),
                Commands.Exec("/spawn wolff", adm));

            // ---- 5. no mining, block log, rollback
            string blockDir = Path.Combine(scratch, "blocklog"); long t = 2_000_000;
            BlockAudit.Reset(); BlockAudit.Dir = blockDir; BlockAudit.MaxBytes = 4096; BlockAudit.KeepFiles = 3; BlockAudit.MaxMem = 400; BlockAudit.Clock = () => t;
            byte stone = Blocks.Id("stone"), planks = Blocks.Id("planks"); const int X = 900000, Y = 10, Z = 900000;
            void Cell(int x, int y, int z) => cells.Add((x, y, z));
            Cell(X, Y, Z);
            Check("blocks: a non-admin cannot remove a block of the world", BlockAudit.Judge(B, false, X, Y, Z, 0) == OnlineRules.NoMiningMsg);
            Check("blocks: an admin can", BlockAudit.Judge(A, true, X, Y, Z, 0) == null);
            Check("blocks: placing is allowed", BlockAudit.Judge(B, false, X, Y, Z, stone) == null);
            BlockAudit.Accept(B, "Bob Test", X, Y, Z, stone, 0); BlockAudit.Flush(); t += 10;
            Check("blocks: another player cannot remove or replace it", BlockAudit.Judge("someone-else", false, X, Y, Z, 0) == OnlineRules.NotYoursMsg && BlockAudit.Judge("someone-else", false, X, Y, Z, planks) == OnlineRules.NotYoursMsg);
            Check("blocks: the player who placed it can remove it", BlockAudit.Judge(B, false, X, Y, Z, 0) == null);
            BlockAudit.Accept(B, "Bob Test", X, Y, Z, 0, 0); BlockAudit.Flush(); t += 10;
            Check("blocks: once removed, the spot cannot be dug further", BlockAudit.Judge(B, false, X, Y, Z, 0) == OnlineRules.NoMiningMsg);
            string q = BlockAudit.QueryCell(X, Y, Z, 1);
            Check("block log: who, what, where, when for the aimed block", q.Contains("2 entries") && q.Contains("Bob Test") && q.Contains("placed stone") && q.Contains("removed stone") && q.Contains($"{X} {Y} {Z}"), q.Replace("\n", " | "));
            // rollback: Bob places c1 and c2, removes c2 again; an admin then replaces c1
            Cell(X + 1, Y, Z); Cell(X + 2, Y, Z);
            BlockAudit.Accept(B, "Bob Test", X + 1, Y, Z, stone, 0); t += 5; BlockAudit.Accept(B, "Bob Test", X + 2, Y, Z, stone, 0); t += 5;
            BlockAudit.Accept(B, "Bob Test", X + 2, Y, Z, 0, 0); t += 5; BlockAudit.Accept(A, "Alice Test", X + 1, Y, Z, planks, 0); t += 5; BlockAudit.Flush();
            Check("block log: by player and by radius", BlockAudit.QueryPlayer("bob test", 1).Contains("5 entries") && BlockAudit.QueryPlayer(B, 1).Contains("5 entries") && BlockAudit.QueryRadius(new Vector3(X, Y, Z), 3, 1).Contains("6 entries") && BlockAudit.QueryPlayer("nobody", 1).Contains("nothing recorded"),
                BlockAudit.QueryPlayer("bob test", 1).Split('\n')[0] + " / " + BlockAudit.QueryRadius(new Vector3(X, Y, Z), 3, 1).Split('\n')[0]);
            var set = new List<(int x, int y, int z, byte id, byte meta)>(); var regen = new List<(int x, int y, int z, byte id, byte meta)>();
            var (undone, skipped) = BlockAudit.Rollback(B, 20, A, "Alice Test", set, regen);   // the last 20 s: only the c2 pair and c1
            Check("rollback: undoes the player's changes in the window, newest first, and leaves what someone changed since",
                undone == 2 && skipped == 1 && regen.Count == 1 && regen[0].x == X + 2 && set.Count == 0 && BlockWorld.ServerCell(BlockAudit.Area, X + 2, Y, Z) == null && BlockWorld.ServerCell(BlockAudit.Area, X + 1, Y, Z) is { } c1 && c1.id == planks,
                $"undone {undone}, skipped {skipped}, set {set.Count}, regen {regen.Count}");
            Check("rollback: is itself in the log", BlockAudit.QueryCell(X + 2, Y, Z, 1).Contains("rolled back") && BlockAudit.QueryCell(X + 2, Y, Z, 1).Contains("Alice Test"));
            set.Clear(); regen.Clear(); var again = BlockAudit.Rollback(B, 20, A, "Alice Test", set, regen);
            Check("rollback: a second run changes nothing", again.undone == 0 && set.Count + regen.Count == 0, $"undone {again.undone}, skipped {again.skipped}");
            set.Clear(); regen.Clear(); var wide = BlockAudit.Rollback(B, 3600, A, "Alice Test", set, regen);
            Check("rollback: a wider window also restores the block Bob took out earlier (his own)", wide.undone == 2 && regen.Count == 1 && regen[0].x == X, $"undone {wide.undone}, skipped {wide.skipped}, set {set.Count}, regen {regen.Count}");
            // rotation and the memory cap
            for (int i = 0; i < 600; i++) { Cell(X + i, Y + 2, Z + 5); BlockAudit.Accept(B, "Bob Test", X + i, Y + 2, Z + 5, stone, 0); if (i % 20 == 19) BlockAudit.Flush(); t++; }
            BlockAudit.Flush(); var files = BlockAudit.Files();
            Check("block log: files rotate and old ones are dropped", files.Count == BlockAudit.KeepFiles && files.All(f => f.bytes <= BlockAudit.MaxBytes + 2048) && Directory.GetFiles(blockDir).Length == BlockAudit.KeepFiles,
                string.Join(", ", files.Select(f => $"{f.file} {f.bytes} B")));
            Check("block log: memory is capped", BlockAudit.Count <= BlockAudit.MaxMem + BlockAudit.MaxMem / 10, BlockAudit.Count + " entries");
            int before = BlockAudit.Count; BlockAudit.Reset(); BlockAudit.Dir = blockDir; BlockAudit.MaxBytes = 4096; BlockAudit.KeepFiles = 3; BlockAudit.MaxMem = 400; BlockAudit.EnsureLoaded();
            Check("block log: read back from the files after a restart", BlockAudit.Count > 0 && BlockAudit.QueryPlayer("Bob Test", 1).Contains("placed stone"), $"{BlockAudit.Count} entries (had {before})");

            // ---- 6. block drops
            BlockDrops.Load();
            Check("block drops: data/blockdrops.json loads with no unknown blocks", BlockDrops.Problems.Count == 0, string.Join("; ", BlockDrops.Problems));
            var seq = new[] { .1f, .2f, .5f, .7f, .3f, .9f, .4f, .6f }; int si = 0; float Rnd() => seq[si++ % seq.Length];
            var d1 = BlockDrops.Pick("wolf", "quad", false, 0, "forest", Rnd); var d2 = BlockDrops.Pick("golem", "hum", true, 1, "tundra", Rnd); var d3 = BlockDrops.Pick("wolf", "quad", false, 0, "forest", () => .99f);
            Check("block drops: by biome and monster, bosses always, sometimes nothing", d1.Count > 0 && d2.Values.Sum() > d1.Values.Sum() && d3.Count == 0 && d1.Keys.Concat(d2.Keys).All(Blocks.Exists),
                "wolf in forest: " + string.Join(", ", d1.Select(kv => kv.Value + " " + kv.Key)) + "; boss golem in shadow tundra: " + string.Join(", ", d2.Select(kv => kv.Value + " " + kv.Key)));
        }
        catch (Exception e) { fails++; sb.Append("FAIL  the test itself threw: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n'); }
        finally
        {
            foreach (var (x, y, z) in cells) BlockWorld.ServerForget(BlockAudit.Area, x, y, z);
            BlockAudit.Reset(); BlockAudit.Dir = ""; BlockAudit.MaxBytes = oldBytes; BlockAudit.KeepFiles = oldKeep; BlockAudit.MaxMem = oldMem; BlockAudit.Clock = oldAuditClock;
            AdminGuard.Reset(); AdminGuard.Clock = oldGuardClock; AdminGuard.LogDir = "";
            Accounts.Dir = oldDir;
        }
        sb.Append(fails == 0 ? "ADMINTEST: ALL PASS" : $"ADMINTEST: {fails} FAILED");
        return sb.ToString();
    }
}
