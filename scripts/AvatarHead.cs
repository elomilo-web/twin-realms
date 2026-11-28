using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-34 heads: MakeHuman (CC0) hair, race ear shapes and face skin for the realistic bodies, made by
/// tools/blender_gothic/mh_head_assets.py (the bodies ARE the MakeHuman base mesh, so its own assets fit them exactly).
/// - Hair "TR_MH_*.glb" (assets/models/hair/mh/) is a rigid mesh on a BoneAttachment3D on the Head bone, written relative to the
///   bone's rest position in model axes; data/avatar.json hairFit can nudge it (pos/rot/scale, model space, like "hold").
///   The strand texture is light grey: the hero's hair colour multiplies it (RealHair).
/// - Ears are shape deltas on the body mesh itself (data/ear_morphs.json, MakeHuman ear targets), blended per race kind by
///   data/avatar.json earMorphs. They are applied in the bind pose like the face sculpt, before it (MaskBody -> EarMesh -> FaceMesh).
/// </summary>
public partial class Avatar
{
    const string MhHairDir = "res://assets/models/hair/mh/";
    /// <summary>True for head hair made by mh_head_assets.py (rigid, on the Head bone); other hair files are skinned glTFs.</summary>
    static bool IsHeadHair(string file) => file.StartsWith("TR_MH_");

