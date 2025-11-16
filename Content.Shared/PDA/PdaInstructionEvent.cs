//IH - start
using Robust.Shared.GameObjects;

namespace Content.Shared.PDA;

/// <summary>
/// Raised on a PDA entity when the UI gathers instruction text.
/// </summary>
public sealed class PdaCollectInstructionEvent : EntityEventArgs
{
    public bool Handled;
    public string? DisplayText;
    public string? CopyText;
}
//IH - end
