using System.Linq;
using Godot;

namespace TwinRealms;

public partial class Ui
{
    /// <summary>
    /// Focus F-39: the Music part of Settings > Audio. A "now playing" line (kept fresh twice a second) with a Skip button, then every
    /// category of data/music.json with a switch, and its songs with a switch each. A song that is off is never picked; a category that
    /// is off hands its place to the next one that fits (home > town > night > exploring), or to silence (Music.cs).
    /// </summary>
    void MusicSettings(VBoxContainer list)
    {
        Music.Load();
        Header("Music", list);
        var h = OptRow(list, "Now playing", "Songs follow where you are: home, town, night, the open world, dungeons.");
        var now = Lbl(Music.NowPlaying(), 15, GoldC, h); now.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; now.AutowrapMode = TextServer.AutowrapMode.WordSmart; now.VerticalAlignment = VerticalAlignment.Center;
        var skip = Btn("Skip", () => { Music.Skip(); now.Text = Music.NowPlaying(); }, true); skip.CustomMinimumSize = new Vector2(80, 30); skip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; h.AddChild(skip);
        var tick = new Timer { WaitTime = .5, Autostart = true, ProcessMode = ProcessModeEnum.Always }; now.AddChild(tick);
        tick.Timeout += () => { if (IsInstanceValid(now)) now.Text = Music.NowPlaying(); };
        foreach (var c in Music.Cats)
        {
            var cat = c; var songs = Music.Tracks.Where(t => t.Cats.Length > 0 && t.Cats[0] == cat.Id).ToList();
            var also = Music.Tracks.Where(t => t.Cats.Length > 1 && t.Cats[0] != cat.Id && t.Cats.Contains(cat.Id)).Select(t => t.Title).ToList();
            string hint = cat.Hint + (also.Count > 0 ? " Also plays: " + string.Join(", ", also) + "." : "");
            Choice(list, cat.Name + $"  ({songs.Count + also.Count} songs)", hint, OffOn, Music.CatOn(cat.Id) ? 1 : 0, i => Music.SetCat(cat.Id, i == 1));
            foreach (var t in songs)
            {
                var tr = t; string where = tr.Cats.Length > 1 ? "  (also " + string.Join(", ", tr.Cats.Skip(1).Select(Music.CatName)) + ")" : "";
                Choice(list, "      " + tr.Title + where, "", OffOn, Music.TrackOn(tr.Id) ? 1 : 0, i => Music.SetTrack(tr.Id, i == 1));
            }
        }
    }
}