    /// <summary>Loads a hair style: MakeHuman head hair on the Head bone, or an older skinned hair glTF (AddPart).</summary>
    List<Node> HairPiece(string file, Color tint)
    {
        if (!IsHeadHair(file)) return AddPart(HairDir + file, "hair", tint, true);
        var res = new List<Node>(); string path = MhHairDir + file;
        if (!ResourceLoader.Exists(path)) { GD.PrintErr("[AVATAR] missing hair " + path); return res; }
        if (_sk == null || _sk.FindBone("Head") < 0) return res;
        Vector3 pos = Vector3.Zero, rot = Vector3.Zero, sc = Vector3.One;
        if (Cfg.TryGetProperty("hairFit", out var hf) && hf.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "_default", file })   // the default first, then this file's own values over it
                if (hf.TryGetProperty(key, out var f) && f.ValueKind == JsonValueKind.Object)
                {
                    pos = V(f, "pos", pos); rot = V(f, "rot", rot);
                    if (f.TryGetProperty("scale", out var sv)) sc = sv.ValueKind == JsonValueKind.Number ? Vector3.One * sv.GetSingle() : V(f, "scale", sc);
                }
        var h = Holder("Head", pos, rot, "hair");
        var inst = GD.Load<PackedScene>(path).Instantiate<Node3D>(); inst.Name = "Hair"; inst.Scale = sc; h.AddChild(inst);
        foreach (var n in inst.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi)
            {
                mi.LodBias = 128f;   // F-04: auto-LODs collapse thin strand cards
                if (!RealHair(mi, tint)) Tint(mi, null, tint, 0, false, 0, Genshin.Kind.Hair);
            }
        res.Add(h); return res;
    }

    // ------------------------------------------------------------------ skin
    static readonly Dictionary<string, Texture2D?> _skinTex = new();
    /// <summary>data/avatar.json skinTexture.&lt;sex&gt;: the face/body skin map that replaces the body's own (null = keep it).</summary>
    static Texture2D? SkinTexture(string sex)
    {
        if (_skinTex.TryGetValue(sex, out var t)) return t;
        string p = Cfg.TryGetProperty("skinTexture", out var st) ? S(st, sex) : "";
        t = p != "" && ResourceLoader.Exists(p) ? GD.Load<Texture2D>(p) : null;
        _skinTex[sex] = t; return t;
    }

    // ------------------------------------------------------------------ ears
    string _earKind = ""; bool _earsHidden;
    static Dictionary<string, Dictionary<string, (Vector3[] at, Vector3[] d)>>? _earData;
    static readonly Dictionary<(ulong, string), ArrayMesh> _earMade = new();

    /// <summary>The ear targets of a body (data/ear_morphs.json), or null when the file is missing.</summary>
    static Dictionary<string, (Vector3[] at, Vector3[] d)>? EarData(string sex)
    {
        if (_earData == null)
        {
            _earData = new();
            const string path = "res://data/ear_morphs.json";
            if (FileAccess.FileExists(path))
            {
                using var doc = JsonUtil.Load(path);
                foreach (var body in doc.RootElement.EnumerateObject())
                {
                    if (body.Value.ValueKind != JsonValueKind.Object) continue;
                    var targets = new Dictionary<string, (Vector3[], Vector3[])>();
                    foreach (var tg in body.Value.EnumerateObject())
                    {
                        int n = tg.Value.GetArrayLength(); var at = new Vector3[n]; var d = new Vector3[n]; int i = 0;
                        foreach (var r in tg.Value.EnumerateArray())
                        {
                            at[i] = new Vector3(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle());
                            d[i] = new Vector3(r[3].GetSingle(), r[4].GetSingle(), r[5].GetSingle()); i++;
                        }
                        targets[tg.Name] = (at, d);
                    }
                    _earData[body.Name] = targets;
                }
            }
        }
        return _earData.TryGetValue(sex, out var t) ? t : null;
    }

    /// <summary>The earMorphs recipe of a race ear kind ("pointy", "fox", "fin"...), when it has one and the body has ear data.</summary>
    bool EarRecipe(string kind, out JsonElement rec)
    {
        rec = default;
        return kind != "" && Cfg.TryGetProperty("earMorphs", out var em) && em.TryGetProperty(kind, out rec) && rec.ValueKind == JsonValueKind.Object
            && rec.TryGetProperty("targets", out var tg) && tg.ValueKind == JsonValueKind.Object && EarData(_sex) != null;
    }

    /// <summary>
    /// The body with this avatar's race ears: the recipe's targets (weights) summed into one move per vertex position, applied in the
    /// bind pose, normals rebuilt around the moved skin. Under a helmet or hood the recipe's "helm" share is used (0 = human ears).
    /// One mesh per masked body, ear kind and share, shared by every avatar.
    /// </summary>
    ArrayMesh EarMesh(ArrayMesh masked)
    {
        if (!EarRecipe(_earKind, out var rec) || masked.GetBlendShapeCount() > 0) return masked;
        float k = _earsHidden ? JsonUtil.F(rec, "helm", 0f) : 1f; if (k <= .001f) return masked;
        var key = (masked.GetInstanceId(), _sex + "|" + _earKind + "|" + k.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        if (_earMade.TryGetValue(key, out var have) && IsInstanceValid(have)) return have;
        var data = EarData(_sex)!;
        // one move per position (the targets list the same bind-pose positions), bucketed on a 0.2 mm grid
        var sum = new Dictionary<Vector3, Vector3>();
        foreach (var t in rec.GetProperty("targets").EnumerateObject())
        {
            if (t.Value.ValueKind != JsonValueKind.Number || !data.TryGetValue(t.Name, out var tg)) continue;
            float w = t.Value.GetSingle() * k;
            for (int i = 0; i < tg.at.Length; i++) sum[tg.at[i]] = sum.GetValueOrDefault(tg.at[i]) + tg.d[i] * w;
        }
        if (sum.Count == 0) return masked;
        static Vector3I G(Vector3 p) => new(Mathf.RoundToInt(p.X * 5000f), Mathf.RoundToInt(p.Y * 5000f), Mathf.RoundToInt(p.Z * 5000f));
        var grid = new Dictionary<Vector3I, List<(Vector3 at, Vector3 d)>>();
        foreach (var (p, d) in sum) { var c = G(p); if (!grid.TryGetValue(c, out var l)) grid[c] = l = new(); l.Add((p, d)); }
        bool Find(Vector3 p, out Vector3 d)
        {
            var c = G(p);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                        if (grid.TryGetValue(c + new Vector3I(x, y, z), out var l))
                            foreach (var (at, dd) in l) if (at.DistanceSquaredTo(p) < 1e-8f) { d = dd; return true; }   // within 0.1 mm (the file keeps 0.01 mm)
            d = default; return false;
        }
        static Vector3I W(Vector3 p) => new(Mathf.RoundToInt(p.X * 10000f), Mathf.RoundToInt(p.Y * 10000f), Mathf.RoundToInt(p.Z * 10000f));   // welds UV seams
        var made = new ArrayMesh(); int movedAll = 0;
        for (int s = 0; s < masked.GetSurfaceCount(); s++)
        {
            var arr = masked.SurfaceGetArrays(s); var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
            var nrms = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
            var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : (int[])arr[(int)Mesh.ArrayType.Index];
            var nv = (Vector3[])verts.Clone(); var moved = new bool[verts.Length]; int nm = 0;
            for (int i = 0; i < verts.Length; i++) if (Find(verts[i], out var d)) { nv[i] = verts[i] + d; moved[i] = true; nm++; }
            if (nm > 0 && nrms != null && idx != null)
            {
                // the triangles touching moved skin, the welded corners they touch, and their winding against the old normals
                var hit = new HashSet<Vector3I>(); float sign = 0f;
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2]; if (!moved[a] && !moved[b] && !moved[c]) continue;
                    hit.Add(W(verts[a])); hit.Add(W(verts[b])); hit.Add(W(verts[c]));
                    sign += (verts[b] - verts[a]).Cross(verts[c] - verts[a]).Dot(nrms[a] + nrms[b] + nrms[c]);
                }
                float sg = sign >= 0f ? 1f : -1f; var acc = new Dictionary<Vector3I, Vector3>();
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                    Vector3I ka = W(verts[a]), kb = W(verts[b]), kc = W(verts[c]); if (!hit.Contains(ka) && !hit.Contains(kb) && !hit.Contains(kc)) continue;
                    var fn = (nv[b] - nv[a]).Cross(nv[c] - nv[a]) * sg;   // area weighted
                    foreach (var kk in new[] { ka, kb, kc }) acc[kk] = acc.GetValueOrDefault(kk) + fn;
                }
                var nn = (Vector3[])nrms.Clone();
                for (int i = 0; i < verts.Length; i++)
                    if (acc.TryGetValue(W(verts[i]), out var an) && hit.Contains(W(verts[i])) && an.LengthSquared() > 1e-14f) nn[i] = an.Normalized();
                arr[(int)Mesh.ArrayType.Normal] = nn;
            }
            arr[(int)Mesh.ArrayType.Vertex] = nv; movedAll += nm;
            for (int ci = (int)Mesh.ArrayType.Custom0; ci <= (int)Mesh.ArrayType.Custom3; ci++) arr[ci] = new Variant();
            made.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, null, null, masked.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights);
            made.SurfaceSetMaterial(s, masked.SurfaceGetMaterial(s));
        }
        if (movedAll == 0) { GD.PrintErr("[AVATAR] ear morph '" + _earKind + "' matched no vertex of the " + _sex + " body (re-run tools/blender_gothic/mh_head_assets.py)"); return masked; }
        if (_earMade.Count > 64) _earMade.Clear();   // one per outfit mask x ear kind; never grows without bound
        _earMade[key] = made; return made;
    }
}
