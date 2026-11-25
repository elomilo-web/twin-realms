using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>World map with discovered points of interest.</summary>
public partial class MapView : Control
{
    public Action<Poi>? Travel;
    // zoom and pan: the world map keeps them while the game runs; a dungeon floor opens centred on you
    static float _zoomW = 1f; static Vector2 _panW;
    float _zoomD = 1.6f; Vector2 _panD; bool _dInit, _drag; float _moved;
    bool Dun => Dungeon.Active;
    float Zoom { get => Dun ? _zoomD : _zoomW; set { if (Dun) _zoomD = value; else _zoomW = value; } }
    Vector2 Pan { get => Dun ? _panD : _panW; set { if (Dun) _panD = value; else _panW = value; } }
    static Vector2 UV(float x, float z) => Dungeon.Active ? new Vector2((x - Dungeon.Origin.X) / (Dungeon.GW * Dungeon.Tile), (z - Dungeon.Origin.Z) / (Dungeon.GH * Dungeon.Tile)) : World.MapUV(x, z);
    Vector2 FromUV(Vector2 uv) => Size / 2 + Pan + (uv - new Vector2(.5f, .5f)) * Size * Zoom;
    Vector2 W2S(float x, float z) => FromUV(UV(x, z));
    Font F => ThemeDB.FallbackFont;
    static readonly Color Ink = new(.07f, .045f, .03f), InkSoft = new(.07f, .045f, .03f, .55f), RuneGold = new(.9f, .72f, .3f);
    const string ParchShader = @"shader_type canvas_item;
float sea(vec4 c){ return step(c.r + .12, c.b) * step(c.g + .03, c.b); }
float hh(vec2 p){ return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
  vec4 c = texture(TEXTURE, UV); vec2 w = max(fwidth(UV), vec2(0.00001)) * 2.0;
  float s = sea(c); float n = hh(floor(FRAGCOORD.xy / 2.0)) * .05;
  vec3 col;
  if (s > .5) {
    col = vec3(.11, .17, .19) + n * .4;
    float wv = sin(FRAGCOORD.x * .09 + sin(FRAGCOORD.y * .21) * 1.6) + FRAGCOORD.y * .35;
    if (fract((FRAGCOORD.x + FRAGCOORD.y) * .11) < .09) col *= .72;
    if (fract(wv * .2) < .03) col += vec3(.05, .07, .07);
  } else {
    float lum = dot(c.rgb, vec3(.3, .59, .11)); float mn = min(c.r, min(c.g, c.b));
    if (mn > .7) col = vec3(.80, .75, .62);
    else if (c.r > c.g * 1.3 && c.r > c.b * 1.3) col = vec3(.36, .17, .1) * (.8 + lum * .6);
    else if (c.r > c.b + .22 && c.g > c.b + .18) col = vec3(.66, .5, .25) * (.85 + lum * .3);
    else if (c.g > c.r * 1.03 && c.g >= c.b) col = mix(vec3(.26, .24, .12), vec3(.46, .38, .2), clamp(lum * 1.6, 0., 1.));
    else col = vec3(.45, .36, .25) * (.7 + lum * .6);
    col += n - .02;
    float edge = max(max(sea(texture(TEXTURE, UV + vec2(w.x, 0))), sea(texture(TEXTURE, UV - vec2(w.x, 0)))), max(sea(texture(TEXTURE, UV + vec2(0, w.y))), sea(texture(TEXTURE, UV - vec2(0, w.y)))));
    col = mix(col, vec3(.07, .045, .03), edge);
  }
  COLOR = vec4(col, 1.0);
}";
    TextureRect? _base;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; ClipContents = true;
        _base = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Texture = World.MapTex, Material = new ShaderMaterial { Shader = new Shader { Code = ParchShader } } };
        AddChild(_base);
    }
    void Label(Vector2 at, string s, int size, Color col) { DrawStringOutline(F, at, s, HorizontalAlignment.Left, -1, size, 4, new Color(.05f, .03f, .02f, .8f)); DrawString(F, at, s, HorizontalAlignment.Left, -1, size, col); }
    // every POI has a wayshrine (Wayshrine.cs); a discovered one is a lit travel point: gold rune
    static bool IsShrine(Poi p) => Game.D.Disc.Contains(p.Id);
    void DrawGlyph(Vector2 s, Poi p, bool known)
    {
        if (!known) { DrawArc(s, 3, 0, Mathf.Tau, 10, InkSoft with { A = .28f }, 1f); return; }
        if (IsShrine(p)) { DrawArc(s, 6, 0, Mathf.Tau, 20, RuneGold, 1.8f); DrawLine(s + new Vector2(0, -9), s + new Vector2(0, 9), RuneGold, 1.8f); DrawCircle(s, 1.6f, RuneGold); return; }
        if (p.Dun != null) { DrawColoredPolygon(new[] { s + new Vector2(-5, 4), s + new Vector2(0, -5), s + new Vector2(5, 4) }, Ink); DrawRect(new Rect2(s + new Vector2(-1.5f, 0), new Vector2(3, 4)), new Color(.8f, .7f, .5f)); return; }
        DrawRect(new Rect2(s - new Vector2(4, 4), new Vector2(8, 8)), Ink); DrawRect(new Rect2(s - new Vector2(2, 2), new Vector2(4, 4)), new Color(.8f, .7f, .5f));
    }
    public override void _Draw()
    {
        var me = Player.Instance;
        if (Dun && !_dInit && Size.X > 0 && me != null) { _dInit = true; _panD = -(UV(me.GlobalPosition.X, me.GlobalPosition.Z) - new Vector2(.5f, .5f)) * Size * _zoomD; ClampPan(); }
        if (Dun) DrawRect(new Rect2(Vector2.Zero, Size), new Color(.03f, .035f, .05f));   // the parchment base is a child drawn behind this control
        if (_base != null) { _base.Visible = !Dun; _base.Position = FromUV(Vector2.Zero); _base.Size = Size * Zoom; if (_base.Texture == null) _base.Texture = World.MapTex; }
        if (Dun)
        {
            // the floor as far as you have explored it
            float cell = Size.X * Zoom / Dungeon.GW;
            for (int z = 0; z < Dungeon.GH; z++) for (int x = 0; x < Dungeon.GW; x++)
            {
                if (!Dungeon.Seen(x, z)) continue; bool fl = Dungeon.Floor(x, z); if (!fl && !Dungeon.Wall(x, z)) continue;
                var c = Dungeon.CellCentre(x, z); var s = W2S(c.X, c.Y);
                DrawRect(new Rect2(s - new Vector2(cell, cell) / 2, new Vector2(cell + .5f, cell + .5f)), fl ? new Color(.42f, .45f, .5f) : new Color(.16f, .17f, .22f));
            }
            foreach (var (pos, down) in Dungeon.Pads)
            {
                int px = Mathf.FloorToInt((pos.X - Dungeon.Origin.X) / Dungeon.Tile), pz = Mathf.FloorToInt((pos.Z - Dungeon.Origin.Z) / Dungeon.Tile); if (!Dungeon.Seen(px, pz)) continue;
                var s = W2S(pos.X, pos.Z); DrawCircle(s, 7, down ? new Color(1f, .75f, .38f) : new Color(.5f, .88f, 1f)); Label(s + new Vector2(10, 5), down ? "Way down" : "Exit", 14, Colors.White);
            }
        }
        else
        {
            foreach (var rd in World.Roads) for (int i = 0; i < rd.Length - 1; i++) DrawDashedLine(W2S(rd[i].X, rd[i].Y), W2S(rd[i + 1].X, rd[i + 1].Y), InkSoft, 1.5f, 6f);
            foreach (var p in World.Pois)
            {
                bool known = Game.D.Disc.Contains(p.Id); var s = W2S(p.X, p.Z);
                DrawGlyph(s, p, known);
                if (known) Label(s + new Vector2(10, 5), p.Name, 15, new Color(.93f, .88f, .76f));
            }
            // soft vignette at the map edges
            for (int i = 0; i < 14; i++)
            {
                float t = i * 4f, a = .38f * (1f - i / 14f) * (1f - i / 14f); var vc = new Color(.03f, .02f, .01f, a);
                DrawRect(new Rect2(0, t, Size.X, 4), vc); DrawRect(new Rect2(0, Size.Y - t - 4, Size.X, 4), vc);
                DrawRect(new Rect2(t, 0, 4, Size.Y), vc); DrawRect(new Rect2(Size.X - t - 4, 0, 4, Size.Y), vc);
            }
        }
        // party members, then you on top
        foreach (var fr in MapMarks.Friends()) { var s = W2S(fr.Pos.X, fr.Pos.Z); MapMarks.Dot(this, s, 6, fr.Party ? MapMarks.PartyCol : MapMarks.OtherCol); Label(s + new Vector2(9, -6), fr.Name, 14, fr.Party ? MapMarks.PartyCol : MapMarks.OtherCol); }
        if (me != null && !Arenas.Active) { var pp = me.GlobalPosition; var ps = W2S(pp.X, pp.Z); var fw = me.GlobalTransform.Basis * Vector3.Forward; var dir = new Vector2(fw.X, fw.Z).Normalized(); if (dir == Vector2.Zero) dir = Vector2.Up; var side = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon(new[] { ps + dir * 11, ps - dir * 7 + side * 6, ps - dir * 3, ps - dir * 7 - side * 6 }, new Color(.55f, .06f, .06f)); DrawPolyline(new[] { ps + dir * 11, ps - dir * 7 + side * 6, ps - dir * 3, ps - dir * 7 - side * 6, ps + dir * 11 }, Ink, 1.5f); }
    }
    void ClampPan() { var lim = Size * Zoom / 2; Pan = new Vector2(Mathf.Clamp(Pan.X, -lim.X, lim.X), Mathf.Clamp(Pan.Y, -lim.Y, lim.Y)); }
    void ZoomAt(Vector2 m, float k)
    {
        float z2 = Mathf.Clamp(Zoom * k, 1f, 8f); var c = Size / 2; Pan = m - c - (m - c - Pan) * (z2 / Zoom); Zoom = z2; ClampPan(); QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && _drag) { Pan += mm.Relative; _moved += mm.Relative.Length(); ClampPan(); QueueRedraw(); AcceptEvent(); return; }
        if (e is not InputEventMouseButton mb) return;
        AcceptEvent();   // the wheel zooms the map, not the window around it
        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) ZoomAt(mb.Position, 1.2f);
        else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) ZoomAt(mb.Position, 1 / 1.2f);
        else if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
        {
            if (mb.Pressed) { _drag = true; _moved = 0; return; }
            _drag = false;
            // a click that did not drag: fast travel to a discovered place
            if (mb.ButtonIndex == MouseButton.Left && _moved < 5 && !Dun)
                foreach (var p in World.Pois) if (Game.D.Disc.Contains(p.Id) && W2S(p.X, p.Z).DistanceTo(mb.Position) < 14) { Travel?.Invoke(p); return; }
        }
    }
    public override void _Process(double delta) { QueueRedraw(); }   // friends move
}
