/// <summary>
/// Any prop the ghost can interact with implements this interface.
/// GhostInteraction talks only to this — no special-casing per prop type.
///
/// Usage examples: LampFlicker, ThrowableChair, DoorSlam, PictureKnock...
/// </summary>
public interface IInteractable
{
    /// <summary>Energy deducted from the ghost when Interact() is called.</summary>
    float EnergyCost { get; }

    /// <summary>True while the prop is busy (flickering, flying, returning…)
    /// GhostInteraction won't highlight or allow interaction during this time.</summary>
    bool IsBusy { get; }

    /// <summary>Show or hide the selection highlight for this specific ghost.</summary>
    void SetHighlight(bool enabled);

    /// <summary>Trigger the interaction. Called after energy is deducted.</summary>
    /// <param name="ghostTransform">The ghost that triggered it (for throw direction etc.)</param>
    void Interact(UnityEngine.Transform ghostTransform);
}