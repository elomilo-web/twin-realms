using Godot;

namespace TwinRealms;

/// <summary>Registers all input actions in code so project.godot stays clean.</summary>
public static class Inputs
{
    public const string Left = "move_left", Right = "move_right", Forward = "move_forward", Back = "move_back";
    public const string Jump = "jump", Sprint = "sprint", Roll = "roll", Release = "release_mouse";

    public static void Setup()
    {
        Bind(Left, Godot.Key.A); Bind(Right, Godot.Key.D); Bind(Forward, Godot.Key.W); Bind(Back, Godot.Key.S);
        Bind(Jump, Godot.Key.Space); Bind(Sprint, Godot.Key.Shift); Bind(Roll, Godot.Key.C);
        Bind(Release, Godot.Key.Escape);
    }

    static void Bind(string action, Godot.Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var ev = new InputEventKey { PhysicalKeycode = key };
        InputMap.ActionAddEvent(action, ev);
    }
}
