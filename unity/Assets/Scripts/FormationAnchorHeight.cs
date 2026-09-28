using UnityEngine;

/// <summary>
/// Per-anchor override for a Swing flight's arc peak height, attached to Attack-phase
/// formation anchors (see FormationSetupWindow.GenerateAnchors). A plain MonoBehaviour
/// on a plain GameObject so Unity's stock Inspector already gives a free, editable
/// field the moment the anchor is selected -- no custom editor UI needed for this.
/// </summary>
public sealed class FormationAnchorHeight : MonoBehaviour
{
    // -1 = not overridden -- GameRunner.GetFormationPeakHeight falls back to
    // BallFlight's own DefaultPeakHeight when this is negative.
    public float peakHeight = -1f;
}
