using Godot;

namespace TwinRealms;

/// <summary>The breath bar (F-20): above the hotbar in the middle, only while the head is under water (and briefly while it refills).</summary>
public partial class HudCanvas
{
    float _breathA;
    void DrawBreath(Player p, Vector2 sz, float u, int f, float dt)
    {
        bool show = p.HeadUnder && !p.Dead;
        _breathA = Mathf.MoveToward(_breathA, show ? 1f : 0f, dt * (show ? 6f : 1.5f));
        if (_breathA <= .01f) return;
        float frac = Mathf.Clamp(p.Breath / Player.MaxBreath, 0, 1), bw = u * 18, bh = Mathf.Max(8, u * .8f);
        var r = new Rect2(sz.X / 2 - bw / 2, sz.Y - u * 12f, bw, bh);
        bool low = frac < .25f; bool blink = low && ((int)(Time.GetTicksMsec() / 200) & 1) == 0;
        var col = (low ? new Color("5aa8d8").Lerp(new Color("d8f0ff"), blink ? .6f : 0f) : new Color("5aa8d8")) with { A = _breathA };
        SlantBar(r, frac, col, 5, 12);
        // bubbles: one per second of breath left (12), drawn over the bar's top edge
        for (int i = 0; i < 12; i++)
        {
            float left = p.Breath - i; if (left <= 0) break;
            var c = new Vector2(r.Position.X + 8 + (bw - 16) * (i + .5f) / 12f, r.Position.Y - bh * .9f);
            float rad = bh * .42f * Mathf.Clamp(left, .3f, 1f);
            DrawCircle(c, rad, new Color(.75f, .9f, 1f, .55f * _breathA)); DrawArc(c, rad, 0, Mathf.Tau, 12, new Color(.9f, .97f, 1f, .9f * _breathA), 1.2f);
        }
        Txt(new Vector2(r.Position.X, r.Position.Y - bh * 1.8f - 4), p.Breath <= 0 ? "DROWNING" : "BREATH", f, (p.Breath <= 0 ? new Color("ff8f86") : new Color("bfe4ff")) with { A = _breathA }, HorizontalAlignment.Center, bw);
    }
}
