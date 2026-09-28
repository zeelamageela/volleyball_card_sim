using UnityEngine;
using VolleyballCore;

/// <summary>
/// Which real moment a player touches the ball. One value per distinct kind of touch,
/// not per narrative event -- Core narrates several lines (Resolve/Shot/Outcome) that
/// all belong to the same single Attack touch, for instance, and GameRunner fires
/// exactly one cue for it. See GameRunner.FireTouchCue for where these actually fire.
/// </summary>
public enum TouchKind
{
    Serve,
    Receive,
    Set,
    Attack,
    Dig,
    /// <summary>An exact-tie hit deflecting back onto the attacker's OWN side.</summary>
    Deflect,
    /// <summary>A failed reception's scramble recovery (or the miss, if it isn't recovered).</summary>
    Chase,
    /// <summary>The opposing team catching a recovered chase as it crosses the net.</summary>
    FreeBallCatch,
}

/// <summary>
/// Optional per-player hook for 2D sprite/flipbook animation. Attach this (or your own
/// subclass) to a player's Transform -- the same child GameRunner already finds under
/// Players/AIPlayers and moves around (Formations/.../{role}'s sibling, i.e. the role
/// GameObject itself) -- to get a cue at the exact instant that player touches the
/// ball, with enough context to pick a clip and face the right way.
///
/// Entirely optional: GameRunner fires these unconditionally (see FireTouchCue), but
/// silently does nothing if a player has no PlayerTouchReceiver on it, so none of this
/// needs to exist before a single sprite frame is drawn -- add it to one player at a
/// time as you build each animation out.
///
/// These billboard sprites have no 3D rig to keep in sync with the ball (that concern
/// -- a hand bone drifting from an authored contact point -- is specific to skeletal
/// characters and doesn't apply here), so this carries only what a flipbook controller
/// actually needs: WHEN to trigger, WHICH clip, and WHICH WAY to face.
/// </summary>
public class PlayerTouchReceiver : MonoBehaviour
{
    /// <summary>
    /// Fired the instant this player genuinely touches the ball -- never when the
    /// decision/narrative for it exists, which can be well before the ball has
    /// physically arrived (see GameRunner.FireTouchCue's own doc comment).
    ///
    /// kind: which touch this is -- the top-level pick of what clip family to play.
    ///
    /// success: only meaningful for Receive/Dig/Deflect/Chase -- whether this touch
    /// actually beat what it needed to. A failed touch is still a real, physical touch
    /// (a shanked pass, a whiffed dig) worth its own reaction, not silence. Always true
    /// for Serve/Set/Attack, which don't fail at the moment of contact itself.
    ///
    /// shot: the kind of attack this touch is either playing (Attack) or receiving
    /// (Dig) -- a hit, a tip, a roll shot, etc. (see ShotKind). Null for every other
    /// kind, including Deflect (mechanically just a dig, no shot type of its own).
    ///
    /// facingTarget: a world-space point (not pre-resolved to a left/right flip) to
    /// face or reach toward -- the ball's destination for an outgoing touch (Serve/
    /// Set/Attack), or roughly where it came from/is headed for an incoming one
    /// (Receive/Dig/Deflect/Chase/FreeBallCatch). How that maps to your own sprites --
    /// a horizontal flip, a set of directional clips, ignoring it entirely -- is
    /// entirely up to how you've drawn them; GameRunner doesn't assume a convention.
    /// </summary>
    public virtual void OnTouch(TouchKind kind, bool success, ShotKind? shot, Vector3 facingTarget)
    {
    }
}
