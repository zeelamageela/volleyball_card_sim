using UnityEngine;
using VolleyballCore;

/// <summary>
/// Which of the four flat facing sprites to show for a camera-billboarded character --
/// the classic front/back/left/right scheme from top-down/2.5D sprite games. Bucketed
/// from a world-space facing direction against the ACTIVE CAMERA's own view, not this
/// player's local axes, so it stays correct no matter which phase camera is currently
/// live or how the two teams' court sides are mirrored.
/// </summary>
public enum SpriteFacing
{
    Front, // body facing the camera -- the viewer sees this player's front
    Back,  // body facing away from the camera -- the viewer sees their back
    Left,  // body facing across, toward the camera's own left
    Right, // body facing across, toward the camera's own right
}

/// <summary>
/// Reference sprite/flipbook controller: subscribes to PlayerTouchReceiver's cues and
/// translates each one into Animator parameters, nothing more -- every actual clip,
/// sub-state, and transition lives in the Animator Controller you build, not here.
/// Safe to attach before any of that exists: with no Animator Controller assigned,
/// every call below is a no-op.
///
/// Sets, on every touch:
///   - a Trigger named exactly after the TouchKind ("Serve", "Receive", "Set",
///     "Attack", "Dig", "Deflect", "Chase", "FreeBallCatch")
///   - an Int "Direction" -- 0=Front, 1=Back, 2=Left, 3=Right (see SpriteFacing)
///   - a Bool "Success" -- see PlayerTouchReceiver.OnTouch's own doc comment for which
///     kinds this is meaningful for
///   - an Int "Shot" -- the ShotKind's own int value (Hit=0, Tip=1, Roll=2,
///     HeavySpin=3, Seam=4), or -1 when this touch carries no shot at all
/// Build the Animator Controller's parameters with exactly these names/types, then
/// whatever states/sub-states/transitions you want off of them -- this component only
/// ever calls Animator.Set*, never touches the state machine directly.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerSpriteAnimator : PlayerTouchReceiver
{
    private static readonly int DirectionParam = Animator.StringToHash("Direction");
    private static readonly int SuccessParam = Animator.StringToHash("Success");
    private static readonly int ShotParam = Animator.StringToHash("Shot");

    // The idle/default facing direction whenever there's nothing to meaningfully face
    // (facingTarget lands right on top of this player) -- real volleyball players
    // stand oriented toward the net at rest, and this player's own parent (its team
    // root) already encodes that as local -Z, the same "local +Z = away from the net"
    // convention GameRunner.GetServeApproach documents.
    [SerializeField] private bool faceNetWhenIdle = true;

    private Animator _animator;

    private void Awake() => _animator = GetComponent<Animator>();

    public override void OnTouch(TouchKind kind, bool success, ShotKind? shot, Vector3 facingTarget)
    {
        if (_animator == null)
        {
            return;
        }
        _animator.SetInteger(DirectionParam, (int)FacingFor(facingTarget));
        _animator.SetBool(SuccessParam, success);
        _animator.SetInteger(ShotParam, shot.HasValue ? (int)shot.Value : -1);
        _animator.SetTrigger(kind.ToString());
    }

    /// <summary>
    /// Buckets facingTarget into one of the four sprite directions, relative to the
    /// active gameplay camera's own view -- so a touch that happens mid-camera-cut
    /// still buckets against whichever camera the player actually sees it through.
    /// </summary>
    private SpriteFacing FacingFor(Vector3 facingTarget)
    {
        Vector3 bodyFacing = Flatten(facingTarget - transform.position);
        if (bodyFacing.sqrMagnitude < 0.0001f)
        {
            bodyFacing = faceNetWhenIdle && transform.parent != null ? Flatten(-transform.parent.forward) : Vector3.forward;
        }
        bodyFacing.Normalize();

        Camera cam = GameRunner.GetActiveGameplayCamera();
        if (cam == null)
        {
            return SpriteFacing.Front;
        }
        Vector3 camForward = Flatten(cam.transform.forward).normalized;
        Vector3 camRight = Flatten(cam.transform.right).normalized;

        float forwardDot = Vector3.Dot(bodyFacing, camForward);
        float rightDot = Vector3.Dot(bodyFacing, camRight);
        if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot))
        {
            // Facing the same way the camera looks means the viewer is looking at this
            // player's back; facing back toward the camera means they see the front.
            return forwardDot > 0f ? SpriteFacing.Back : SpriteFacing.Front;
        }
        return rightDot > 0f ? SpriteFacing.Right : SpriteFacing.Left;
    }

    private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);
}
