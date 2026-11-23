using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero and the block world: build mode (Inputs.BuildMode, N) with a crosshair target, hold attack to dig (cracks grow, time from the
/// block's hardness and the weapon as a tool), heavy attack places the chosen block (mouse wheel picks, Inputs.BlockRotate turns logs)
/// with a ghost preview that turns red where it cannot go; swimming in water blocks, burning in lava, sliding on ice and frozen blocks.
/// </summary>
public partial class Player
{
    public static bool BuildMode;
    /// <summary>Tests aim here instead of at the crosshair (block x, y, z and the face normal).</summary>
    public static (Vector3I b, Vector3I n)? ForcedTarget;
    (Vector3I b, Vector3I n)? _bTarget; float _digT; Vector3I _digAt; float _placeCd; byte _rot;
    string _sel = ""; MeshInstance3D? _ghost, _crack; StandardMaterial3D? _ghostMat, _crackMat; ImageTexture[]? _cracks;
    float _fluidSpeed = 1f, _accelMul = 1f; float _lavaT;
    public string BlockSelected => _sel;

    /// <summary>Water, lava and ice before this frame's movement: speed and acceleration factors, swimming, lava burns.</summary>
    void BlockMove(float dt, ref Vector3 v)
    {
        _fluidSpeed = 1f; _accelMul = 1f;
        if (!BlockWorld.Enabled) return;
        string body = BlockWorld.FluidAt(GlobalPosition + Vector3.Up * .9f), head = BlockWorld.FluidAt(GlobalPosition + Vector3.Up * 1.5f);
        if (body != "")
        {
            // swimming: sink slowly, Jump rises; slower on the move
            v.Y += _gravity * dt * .85f;
            bool up = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(Inputs.Jump);
            v.Y = Mathf.MoveToward(v.Y, up ? Blocks.F("fluids.swim.rise", 3.2f) : -Blocks.F("fluids.swim.sink", 1.6f), 9f * dt);
            if (up && head == "" && IsOnWall()) v.Y = Mathf.Max(v.Y, JumpVelocity * .8f);   // climb out onto the bank
            _fluidSpeed = Blocks.F("fluids.swim.speed", .6f);
            if (body == "lava")
            {
                _lavaT -= dt; _fluidSpeed *= .5f;
                if (_lavaT <= 0) { _lavaT = .5f; TakeHit(new HitInfo { Damage = Blocks.F("fluids.lavaDps", 18) * .5f * (1 - Mathf.Clamp(Game.TS["res_fire"], -100, 75) / 100f), From = GlobalPosition + Vector3.Down, NoBlock = true, Type = "fire", Fire = true }); }
            }
        }
        if (IsOnFloor() && BlockWorld.SlipperyAt(GlobalPosition)) _accelMul = .12f;
    }

    /// <summary>Build mode input (replaces attacking while it is on). Called every physics frame.</summary>
    void BlockInput(float dt, bool captured)
    {
        if (captured && Input.IsActionJustPressed(Inputs.BuildMode)) { BuildMode = !BuildMode; Ui.Note(BuildMode ? "Build mode: hold attack to dig, heavy attack to place, mouse wheel to choose" : "Build mode off", UiTheme.Highlight); }
        BlockHud();
        if (!BuildMode || !BlockWorld.Enabled || BlockWorld.I!.Area == null) { HideBlockFx(); _digT = 0; return; }
        if (captured && Input.IsActionJustPressed(Inputs.BlockRotate)) { _rot = (byte)((_rot + 1) % 3); Ui.Note("Placing along " + (_rot == 0 ? "up" : _rot == 1 ? "east-west" : "north-south"), Ui.Dim); }
        if (_sel == "" || Game.D.Mats.GetValueOrDefault(Blocks.Item(_sel)) <= 0) _sel = Owned().FirstOrDefault() ?? "";
        _placeCd = Mathf.Max(0, _placeCd - dt);
        _bTarget = ForcedTarget ?? Aim();
        if (_bTarget is not { } t) { HideBlockFx(); _digT = 0; return; }
        var a = BlockWorld.I.Area!;
        // dig: hold attack
        bool dig = (captured || ForcedTarget != null) && Input.IsActionPressed(Inputs.Attack);
        byte id = a.Get(t.b.X, t.b.Y, t.b.Z);
        if (dig && id != 0 && id != 255)
        {
            if (t.b != _digAt) { _digAt = t.b; _digT = 0; }
            var def = Blocks.Def(id); float need = Blocks.DigTime(def, Game.D.WType()) * (BlockWorld.I.Frozen(t.b.X, t.b.Y, t.b.Z) ? .5f : 1f);
            if (float.IsInfinity(need) || a.Gen.Protected(t.b.X, t.b.Y, t.b.Z)) { if (_digT == 0) Ui.Note(def.Name + " cannot be broken", Ui.Dim); _digT = .001f; }
            else
            {
                _digT += dt; _modelYaw = Rig?.Yaw ?? _modelYaw;
                if (_digT >= need) { BlockFx.Dig(t.b.X, t.b.Y, t.b.Z); _digT = 0; }
                ShowCrack(t.b, Mathf.Clamp(_digT / need, 0, 1));
            }
        }
        else { _digT = 0; if (_crack != null) _crack.Visible = false; }
        // place: heavy attack
        var at = t.b + t.n; string? why = _sel == "" ? "No blocks in your pack" : BlockFx.CanPlace(at.X, at.Y, at.Z);
        ShowGhost(at, why == null);
        if ((captured || ForcedTarget != null) && Input.IsActionJustPressed(Inputs.Heavy) && _placeCd <= 0)
        {
            _placeCd = Blocks.F("world.placeCooldown", .18f);
            if (why != null) Ui.Note(why, new Color(1, .6f, .5f));
            else { var bid = Blocks.Id(_sel); if (BlockFx.Place(at.X, at.Y, at.Z, bid, _rot)) { var m = Game.D.Mats; string k = Blocks.Item(_sel); m[k] = m.GetValueOrDefault(k) - 1; if (m[k] <= 0) m.Remove(k); Game.Notify(); } }
        }
    }
    /// <summary>Mouse wheel in build mode picks the next block owned (Ui forwards wheel events here).</summary>
    public void CycleBlock(int dir)
    {
        var l = Owned().ToList(); if (l.Count == 0) { _sel = ""; return; }
        int i = l.IndexOf(_sel); i = ((i < 0 ? 0 : i + dir) % l.Count + l.Count) % l.Count; _sel = l[i];
    }
    static IEnumerable<string> Owned()
    {
        var order = new List<string>(); if (Blocks.Node("build.order") is { } o) foreach (var x in o.EnumerateArray()) order.Add(x.GetString() ?? "");
        foreach (var d in Blocks.Defs) if (d.Id != 0 && !d.IsFluid && !order.Contains(d.Key)) order.Add(d.Key);
        foreach (var k in order) if (Game.D.Mats.GetValueOrDefault(Blocks.Item(k)) > 0) yield return k;
    }

    /// <summary>The block under the crosshair within reach, by stepping through the block grid (no physics ray: water does not stop it).</summary>
    (Vector3I b, Vector3I n)? Aim()
    {
        var cam = GetViewport().GetCamera3D(); var a = BlockWorld.I?.Area; if (cam == null || a == null) return null;
        var o = cam.GlobalPosition; var dir = -cam.GlobalTransform.Basis.Z;
        float reach = Blocks.F("world.reach", 6f), max = (o - (GlobalPosition + Vector3.Up * 1.5f)).Length() + reach;
        var cell = new Vector3I(Mathf.FloorToInt(o.X), Mathf.FloorToInt(o.Y), Mathf.FloorToInt(o.Z));
        var step = new Vector3I(Math.Sign(dir.X), Math.Sign(dir.Y), Math.Sign(dir.Z));
        float T(float p, float d, int c, int s) => d == 0 ? float.PositiveInfinity : ((s > 0 ? c + 1 : c) - p) / d;
        float tx = T(o.X, dir.X, cell.X, step.X), ty = T(o.Y, dir.Y, cell.Y, step.Y), tz = T(o.Z, dir.Z, cell.Z, step.Z);
        float dx = step.X == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.X), dy = step.Y == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Y), dz = step.Z == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Z);
        var n = Vector3I.Zero; float t = 0;
        for (int i = 0; i < 128 && t <= max; i++)
        {
            byte id = a.Get(cell.X, cell.Y, cell.Z);
            if (id != 0 && id != 255 && !Blocks.Def(id).IsFluid)
            {
                var hit = o + dir * t; if ((hit - (GlobalPosition + Vector3.Up)).Length() > reach + 1f) return null;
                return (cell, n);
            }
            if (tx < ty && tx < tz) { t = tx; tx += dx; cell.X += step.X; n = new Vector3I(-step.X, 0, 0); }
            else if (ty < tz) { t = ty; ty += dy; cell.Y += step.Y; n = new Vector3I(0, -step.Y, 0); }
            else { t = tz; tz += dz; cell.Z += step.Z; n = new Vector3I(0, 0, -step.Z); }
        }
        return null;
    }

    Label? _bHud; CanvasLayer? _bLayer;
    /// <summary>The build-mode line at the bottom of the screen: the chosen block, how many, the keys.</summary>
    void BlockHud()
    {
        if (_bHud == null || !IsInstanceValid(_bHud))
        {
            _bLayer = new CanvasLayer { Layer = 5 }; AddChild(_bLayer);
            _bHud = new Label { HorizontalAlignment = HorizontalAlignment.Center, AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -150, OffsetBottom = -122 };
            _bHud.AddThemeFontSizeOverride("font_size", 17); _bHud.AddThemeColorOverride("font_outline_color", Colors.Black); _bHud.AddThemeConstantOverride("outline_size", 6);
            _bLayer.AddChild(_bHud);
        }
        _bHud.Visible = BuildMode && BlockWorld.Enabled;
        if (!_bHud.Visible) return;
        string blk = _sel == "" ? "no blocks in your pack (dig some)" : $"{Blocks.Def(Blocks.Id(_sel)).Name} x{Game.D.Mats.GetValueOrDefault(Blocks.Item(_sel))}";
        _bHud.Text = $"BUILD MODE   {blk}   ·   hold {Inputs.KeyOf(Inputs.Attack)} dig   ·   {Inputs.KeyOf(Inputs.Heavy)} place   ·   wheel choose   ·   {Inputs.KeyOf(Inputs.BlockRotate)} rotate   ·   {Inputs.KeyOf(Inputs.BuildMode)} leave";
        _bHud.AddThemeColorOverride("font_color", _sel == "" ? new Color(1, .75f, .6f) : UiTheme.Highlight);
    }

    // ---------------------------------------------------------------- looks: the ghost block and the cracks
    void ShowGhost(Vector3I at, bool ok)
    {
        if (_ghost == null || !IsInstanceValid(_ghost))
        {
            _ghostMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = false };
            _ghost = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.01f, 1.01f, 1.01f) }, MaterialOverride = _ghostMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
            AddChild(_ghost);
        }
        _ghost.Visible = true; _ghost.GlobalPosition = new Vector3(at.X + .5f, at.Y + .5f, at.Z + .5f);
        var col = _sel != "" ? Blocks.Tint(Blocks.Def(Blocks.Id(_sel))) : Colors.White;
        _ghostMat!.AlbedoColor = ok ? new Color(col.Lerp(Colors.White, .4f), .35f) : new Color(1f, .25f, .2f, .35f);
    }
    void ShowCrack(Vector3I b, float k)
    {
        _cracks ??= MakeCracks();
        if (_crack == null || !IsInstanceValid(_crack))
        {
            _crackMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0, 0, 0, .85f), Uv1Triplanar = true, Uv1WorldTriplanar = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
            _crack = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.02f, 1.02f, 1.02f) }, MaterialOverride = _crackMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
            AddChild(_crack);
        }
        _crack.Visible = k > .02f; _crack.GlobalPosition = new Vector3(b.X + .5f, b.Y + .5f, b.Z + .5f);
        _crackMat!.AlbedoTexture = _cracks[Mathf.Clamp((int)(k * _cracks.Length), 0, _cracks.Length - 1)];
    }
    void HideBlockFx() { if (_ghost != null && IsInstanceValid(_ghost)) _ghost.Visible = false; if (_crack != null && IsInstanceValid(_crack)) _crack.Visible = false; }
    /// <summary>Five crack stages, drawn once: random walks from the centre, more and longer each stage.</summary>
    static ImageTexture[] MakeCracks()
    {
        var res = new ImageTexture[5]; var rnd = new Random(7); const int N = 32;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8); img.Fill(new Color(0, 0, 0, 0));
        for (int s = 0; s < 5; s++)
        {
            for (int line = 0; line < 3; line++)
            {
                float x = N / 2f, y = N / 2f, ang = (float)rnd.NextDouble() * Mathf.Tau;
                for (int k = 0; k < 6 + s * 3; k++) { ang += (float)(rnd.NextDouble() - .5) * 1.2f; x += Mathf.Cos(ang) * 1.3f; y += Mathf.Sin(ang) * 1.3f; int ix = ((int)x % N + N) % N, iy = ((int)y % N + N) % N; img.SetPixel(ix, iy, new Color(0, 0, 0, 1)); }
            }
            res[s] = ImageTexture.CreateFromImage((Image)img.Duplicate());
        }
        return res;
    }
}
