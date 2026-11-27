using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>
/// Online server rules for the block world (focus F-44), as seen by a player's own game: on the online server nobody but an admin
/// mines the overworld (monsters drop building blocks instead, BlockDrops.cs); placing is allowed outside town zones; a block a
/// player placed can be taken out again by that player. The game refuses at once for feel; the server refuses for truth (BlockAudit.Judge).
/// </summary>
public static class OnlineRules
{
    public const string NoMiningMsg = "Mining is off on the online server: monsters drop building blocks. You can still remove blocks you placed.";
    public const string NotYoursMsg = "Another player placed that block: only they (or an admin) can remove it.";
    /// <summary>This machine plays on the online server without admin rights: no digging, no block-breaking spells.</summary>
    public static bool NoMining => Game.Online && Net.IsClient && !Admin.IsAdmin;
    /// <summary>Why this block may not be dug here (null = go ahead; for a placed block the server has the last word).</summary>
    public static string? DenyDig(BlockArea a, int x, int y, int z)
    {
        if (!NoMining || a.Key != BlockAudit.Area) return null;   // dungeons are private instances
        return a.Diff.TryGetValue(BlockArea.BKey(x, y, z), out ushort v) && (v & 255) != 0 ? null : NoMiningMsg;   // only something a player put there
    }
}

/// <summary>
/// The online server's record of every block placed and removed in the shared overworld (focus F-44): who (account and hero name), what,
/// where, when. Kept in memory (the newest MaxMem entries, for /blocklog and /rollback) and appended to rotating text files in
/// &lt;server data&gt;/blocklog/ (blocks.log, then blocks.1.log ... blocks.(KeepFiles-1).log; a file is rotated at MaxBytes, the oldest deleted).
/// One line per change, tab separated: unix time, account key, hero name, action (P placed, R removed, U undone by a rollback),
/// block key now, meta, block key before ("-" = the generated world), meta before, x, y, z.
/// The server has no block world of its own: what it knows of a cell is the change list it keeps for newcomers (BlockWorld.ServerCell),
/// so "placed by a player" = a non-air cell in that list, and its owner is whoever put it there.
/// </summary>
public static class BlockAudit
{
    public const string Area = "overworld";
    public struct Entry
    {
        public long T; public string Acct, Name; public char Act; public byte Id, Meta; public short Prev; public byte PrevMeta; public int X, Y, Z;
        /// <summary>The block the entry is about: the one placed, or the one taken out.</summary>
        public string Block => Act == 'R' ? KeyOf(Prev) : KeyOf(Id);
    }
    static readonly List<Entry> _mem = new();
    static readonly Dictionary<long, string> _owner = new();   // cell -> account that placed what is there now
    static readonly StringBuilder _pending = new();
    static bool _loaded; static long _size = -1;
    public static int MaxBytes = 1024 * 1024, KeepFiles = 5, MaxMem = 60000;
    /// <summary>Folder of the log files ("" = &lt;server data&gt;/blocklog). The self-test points it at a scratch folder.</summary>
    public static string Dir = "";
    public static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static int Count => _mem.Count;

    static string Folder { get { string d = Dir != "" ? Dir : Path.Combine(Accounts.Dir, "blocklog"); Directory.CreateDirectory(d); return d; } }
    static string FileN(int i) => Path.Combine(Folder, i == 0 ? "blocks.log" : $"blocks.{i}.log");
    static string KeyOf(int id) => id < 0 ? "-" : id == 0 ? "air" : Blocks.Def((byte)id).Key;
    static string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Forgets everything in memory (the self-test; files stay).</summary>
    public static void Reset() { _mem.Clear(); _owner.Clear(); _pending.Clear(); _loaded = false; _size = -1; }

