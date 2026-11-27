using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Character creator face work on the Avatar (focus F-36; data/creator.json). The head models have no blend shapes, so:
/// - face shape, eyes, brows and mouth are SCULPTED: body vertices near a landmark are moved in the bind pose (real geometry; the
///   painted brows and lips ride on the skin). The eyeballs move and grow rigidly with their landmark and the lids are kept outside them.
/// - markings are a TINT: a mask drawn from shapes, projected onto the front of the face as a second pass over the skin.
/// - a chosen eye colour recolours the iris of the eye texture with a small shader.
/// Runs at the end of MaskBody (every Dress); an avatar without creator choices keeps the plain meshes and materials.
/// </summary>
public partial class Avatar
{
    MeshInstance3D? _eyeMi; ArrayMesh? _eyeFull;
    ArrayMesh? _faceFrom; Mesh? _faceMade;   // the masked body this avatar's face was last made from, and the result

    struct Op { public Vector3 C, Move, Scale; public float R2, Rot, Side; }

    /// <summary>The body mesh with this hero's face sculpt and marking channels (or the mesh itself when there is nothing to do).</summary>
    Mesh FaceMesh(ArrayMesh masked)
    {
        var lk = Look.Custom; if (lk == null || _bodyMi == null) return masked;
        if (_faceFrom == masked && _faceMade != null) return _faceMade;
        var raw = Creator.Ops(lk); var mark = Creator.MarkOf(lk);
        _faceFrom = masked; _faceMade = masked;
        if (raw.Count == 0 && mark == null) { SetMark(null, default); return masked; }
        // the eyes anchor everything: measured from the eye mesh (the +x eyeball's box; the other one mirrors it)
        if (_eyeFull == null || _eyeFull.GetSurfaceCount() == 0 || masked.GetBlendShapeCount() > 0 || masked.GetSurfaceCount() == 0) return masked;
        var ev = (Vector3[])_eyeFull.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = -lo; int n = 0;
        foreach (var v in ev) if (v.X > 0) { lo = lo.Min(v); hi = hi.Max(v); n++; }
        if (n < 8) return masked;
        var eye = (lo + hi) * .5f; float eyeR = (hi.X - lo.X + hi.Y - lo.Y) * .25f;
        if (eyeR < .004f || eyeR > .04f) return masked;   // not a pair of human-sized eyeballs
        var mid = new Vector3(0, eye.Y, hi.Z);            // between the eyes, at the front of the eyeballs: the landmarks' origin

        var ops = new List<Op>();
        foreach (var o in raw)
        {
            Vector3 at; if (o.At == "eye") at = eye - mid; else if (Creator.Landmark(o.At) is { } lm) at = lm; else continue;
            if (Mathf.Abs(at.X) < .0005f) ops.Add(new Op { C = mid + new Vector3(0, at.Y, at.Z), Move = new Vector3(0, o.Move.Y, o.Move.Z), Scale = o.Scale, R2 = o.R * o.R, Rot = 0, Side = 0 });
            else foreach (float s in new[] { 1f, -1f })
                    ops.Add(new Op { C = mid + new Vector3(s * at.X, at.Y, at.Z), Move = new Vector3(s * o.Move.X, o.Move.Y, o.Move.Z), Scale = o.Scale, R2 = o.R * o.R, Rot = Mathf.DegToRad(o.Rot) * s, Side = s });
        }
        Vector3 Shift(Vector3 p)
        {
            var d = Vector3.Zero;
            foreach (var o in ops)
            {
                var rel = p - o.C; float d2 = rel.LengthSquared(); if (d2 >= o.R2) continue;
                float w = 1f - d2 / o.R2; w *= w;
                var r2 = rel * Vector3.One.Lerp(o.Scale, w);
                if (o.Rot != 0) { float a = o.Rot * w, c = Mathf.Cos(a), s = Mathf.Sin(a); r2 = new Vector3(r2.X * c - r2.Y * s, r2.X * s + r2.Y * c, r2.Z); }
                d += r2 - rel + o.Move * w;
            }
            return d;
        }
        // each eyeball: moved by what happens at its centre, grown by the (even) scale there
        var eyeAt = new[] { eye, new Vector3(-eye.X, eye.Y, eye.Z) }; var eyeTo = new Vector3[2]; var eyeK = new float[2];
        for (int e = 0; e < 2; e++)
        {
            float k = 1f;
            foreach (var o in ops) { float d2 = (eyeAt[e] - o.C).LengthSquared(); if (d2 >= o.R2) continue; float w = 1f - d2 / o.R2; w *= w; bool even = Mathf.Abs(o.Scale.X - o.Scale.Y) < .001f && Mathf.Abs(o.Scale.Y - o.Scale.Z) < .001f; if (even) k *= Mathf.Lerp(1f, o.Scale.X, w); }   // only an even scale (the eye size slider) grows the ball; shape presets reshape the lids around it
            eyeK[e] = k; eyeTo[e] = eyeAt[e] + Shift(eyeAt[e]);
        }
        bool eyesMoved = false; for (int e = 0; e < 2; e++) if (Mathf.Abs(eyeK[e] - 1f) > .001f || eyeTo[e].DistanceSquaredTo(eyeAt[e]) > 1e-9f) eyesMoved = true;

        // ---- the body: sculpt the head, keep the lids outside the eyeballs, and (for markings) a front projection of the face in UV2
        float bw = Creator.Num("markBox", "w", .18f), bh = Creator.Num("markBox", "h", .24f), bcy = Creator.Num("markBox", "cy", -.03f);
        float headLo = mid.Y - .2f, headHi = mid.Y + .2f;
        var made = new ArrayMesh();
        for (int s = 0; s < masked.GetSurfaceCount(); s++)
        {
            var arr = masked.SurfaceGetArrays(s); var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
            var nrms = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : (Vector3[])arr[(int)Mesh.ArrayType.Normal];
            var nv = new Vector3[verts.Length]; var uv2 = mark != null ? new Vector2[verts.Length] : null;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i]; nv[i] = p;
                if (uv2 != null) uv2[i] = new Vector2(.5f, 20f);   // far off the mask: no marking
                if (p.Y < headLo || p.Y > headHi || Mathf.Abs(p.X) > .16f) continue;   // only the head (the arms pass this height in the bind pose)
                var q = ops.Count > 0 ? p + Shift(p) : p;
                for (int e = 0; e < 2; e++)
                {
                    float d0 = p.DistanceTo(eyeAt[e]); if (d0 < eyeR * .9f || d0 > eyeR * 1.8f) continue;
                    // as far outside the (grown) eyeball as it was before, up to 2 mm; vertices that sat just inside keep their depth
                    float need = d0 >= eyeR ? eyeR * eyeK[e] + Mathf.Min(d0 - eyeR, .002f) : eyeR * eyeK[e] * (d0 / eyeR);
                    var off = q - eyeTo[e]; float dn = off.Length();
                    if (dn < need && dn > 1e-5f) q = eyeTo[e] + off * (need / dn);
                }
                nv[i] = q;
                if (uv2 != null)
                {
                    // a straight-on projection of the face. Skin that faces backwards (the back of the head, behind the ears) is sent far off the
                    // mask instead (v + 20), so nothing shows there. (No vertex colours: imported materials multiply them into the albedo.)
                    float nz = nrms != null && i < nrms.Length ? nrms[i].Normalized().Z : 1f;
                    bool front = nz > -.05f && p.Z > mid.Z - .1f;
                    uv2[i] = new Vector2(.5f + p.X / bw, .5f - (p.Y - (mid.Y + bcy)) / bh + (front ? 0f : 20f));
                }
            }
            arr[(int)Mesh.ArrayType.Vertex] = nv;
            if (uv2 != null) arr[(int)Mesh.ArrayType.TexUV2] = uv2;
            for (int ci = (int)Mesh.ArrayType.Custom0; ci <= (int)Mesh.ArrayType.Custom3; ci++) arr[ci] = new Variant();
            var flags = masked.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
            made.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, null, null, flags);
            made.SurfaceSetMaterial(s, masked.SurfaceGetMaterial(s));
        }
        // ---- the eyeballs
        if (_eyeMi != null && IsInstanceValid(_eyeMi))
        {
            if (!eyesMoved) _eyeMi.Mesh = _eyeFull;
            else
            {
                var em = new ArrayMesh();
                for (int s = 0; s < _eyeFull.GetSurfaceCount(); s++)
                {
                    var arr = _eyeFull.SurfaceGetArrays(s); var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex]; var nv = new Vector3[verts.Length];
                    for (int i = 0; i < verts.Length; i++) { int e = verts[i].X > 0 ? 0 : 1; nv[i] = eyeTo[e] + (verts[i] - eyeAt[e]) * eyeK[e]; }
                    arr[(int)Mesh.ArrayType.Vertex] = nv;
                    for (int ci = (int)Mesh.ArrayType.Custom0; ci <= (int)Mesh.ArrayType.Custom3; ci++) arr[ci] = new Variant();
                    em.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, null, null, _eyeFull.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights);
                    em.SurfaceSetMaterial(s, _eyeFull.SurfaceGetMaterial(s));
                }
                _eyeMi.Mesh = em;
            }
        }
        SetMark(mark, Creator.MarkColor(lk));
        _faceMade = made; return made;
    }

    // ------------------------------------------------------------------ markings (a tint over the skin)
    static Shader? _markShader; static readonly Dictionary<string, ImageTexture> _markTex = new();
    const string MarkCode = "shader_type spatial;\nrender_mode blend_mix, depth_draw_never, cull_back, specular_disabled;\n"
        + "uniform sampler2D mark : filter_linear, repeat_disable;\nuniform vec4 tint : source_color = vec4(0.4, 0.1, 0.1, 1.0);\n"
        + "void vertex() { VERTEX += NORMAL * 0.0004; }\n"   // a hair above the skin so the two passes never fight for depth
        + "void fragment() { float a = texture(mark, UV2).a * tint.a; ALBEDO = tint.rgb; ROUGHNESS = 0.85; ALPHA = a; }\n";

    void SetMark(Creator.MarkDef? mark, Color colour)
    {
        if (_bodyMi == null || !IsInstanceValid(_bodyMi) || _bodyMi.Mesh == null) return;
        for (int s = 0; s < _bodyMi.GetSurfaceOverrideMaterialCount(); s++)
        {
            var m = _bodyMi.GetSurfaceOverrideMaterial(s);
            if (m == null)
            {   // never put a pass on a material other characters share
                if (mark == null || _bodyMi.GetActiveMaterial(s) is not { } shared) continue;
                m = (Material)shared.Duplicate(); _bodyMi.SetSurfaceOverrideMaterial(s, m);
            }
            if (mark == null) { if (m.NextPass is ShaderMaterial old && old.Shader == _markShader) m.NextPass = null; continue; }
            _markShader ??= new Shader { Code = MarkCode };
            var pass = new ShaderMaterial { Shader = _markShader };
            pass.SetShaderParameter("mark", MarkTexture(mark)); pass.SetShaderParameter("tint", colour with { A = Mathf.Clamp(Creator.Num("markBox", "alpha", .85f), .1f, 1f) });
            m.NextPass = pass;
        }
    }

    /// <summary>The marking's mask (alpha) drawn from its shapes; one texture per marking, shared by every hero wearing it.</summary>
    static ImageTexture MarkTexture(Creator.MarkDef mark)
    {
        if (_markTex.TryGetValue(mark.Name, out var have)) return have;
        float bw = Creator.Num("markBox", "w", .18f), bh = Creator.Num("markBox", "h", .24f), bcy = Creator.Num("markBox", "cy", -.03f);
        int W = Mathf.Clamp((int)Creator.Num("markBox", "px", 384), 64, 1024), H = Mathf.Clamp(Mathf.RoundToInt(W * bh / bw), 64, 2048);
        var a = new float[W * H];
        // metres (from the point between the eyes) to pixels and back
        float Px(float x) => (x / bw + .5f) * W; float Py(float y) => (.5f - (y - bcy) / bh) * H;
        float Mx(int i) => ((i + .5f) / W - .5f) * bw; float My(int j) => bcy + (.5f - (j + .5f) / H) * bh;
        void Line(Vector2 p0, Vector2 p1, float wd, float strength)
        {
            float half = wd * .5f, soft = Mathf.Max(.0004f, wd * .18f), pad = half + soft;
            int i0 = Mathf.Max(1, Mathf.FloorToInt(Px(Mathf.Min(p0.X, p1.X) - pad))), i1 = Mathf.Min(W - 2, Mathf.CeilToInt(Px(Mathf.Max(p0.X, p1.X) + pad)));
            int j0 = Mathf.Max(1, Mathf.FloorToInt(Py(Mathf.Max(p0.Y, p1.Y) + pad))), j1 = Mathf.Min(H - 2, Mathf.CeilToInt(Py(Mathf.Min(p0.Y, p1.Y) - pad)));
            var ab = p1 - p0; float l2 = Mathf.Max(ab.LengthSquared(), 1e-10f);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(Mx(i), My(j)); float t = Mathf.Clamp((p - p0).Dot(ab) / l2, 0f, 1f);
                    float d = p.DistanceTo(p0 + ab * t), v = (1f - Mathf.SmoothStep(half - soft, half + soft, d)) * strength;
                    if (v > a[j * W + i]) a[j * W + i] = v;
                }
        }
        if (mark.Has)
            foreach (var sh in mark.Shapes.EnumerateArray())
            {
                bool mirror = sh.TryGetProperty("mirror", out var mv) && mv.ValueKind == JsonValueKind.True;
                if (sh.TryGetProperty("line", out var ln) && ln.ValueKind == JsonValueKind.Array && ln.GetArrayLength() >= 4)
                {
                    var p0 = new Vector2(ln[0].GetSingle(), ln[1].GetSingle()); var p1 = new Vector2(ln[2].GetSingle(), ln[3].GetSingle());
                    float wd = Mathf.Max(.0005f, JsonUtil.F(sh, "w", .003f));
                    Line(p0, p1, wd, 1f); if (mirror) Line(new Vector2(-p0.X, p0.Y), new Vector2(-p1.X, p1.Y), wd, 1f);
                }
                else if (sh.TryGetProperty("dots", out var dt) && dt.ValueKind == JsonValueKind.Object)
                {
                    var c = dt.TryGetProperty("c", out var cv) && cv.ValueKind == JsonValueKind.Array && cv.GetArrayLength() >= 2 ? new Vector2(cv[0].GetSingle(), cv[1].GetSingle()) : Vector2.Zero;
                    float rx = JsonUtil.F(dt, "rx", .04f), ry = JsonUtil.F(dt, "ry", .02f), size = Mathf.Max(.0004f, JsonUtil.F(dt, "size", .001f));
                    int count = Mathf.Clamp((int)JsonUtil.F(dt, "n", 40), 0, 2000); var rng = new Random((int)JsonUtil.F(dt, "seed", 1));
                    for (int k = 0; k < count; k++)
                    {
                        double ang = rng.NextDouble() * Math.PI * 2, rad = Math.Sqrt(rng.NextDouble());
                        var p = c + new Vector2((float)(Math.Cos(ang) * rad) * rx, (float)(Math.Sin(ang) * rad) * ry);
                        float sz = size * (.6f + .9f * (float)rng.NextDouble()), st = .5f + .45f * (float)rng.NextDouble();
                        Line(p, p, sz * 2f, st); if (mirror) Line(new Vector2(-p.X, p.Y), new Vector2(-p.X, p.Y), sz * 2f, st);
                    }
                }
            }
        var data = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { data[i * 4] = 255; data[i * 4 + 1] = 255; data[i * 4 + 2] = 255; data[i * 4 + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(a[i] * 255f), 0, 255); }   // the border row and column stay clear (Line never writes them)
        var tex = ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rgba8, data));
        _markTex[mark.Name] = tex; return tex;
    }

    // ------------------------------------------------------------------ eye colour
    static Shader? _irisShader;
    const string IrisCode = "shader_type spatial;\nrender_mode cull_disabled;\n"
        + "uniform sampler2D tex : source_color, filter_linear_mipmap;\nuniform vec4 iris : source_color = vec4(0.3, 0.5, 0.7, 1.0);\n"
        + "uniform float iris_r = 0.086;\nuniform float gain = 6.0;\nuniform float glow = 0.0;\n"
        + "void fragment() {\n vec3 t = texture(tex, UV).rgb;\n float r = length(UV - vec2(0.5));\n float m = 1.0 - smoothstep(iris_r * 0.9, iris_r * 1.04, r);\n"
        + " float lum = min(dot(t, vec3(0.299, 0.587, 0.114)) * gain, 1.1);\n vec3 ic = iris.rgb * lum;\n ALBEDO = mix(t, ic, m);\n ROUGHNESS = 0.14;\n SPECULAR = 0.5;\n EMISSION = ic * m * glow;\n}\n";

    /// <summary>
    /// A chosen eye colour: the eyeball keeps its texture (white, pupil, the iris pattern) and the iris takes the colour; glowing races
    /// glow from the iris. False when the eye mesh has no texture to work from (the caller keeps the old look).
    /// </summary>
    static bool IrisEyes(MeshInstance3D mi, Appearance ap)
    {
        if (mi.Mesh == null) return false; bool any = false;
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            if (mi.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D b || b.AlbedoTexture == null) continue;
            _irisShader ??= new Shader { Code = IrisCode };
            var m = new ShaderMaterial { Shader = _irisShader };
            m.SetShaderParameter("tex", b.AlbedoTexture); m.SetShaderParameter("iris", ap.Eyes);
            m.SetShaderParameter("iris_r", Creator.Num("iris", "irisR", .086f)); m.SetShaderParameter("gain", Creator.Num("iris", "irisGain", 6f));
            m.SetShaderParameter("glow", ap.GlowEyes ? Creator.Num("iris", "glow", 1.6f) : 0f);
            mi.SetSurfaceOverrideMaterial(s, m); any = true;
        }
        return any;
    }
}
