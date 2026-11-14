using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A character built from the Quaternius universal rig (data/avatar.json): a plain base body (male or female, skin coloured
/// by race), hair, and every equipped piece as a separate asset bound to the same skeleton, so gear moves with the body.
/// Weapons and shields are separate models held in the hand bones; items without a model yet fall back to procedural shapes.
/// Trinkets are never shown. Animations come from the Universal Animation Libraries, merged once per body.
/// The model faces -Z like the rest of the game.
/// </summary>
public partial class Avatar : Node3D
{
    // ------------------------------------------------------------------ data
    static JsonDocument? _doc;
    static JsonElement Cfg { get { _doc ??= JsonUtil.Load("res://data/avatar.json"); return _doc.RootElement; } }
    static string S(JsonElement e, string k, string d = "") => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? d : d;
    static List<string> L(JsonElement e, string k) { var l = new List<string>(); if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array) foreach (var x in v.EnumerateArray()) l.Add(x.GetString() ?? ""); return l; }
    const string OutfitDir = "res://assets/models/outfit/", HairDir = "res://assets/models/hair/", WeaponDir = "res://assets/models/weapons/";

    public Node3D Body = null!; public bool Female; public Appearance Look = new();
    Skeleton3D _sk = null!; AnimationPlayer _ap = null!; string _sex = "male";
    readonly Dictionary<string, List<Node>> _slot = new();
    readonly Dictionary<string, BoneAttachment3D> _att = new();
    readonly List<Node> _hair = new();
    public Node3D? BladeBase, BladeTip; public bool HasModel;

    static bool? _ok;
    /// <summary>True when the character models are installed (otherwise callers keep their procedural bodies).</summary>
    public static bool Available => _ok ??= ResourceLoader.Exists(S(Cfg.GetProperty("bodies"), "male"));

    // ------------------------------------------------------------------ body
    public void Build(Appearance ap, string seed)
    {
        Look = ap; Female = ap.Female; _sex = Female ? "female" : "male";
        var ps = GD.Load<PackedScene>(S(Cfg.GetProperty("bodies"), _sex));
        Body = ps.Instantiate<Node3D>(); Body.Name = "Body"; AddChild(Body);
        Body.Rotation = new Vector3(0, Mathf.Pi, 0);   // glTF faces +Z, the game faces -Z
        Body.Scale = new Vector3(ap.Bulk, ap.Height, ap.Bulk);
        _sk = (Skeleton3D)Body.FindChild("Skeleton3D", true, false)!;
        foreach (var c in _sk.GetChildren())
            if (c is MeshInstance3D mi)
            {
                if (mi.Name.ToString().Contains("Eye") && !mi.Name.ToString().Contains("brow")) { if (ap.GlowEyes) Tint(mi, null, ap.Eyes, 2.5f); continue; }
                if (mi.Name.ToString().Contains("brow")) { Tint(mi, null, ap.Hair.Darkened(.2f), 0, true, 0, Genshin.Kind.Hair); continue; }
                Tint(mi, null, ap.Skin, 0, true, 0, Genshin.Kind.Skin);   // flat skin colour reads best under the toon shader and follows the race
            }
        // hair, picked from the list by the seed so a hero always keeps the same style
        var hl = L(Cfg.GetProperty("hair"), _sex); if (hl.Count > 0)
        {
            int h = Math.Abs(seed.GetHashCode()) % hl.Count;
            _hair.AddRange(AddPart(HairDir + hl[h] + ".gltf", "hair", ap.Hair, true));
            if (ap.Beard && !Female) _hair.AddRange(AddPart(HairDir + S(Cfg.GetProperty("hair"), "beard") + ".gltf", "hair", ap.Hair, true));
        }
        if (Cfg.TryGetProperty("raceFeatures", out var rf) && rf.ValueKind == JsonValueKind.True) RaceFeatures(ap);   // off: no cones for ears, tusks or horns
        _ap = new AnimationPlayer { Name = "Anim" }; Body.AddChild(_ap); _ap.RootNode = new NodePath("..");
        _ap.AddAnimationLibrary("", Library(_sex, _sk));
        HasModel = true; Play("idle");
    }

    /// <summary>Copies a material and recolours it. flat = drop the texture and use the colour as is. grow pushes clothing out along its normals so the body never shows through.</summary>
    static void Tint(MeshInstance3D mi, Color? mul, Color? set, float glow = 0, bool flat = false, float grow = 0, Genshin.Kind kind = Genshin.Kind.Cloth, bool vertexColor = false)
    {
        for (int s = 0; s < (mi.Mesh?.GetSurfaceCount() ?? 0); s++)
        {
            if (mi.GetActiveMaterial(s) is not BaseMaterial3D b) continue;
            var d = (BaseMaterial3D)b.Duplicate();
            if (vertexColor) d.VertexColorUseAsAlbedo = true;   // kit pieces are coloured per vertex only
            if (flat && set != null) { d.AlbedoTexture = null; d.AlbedoColor = set.Value; }
            else if (set != null) d.AlbedoColor = set.Value;
            if (mul != null) d.AlbedoColor = d.AlbedoColor * mul.Value;
            if (glow > 0) { d.EmissionEnabled = true; d.Emission = set ?? d.AlbedoColor; d.EmissionEnergyMultiplier = glow; }
            d.NormalEnabled = false;   // the toon look does not use normal maps
            if (grow > 0) { d.Grow = true; d.GrowAmount = grow; }
            Toon.Apply(d); mi.SetSurfaceOverrideMaterial(s, Genshin.From(d, kind));   // characters get the Genshin-style shader (Genshin.cs)
        }
    }

    /// <summary>Loads an outfit/hair glTF and moves its skinned meshes onto our skeleton (bound by bone name).</summary>
    List<Node> AddPart(string path, string slot, Color? tint = null, bool flat = false)
    {
        var res = new List<Node>();
        if (!ResourceLoader.Exists(path)) { GD.PrintErr("[AVATAR] missing part " + path); return res; }
        var inst = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        var psk = (Skeleton3D?)inst.FindChild("Skeleton3D", true, false);
        if (_sk == null) { GD.PrintErr("[AVATAR] AddPart before Build: " + path); inst.QueueFree(); return res; }
        if (psk != null)
            foreach (var c in psk.GetChildren())
            {
                if (c is not MeshInstance3D mi) continue;
                var skin = mi.Skin?.Duplicate() as Skin;
                if (skin != null)
                    for (int i = 0; i < skin.GetBindCount(); i++)
                    {
                        string bn = skin.GetBindName(i)?.ToString() ?? ""; int bb = skin.GetBindBone(i);
                        if (bn == "" && bb >= 0 && bb < psk.GetBoneCount()) skin.SetBindName(i, psk.GetBoneName(bb));   // bind by name, not index
                    }
                psk.RemoveChild(mi); mi.Owner = null; _sk.AddChild(mi); mi.Skeleton = new NodePath(".."); mi.Skin = skin;
                float grow = slot == "hair" ? 0f : .006f;
                var kind = slot == "hair" ? Genshin.Kind.Hair : Genshin.Kind.Cloth;
                if (tint != null) Tint(mi, flat ? null : tint, flat ? tint : null, 0, flat, grow, kind); else Tint(mi, null, null, 0, false, grow, kind);
                res.Add(mi);
            }
        inst.QueueFree();
        if (!_slot.TryGetValue(slot, out var l)) _slot[slot] = l = new List<Node>(); l.AddRange(res);
        return res;
    }

    void Clear(string slot) { if (_slot.TryGetValue(slot, out var l)) { foreach (var n in l) if (IsInstanceValid(n)) { n.GetParent()?.RemoveChild(n); n.QueueFree(); } l.Clear(); } }

    BoneAttachment3D Att(string bone)
    {
        if (_sk == null) throw new InvalidOperationException("Avatar.Build must run before gear is added");
        if (_att.TryGetValue(bone, out var a)) return a;
        a = new BoneAttachment3D { Name = "At_" + bone, BoneName = bone }; _sk.AddChild(a); _att[bone] = a; return a;
    }
    /// <summary>A holder on a bone whose axes match the model (Y up, +Z forward in glTF space) at rest, offset in model space.</summary>
    Node3D Holder(string bone, Vector3 posModel, Vector3 rotDeg, string slot)
    {
        var at = Att(bone);
        var h = new Node3D { Name = "Hold_" + slot, Transform = HoldXf(_sk, bone, posModel, rotDeg) };
        h.SetMeta("bone", bone); at.AddChild(h);
        if (!_slot.TryGetValue(slot, out var l)) _slot[slot] = l = new List<Node>(); l.Add(h);
        return h;
    }
    /// <summary>The holder's transform relative to its bone for a position and rotation given in model space at rest (see data/avatar.json "hold").</summary>
    public static Transform3D HoldXf(Skeleton3D sk, string bone, Vector3 posModel, Vector3 rotDeg)
    {
        int bi = sk.FindBone(bone);
        return HoldXf(bi >= 0 ? sk.GetBoneGlobalRest(bi).Basis.Orthonormalized() : Basis.Identity, posModel, rotDeg);
    }
    /// <summary>Same, from the bone's rest basis (skeleton space, orthonormalized).</summary>
    public static Transform3D HoldXf(Basis rest, Vector3 posModel, Vector3 rotDeg)
    {
        var want = Basis.FromEuler(rotDeg * (Mathf.Pi / 180f));
        return new Transform3D(rest.Inverse() * want, rest.Inverse() * posModel);
    }

    // ------------------------------------------------------------------ for test bots (FitBot)
    public Skeleton3D? Skel => _sk;
    /// <summary>World position of a hand (where spells and shots leave from), or null before the body is built.</summary>
    public Vector3? HandPos(bool left)
    {
        if (_sk == null) return null; int b = _sk.FindBone(left ? "hand_l" : "hand_r"); if (b < 0) return null;
        return _sk.GlobalTransform * _sk.GetBoneGlobalPose(b).Origin;
    }
    /// <summary>The base body mesh (set once the avatar has been dressed).</summary>
    public MeshInstance3D? BodyMesh => _bodyMi;
    public IReadOnlyList<Node> SlotNodes(string slot) => _slot.TryGetValue(slot, out var l) ? l : Array.Empty<Node>();
    public float ClipLength(string clip) => _ap != null && _ap.HasAnimation(clip) ? (float)_ap.GetAnimation(clip).Length : 0f;
    /// <summary>Holds an action's clip still at a fraction of its length (0..1), so a pose can be measured or photographed.</summary>
    public void PoseAt(string key, float frac, int step = 0)
    {
        if (_ap == null) return; string n = Clip(key, step); if (!_ap.HasAnimation(n)) n = Clip("idle");
        if (!_ap.HasAnimation(n)) return;
        _ap.Play(n, 0); _ap.Seek(_ap.GetAnimation(n).Length * Mathf.Clamp(frac, 0, 1), true); _ap.Pause(); Cur = n;
    }

    /// <summary>
    /// Rigid Twin Realms kit pieces (assets/models/kit/&lt;model&gt;.glb) on bones, from a gear rule's "kit" list:
    /// { "model": "armor/helm_knight", "bone": "Head", "pos": [x,y,z], "rot": [deg], "scale": s or [x,y,z] }. pos/rot are in model space at rest, like "hold".
    /// </summary>
    List<Node> KitPieces(JsonElement rule, string slot)
    {
        var made = new List<Node>();
        if (!rule.TryGetProperty("kit", out var list) || list.ValueKind != JsonValueKind.Array) return made;
        foreach (var k in list.EnumerateArray())
        {
            string path = "res://assets/models/kit/" + S(k, "model") + ".glb", bone = S(k, "bone", "Head");
            if (!ResourceLoader.Exists(path)) { GD.PrintErr("[AVATAR] missing kit piece " + path); continue; }
            if (_sk.FindBone(bone) < 0) { GD.PrintErr("[AVATAR] no bone " + bone + " for " + path); continue; }
            var h = Holder(bone, V(k, "pos", Vector3.Zero), V(k, "rot", Vector3.Zero), slot);
            var sc = k.TryGetProperty("scale", out var sv) && sv.ValueKind == JsonValueKind.Number ? Vector3.One * sv.GetSingle() : V(k, "scale", Vector3.One);
            var inst = GD.Load<PackedScene>(path).Instantiate<Node3D>(); inst.Scale = sc; h.AddChild(inst);
            foreach (var n in inst.FindChildren("*", "MeshInstance3D", true, false)) if (n is MeshInstance3D mi) Tint(mi, null, null, vertexColor: true);
            made.Add(h);
        }
        return made;
    }
    static Vector3 V(JsonElement e, string k, Vector3 d) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : d;

    // ------------------------------------------------------------------ hide the body under clothes
    // The free outfits were made for a slimmer body than the free base body, so skin would poke through tight clothes.
    // After dressing, body triangles that lie under any clothing vertex are removed (cached per outfit), like most games do.
    MeshInstance3D? _bodyMi; ArrayMesh? _bodyFull;
    static readonly Dictionary<string, ArrayMesh> _masked = new();
    void MaskBody()
    {
        if (_bodyMi == null) foreach (var c in _sk.GetChildren()) if (c is MeshInstance3D mi && !_slotHas(mi) && !mi.Name.ToString().Contains("Eye") && !mi.Name.ToString().Contains("brow")) { _bodyMi = mi; _bodyFull = mi.Mesh as ArrayMesh; break; }
        if (_bodyMi == null || _bodyFull == null) return;
        // cloth vertices bucketed in 3 cm cells; a body vertex is "under cloth" if a cloth vertex is within R of it.
        // The head slot is left out: hoods are loose, and their face opening would otherwise cut into the face.
        const float R = .045f; var grid = new Dictionary<Vector3I, List<Vector3>>(); var key = new System.Text.StringBuilder(_sex);
        foreach (var slot in new[] { "armor", "hands", "feet" })
            if (_slot.TryGetValue(slot, out var l))
                foreach (var n in l) if (n is MeshInstance3D mi && mi.Mesh != null && mi.Skin != null)
                    {
                        key.Append('|').Append(mi.Mesh.ResourcePath).Append(mi.Name);
                        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                            foreach (var v in (Vector3[])mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex])
                            { var c = Cell(v, R); if (!grid.TryGetValue(c, out var cl)) grid[c] = cl = new List<Vector3>(); cl.Add(v); }
                    }
        bool Under(Vector3 p)
        {
            var c = Cell(p, R);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                        if (grid.TryGetValue(c + new Vector3I(x, y, z), out var cl)) foreach (var q in cl) if (q.DistanceSquaredTo(p) < R * R) return true;
            return false;
        }
        string k = key.ToString(); int nb = _sk.FindBone("neck_01");
        float neckY = nb >= 0 ? _sk.GetBoneGlobalRest(nb).Origin.Y + .02f : 1.5f;
        if (!_masked.TryGetValue(k, out var masked))
        {
            masked = new ArrayMesh();
            for (int bs = 0; bs < _bodyFull.GetBlendShapeCount(); bs++) masked.AddBlendShape(_bodyFull.GetBlendShapeName(bs));
            masked.BlendShapeMode = _bodyFull.BlendShapeMode;
            for (int s = 0; s < _bodyFull.GetSurfaceCount(); s++)
            {
                var arr = _bodyFull.SurfaceGetArrays(s); var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex]; var idx = (int[])arr[(int)Mesh.ArrayType.Index];
                bool[] under = new bool[verts.Length];
                for (int i = 0; i < verts.Length; i++) under[i] = verts[i].Y < neckY && Under(verts[i]);   // never cut the face (collars and scarves sit close to the chin)
                var keep = new List<int>(idx.Length);
                for (int t = 0; t + 2 < idx.Length; t += 3) if (!(under[idx[t]] && under[idx[t + 1]] && under[idx[t + 2]])) { keep.Add(idx[t]); keep.Add(idx[t + 1]); keep.Add(idx[t + 2]); }
                arr[(int)Mesh.ArrayType.Index] = keep.ToArray();
                for (int ci = (int)Mesh.ArrayType.Custom0; ci <= (int)Mesh.ArrayType.Custom3; ci++) arr[ci] = new Variant();   // unused extra channels; re-adding them needs their packed format
                var flags = _bodyFull.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;   // keep 8-bone skinning if the body uses it
                masked.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, _bodyFull.SurfaceGetBlendShapeArrays(s), null, flags);
                masked.SurfaceSetMaterial(s, _bodyFull.SurfaceGetMaterial(s));
            }
            _masked[k] = masked;
        }
        _bodyMi.Mesh = masked;
    }
    static Vector3I Cell(Vector3 v, float r) => new(Mathf.FloorToInt(v.X / r), Mathf.FloorToInt(v.Y / r), Mathf.FloorToInt(v.Z / r));
    bool _slotHas(Node n) { foreach (var l in _slot.Values) if (l.Contains(n)) return true; return false; }

    // ------------------------------------------------------------------ back accessories
    public Node3D? Cape;
    /// <summary>A plain cloth cape on the upper back: the fallback when an equipped cape has no kit model. Rotate Cape about X to let it stream back.</summary>
    public void AddCape(Color c)
    {
        var hold = Cfg.GetProperty("hold").GetProperty("cape");
        var h = Holder("spine_03", V(hold, "pos", new Vector3(0, .12f, -.14f)), V(hold, "rot", Vector3.Zero), "cape");
        Cape = new Node3D { Name = "Cape" }; h.AddChild(Cape);
        var mat = new StandardMaterial3D { AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = .9f };
        Cape.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.44f, .95f, .02f) }, MaterialOverride = mat, Position = new Vector3(0, -.47f, 0) });
    }

    /// <summary>Feathered wings on the upper back (Golden Wings, Scarletta's single angel wing). Each wing is a pivot that FlapWings moves.</summary>
    public Node3D? WingL, WingR;
    public void AddWings(Color feather, Color edge, bool left = true, bool right = true, float span = 1f, float glow = .5f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = feather, Metallic = .55f, Roughness = .35f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = glow > 0, Emission = feather, EmissionEnergyMultiplier = glow };
        var tip = new StandardMaterial3D { AlbedoColor = edge, Metallic = .8f, Roughness = .25f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = edge, EmissionEnergyMultiplier = glow + .3f };
        foreach (float s in new[] { 1f, -1f })
        {
            if (s > 0 && !left || s < 0 && !right) continue;
            var h = Holder("spine_03", new Vector3(s * .08f, .1f, -.17f), Vector3.Zero, "cape");
            var pivot = new Node3D { Name = s > 0 ? "WingL" : "WingR" }; h.AddChild(pivot);
            if (s > 0) WingL = pivot; else WingR = pivot;
            // the arm of the wing: rises outward, then the feathers fan down from it
            float arm = .62f * span, rise = Mathf.DegToRad(32);
            var armDir = new Vector3(s * Mathf.Cos(rise), Mathf.Sin(rise), -.15f).Normalized();
            pivot.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .035f * span, Height = arm, RadialSegments = 8, Rings = 2 }, MaterialOverride = tip, Position = armDir * arm / 2, Rotation = new Vector3(0, 0, -s * (Mathf.Pi / 2 - rise)) });
            for (int i = 0; i < 9; i++)
            {
                float t = .2f + .8f * i / 8f; var root = armDir * arm * t;
                float ang = Mathf.DegToRad(Mathf.Lerp(-118, -28, i / 8f)), len = (.38f + .62f * Mathf.Sin(Mathf.Pi * (.15f + .7f * i / 8f))) * span;   // longest near the tip
                var dir = new Vector3(s * Mathf.Cos(ang) * .35f + s * .2f * t, Mathf.Sin(ang), -.08f).Normalized();
                // the feather's long side (local Y) runs along dir, its flat face toward the back (Z)
                var zAx = new Vector3(0, 0, 1); var xAx = dir.Cross(zAx).Normalized(); zAx = xAx.Cross(dir).Normalized();
                var f = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.11f * span, len, .018f) }, MaterialOverride = i >= 7 ? tip : mat, Position = root + dir * len / 2 + new Vector3(0, 0, -.012f * i) };
                f.Basis = new Basis(xAx, dir, zAx); pivot.AddChild(f);
                // a shorter covert feather over each primary
                var c = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.1f * span, len * .45f, .02f) }, MaterialOverride = mat, Position = root + dir * len * .22f + new Vector3(0, 0, .015f) };
                c.Basis = f.Basis; pivot.AddChild(c);
            }
        }
        FlapWings(0, false);
    }
    /// <summary>Wings beat while flying and fold back along the body otherwise.</summary>
    public void FlapWings(float t, bool flying)
    {
        foreach (var (w, s) in new[] { (WingL, 1f), (WingR, -1f) })
        {
            if (w == null || !IsInstanceValid(w)) continue;
            w.Rotation = flying ? new Vector3(0, -s * .1f, s * Mathf.Sin(t * 7f) * .55f) : new Vector3(.15f, -s * .9f, -s * .25f + s * Mathf.Sin(t * 1.3f) * .03f);
        }
    }
    /// <summary>A holder on a bone for a custom piece (bosses' own weapons and ornaments), placed in model space at rest like "hold".</summary>
    public Node3D Socket(string bone, Vector3 pos, Vector3 rotDeg, string slot = "extra") => Holder(bone, pos, rotDeg, slot);
    /// <summary>The right-hand holder a held weapon of this type would use: +Z runs along the blade from the grip.</summary>
    public Node3D HandSocket(string wt = "sword") { var (hp, hr) = WeaponHold(wt, ""); return Holder("hand_r", hp, hr, "extra"); }

    // ------------------------------------------------------------------ race features (procedural for now)
    void RaceFeatures(Appearance ap)
    {
        if (!ap.PointyEars && !ap.Tusks && !ap.Horns) return;
        var head = Holder("Head", new Vector3(0, .1f, 0), Vector3.Zero, "race");
        var skin = new StandardMaterial3D { AlbedoColor = ap.Skin }; var bone = new StandardMaterial3D { AlbedoColor = new Color(.92f, .88f, .76f) };
        foreach (float s in new[] { -1f, 1f })
        {
            if (ap.PointyEars) head.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .028f, Height = .13f, RadialSegments = 6 }, MaterialOverride = skin, Position = new Vector3(s * .085f, .0f, -.01f), Rotation = new Vector3(-.5f, 0, -s * 1.15f) });
            if (ap.Tusks) head.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .012f, Height = .05f, RadialSegments = 5 }, MaterialOverride = bone, Position = new Vector3(s * .028f, -.07f, .085f), Rotation = new Vector3(-.3f, 0, 0) });
            if (ap.Horns) head.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = .028f, Height = .16f, RadialSegments = 6 }, MaterialOverride = bone, Position = new Vector3(s * .06f, .1f, .0f), Rotation = new Vector3(-.5f, 0, -s * .5f) });
        }
    }

    // ------------------------------------------------------------------ gear
    /// <summary>Finds the gear rule for an item name (first rule whose keyword occurs in the name; "" matches anything).</summary>
    static JsonElement? Rule(string slot, string name)
    {
        if (!Cfg.TryGetProperty(slot, out var list) || list.ValueKind != JsonValueKind.Array) return null;
        foreach (var r in list.EnumerateArray()) foreach (var k in L(r, "match")) if (k == "" || name.Contains(k)) return r;
        return null;
    }
    string Part(string p)
    {
        var name = p.Replace("{s}", Female ? "Female" : "Male");
        if (Cfg.TryGetProperty("partAlias", out var al) && al.TryGetProperty(name, out var a)) name = a.GetString() ?? name;
        return OutfitDir + name + ".gltf";
    }
    Color? TintOf(JsonElement r, Color classTint)
    {
        var t = S(r, "tint"); if (t == "") return null; if (t == "class") return classTint.Lightened(.25f); return Color.FromHtml(t);
    }

    /// <summary>Dresses the body from equipped items (null = nothing in that slot). classTint colours robes; outfitTint (enemies, NPCs) colours everything.</summary>
    public void Dress(Func<string, Item?> eq, string wt, Color classTint, Color? outfitTint = null)
    {
        foreach (var s in new[] { "armor", "head", "hands", "feet", "weapon", "shield", "cape" }) Clear(s);
        BladeBase = BladeTip = null; Cape = null; WingL = WingR = null; bool hideHair = false;
        // cape: an equipment slot (none when nothing is equipped)
        var cp = eq("cape");
        if (cp != null && Rule("cape", cp.Name) is { } cr)
        {
            var kit = KitPieces(cr, "cape");
            if (kit.Count > 0 && kit[0] is Node3D ch && ch.GetChildCount() > 0) Cape = ch.GetChild<Node3D>(0);   // the piece itself swings about its top (Player)
            else if (S(cr, "proc") == "wings" || cp.Wings) AddWings(new Color(1f, .8f, .32f), new Color(1f, .93f, .6f));   // Golden Wings
            else if (S(cr, "proc") == "cape") AddCape(GearFx.ColorOf(cp));
            foreach (var n in SlotNodes("cape")) if (n is Node3D n3) GearFx.Apply(n3, cp, false);
        }
        foreach (var slot in new[] { "armor", "hands", "feet", "head" })
        {
            var it = eq(slot); List<string> parts; Color? tint = null; string proc = ""; JsonElement? rule = null;
            if (it == null) { parts = L(Cfg.GetProperty("default"), slot); tint = outfitTint; }
            else
            {
                var r = Rule(slot, it.Name); if (r == null) continue; rule = r;
                parts = L(r.Value, "parts"); proc = S(r.Value, "proc");
                var ruleTint = TintOf(r.Value, classTint);
                tint = ruleTint != null && outfitTint != null ? ruleTint.Value.Lerp(outfitTint.Value, .5f) : ruleTint ?? outfitTint;
                if (r.Value.TryGetProperty("hideHair", out var hh) && hh.ValueKind == JsonValueKind.True) hideHair = true;
            }
            // tints multiply textured cloth, so they are softened toward white to recolour without going muddy or black
            Color? soft = tint?.Lerp(Colors.White, .35f);
            var made = new List<Node>(); foreach (var p in parts) made.AddRange(AddPart(Part(p), slot, soft));
            var kit = rule != null ? KitPieces(rule.Value, slot) : new List<Node>(); made.AddRange(kit);
            if (proc != "" && kit.Count == 0) made.Add(Helmet(proc, it!));   // the procedural shape only when no kit piece loaded
            if (it != null) foreach (var n in made) if (n is Node3D n3) GearFx.Apply(n3, it, false);
        }
        foreach (var h in _hair) if (h is Node3D h3) h3.Visible = !hideHair;
        MaskBody();
        // weapon and shield
        var w = eq("weapon"); var wcfg = Cfg.GetProperty("weapons");
        bool twoHanded = Weapons.Get(wt).Hands > 1 || Weapons.Get(wt).LeftHand;
        if (wcfg.TryGetProperty(wt, out var wc)) HoldWeapon(wt, wc, w);
        var sh = eq("shield"); if (sh != null && !twoHanded) HoldShield(sh);
    }

    void HoldWeapon(string wt, JsonElement wc, Item? it)
    {
        bool left = S(wc, "hand") == "left"; var hold = Cfg.GetProperty("hold").GetProperty(left ? "left" : "right");
        var (hp, hr) = WeaponHold(wt, Weapons.KitModelFor(wt, it).Model);
        var h = Holder(left ? "hand_l" : "hand_r", hp, hr, "weapon");
        if (wc.TryGetProperty("proc", out _))
        {
            // no model yet: the procedural weapon, turned to point along +Z like the models
            var turn = new Node3D { Rotation = new Vector3(0, Mathf.Pi, 0) }; h.AddChild(turn);
            var (b, t) = Weapons.Build(turn, wt, it); BladeBase = b; BladeTip = t; GearFx.Apply(turn, it, true, 0, .9f); return;
        }
        string model = S(wc, "model"); if (it != null && Cfg.TryGetProperty("weaponNames", out var wn)) foreach (var p in wn.EnumerateObject()) if (it.Name.Contains(p.Name)) { model = p.Value.GetString() ?? model; break; }
        float len = JsonUtil.F(wc, "len", 1f), grip = JsonUtil.F(wc, "grip", 0f);
        var inst = LoadModel(WeaponDir + model + ".fbx", len); if (inst == null) return;
        inst.Position = new Vector3(0, 0, -grip * len); h.AddChild(inst);
        BladeBase = new Node3D { Position = new Vector3(0, 0, len * (.25f - grip)) }; BladeTip = new Node3D { Position = new Vector3(0, 0, len * (1 - grip)) };
        h.AddChild(BladeBase); h.AddChild(BladeTip);
        GearFx.Apply(h, it, true, -grip * len, len);
    }

    /// <summary>
    /// Where a weapon sits in the hand (model space at rest): data/avatar.json hold.right/left, the type's "rot", and a per-model
    /// override in weapons.&lt;type&gt;.models.&lt;kit model&gt; { pos, rot } (orbs and tomes need a different grip than wands).
    /// </summary>
    public static (Vector3 pos, Vector3 rot) WeaponHold(string wt, string model)
    {
        var w = Cfg.GetProperty("weapons"); if (!w.TryGetProperty(wt, out var wc)) return (Vector3.Zero, Vector3.Zero);
        var hold = Cfg.GetProperty("hold").GetProperty(S(wc, "hand") == "left" ? "left" : "right");
        var pos = V(hold, "pos", Vector3.Zero); var rot = V(wc, "rot", V(hold, "rot", Vector3.Zero));
        if (model != "" && wc.TryGetProperty("models", out var ms) && ms.TryGetProperty(model, out var m)) { pos = V(m, "pos", pos); rot = V(m, "rot", rot); }
        return (pos, rot);
    }

    void HoldShield(Item it)
    {
        string model = "Shield_Round"; if (Cfg.TryGetProperty("shields", out var sm)) foreach (var p in sm.EnumerateObject()) if (p.Name == "" || it.Name.Contains(p.Name)) { model = p.Value.GetString() ?? model; if (p.Name != "") break; }
        var hold = Cfg.GetProperty("hold").GetProperty("shield");
        var h = Holder("lowerarm_l", V(hold, "pos", Vector3.Zero), V(hold, "rot", Vector3.Zero), "shield");
        var inst = LoadModel(WeaponDir + model + ".fbx", JsonUtil.F(Cfg, "shieldSize", .62f), true); if (inst == null) return;
        h.AddChild(inst); GearFx.Apply(h, it, false);
    }

    /// <summary>Loads a prop model inside a pivot. Weapons: the long axis is turned to point along +Z from the grip (the model's origin)
    /// and scaled to size metres, whatever axis/units the file used. longest = just scale the largest side to size (shields).</summary>
    static Node3D? LoadModel(string path, float size, bool longest = false)
    {
        if (!ResourceLoader.Exists(path)) { GD.PrintErr("[AVATAR] missing model " + path); return null; }
        var inst = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        var box = new Aabb(); bool any = false;
        void Walk(Node n, Transform3D t)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null) { var ab = t * mi.GetAabb(); box = any ? box.Merge(ab) : ab; any = true; }
            foreach (var c in n.GetChildren()) Walk(c, c is Node3D n3 ? t * n3.Transform : t);
        }
        Walk(inst, inst.Transform);   // includes the importer's unit scale and axis turn (FBX meshes come in at x100, Z-up)
        var pivot = new Node3D { Name = "Model" }; pivot.AddChild(inst);
        if (!any) return pivot;
        var sz = box.Size;
        if (longest) { pivot.Scale = Vector3.One * (size / Mathf.Max(sz.X, Mathf.Max(sz.Y, sz.Z))); return pivot; }
        int ax = sz.X >= sz.Y && sz.X >= sz.Z ? 0 : sz.Y >= sz.Z ? 1 : 2;
        float lo = box.Position[ax], hi = box.End[ax]; bool neg = -lo > hi; float len = Mathf.Max(Mathf.Abs(lo), Mathf.Abs(hi));
        var b = ax == 2 ? Basis.Identity : ax == 1 ? new Basis(Vector3.Right, Mathf.Pi / 2) : new Basis(Vector3.Up, -Mathf.Pi / 2);   // long axis -> +Z
        if (neg) b = new Basis(Vector3.Up, Mathf.Pi) * b;
        pivot.Transform = new Transform3D(b.Scaled(Vector3.One * (size / Mathf.Max(.0001f, len))), Vector3.Zero);
        return pivot;
    }

    /// <summary>Helmets without a model yet: simple shapes on the head, coloured like the item.</summary>
    Node3D Helmet(string kind, Item it)
    {
        var h = Holder("Head", new Vector3(0, .1f, .01f), Vector3.Zero, "head");
        var steel = new StandardMaterial3D { AlbedoColor = new Color(.75f, .78f, .84f), Metallic = .6f, Roughness = .4f };
        var gold = new StandardMaterial3D { AlbedoColor = new Color(.9f, .72f, .32f), Metallic = .7f, Roughness = .35f };
        var leather = new StandardMaterial3D { AlbedoColor = new Color(.42f, .28f, .16f) };
        void P(Mesh m, Material mt, Vector3 pos, Vector3? rot = null) => h.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = pos, Rotation = rot ?? Vector3.Zero });
        switch (kind)
        {
            case "greathelm": P(new CylinderMesh { TopRadius = .135f, BottomRadius = .14f, Height = .25f, RadialSegments = 14 }, steel, new Vector3(0, .0f, 0)); P(new SphereMesh { Radius = .136f, Height = .14f, IsHemisphere = true, RadialSegments = 14, Rings = 4 }, steel, new Vector3(0, .12f, 0)); P(new BoxMesh { Size = new Vector3(.17f, .018f, .02f) }, new StandardMaterial3D { AlbedoColor = new Color(.05f, .05f, .06f) }, new Vector3(0, .02f, .135f)); break;
            case "helm": P(new SphereMesh { Radius = .128f, Height = .19f, IsHemisphere = true, RadialSegments = 14, Rings = 5 }, steel, new Vector3(0, .02f, -.005f)); P(new CylinderMesh { TopRadius = .13f, BottomRadius = .13f, Height = .03f, RadialSegments = 14 }, gold, new Vector3(0, .02f, -.005f)); P(new BoxMesh { Size = new Vector3(.022f, .09f, .015f) }, steel, new Vector3(0, -.03f, .128f)); break;
            case "cap": P(new SphereMesh { Radius = .122f, Height = .14f, IsHemisphere = true, RadialSegments = 12, Rings = 4 }, leather, new Vector3(0, .04f, -.01f)); break;
            default: P(new TorusMesh { InnerRadius = .108f, OuterRadius = .118f, Rings = 18, RingSegments = 4 }, gold, new Vector3(0, .04f, 0), new Vector3(-.12f, 0, 0)); P(new SphereMesh { Radius = .016f, Height = .032f }, new StandardMaterial3D { AlbedoColor = GearFx.ColorOf(it), EmissionEnabled = true, Emission = GearFx.ColorOf(it), EmissionEnergyMultiplier = 1.5f }, new Vector3(0, .055f, .115f)); break;
        }
        return h;
    }

    // ------------------------------------------------------------------ animation
    static readonly Dictionary<string, AnimationLibrary> _libs = new();
    const string Baked = "res://assets/models/anim/clips.res";
    /// <summary>Tools (--measure-attacks): load every clip of the source libraries instead of the small baked set.</summary>
    public static bool UseSources;
    /// <summary>Every clip name this body can play.</summary>
    public IEnumerable<string> ClipNames => _ap == null ? Array.Empty<string>() : _ap.GetAnimationList().Select(n => n.ToString());
    /// <summary>The clips for one body. Uses the small baked library (only the clips the game plays) and scales the pelvis
    /// height to this body; without it, merges the full source libraries (slow, and they are not exported).</summary>
    static AnimationLibrary Library(string key, Skeleton3D target)
    {
        if (_libs.TryGetValue(key, out var lib)) return lib;
        int tp = target.FindBone("pelvis"); float tgt = tp >= 0 ? target.GetBoneRest(tp).Origin.Z : 1f;
        if (!UseSources && ResourceLoader.Exists(Baked) && GD.Load<AnimationLibrary>(Baked) is { } baked)
        {
            float k = tgt / JsonUtil.F(Cfg, "animPelvis", .9167f); lib = new AnimationLibrary();
            foreach (var n in baked.GetAnimationList())
            {
                var a = (Animation)baked.GetAnimation(n).Duplicate(true);
                for (int t = 0; t < a.GetTrackCount(); t++) if (a.TrackGetType(t) == Animation.TrackType.Position3D)
                        for (int i = 0; i < a.TrackGetKeyCount(t); i++) a.TrackSetKeyValue(t, i, (Vector3)a.TrackGetKeyValue(t, i) * k);
                lib.AddAnimation(n, a);
            }
        }
        else lib = Merge(tgt, null);
        AddAttackAnims(lib);
        GD.Print($"[AVATAR] {key}: {lib.GetAnimationList().Count} clips");
        _libs[key] = lib; return lib;
    }

    /// <summary>--list-anims: prints every clip name in the source animation libraries (to pick names for "clips").</summary>
    public static void ListSources()
    {
        foreach (var src in L(Cfg, "anims"))
        {
            if (!ResourceLoader.Exists(src)) { GD.Print("[ANIMS] missing " + src); continue; }
            var inst = GD.Load<PackedScene>(src).Instantiate<Node>(); var ap = FindAp(inst);
            if (ap != null) GD.Print($"[ANIMS] {src}: " + string.Join(" ", ap.GetAnimationList().Select(n => { var s = n.ToString(); int b = s.IndexOf('|'); return b >= 0 ? s[(b + 1)..] : s; })));
            inst.Free();
        }
    }

    /// <summary>--bake-anims: merges the source libraries at the animation rig's own hip height and saves only the clips in use.</summary>
    public static void Bake()
    {
        var used = new HashSet<string>();
        foreach (var p in Cfg.GetProperty("clips").EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) used.Add(p.Value.GetString()!); else if (p.Value.ValueKind == JsonValueKind.Array) foreach (var x in p.Value.EnumerateArray()) used.Add(x.GetString()!);
        foreach (var (_, d) in AttackDefs()) used.Add(d.Clip);   // the clips attack animations are cut from
        var lib = Merge(JsonUtil.F(Cfg, "animPelvis", .9167f), used);
        var err = ResourceSaver.Save(lib, Baked, ResourceSaver.SaverFlags.Compress);
        GD.Print($"[AVATAR] baked {lib.GetAnimationList().Count} clips to {Baked}: {err}");
    }

    /// <summary>Both source libraries merged: tracks re-pathed to our skeleton, root motion dropped, only the pelvis keeps its
    /// position track (scaled to hip height tgt), so limbs keep the body's own proportions. only = keep just these clips.</summary>
    static AnimationLibrary Merge(float tgt, HashSet<string>? only)
    {
        var lib = new AnimationLibrary();
        foreach (var src in L(Cfg, "anims"))
        {
            if (!ResourceLoader.Exists(src)) continue;
            var inst = GD.Load<PackedScene>(src).Instantiate<Node>();
            var ap = (AnimationPlayer?)inst.FindChild("AnimationPlayer", true, false) ?? FindAp(inst); var ssk = (Skeleton3D?)inst.FindChild("Skeleton3D", true, false);
            if (ap == null || ssk == null) { inst.Free(); continue; }
            bool fbx = src.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase); float unit = fbx ? 100f : 1f;
            int sp = ssk.FindBone("pelvis"); float k = sp >= 0 ? tgt / (ssk.GetBoneRest(sp).Origin.Z * unit) : 1f;
            foreach (var name in ap.GetAnimationList())
            {
                string clean = name.ToString(); int bar = clean.IndexOf('|'); if (bar >= 0) clean = clean[(bar + 1)..];
                // root-motion clips only when asked for by name (their travel is dropped with the root track, so they play in place)
                if ((clean.EndsWith("_RM") && !(UseSources || (only != null && only.Contains(clean)))) || clean.Contains("TPose") || lib.HasAnimation(clean) || (only != null && !only.Contains(clean))) continue;
                var a = (Animation)ap.GetAnimation(name).Duplicate(true);
                for (int t = a.GetTrackCount() - 1; t >= 0; t--)
                {
                    string path = a.TrackGetPath(t).ToString(); int colon = path.IndexOf(':'); string bone = colon >= 0 ? path[(colon + 1)..] : "";
                    var type = a.TrackGetType(t);
                    if (bone == "" || bone == "root" || type == Animation.TrackType.Scale3D || (type == Animation.TrackType.Position3D && bone != "pelvis")) { a.RemoveTrack(t); continue; }
                    a.TrackSetPath(t, "Armature/Skeleton3D:" + bone);
                    if (type == Animation.TrackType.Position3D)
                        for (int i = 0; i < a.TrackGetKeyCount(t); i++) a.TrackSetKeyValue(t, i, (Vector3)a.TrackGetKeyValue(t, i) * unit * k);
                }
                lib.AddAnimation(clean, a);
            }
            inst.Free();
        }
        return lib;
    }
    static AnimationPlayer? FindAp(Node n) { foreach (var c in n.GetChildren()) { if (c is AnimationPlayer a) return a; if (FindAp(c) is { } x) return x; } return null; }

    // ------------------------------------------------------------------ attack animations (data/avatar.json "attackAnims")
    /// <summary>A named attack animation: a slice of a library clip (from..to, as fractions), maybe reversed; hit = when the blow lands in it (0..1).</summary>
    public class AttackAnim { public string Clip = ""; public float From, To = 1, Hit = .5f; public bool Reverse; }
    static Dictionary<string, AttackAnim>? _atkDefs;
    public static Dictionary<string, AttackAnim> AttackDefs()
    {
        if (_atkDefs != null) return _atkDefs; _atkDefs = new();
        if (!Cfg.TryGetProperty("attackAnims", out var aa)) return _atkDefs;
        foreach (var p in aa.EnumerateObject())
        {
            if (p.Name.StartsWith("_") || p.Value.ValueKind != JsonValueKind.Object) continue; var e = p.Value;
            _atkDefs[p.Name] = new AttackAnim { Clip = S(e, "clip"), From = JsonUtil.F(e, "from", 0), To = JsonUtil.F(e, "to", 1), Hit = JsonUtil.F(e, "hit", .5f), Reverse = e.TryGetProperty("reverse", out var r) && r.ValueKind == JsonValueKind.True };
        }
        return _atkDefs;
    }
    /// <summary>Attack animations by number (1-based, 0 = none) for the network: the same order on every machine (it is the data file's order).</summary>
    static List<string>? _atkOrder;
    public static int AttackIndex(string id) { _atkOrder ??= AttackDefs().Keys.ToList(); return _atkOrder.IndexOf(id) + 1; }
    public static string AttackIdAt(int i) { _atkOrder ??= AttackDefs().Keys.ToList(); return i >= 1 && i <= _atkOrder.Count ? _atkOrder[i - 1] : ""; }
    /// <summary>Animation name of an attack animation in the library.</summary>
    public static string AtkName(string id) => "atk_" + id;
    /// <summary>Cuts every attack animation out of its source clip (sampled at 30 frames a second) and adds it to the library.</summary>
    static void AddAttackAnims(AnimationLibrary lib)
    {
        foreach (var (id, d) in AttackDefs())
        {
            if (!lib.HasAnimation(d.Clip) || lib.HasAnimation(AtkName(id))) continue;
            var src = lib.GetAnimation(d.Clip); float len = (float)src.Length, t0 = Mathf.Clamp(d.From, 0, 1) * len, t1 = Mathf.Clamp(d.To, 0, 1) * len;
            if (t1 - t0 < .05f) continue;
            var a = new Animation { Length = t1 - t0, LoopMode = Animation.LoopModeEnum.None };
            for (int t = 0; t < src.GetTrackCount(); t++)
            {
                var type = src.TrackGetType(t); if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D) continue;
                int nt = a.AddTrack(type); a.TrackSetPath(nt, src.TrackGetPath(t)); a.TrackSetInterpolationType(nt, Animation.InterpolationType.Linear);
                int frames = Mathf.Max(2, Mathf.CeilToInt((t1 - t0) * 30f));
                for (int f = 0; f <= frames; f++)
                {
                    float at = t0 + (t1 - t0) * f / frames, key = d.Reverse ? (t1 - at) : (at - t0);
                    if (type == Animation.TrackType.Rotation3D) a.RotationTrackInsertKey(nt, key, src.RotationTrackInterpolate(t, at));
                    else a.PositionTrackInsertKey(nt, key, src.PositionTrackInterpolate(t, at));
                }
            }
            lib.AddAnimation(AtkName(id), a);
        }
    }
    /// <summary>
    /// Plays an attack animation so its blow lands when the attack deals damage: the clip runs at the speed that brings its own hit moment
    /// to hitAt seconds (then keeps that pace). Returns false if there is no such animation (the caller falls back to the old clips).
    /// </summary>
    public bool PlayAttack(string id, float hitAt, bool restart, float blend = .06f)
    {
        if (_ap == null || !AttackDefs().TryGetValue(id, out var d)) return false;
        string n = AtkName(id); if (!_ap.HasAnimation(n)) return false;
        if (n == Cur && !restart && _ap.IsPlaying()) return true;
        float len = (float)_ap.GetAnimation(n).Length, hit = d.Reverse ? 1 - d.Hit : d.Hit;
        float sp = Mathf.Clamp(hit * len / Mathf.Max(.05f, hitAt), .35f, 3.5f);
        _ap.SpeedScale = 1f; _ap.Play(n, blend, sp); if (restart) _ap.Seek(0, true); Cur = n; _rev = false;
        return true;
    }
    /// <summary>Plays a recovery (follow-through) animation stretched over this many seconds.</summary>
    public void PlayRecovery(string id, float secs)
    {
        if (_ap == null || !AttackDefs().ContainsKey(id)) return; string n = AtkName(id); if (!_ap.HasAnimation(n) || n == Cur) return;
        _ap.SpeedScale = 1f; _ap.Play(n, .1f, Mathf.Clamp((float)_ap.GetAnimation(n).Length / Mathf.Max(.1f, secs), .5f, 3f)); Cur = n; _rev = false;
    }

    public string Cur = "";
    /// <summary>Clip name for an action key in data/avatar.json "clips" (a list = combo steps).</summary>
    public static string Clip(string key, int step = 0)
    {
        if (!Cfg.GetProperty("clips").TryGetProperty(key, out var v)) return key;
        if (v.ValueKind == JsonValueKind.Array) { int n = v.GetArrayLength(); return n == 0 ? key : v[step % n].GetString() ?? key; }
        return v.GetString() ?? key;
    }
    /// <summary>Plays an action (clip key or clip name). fit &gt; 0 stretches it to that many seconds; restart replays it.</summary>
    public void Play(string key, float blend = .18f, float fit = 0, bool restart = false, int step = 0, bool reverse = false)
    {
        if (_ap == null) return; string n = Clip(key, step); if (!_ap.HasAnimation(n)) return;
        _ap.SpeedScale = 1f;   // Move sets the gait scale after this; attacks and casts play at their own fitted speed
        if (n == Cur && !restart && _ap.IsPlaying() && reverse == _rev) return;
        float sp = fit > 0 ? (float)_ap.GetAnimation(n).Length / fit : 1f;
        _ap.Play(n, blend, reverse ? -sp : sp, reverse); if (restart) _ap.Seek(0, true); Cur = n; _rev = reverse;
    }
    bool _rev;
    /// <summary>A one-shot clip (attack, cast, hit) is still running.</summary>
    public bool Busy => _ap != null && _ap.IsPlaying() && _ap.HasAnimation(Cur) && _ap.GetAnimation(Cur).LoopMode == Animation.LoopModeEnum.None;
    public float Speed { set { if (_ap != null) _ap.SpeedScale = value; } }

    /// <summary>Walking / running / idle from ground speed (m/s).</summary>
    /// <summary>Walking / running / idle from ground speed (m/s). back = moving backwards (the walk or run plays in reverse; the clips only go forward).</summary>
    public void Move(float speed, bool grounded, bool armed, bool shield, bool back = false)
    {
        if (!grounded) { Play("fall", .2f); return; }
        bool moving = speed > .4f;
        if (Busy && Cur != Clip("land") && !moving) return;   // a one-shot clip runs on only while standing
        // quick blend into motion so the feet start with the body; slower between gaits and back to idle
        bool fromStill = Cur == Clip("idle") || Cur == Clip("idleArmed") || Cur == Clip("idleShield") || Busy;
        string gait = speed > Gait("sprintAt", 6.2f) && !back ? "sprint" : speed > Gait("runAt", 2.6f) ? "run" : moving ? "walk" : "";
        Play(gait != "" ? gait : shield ? "idleShield" : armed ? "idleArmed" : "idle", moving && fromStill ? .07f : .2f, 0, false, 0, back && moving);
        // feet keep pace with the ground: the clip plays at (speed / the clip's own speed), within limits (data/avatar.json "gait")
        if (_ap != null) _ap.SpeedScale = gait == "" ? 1f : Mathf.Clamp(speed / Gait(gait, 2.5f), Gait("minScale", .8f), Gait("maxScale", 2f));
    }
    static float Gait(string k, float d) => Cfg.TryGetProperty("gait", out var g) ? JsonUtil.F(g, k, d) : d;

    // ------------------------------------------------------------------ helpers for callers
    /// <summary>An item-like stand-in for NPC / enemy outfits from data/avatar.json ("armor": "Plate" ...).</summary>
    public static Func<string, Item?> Outfit(JsonElement o)
    {
        var d = new Dictionary<string, Item?>();
        foreach (var s in new[] { "armor", "head", "hands", "feet", "shield", "cape" }) if (S(o, s) != "") d[s] = new Item { Name = S(o, s), Slot = s };
        return s => d.GetValueOrDefault(s);
    }
    public static JsonElement? Npc(string role) => Cfg.GetProperty("npcs").TryGetProperty(role, out var v) ? v : null;
    public static JsonElement? Foe(string type) => Cfg.GetProperty("enemies").TryGetProperty(type, out var v) ? v : null;
    public static string Str(JsonElement e, string k, string d = "") => S(e, k, d);
}
