using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Interactive passive tree. Two layers (A-15): the base tree (about 740 nodes: 18 class paths, 5 themed paths, bridges) and the
/// advanced tree (one path per base path: Mage -> Archmage ...), switched with TreeView.Layer (the tabs in the tree window).
/// Opens centred on your class start (advanced: on the first path you can open). Wheel zooms toward the cursor, drag pans,
/// left click takes a node next to your path, or the whole shortest path to a far node when you have the points; right click refunds
/// one that nothing else depends on. Hovering shows the node in a card by the mouse and the path to it. Search lights up matches.
/// The view (zoom and pan, per layer) is kept while the menu stays open, so taking a node does not throw you back to your start.
/// </summary>
public partial class TreeView : Control, IPadFocus
{
    public Action? Changed; public Action<int, Vector2>? HoverAt;
    /// <summary>Which layer is shown: 0 the base tree, 1 the advanced tree.</summary>
    public static int Layer;
    public static bool HasSaved { get => _saved[Layer]; set { if (!value) _saved[0] = _saved[1] = false; else _saved[Layer] = true; } }
    static readonly bool[] _saved = new bool[2]; static readonly float[] _sZoom = new float[2]; static readonly Vector2[] _sPan = new Vector2[2];
    float _zoom = 1f; Vector2 _pan = Vector2.Zero; bool _drag; int _hover = -1; float _scale = 1f, _k = 1f; int _start;
    List<int>? _path; readonly HashSet<int> _match = new(); readonly List<TreeNode> _vis = new();
    static Font F => ThemeDB.FallbackFont;
    /// <summary>Zoom in the old tree's units: the 23-wedge tree is 1.75 times as wide, so sizes and label thresholds follow _zoom / _k.</summary>
    float Z => _zoom / _k;
    Vector2 ToScreen(TreeNode n) => Size / 2 + _pan + new Vector2(n.X, n.Y) * _scale * _zoom;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; TreeDb.Load(); _start = TreeDb.StartFor(Game.D.Class);
        Layer = Mathf.Clamp(Layer, 0, 1); foreach (var n in TreeDb.Nodes) if (n.Layer == Layer) _vis.Add(n);
        if (_vis.Count == 0) { Layer = 0; foreach (var n in TreeDb.Nodes) if (n.Layer == 0) _vis.Add(n); }
        float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f; foreach (var n in _vis) { minx = Mathf.Min(minx, n.X); maxx = Mathf.Max(maxx, n.X); miny = Mathf.Min(miny, n.Y); maxy = Mathf.Max(maxy, n.Y); }
        var box = CustomMinimumSize == Vector2.Zero ? new Vector2(960, 560) : CustomMinimumSize;
        _scale = Mathf.Min((box.X - 30) / (maxx - minx + 60), (box.Y - 20) / (maxy - miny + 60));
        _k = Mathf.Max(.5f, (maxx - minx + 60) / 3380f);   // 3380 = the width of the first, 13-wedge tree the sizes were tuned for
        if (_saved[Layer]) { _zoom = _sZoom[Layer]; _pan = _sPan[Layer]; } else Home();
        MouseExited += () => { _hover = -1; _path = null; HoverAt?.Invoke(-1, Vector2.Zero); QueueRedraw(); };
    }
    void Save() { _saved[Layer] = true; _sZoom[Layer] = _zoom; _sPan[Layer] = _pan; }
    public void CenterOn(TreeNode n, float zoom) { _zoom = zoom * _k; _pan = -new Vector2(n.X, n.Y) * _scale * _zoom; Save(); QueueRedraw(); }
    /// <summary>The node this hero's view starts on: the class start; in the advanced tree a path already begun, else the class's own advanced path.</summary>
    TreeNode HomeNode()
    {
        if (Layer == 0) return TreeDb.Get(_start) ?? _vis[0];
        var tree = Game.D.Tree; AdvPath? best = null;
        foreach (var p in TreeDb.Paths.Values) if (tree.Contains(p.Root)) { best = p; break; }
        string arm = TreeDb.Get(_start)?.Arm ?? "";
        best ??= TreeDb.Paths.Values.FirstOrDefault(p => p.Base == arm) ?? TreeDb.Paths.Values.FirstOrDefault(p => TreeDb.PathOpen(p, tree, out _));
        return (best != null ? TreeDb.Get(best.Root) : null) ?? _vis[0];
    }
    public void Home() => CenterOn(HomeNode(), 3.2f);
    /// <summary>The whole layer in view.</summary>
    public void Whole() { _zoom = 1f; _pan = Vector2.Zero; Save(); QueueRedraw(); }
    public void ZoomBy(float f) => ZoomAt(Size / 2, f);
    public void SetSearch(string s)
    {
        _match.Clear(); s = s.Trim().ToLowerInvariant();
        if (s.Length >= 2) foreach (var n in _vis) if (n.Name.ToLowerInvariant().Contains(s) || n.Desc.ToLowerInvariant().Contains(s) || n.Mods.Any(kv => kv.Key.ToLowerInvariant().Contains(s) || TreeDb.StatText(kv.Key, kv.Value).ToLowerInvariant().Contains(s))) _match.Add(n.Id);
        QueueRedraw();
    }
    public int Matches => _match.Count;
    /// <summary>Shortest run of untaken nodes from what you have to n (n last); null if taken or unreachable. In the advanced tree a
    /// path starts at its gate, and only at a gate whose conditions are met (TreeDb.PathOpen).</summary>
    public List<int>? PathTo(int target)
    {
        var tn = TreeDb.Get(target); if (tn == null) return null;
        var tree = Game.D.Tree; var have = new HashSet<int>(tree) { _start }; if (have.Contains(target)) return null;
        var prev = new Dictionary<int, int>(); var q = new Queue<int>();
        foreach (var h in have) { if (TreeDb.Get(h) is not { } hn || hn.Layer != tn.Layer) continue; prev[h] = -1; q.Enqueue(h); }
        if (tn.Layer == 1) foreach (var p in TreeDb.Paths.Values) if (!have.Contains(p.Root) && TreeDb.PathOpen(p, tree, out _)) { prev[p.Root] = -1; q.Enqueue(p.Root); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(); if (c == target) break;
            foreach (var a in TreeDb.Nodes[c].Adj) { if (prev.ContainsKey(a)) continue; prev[a] = c; q.Enqueue(a); }   // other class starts cost a point like any node
        }
        if (!prev.ContainsKey(target)) return null;
        var path = new List<int>(); for (int c = target; c != -1 && !have.Contains(c); c = prev[c]) path.Add(c); path.Reverse(); return path;
    }
    static bool Big(TreeNode n) => n.Type is "start" or "branch" or "gate";
    float Rad(TreeNode n) => (Big(n) ? 15f : n.Type == "hub" ? 13f : n.Type == "key" ? 12f : n.Type == "notable" ? 8.5f : 5f) * Mathf.Clamp(Z / 2.5f, 0.55f, 1.5f);
    public override void _Draw()
    {
        var d = Game.D; var alloc = new HashSet<int>(d.Tree); alloc.Add(_start); var set = new HashSet<int>(d.Tree);
        DrawRect(new Rect2(Vector2.Zero, Size), Layer == 1 ? new Color(.045f, .03f, .06f) : new Color(.02f, .03f, .06f));
        var c0 = Size / 2 + _pan; foreach (var r in TreeDb.Rings[Layer]) DrawArc(c0, r * _scale * _zoom, 0, Mathf.Tau, 96, new Color(.15f, .22f, .32f, .35f), 1f);
        var onPath = _path != null ? new HashSet<int>(_path) : new HashSet<int>();
        bool afford = _path != null && _path.Count <= d.SkillPts;
        var pathCol = afford ? new Color(.45f, 1f, .6f) : new Color(1f, .55f, .45f);
        // advanced layer: which paths are open (their gate can be taken or is taken); the rest are drawn dim
        HashSet<string>? shut = null;
        if (Layer == 1) { shut = new HashSet<string>(); foreach (var p in TreeDb.Paths.Values) if (!set.Contains(p.Root) && !TreeDb.PathOpen(p, set, out _)) shut.Add(p.Id); }
        var view = GetRect().Grow(400);
        foreach (var n in _vis) foreach (var a in n.Adj) if (a > n.Id)
        {
            var m = TreeDb.Nodes[a]; bool on = alloc.Contains(n.Id) && alloc.Contains(a); var p0 = ToScreen(n); var p1 = ToScreen(m);
            if (!view.HasPoint(p0 + Position) && !view.HasPoint(p1 + Position)) continue;
            bool pl = (onPath.Contains(n.Id) || alloc.Contains(n.Id)) && (onPath.Contains(a) || alloc.Contains(a)) && (onPath.Contains(n.Id) || onPath.Contains(a));
            DrawLine(p0, p1, on ? new Color(.78f, .6f, .27f) : pl ? pathCol : new Color(.2f, .2f, .22f), on ? 3f : pl ? 2.6f : 1.4f);
        }
        foreach (var n in _vis)
        {
            var p = ToScreen(n); if (p.X < -40 || p.Y < -40 || p.X > Size.X + 40 || p.Y > Size.Y + 40) continue;
            bool on = alloc.Contains(n.Id); bool can = TreeDb.CanAlloc(n, set, d.SkillPts, _start); float r = Rad(n);
            var gold = new Color(.8f, .62f, .26f); bool hov = _hover == n.Id; bool isStart = Big(n); bool dim = shut != null && shut.Contains(n.Arm);
            float pulse = .65f + .35f * Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 2.1f), k = r + 4;
            if (on) { DrawCircle(p, r + 8, gold with { A = .10f }); DrawCircle(p, r + 5, gold with { A = .16f }); }
            var ring = on ? gold.Lightened(hov ? .35f : 0f) : can ? (gold with { A = hov ? 1f : pulse }) : isStart ? n.Col.Darkened(dim ? .75f : .5f) : new Color(.22f, .22f, .25f).Darkened(dim ? .35f : 0f).Lightened(hov ? .5f : 0f);
            var fill = on ? gold.Darkened(.1f).Lightened(hov ? .25f : 0f) : isStart ? n.Col.Darkened(dim ? .8f : .55f) : new Color(.09f, .09f, .1f).Lightened(hov ? .25f : 0f);
            if (n.Type == "key")
            {
                DrawColoredPolygon(new[] { p + new Vector2(0, -k - 3), p + new Vector2(k + 3, 0), p + new Vector2(0, k + 3), p + new Vector2(-k - 3, 0) }, ring);
                DrawColoredPolygon(new[] { p + new Vector2(0, -k + 1), p + new Vector2(k - 1, 0), p + new Vector2(0, k - 1), p + new Vector2(-k + 1, 0) }, new Color(.05f, .05f, .06f));
                DrawArc(p, r + 1, 0, Mathf.Tau, 28, ring, 1.5f);
                DrawCircle(p, r - 2, fill);
            }
            else { DrawCircle(p, r + 2, ring); DrawCircle(p, r, isStart ? fill : on ? fill : new Color(.07f, .07f, .08f)); if (isStart || (on && n.Type != "small")) DrawCircle(p, r * .55f, on ? n.Col.Lerp(gold, .5f) : n.Col.Darkened(dim ? .8f : .55f)); }
            if (n.Type == "gate" && dim) { DrawRect(new Rect2(p + new Vector2(-r * .32f, -r * .05f), new Vector2(r * .64f, r * .46f)), new Color(.55f, .52f, .5f)); DrawArc(p + new Vector2(0, -r * .05f), r * .22f, Mathf.Pi, Mathf.Tau, 10, new Color(.55f, .52f, .5f), 2f); }   // a padlock: this path is still shut
            if (onPath.Contains(n.Id)) DrawArc(p, r + 4, 0, Mathf.Tau, 20, pathCol, 2f);
            if (_match.Contains(n.Id)) { DrawArc(p, r + 7, 0, Mathf.Tau, 24, new Color(.35f, .95f, 1f), 2.5f); DrawArc(p, r + 11, 0, Mathf.Tau, 24, new Color(.35f, .95f, 1f, .35f), 2f); }
            if (n.Type == "start" && n.Id == _start) DrawArc(p, r + 7, 0, Mathf.Tau, 32, UiTheme.Highlight, 2.5f);
            if (_hover == n.Id) DrawArc(p, r + 5, 0, Mathf.Tau, 24, Colors.White, 2f);
        }
        foreach (var n in _vis)
        {
            if (!(Big(n) || (n.Type == "key" && Z > 1.6f) || (n.Type == "notable" && Z > 3.6f))) continue;
            var p = ToScreen(n); if (p.X < -40 || p.Y < -40 || p.X > Size.X + 40 || p.Y > Size.Y + 40) continue;
            float r = Rad(n); int fs = Big(n) ? 15 : n.Type == "key" ? 13 : 12;
            var col = Big(n) ? n.Col.Lightened(.35f) : n.Type == "key" ? new Color(1f, .85f, .5f) : new Color(.8f, .85f, .95f);
            if (shut != null && shut.Contains(n.Arm)) col = col.Darkened(.45f);
            float w = F.GetStringSize(n.Name, HorizontalAlignment.Left, -1, fs).X;
            var tl = new Vector2(Mathf.Clamp(p.X - w / 2, 4, Mathf.Max(4, Size.X - w - 4)), Mathf.Clamp(p.Y + r + 6, 4, Size.Y - fs - 8));
            DrawRect(new Rect2(tl - new Vector2(4, 1), new Vector2(w + 8, fs + 6)), new Color(0, 0, 0, .72f));
            DrawString(F, tl + new Vector2(0, fs), n.Name, HorizontalAlignment.Left, -1, fs, col);
        }
    }
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }   // slow pulse on available nodes
    int Pick(Vector2 pos) { int best = -1; float bd = 1e9f; foreach (var n in _vis) { float d = ToScreen(n).DistanceTo(pos); if (d < Rad(n) + 6 && d < bd) { bd = d; best = n.Id; } } return best; }
    void ZoomAt(Vector2 mouse, float factor)
    {
        float z2 = Mathf.Clamp(_zoom * factor, 0.7f, 9f * _k); var c = Size / 2; _pan = mouse - c - (mouse - c - _pan) * (z2 / _zoom); _zoom = z2; Save(); QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (_drag) { _pan += mm.Relative; Save(); QueueRedraw(); }
            int h = Pick(mm.Position);
            if (h != _hover) { _hover = h; _path = h >= 0 ? PathTo(h) : null; QueueRedraw(); }
            HoverAt?.Invoke(_hover, GetGlobalMousePosition());
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) ZoomAt(mb.Position, 1.15f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) ZoomAt(mb.Position, 1 / 1.15f);
            else if (mb.ButtonIndex == MouseButton.Middle) { _drag = mb.Pressed; }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                int id = Pick(mb.Position);
                if (id < 0) { _drag = true; return; }
                Take(id);
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _drag = false;
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed) { int id = Pick(mb.Position); if (id >= 0) Refund(id); }
        }
        else if (HasFocus()) PadGui(e);
    }
    /// <summary>Why an untaken node cannot be reached at all ("" = it can): an advanced path whose gate is still shut.</summary>
    public static string LockedWhy(TreeNode n)
    {
        if (TreeDb.PathOf(n) is not { } p || Game.D.Tree.Contains(p.Root)) return "";
        return TreeDb.PathOpen(p, Game.D.Tree, out var why) ? "" : why;
    }
    void Take(int id)
    {
        var d = Game.D; var path = PathTo(id);
        if (path == null || path.Count == 0)
        {
            if (TreeDb.Get(id) is { } ln && !d.Tree.Contains(id) && LockedWhy(ln) is { Length: > 0 } why) Ui.Note($"{TreeDb.PathOf(ln)!.Name} is locked. {why}.", new Color(1f, .6f, .5f));
            Sfx.Play("click"); return;
        }
        if (path.Count > d.SkillPts) { Ui.Note($"That path takes {path.Count} skill tree points; you have {d.SkillPts}", new Color(1f, .6f, .5f)); Sfx.Play("click"); return; }
        foreach (var pid in path) { var set = new HashSet<int>(d.Tree); var pn = TreeDb.Nodes[pid]; if (!TreeDb.CanAlloc(pn, set, d.SkillPts, _start)) break; d.Tree.Add(pid); d.SkillPts--; }
        Sfx.Play("level"); _path = null; PadKeep = id; Changed?.Invoke();
    }
    void Refund(int id)
    {
        var n = TreeDb.Nodes[id]; var set = new HashSet<int>(Game.D.Tree); if (!set.Contains(id)) return;
        string why = TreeDb.DeallocWhy(n, set, _start);
        if (why == "") { Game.D.Tree.Remove(id); Game.D.SkillPts++; Sfx.Play("click"); PadKeep = id; Changed?.Invoke(); }
        else Ui.Note(why, new Color(1f, .6f, .5f));
    }

    // ---- controller: a node cursor. D-pad / left stick step to the node in that direction (linked nodes first), A takes it (or the path
    // to it), Y / Triangle refunds, the right stick pans. The cursor survives the rebuild after taking a node (PadKeep).
    static int PadKeep = -1;
    public string PadCursor => _hover >= 0 ? "node" + _hover : "-";
    bool Shown(int id) => TreeDb.Get(id) is { } n && n.Layer == Layer;
    void PadSet(int id)
    {
        if (id >= 0 && !Shown(id)) id = HomeNode().Id;
        _hover = id; _path = id >= 0 ? PathTo(id) : null; QueueRedraw();
        if (id < 0) { HoverAt?.Invoke(-1, Vector2.Zero); return; }
        var p = ToScreen(TreeDb.Nodes[id]); var inner = new Rect2(Vector2.Zero, Size).Grow(-70);
        if (!inner.HasPoint(p)) { _pan += Size / 2 - p; Save(); p = ToScreen(TreeDb.Nodes[id]); }
        HoverAt?.Invoke(id, GlobalPosition + p + new Vector2(14, -10));
    }
    public void PadPan(Vector2 v) { _pan -= v * 700f; Save(); QueueRedraw(); }
    void PadGui(InputEvent e)
    {
        if (e.IsActionPressed("ui_accept") && !e.IsEcho()) { if (_hover >= 0) Take(_hover); AcceptEvent(); return; }
        if (e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Y }) { if (_hover >= 0) Refund(_hover); AcceptEvent(); return; }
        var dir = e.IsActionPressed("ui_left", true) ? Vector2.Left : e.IsActionPressed("ui_right", true) ? Vector2.Right : e.IsActionPressed("ui_up", true) ? Vector2.Up : e.IsActionPressed("ui_down", true) ? Vector2.Down : Vector2.Zero;
        if (dir == Vector2.Zero) return;
        if (_hover < 0) { AcceptEvent(); PadSet(HomeNode().Id); return; }
        var cur = TreeDb.Nodes[_hover]; var p0 = ToScreen(cur); int best = -1; float bs = 1e9f;
        foreach (var n in _vis)
        {
            if (n.Id == cur.Id) continue; var dv = ToScreen(n) - p0; float along = dv.Dot(dir); if (along < 3f) continue;
            float perp = Mathf.Abs(dv.Cross(dir)); if (perp > along * 1.3f) continue;
            float s = (along + perp * 2f) * (cur.Adj.Contains(n.Id) ? .45f : 1f);
            if (s < bs) { bs = s; best = n.Id; }
        }
        if (best >= 0) { AcceptEvent(); Sfx.UiHover(); PadSet(best); }   // nothing that way: the focus leaves the tree for the toolbar
    }
    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter) Callable.From(() => { if (IsInstanceValid(this) && HasFocus()) PadSet(PadKeep >= 0 && Shown(PadKeep) ? PadKeep : _hover >= 0 ? _hover : HomeNode().Id); }).CallDeferred();
        else if (what == NotificationFocusExit) { _hover = -1; _path = null; HoverAt?.Invoke(-1, Vector2.Zero); QueueRedraw(); }
    }
}
