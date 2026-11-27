using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dev commands for A-29 to A-32: "chattest", "friendtest", "tradetest", "partytest" (registered in Chat.Register). Each runs the
/// SERVER-side functions on this machine with fake senders / fake accounts in a scratch folder and prints PASS / FAIL per step.
/// They are not a test with two real clients: nothing goes over the network here.
/// </summary>
public static class SocialTests
{
    static string _oldDir = "", _dir = "";
    static string? NotOffline(string name) => Net.Dedicated || Game.Online ? $"{name}: run it in an offline game (it switches the account folder to a scratch one)" : null;
    static void Scratch()
    {
        _oldDir = Accounts.Dir; _dir = Path.Combine(ProjectSettings.GlobalizePath("user://"), "social_selftest");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(Path.Combine(_dir, "accounts")); Directory.CreateDirectory(Path.Combine(_dir, "saves"));
        Accounts.Dir = _dir; AuctionServer.Forget(); OnlineCfg.Load(); SocialLog.Dir = _dir;
    }
    static void Unscratch()
    {
        Accounts.Dir = _oldDir; AuctionServer.Forget(); SocialLog.Dir = "";
        try { if (_dir != "" && Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }
    static string? Make(string key, PlayerStats d) { var f = new Game.SaveFile(); f.Slots[0] = d; f.Last = 0; return Accounts.StoreSave(key, Auction.ToJson(f)); }
    static PlayerStats Hero(string key) => Auction.FromJson<Game.SaveFile>(Accounts.LoadSave(key))!.Slots[0]!;
    static string End(string name, List<string> log, int fails, string what)
    {
        log.Add(fails == 0 ? $"{name}: ALL {log.Count} STEPS PASS ({what}; not a test with two real clients)" : $"{name}: {fails} FAILED");
        return string.Join("\n", log);
    }

    // ------------------------------------------------------------------ chattest
    public static string Chat()
    {
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        string oldLog = SocialLog.Dir; string dir = Path.Combine(ProjectSettings.GlobalizePath("user://"), "chat_selftest");
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir); SocialLog.Dir = dir;
            string id = "test:" + Guid.NewGuid().ToString("N"); double t = 1000; int max = ChatServer.MaxLen;
            Check("clean: control characters and line breaks out, spaces collapsed, trimmed", ChatServer.Clean("  hi\r\n\tthere \u0007 you  ") == "hi there you", ChatServer.Clean("  hi\r\n\tthere \u0007 you  "));
            Check($"clean: cut at the length cap ({max})", ChatServer.Clean(new string('x', max + 50)).Length == max);
            Check("an empty line is dropped without a message", ChatServer.Check(id, " \n ", 0, t) is (false, _, ""));
            var r = ChatServer.Check(id, "hello  world", 0, t);
            Check("a normal line passes, cleaned", r.ok && r.text == "hello world", r.text);
            r = ChatServer.Check(id, "too soon", 0, t + .01);
            Check("a second line right behind it is refused (minimum gap)", !r.ok && r.msg != "", r.msg);
            t += 5; r = ChatServer.Check(id, "hello world", 0, t); var r2 = ChatServer.Check(id, "hello world", 0, t + 1);
            Check("the same line twice in a row is refused", r.ok && !r2.ok, r2.msg);
            // rate limit: fill the window from a fresh sender
            string id2 = id + "b"; int win = Math.Max(1, SocialCfg.I("chat.rate.maxInWindow", 6)); double step = SocialCfg.F("chat.rate.minGapSec", .4f) + .05, t2 = 5000; int passed = 0;
            for (int i = 0; i < win + 3; i++) { if (ChatServer.Check(id2, "line " + i, 0, t2).ok) passed++; t2 += step; }
            Check($"rate limit: only {win} of {win + 3} quick lines pass", passed == win || step * (win + 3) > SocialCfg.F("chat.rate.windowSec", 8), $"passed {passed}");
            Check("after the window the sender may speak again", ChatServer.Check(id2, "later", 0, t2 + SocialCfg.F("chat.rate.windowSec", 8) + 1).ok);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            r = ChatServer.Check(id + "m", "let me speak", now + 600, 9000);
            Check("a muted account is refused and told until when", !r.ok && r.msg.Contains("muted"), r.msg);
            Check("a mute for good is refused", !ChatServer.Check(id + "m", "let me speak", long.MaxValue, 9001).ok);
            Check("a mute that has ended lets the line through", ChatServer.Check(id + "m", "free again", now - 5, 9002).ok);
            var words = SocialCfg.List("chat.profanity.words");
            if (words.Count > 0)
            {
                string bad = "you " + words[0].ToUpperInvariant() + " you", masked = ChatServer.Mask(bad);
                Check("profanity mask: a listed word is masked whatever its case, the rest stays", masked.Length == bad.Length && !masked.Contains(words[0], StringComparison.OrdinalIgnoreCase) && masked.StartsWith("you ") && masked.EndsWith(" you"), masked);
                bool on = SocialCfg.B("chat.profanity.enabled", false); r = ChatServer.Check(id + "p", bad, 0, 9100);
                Check(on ? "the server filter is ON in data: the line arrives masked" : "the server filter is OFF by default: the line arrives as typed", r.ok && (on ? r.text == masked : r.text == bad), r.text);
            }
            Check("names: brackets and control characters out, at most 24 characters", ChatServer.CleanName("[GM]\nBob" + new string('x', 40)).Length == 24 && !ChatServer.CleanName("[GM] Bob").Contains('['));
            // the moderation log and its rotation (tiny limit so it rotates here)
            for (int i = 0; i < 60; i++) SocialLog.Write("chat", "WORLD\tTester [abcdef12]\t\t" + new string('y', 80), 1, 3);
            string p = SocialLog.PathOf("chat"); int files = Directory.GetFiles(Path.GetDirectoryName(p)!).Length;
            Check("log: written, rotated by size, at most 3 files kept", File.Exists(p) && File.Exists(p + ".1") && files <= 3 && new FileInfo(p).Length < 4096, $"{files} files");
            ChatServer.Log(ChatServer.Whisper, "Ann", "0123456789abcdef", "Bob", "psst");
            Check("log line: channel, speaker with the account's first 8 characters, the receiver, the text", File.ReadAllText(p).Contains("WHISPER\tAnn [01234567]\t-> Bob\tpsst") || !SocialCfg.B("chat.logWhispers", true));
            ChatServer.Forget(id); ChatServer.Forget(id2); ChatServer.Forget(id + "m"); ChatServer.Forget(id + "p");
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally { SocialLog.Dir = oldLog; try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }
        return End("chattest", log, fails, "the server's chat rules on fake senders");
    }

    // ------------------------------------------------------------------ friendtest
    public static string Friends()
    {
        if (NotOffline("friendtest") is { } no) return no;
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        try
        {
            Scratch();
            var ra = Accounts.Register("ann@example.com", "password-ann-1"); var rb = Accounts.Register("bob@example.com", "password-bob-1"); var rc = Accounts.Register("cyd@example.com", "password-cyd-1");
            string A = ra.Key, B = rb.Key, C = rc.Key;
            Check("three fake accounts registered", ra.Ok && rb.Ok && rc.Ok);
            Accounts.Account Acc(string k) { var a = Accounts.Info(k)!; FriendsServer.Fix(a); return a; }
            Check("a new account has empty lists", Acc(A).Friends.Count == 0 && Acc(A).FriendIn.Count == 0 && Acc(A).Blocked.Count == 0);
            // an account file from before A-32 (no list fields at all) still loads
            string oldFile = Path.Combine(_dir, "accounts", "0000old.json"); File.WriteAllText(oldFile, "{\"Email\":\"old@example.com\",\"Salt\":\"\",\"Hash\":\"\",\"Iter\":1,\"Admin\":false,\"MuteUntil\":0}");
            var old = Accounts.Info("0000old");
            Check("an account file from before friends existed loads with empty lists", old != null && old.Friends != null && old.Friends.Count == 0 && old.Blocked != null);
            Check("you cannot befriend yourself", !FriendsServer.Request(A, "Ann", A, "Ann").ok);
            var r = FriendsServer.Request(A, "Ann", B, "Bob");
            Check("request: stored on the other account, nobody is a friend yet", r.ok && !r.mutual && Acc(B).FriendIn.Exists(p => p.Key == A && p.Name == "Ann") && Acc(A).Friends.Count == 0 && Acc(B).Friends.Count == 0, r.msg);
            FriendsServer.Request(A, "Ann", B, "Bob");
            Check("the same request again is not stored twice", Acc(B).FriendIn.Count == 1);
            var an = FriendsServer.Answer(B, "Bob", A, true);
            Check("accept: both are friends, the request is gone", an.ok && Acc(A).Friends.Exists(p => p.Key == B && p.Name == "Bob") && Acc(B).Friends.Exists(p => p.Key == A) && Acc(B).FriendIn.Count == 0, an.msg);
            Check("asking a friend again is refused", !FriendsServer.Request(A, "Ann", B, "Bob").ok);
            var rm = FriendsServer.Remove(A, B, "Bob");
            Check("remove: gone from both lists", rm.ok && Acc(A).Friends.Count == 0 && Acc(B).Friends.Count == 0);
            FriendsServer.Request(A, "Ann", C, "Cyd"); an = FriendsServer.Answer(C, "Cyd", A, false);
            Check("decline: the request is gone, nobody is a friend", an.ok && Acc(C).FriendIn.Count == 0 && Acc(A).Friends.Count == 0 && Acc(C).Friends.Count == 0);
            Check("answering a request that does not exist is refused", !FriendsServer.Answer(C, "Cyd", B, true).ok);
            FriendsServer.Request(A, "Ann", B, "Bob"); r = FriendsServer.Request(B, "Bob", A, "Ann");
            Check("two players asking each other become friends at once", r.ok && r.mutual && Acc(A).Friends.Exists(p => p.Key == B) && Acc(B).Friends.Exists(p => p.Key == A) && Acc(A).FriendIn.Count == 0 && Acc(B).FriendIn.Count == 0, r.msg);
            var bl = FriendsServer.Block(B, A, "Ann");
            Check("block: stored, and the friendship ends on both sides", bl.ok && Acc(B).Blocked.Exists(p => p.Key == A) && Acc(B).Friends.Count == 0 && Acc(A).Friends.Count == 0, bl.msg);
            r = FriendsServer.Request(A, "Ann", B, "Bob");
            Check("a blocked player's request looks sent but is not stored", r.ok && Acc(B).FriendIn.Count == 0, r.msg);
            Check("you cannot ask someone you block", !FriendsServer.Request(B, "Bob", A, "Ann").ok);
            var ub = FriendsServer.Unblock(B, A, "Ann");
            Check("unblock: off the list; requests work again", ub.ok && Acc(B).Blocked.Count == 0 && FriendsServer.Request(A, "Ann", B, "Bob").ok && Acc(B).FriendIn.Count == 1);
            Accounts.Update(C, a => a.Hero = "Cyd");
            Check("an offline player is found by the last hero's name", Accounts.FindByHero("cyd").Exists(x => x.key == C));
            Check("passwords and bans are untouched by the list changes (login still works)", Accounts.Login("ann@example.com", "password-ann-1").Ok);
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally { Unscratch(); }
        return End("friendtest", log, fails, "the friends list on fake accounts");
    }

    // ------------------------------------------------------------------ tradetest
    public static string Trade()
    {
        if (NotOffline("tradetest") is { } no) return no;
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        try
        {
            Scratch();
            const string A = "ta", B = "tb";
            Item It(string name, string slot) { var it = Items.Roll(6, 0, slot); it.Name = name; return it; }
            var da = new PlayerStats { Name = "Ansel", Gold = 500, HpPot = 5 }; da.Inv.Add(It("Test Sword", "weapon")); da.Inv.Add(It("Test Helm", "head")); da.Mats["iron_ore"] = 10;
            da.Pets.Add(PetDb.New("wolf", 7, "iron")); da.Pets.Add(PetDb.New("boar", 4, "iron")); da.ActivePet = da.Pets[1].Id; da.Pets[0].Ot = "Ansel";
            var db = new PlayerStats { Name = "Brida", Gold = 100 }; db.Inv.Add(It("Test Bow", "weapon")); db.Mats["blk_stone"] = 20;
            Check("two fake accounts stored", Make(A, da) == null && Make(B, db) == null);
            var a0 = Hero(A); var b0 = Hero(B); string wolf = a0.Pets[0].Id;
            int Total(Func<PlayerStats, int> f) => f(Hero(A)) + f(Hero(B));

            // ---- the offer as the server sees it
            var ghost = It("Ghost Blade", "weapon");
            var askA = new TradeOffer { Gold = 99999 }; askA.Items.Add(a0.Inv[0]); askA.Items.Add(ghost); askA.Stacks["mat:iron_ore"] = 50; askA.Stacks["pot:hp"] = 2; askA.Stacks["mat:nothing_here"] = 3;
            askA.Pets.Add(a0.Pets[0]); askA.Pets.Add(a0.Pets[1]);
            var va = AuctionServer.TradeView(A, askA)!;
            Check("offer: an item that is not in the server's save is left out", va.Items.Count == 1 && va.Items[0].Name == "Test Sword");
            Check("offer: stacks are capped to what the save holds, unknown ones dropped", va.Stacks.GetValueOrDefault("mat:iron_ore") == 10 && va.Stacks.GetValueOrDefault("pot:hp") == 2 && !va.Stacks.ContainsKey("mat:nothing_here"), string.Join(",", va.Stacks.Select(kv => kv.Key + "=" + kv.Value)));
            Check("offer: gold capped to the purse; the companion that is out is not tradable", va.Gold == 500 && va.Pets.Count == 1 && va.Pets[0].Id == wolf);
            Check("offer: the same item cannot be offered twice", AuctionServer.TradeView(A, new TradeOffer { Items = { a0.Inv[0], a0.Inv[0] } })!.Items.Count == 1);
            Check("offer: no save on the server yet = no offer", AuctionServer.TradeView("nobody", askA) == null);
            Check("message limits: an offer with too many items is refused whole", TwinRealms.Trade.Parse(Auction.ToJson(new TradeOffer { Items = Enumerable.Range(0, TwinRealms.Trade.MaxItems + 1).Select(_ => a0.Inv[0]).ToList() })) == null);

            askA = new TradeOffer { Gold = 50 }; askA.Items.Add(a0.Inv[0]); askA.Stacks["mat:iron_ore"] = 4; askA.Stacks["pot:hp"] = 2; askA.Pets.Add(a0.Pets[0]);
            var askB = new TradeOffer { Gold = 10 }; askB.Items.Add(b0.Inv[0]); askB.Stacks["mat:blk_stone"] = 15;
            va = AuctionServer.TradeView(A, askA)!; var vb = AuctionServer.TradeView(B, askB)!; string sa = Auction.ToJson(va), sb = Auction.ToJson(vb);

            // ---- refusals: nothing may change
            var r = AuctionServer.TradeSwap(A, va, "{\"tampered\":1}", B, vb, sb);
            Check("swap refused when an offer is not what the partner was shown; nothing changed", !r.ok && Hero(A).Inv.Count == 2 && Hero(B).Inv.Count == 1 && Hero(A).Gold == 500, r.msg);
            Check("swap with yourself refused", !AuctionServer.TradeSwap(A, va, sa, A, va, sa).ok);
            // B's pack full: A's item cannot arrive (B gives nothing back in this try)
            var full = Hero(B); while (full.Inv.Count < Auction.PackSize) full.Inv.Add(It("Filler " + full.Inv.Count, "feet")); Make(B, full);
            var none = AuctionServer.TradeView(B, new TradeOffer())!;
            r = AuctionServer.TradeSwap(A, va, sa, B, none, Auction.ToJson(none));
            Check("pack space is checked first: a full pack refuses the trade, nothing moved", !r.ok && r.msg.Contains("pack") && Hero(A).Inv.Count == 2 && Hero(A).Gold == 500, r.msg);
            Make(B, db); vb = AuctionServer.TradeView(B, askB)!; sb = Auction.ToJson(vb);
            // A spends the offered gold before the swap (a newer save arrives): the offer no longer matches what was shown
            var poor = Hero(A); poor.Gold = 20; Make(A, poor);
            r = AuctionServer.TradeSwap(A, va, sa, B, vb, sb);
            Check("an offer that changed after it was shown is refused (gold spent meanwhile)", !r.ok && Hero(B).Gold == 100, r.msg);
            Make(A, da); a0 = Hero(A); va = AuctionServer.TradeView(A, askA)!; sa = Auction.ToJson(va);
            int items = Total(d => d.Inv.Count), gold = Total(d => d.Gold), ore = Total(d => d.Mats.GetValueOrDefault("iron_ore")), stone = Total(d => d.Mats.GetValueOrDefault("blk_stone")), pots = Total(d => d.HpPot), pets = Total(d => d.Pets.Count);
            string lateA = Accounts.LoadSave(A);   // A's save from before the trade, as an upload already on its way would be

            // ---- the swap
            r = AuctionServer.TradeSwap(A, va, sa, B, vb, sb); var ha = Hero(A); var hb = Hero(B);
            Check("swap: done in one step, both deltas returned", r.ok && r.a != null && r.b != null, r.msg);
            Check("swap: items changed hands (A has the bow, B the sword)", ha.Inv.Exists(i => i.Name == "Test Bow") && !ha.Inv.Exists(i => i.Name == "Test Sword") && hb.Inv.Exists(i => i.Name == "Test Sword") && !hb.Inv.Exists(i => i.Name == "Test Bow"));
            Check("swap: gold moved (A 460, B 140)", ha.Gold == 460 && hb.Gold == 140, $"A {ha.Gold} B {hb.Gold}");
            Check("swap: materials, blocks and potions moved", ha.Mats.GetValueOrDefault("iron_ore") == 6 && hb.Mats.GetValueOrDefault("iron_ore") == 4 && ha.Mats.GetValueOrDefault("blk_stone") == 15 && hb.Mats.GetValueOrDefault("blk_stone") == 5 && ha.HpPot == 3 && hb.HpPot == db.HpPot + 2,
                $"A ore {ha.Mats.GetValueOrDefault("iron_ore")} stone {ha.Mats.GetValueOrDefault("blk_stone")} pots {ha.HpPot}; B ore {hb.Mats.GetValueOrDefault("iron_ore")} stone {hb.Mats.GetValueOrDefault("blk_stone")} pots {hb.HpPot}");
            var got = hb.Pets.Find(p => p.Species == "wolf");
            Check("swap: the pet has exactly one owner, with its level and trainer", got != null && got.Level == 7 && got.Ot == "Ansel" && !ha.Pets.Exists(p => p.Id == wolf) && ha.Pets.Count == 1 && hb.Pets.Count == 1);
            Check("nothing was created or lost (items, gold, ore, stone, potions, pets)", Total(d => d.Inv.Count) == items && Total(d => d.Gold) == gold && Total(d => d.Mats.GetValueOrDefault("iron_ore")) == ore && Total(d => d.Mats.GetValueOrDefault("blk_stone")) == stone && Total(d => d.HpPot) == pots && Total(d => d.Pets.Count) == pets);

            // ---- a disconnect: A's game never got the message and its old save arrives afterwards
            string? e2 = Accounts.StoreSave(A, lateA); ha = Hero(A);
            Check("a late save from before the trade cannot bring anything back (no duplicate)", e2 == null && !ha.Inv.Exists(i => i.Name == "Test Sword") && ha.Inv.Exists(i => i.Name == "Test Bow") && ha.Gold == 460 && ha.Mats.GetValueOrDefault("iron_ore") == 6 && ha.HpPot == 3 && !ha.Pets.Exists(p => p.Id == wolf),
                $"A items {string.Join(",", ha.Inv.Select(i => i.Name))} gold {ha.Gold} ore {ha.Mats.GetValueOrDefault("iron_ore")}");
            Check("the same late save a second time changes nothing either", Accounts.StoreSave(A, lateA) == null && Hero(A).Gold == 460 && Hero(A).Inv.Count == 2);
            var local = Auction.FromJson<Game.SaveFile>(lateA)!.Slots[0]!; Auction.Apply(local, r.a!);
            Check("the delta applied by the player's game gives the same hero as the server's save", local.Gold == ha.Gold && local.HpPot == ha.HpPot && local.Mats.GetValueOrDefault("blk_stone") == ha.Mats.GetValueOrDefault("blk_stone") && local.Inv.Select(i => i.Name).OrderBy(x => x).SequenceEqual(ha.Inv.Select(i => i.Name).OrderBy(x => x)) && local.Pets.Count == ha.Pets.Count);
            r = AuctionServer.TradeSwap(A, va, sa, B, vb, sb);
            Check("the same swap again is refused (the things are gone)", !r.ok && Total(d => d.Inv.Count) == items && Total(d => d.Gold) == gold, r.msg);
            // a gift: one side gives, the other nothing
            var gift = AuctionServer.TradeView(B, new TradeOffer { Gold = 40 })!; none = AuctionServer.TradeView(A, new TradeOffer())!;
            r = AuctionServer.TradeSwap(B, gift, Auction.ToJson(gift), A, none, Auction.ToJson(none));
            Check("a one-sided trade (a gift of gold) works", r.ok && Hero(A).Gold == 500 && Hero(B).Gold == 100, r.msg);
            Check("an empty trade is refused", !AuctionServer.TradeSwap(A, none, Auction.ToJson(none), B, AuctionServer.TradeView(B, new TradeOffer())!, Auction.ToJson(new TradeOffer())).ok);
            Check("totals unchanged at the end", Total(d => d.Inv.Count) == items && Total(d => d.Gold) == gold && Total(d => d.Pets.Count) == pets);
            AuctionServer.TradeLog("Ansel [ta] gave: test\tBrida [tb] gave: test");
            Check("the trade log is written", File.Exists(SocialLog.PathOf("trade")));
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally { Unscratch(); }
        return End("tradetest", log, fails, "the server-run swap on fake accounts");
    }

    // ------------------------------------------------------------------ partytest
    public static string Party()
    {
        var log = new List<string>(); int fails = 0;
        void Check(string name, bool ok, string detail = "") { log.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? "   [" + detail + "]" : "")}"); if (!ok) fails++; }
        try
        {
            var party = new List<long> { 11, 22, 33 }; var online = new List<long> { 11, 22, 44, 55 };
            var to = PartyQuests.Recipients(party, 11, online);
            Check("relay: an event goes to the sender's party members who are online, not back to the sender", to.Count == 1 && to[0] == 22, string.Join(",", to));
            Check("relay: a player outside the party reaches nobody in it", PartyQuests.Recipients(party, 44, online).Count == 0);
            Check("relay: unknown kinds, long ids and absurd counts are refused", !PartyQuests.ValidEvent("xx", "wolves", "", "", 1) && !PartyQuests.ValidEvent("qp", new string('a', 80), "", "", 1) && !PartyQuests.ValidEvent("qp", "wolves", "", "", 999999999) && !PartyQuests.ValidEvent("qp", "", "", "", 1) && PartyQuests.ValidEvent("qp", "wolves", "", "", 3));
            var st = new Story.Stage { Id = "hunt", Then = "report" };
            Check("story: a stage event may only lead to that stage's own next stage, never to a failure", PartyQuests.StageOk(st, "hunt", "report") && !PartyQuests.StageOk(st, "hunt", "done") && !PartyQuests.StageOk(st, "other", "report") && !PartyQuests.StageOk(new Story.Stage { Id = "x", Then = "fail" }, "x", "fail"));

            // ---- a member's steps applied to THIS hero's old quest (state put back afterwards)
            Quests.ById("");   // loads the definitions
            var q = Quests.All.Find(x => x.Kind == "kill" && x.Need >= 3) ?? Quests.All.FirstOrDefault(x => x.Need >= 3);
            if (q == null || Player.Instance == null) Check("old quest steps (needs a hero in the world and a quest with 3+ steps)", false, "skipped: no hero or no such quest");
            else
            {
                var s = Game.Q(q.Id); int s0 = s.S, n0 = s.N;
                try
                {
                    s.S = 1; s.N = 0; PartyQuests.Apply(22, "Tester", "qp", q.Id, "", "", 2);
                    Check($"\"{q.Name}\": a member's count of 2 becomes ours", s.S == 1 && s.N == 2, $"S {s.S} N {s.N}");
                    PartyQuests.Apply(22, "Tester", "qp", q.Id, "", "", 1);
                    Check("a lower count never takes progress away (and the same kill is not counted twice)", s.N == 2);
                    PartyQuests.Apply(22, "Tester", "qp", q.Id, "", "", q.Need + 5);
                    Check("a member finishing the objective finishes it for us (ready to turn in, count capped)", s.S == 2 && s.N == q.Need, $"S {s.S} N {s.N}");
                    s.S = 3; s.N = q.Need; PartyQuests.Apply(22, "Tester", "qd", q.Id, "", "", 0);
                    Check("a quest we already turned in is not rewarded again", s.S == 3);
                    int offers = PartyQuests.Offers.Count; s.S = 0; s.N = 0; PartyQuests.Apply(22, "Tester", "qd", q.Id, "", "", 0);
                    Check("a quest we have not started is never completed for us (it is offered instead, when we may take it)", s.S == 0 && s.N == 0, $"offers {offers} -> {PartyQuests.Offers.Count}");
                    PartyQuests.Offers.RemoveAll(o => o.Who == "Tester");
                    Check("applying a member's step sends nothing on (no echo)", !PartyQuests.Applying);
                }
                finally { s.S = s0; s.N = n0; Game.Notify(); }
            }
            Check("party size limit comes from data (data/social.json party.max)", Net.PartyMax == Math.Clamp(SocialCfg.I("party.max", 6), 2, 40), Net.PartyMax.ToString());
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        return End("partytest", log, fails, "relay rules and a member's steps applied to this hero");
    }
}
