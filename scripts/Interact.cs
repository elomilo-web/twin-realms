using Godot;

namespace TwinRealms;

/// <summary>Anything the player can use with E. Add the node to group "interact".</summary>
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract { get; }
    void Interact();
}