    /// <summary>First use: settings (data/tuning.json game.online.blockLog*) and the newest entries of the files back into memory for /blocklog.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return; _loaded = true;
        if (Dir == "") { MaxBytes = Math.Max(4096, Tune.I("online.blockLogKb", MaxBytes / 1024) * 1024); KeepFiles = Math.Max(1, Tune.I("online.blockLogFiles", KeepFiles)); MaxMem = Math.Max(100, Tune.I("online.blockLogMemory", MaxMem)); }
        try
        {
            for (int i = KeepFiles - 1; i >= 0; i--)
            {
                if (!File.Exists(FileN(i))) continue;
                foreach (var line in File.ReadLines(FileN(i))) if (Parse(line) is { } e) { _mem.Add(e); if (_mem.Count > MaxMem + MaxMem / 10) _mem.RemoveRange(0, _mem.Count - MaxMem); }
            }
            _size = File.Exists(FileN(0)) ? new FileInfo(FileN(0)).Length : 0;
        }
        catch (Exception e) { GD.PushWarning("[BLOCKLOG] old log unreadable: " + e.Message); }
    }
    static Entry? Parse(string line)
    {
        var p = line.Split('\t'); if (p.Length < 11 || !long.TryParse(p[0], out long t) || p[3].Length != 1) return null;
        short Id(string k) => k == "-" ? (short)-1 : k == "air" ? (short)0 : Blocks.Id(k);
        if (!int.TryParse(p[8], out int x) || !int.TryParse(p[9], out int y) || !int.TryParse(p[10], out int z)) return null;
        byte.TryParse(p[5], out byte m); byte.TryParse(p[7], out byte pm);
        return new Entry { T = t, Acct = p[1], Name = p[2], Act = p[3][0], Id = (byte)Math.Max((short)0, Id(p[4])), Meta = m, Prev = Id(p[6]), PrevMeta = pm, X = x, Y = y, Z = z };
    }

    // ---------------------------------------------------------------- the rules (server)
    /// <summary>
    /// May this account make this change in the shared overworld? Null = yes. Town zones are checked by the caller (NetBlocks.cs).
    /// Non-admins never remove a block of the generated world, and never remove or replace a block another player placed
    /// (opening and closing a door is not replacing it).
    /// </summary>
    public static string? Judge(string acct, bool admin, int x, int y, int z, byte id)
    {
        if (admin) return null;
        var cur = BlockWorld.ServerCell(Area, x, y, z); bool placed = cur is { } c && c.id != 0;
        string owner = _owner.GetValueOrDefault(BlockArea.BKey(x, y, z), "");
        if (id == 0) return !placed ? OnlineRules.NoMiningMsg : owner != acct || acct == "" ? OnlineRules.NotYoursMsg : null;
        if (placed && (owner != acct || acct == "")) return BlockUse.IsDoor(cur!.Value.id) && BlockUse.IsDoor(id) ? null : OnlineRules.NotYoursMsg;
        return null;
    }

    /// <summary>An accepted change: remembered as the cell's state, its owner updated, and written to the log (call Flush after the batch).</summary>
    public static void Accept(string acct, string name, int x, int y, int z, byte id, byte meta, char act = '\0')
    {
        EnsureLoaded();
        var cur = BlockWorld.ServerCell(Area, x, y, z); long k = BlockArea.BKey(x, y, z);
        BlockWorld.KeepServerCell(Area, x, y, z, id, meta);
        // a door swinging: not a placement (a door of the generated world keeps no owner)
        if (act == '\0' && id != 0 && BlockUse.IsDoor(id) && (cur is { } d ? BlockUse.IsDoor(d.id) : BlockUse.IsOpen(id))) return;
        if (cur is { } same && same.id == id && same.meta == meta) return;   // nothing changed
        if (id == 0) _owner.Remove(k); else _owner[k] = acct;
        Add(new Entry { T = Clock(), Acct = acct, Name = Clean(name), Act = act != '\0' ? act : id == 0 ? 'R' : 'P', Id = id, Meta = meta, Prev = cur is { } c ? c.id : (short)-1, PrevMeta = cur?.meta ?? 0, X = x, Y = y, Z = z });
    }
    static void Add(Entry e)
    {
        _mem.Add(e); if (_mem.Count > MaxMem + MaxMem / 10) _mem.RemoveRange(0, _mem.Count - MaxMem);
        _pending.Append(e.T).Append('\t').Append(e.Acct).Append('\t').Append(e.Name).Append('\t').Append(e.Act).Append('\t').Append(KeyOf(e.Id)).Append('\t').Append(e.Meta).Append('\t')
            .Append(KeyOf(e.Prev)).Append('\t').Append(e.PrevMeta).Append('\t').Append(e.X).Append('\t').Append(e.Y).Append('\t').Append(e.Z).Append('\n');
    }
    /// <summary>Writes the lines gathered since the last call; rotates the files first when the current one is full.</summary>
    public static void Flush()
    {
        if (_pending.Length == 0) return; string text = _pending.ToString(); _pending.Clear();
        try
        {
            if (_size < 0) _size = File.Exists(FileN(0)) ? new FileInfo(FileN(0)).Length : 0;
            if (_size > 0 && _size + text.Length > MaxBytes)
            {
                if (File.Exists(FileN(KeepFiles - 1))) File.Delete(FileN(KeepFiles - 1));
                for (int i = KeepFiles - 2; i >= 0; i--) if (File.Exists(FileN(i))) { if (i + 1 <= KeepFiles - 1) File.Move(FileN(i), FileN(i + 1), true); else File.Delete(FileN(i)); }
                _size = 0;
            }
            File.AppendAllText(FileN(0), text); _size += text.Length;
        }
        catch (Exception e) { GD.PushWarning("[BLOCKLOG] not written: " + e.Message); }
    }
    /// <summary>The log files that exist now and their sizes (self-test, /blocklog files).</summary>
    public static List<(string file, long bytes)> Files()
    {
        var l = new List<(string, long)>();
        for (int i = 0; i < Math.Max(KeepFiles, 12); i++) if (File.Exists(FileN(i))) l.Add((Path.GetFileName(FileN(i)), new FileInfo(FileN(i)).Length));
        return l;
    }

    // ---------------------------------------------------------------- looking things up
    static string Line(Entry e, long now)
    {
        long s = Math.Max(0, now - e.T); string ago = s < 90 ? s + " s" : s < 5400 ? s / 60 + " min" : s < 172800 ? s / 3600 + " h" : s / 86400 + " d";
        string what = e.Act == 'P' ? "placed " + e.Block + (e.Prev > 0 ? " over " + KeyOf(e.Prev) : "") : e.Act == 'R' ? "removed " + (e.Prev < 0 ? "a world block" : e.Block) : $"rolled back {KeyOf(e.Prev)} -> {(e.Id == 0 ? "air / world block" : KeyOf(e.Id))}";
        return $"{DateTimeOffset.FromUnixTimeSeconds(e.T):MM-dd HH:mm}Z ({ago} ago)  {e.Name} [{Short(e.Acct)}]  {what}  at {e.X} {e.Y} {e.Z}";
    }
    static string Short(string acct) => acct.Length > 8 ? acct[..8] : acct;
    static bool IsPlayer(Entry e, string who) => e.Name.Equals(who, StringComparison.OrdinalIgnoreCase) || (who.Length >= 6 && e.Acct.StartsWith(who, StringComparison.OrdinalIgnoreCase));

    /// <summary>Newest first, one page. match picks the entries; title names the search in the header.</summary>
    public static string Query(Func<Entry, bool> match, string title, int page, int perPage = 8)
    {
        EnsureLoaded(); var hits = new List<Entry>();
        for (int i = _mem.Count - 1; i >= 0; i--) if (match(_mem[i])) hits.Add(_mem[i]);
        if (hits.Count == 0) return $"block log, {title}: nothing recorded";
        int pages = (hits.Count + perPage - 1) / perPage; page = Math.Clamp(page, 1, pages); long now = Clock();
        var sb = new StringBuilder($"block log, {title}: {hits.Count} entr{(hits.Count == 1 ? "y" : "ies")}, page {page}/{pages}");
        foreach (var e in hits.Skip((page - 1) * perPage).Take(perPage)) sb.Append('\n').Append(Line(e, now));
        if (page < pages) sb.Append($"\n(next page: add {page + 1} at the end)");
        return sb.ToString();
    }
    public static string QueryCell(int x, int y, int z, int page) => Query(e => e.X == x && e.Y == y && e.Z == z, $"block {x} {y} {z}", page);
    public static string QueryRadius(Vector3 at, float r, int page)
    {
        int cx = Mathf.FloorToInt(at.X), cy = Mathf.FloorToInt(at.Y), cz = Mathf.FloorToInt(at.Z); float r2 = r * r;
        return Query(e => { float dx = e.X - cx, dy = e.Y - cy, dz = e.Z - cz; return dx * dx + dy * dy + dz * dz <= r2; }, $"within {r:0} m of {cx} {cy} {cz}", page);
    }
    public static string QueryPlayer(string who, int page) => Query(e => IsPlayer(e, who), "player " + who, page);
    /// <summary>Hero names in the log (for autocomplete and "did you mean").</summary>
    public static IEnumerable<string> Names() { EnsureLoaded(); return _mem.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase); }

    /// <summary>"30m", "2h", "1d", "45s" (a bare number is minutes) as seconds; 0 if it is not a time.</summary>
    public static long ParseSpan(string s)
    {
        s = s.Trim().ToLowerInvariant(); if (s.Length == 0) return 0;
        long mul = 60; char u = s[^1];
        if (char.IsLetter(u)) { mul = u switch { 's' => 1, 'm' => 60, 'h' => 3600, 'd' => 86400, 'w' => 604800, _ => 0 }; s = s[..^1]; }
        return mul > 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? (long)(v * mul) : 0;
    }

    // ---------------------------------------------------------------- rollback
    /// <summary>
    /// Undoes a player's placements and removals of the last windowSec seconds, newest first. A change is only undone while the cell still
    /// holds what that change left there (someone building on the spot since is not overwritten: counted as skipped). Each undo is itself
    /// logged (action U, by the admin). Returns the cells to send to everyone: `set` with a known block, `regen` back to the generated world.
    /// </summary>
    public static (int undone, int skipped) Rollback(string who, long windowSec, string byAcct, string byName,
        List<(int x, int y, int z, byte id, byte meta)> set, List<(int x, int y, int z, byte id, byte meta)> regen)
    {
        EnsureLoaded(); long since = Clock() - windowSec; int undone = 0, skipped = 0;
        var mine = new List<Entry>();
        for (int i = _mem.Count - 1; i >= 0; i--) { var e = _mem[i]; if (e.T < since) break; if (e.Act != 'U' && IsPlayer(e, who)) mine.Add(e); }
        var final = new Dictionary<long, (int x, int y, int z, short id, byte meta)>();
        foreach (var e in mine)   // newest first
        {
            var cur = BlockWorld.ServerCell(Area, e.X, e.Y, e.Z); long k = BlockArea.BKey(e.X, e.Y, e.Z);
            bool still = cur is { } c ? c.id == e.Id : false;
            if (!still) { skipped++; continue; }
            if (e.Prev < 0) { BlockWorld.ServerForget(Area, e.X, e.Y, e.Z); _owner.Remove(k); }
            else
            {
                BlockWorld.KeepServerCell(Area, e.X, e.Y, e.Z, (byte)e.Prev, e.PrevMeta);
                if (e.Prev == 0) _owner.Remove(k); else _owner[k] = e.Act == 'R' ? e.Acct : "";   // their own block back; otherwise nobody's (admins only)
            }
            Add(new Entry { T = Clock(), Acct = byAcct, Name = Clean(byName), Act = 'U', Id = (byte)Math.Max((short)0, e.Prev), Meta = e.PrevMeta, Prev = e.Id, PrevMeta = e.Meta, X = e.X, Y = e.Y, Z = e.Z });
            final[k] = (e.X, e.Y, e.Z, e.Prev, e.PrevMeta); undone++;
        }
        foreach (var f in final.Values) { if (f.id < 0) regen.Add((f.x, f.y, f.z, 0, 0)); else set.Add((f.x, f.y, f.z, (byte)f.id, f.meta)); }
        Flush();
        return (undone, skipped);
    }
}
