using Godot;

namespace TwinRealms;

/// <summary>
/// Macros (two per hero, keys Macro 1 / Macro 2 in Settings > Controls, set up in Skills > Macros) and auto potions.
///  - A macro is a chain of learned skills; they need not be on the hotbar. Each step waits for the previous skill to finish
///    (wind-up, swing, leap), then for the macro's gap plus the step's own delay.
///  - Loop modes (Macro.Loop): 0 tap = one pass, hold = loops while held; 1 tap starts looping, tap again stops; 2 always one pass.
///  - While looping, a skill that is not ready (cooldown, mana, stamina) is skipped. A single pass waits for it up to
///    Macro.MaxWait seconds, then skips it.
///  - Aimed ground skills land at the crosshair; charged spells fire uncharged. Using a hotbar skill, rolling or dying stops it.
/// Auto potions (Settings > Game): below the chosen share of health or mana, drink one, at most once per AutoPotDelay seconds.
/// </summary>
public partial class Player
{
    int _mRun = -1, _mStep; float _mGapT, _mWaitT; bool _mToggle; readonly bool[] _mDown = new bool[2];
    float _autoHpT, _autoMpT;
    /// <summary>Skills cast by macros since the game started (tests).</summary>
    public int MacroCasts;

    static string MacroAction(int m) => m == 0 ? Inputs.Macro1 : Inputs.Macro2;

    /// <summary>For the HUD: "Macro 1  2/4  looping", or "" when no macro runs.</summary>
    public string MacroStatus
    {
        get
        {
            if (_mRun < 0) return "";
            var mac = Game.D.MacroAt(_mRun); string name = mac.Name != "" ? mac.Name : $"Macro {_mRun + 1}";
            return $"{name}  {System.Math.Min(_mStep + 1, mac.Steps.Count)}/{mac.Steps.Count}" + (MacroLooping(mac, true) ? "  looping" : "");
        }
    }

    bool MacroLooping(Macro mac, bool captured) => mac.Loop switch
    {
        0 => captured && Input.IsActionPressed(MacroAction(_mRun)),
        1 => _mToggle,
        _ => false,
    };

    public void StopMacro() { _mRun = -1; _mStep = 0; _mGapT = 0; _mWaitT = 0; _mToggle = false; }

    void MacroKeys(bool captured, float dt)
    {
        for (int m = 0; m < 2; m++)
        {
            // a press = the key went down since the last physics frame (more reliable than "just pressed" for very short taps)
            bool down = captured && Input.IsActionPressed(MacroAction(m)), was = _mDown[m]; _mDown[m] = down;
            if (!down || was) continue;
            var mac = Game.D.MacroAt(m);
            if (_mRun == m && mac.Loop == 1) { StopMacro(); Ui.Note($"{(mac.Name != "" ? mac.Name : $"Macro {m + 1}")} stopped", UiTheme.Dim); continue; }
            if (mac.Steps.Count == 0) { Ui.Note($"Macro {m + 1} is empty: set it up in Skills ({Inputs.KeyOf(Inputs.SkillsUi)}) > Macros", new Color(.7f, .8f, 1)); continue; }
            _mRun = m; _mStep = 0; _mGapT = 0; _mWaitT = 0; _mToggle = mac.Loop == 1;
        }
        if (_mRun < 0) return;
        if (Dead || Mounted || _holdSlot >= 0) { StopMacro(); return; }   // a hotbar skill being aimed or charged takes over
        var cur = Game.D.MacroAt(_mRun);
        if (cur.Steps.Count == 0) { StopMacro(); return; }
        bool looping = MacroLooping(cur, captured);
        if (_mStep >= cur.Steps.Count)
        {
            if (!looping) { StopMacro(); return; }
            _mStep = 0;
        }
        if (_mGapT > 0) { _mGapT -= dt; return; }
        if (Busy || _rollT > 0 || _stagger > 0) return;   // the previous skill (or a dodge) is still going: wait for it
        var step = cur.Steps[_mStep]; var k = Skills.Get(step.Skill);
        if (k == null || !Game.D.Skills.ContainsKey(step.Skill)) { _mStep++; _mWaitT = 0; return; }   // unlearned since: skip
        if (CanCast(step.Skill, true))
        {
            Vector3? at = Aimed(k) ? AimGround(k) : null;
            if (CastSkill(step.Skill, at, 0, true)) { MacroCasts++; _mGapT = Mathf.Max(0, cur.Gap) + Mathf.Max(0, step.Delay); _mStep++; _mWaitT = 0; return; }
        }
        // not ready: skip it while looping, wait a while on a single pass
        _mWaitT += dt;
        if (looping || _mWaitT >= cur.MaxWait) { _mStep++; _mWaitT = 0; }
    }

    void AutoPotions(float dt)
    {
        _autoHpT -= dt; _autoMpT -= dt;
        if (Dead) return;
        var s = Game.Set; var D = Game.D;
        if (s.AutoHp > 0 && _autoHpT <= 0 && D.HpPot > 0 && Hp < D.MaxHp() * s.AutoHp) { UsePotion(true); _autoHpT = Mathf.Max(.5f, s.AutoPotDelay); }
        if (s.AutoMp > 0 && _autoMpT <= 0 && D.MpPot > 0 && Mp < D.MaxMp() * s.AutoMp) { UsePotion(false); _autoMpT = Mathf.Max(.5f, s.AutoPotDelay); }
    }
}
