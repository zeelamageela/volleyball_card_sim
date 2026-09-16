using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the ball on a real projectile arc (Rigidbody + gravity), not a flat
/// Vector3.Lerp, using Forrest Smith's public-domain ballistic solver (fts.cs,
/// see Assets/Scripts/fts.cs). Given a destination and a desired peak height,
/// FlyTo() computes the launch velocity and a *per-shot* gravity value, then
/// integrates that gravity manually every FixedUpdate -- the Rigidbody's own
/// gravity is disabled, since Physics.gravity is one shared global value and
/// every shot here needs its own arc shape (a serve arcs differently than a
/// hard-driven hit), not a fixed constant.
///
/// Optionally, a flight can pause partway through: once it's covered
/// pauseAtFraction of the distance, it eases into a genuine slow-motion hold --
/// the *same* arc keeps running, just at a tiny fraction of real speed, not a
/// stop-and-relaunch -- and stays there until Resume() is called, then eases back
/// up to full speed and finishes. This is driven by a local, per-object "flight
/// clock" that advances at a scaled rate, not the global Time.timeScale -- only
/// the ball slows down, nothing else in the game does.
///
/// The ball sits isKinematic at rest between touches (so it isn't nudged by
/// stray physics) and only becomes a real simulated body for the duration of
/// each flight. Its collider is a trigger (set once in the Editor) so it never
/// physically bounces off a player or the net mid-flight -- the outcome of each
/// touch is already decided by the card resolution before the ball ever moves;
/// this is presentation, not gameplay-affecting physics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallFlight : MonoBehaviour
{
    [SerializeField] private float defaultLateralSpeed = 8f;
    [SerializeField] private float defaultPeakHeight = 2.5f;

    [Header("Slow-motion hold (when FlyTo is given a pauseAtFraction)")]
    [SerializeField] private float pausedTimeScale = 0.03f; // "barely moving" -- fraction of real speed while held
    [SerializeField] private float slowdownWindowFraction = 0.25f; // start easing toward the hold this fraction of THIS flight's own duration before the pause point -- a fixed absolute duration would dominate a short leg (e.g. receiver->setter) and start the ease almost immediately, so it's scaled per-flight instead
    [SerializeField] private float timeScaleRampSpeed = 2f; // how fast local time scale eases toward its target, per real second
    [SerializeField] private float holdSettleDuration = 0.6f; // real seconds for residual momentum to bleed off once genuinely held -- a human's decision time is unbounded, so the held velocity can't just stay constant forever or it drifts an unbounded distance; it settles to a true rest instead

    [Header("Serve toss (TossVertical)")]
    [SerializeField] private float tossGravity = 20f; // independent of each lateral flight's own solved gravity -- this is a fixed, presentation-only constant for the toss's up/down motion

    private Rigidbody _rb;

    // Per-flight state, reset at the start of each FlyTo call.
    private Vector3 _velocity;        // "true" velocity, evolving at the flight's own scaled rate
    private float _currentGravity;
    private bool _inFlight;
    private float _elapsedFlightTime; // scaled flight-clock time elapsed
    private float _totalFlightTime;
    private float _pauseAtFlightTime; // +infinity if this flight never pauses
    private float _slowdownWindow;    // this flight's own eased-approach duration, derived from slowdownWindowFraction
    private float _localTimeScale;
    private bool _resumeRequested;
    private float _holdTime; // real seconds spent genuinely at the hold cap, for the settle decay

    /// <summary>
    /// True from the start of FlyTo until it finishes (including while held mid-flight).
    /// A caller that wants to start a new flight the moment a decision goes live -- before
    /// the *previous* flight's resumed tail end has necessarily finished playing out --
    /// needs to wait for this to go false first: FlyTo isn't reentrant-safe, since a second
    /// call would overwrite the first's in-progress state on the same Rigidbody.
    /// </summary>
    public bool IsInFlight => _inFlight;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // we apply our own per-shot gravity in FixedUpdate instead
        _rb.isKinematic = true;
    }

    private void FixedUpdate()
    {
        if (!_inFlight)
        {
            return;
        }

        if (!_resumeRequested)
        {
            // Ease toward "barely moving" directly as a function of how far through
            // _slowdownWindow we are (a flight-clock quantity), not a fixed real-seconds
            // rate -- timeScaleRampSpeed's real-time rate takes ~0.5s to fully ease down
            // regardless of the flight's own length, so on a short leg (the
            // receiver->setter leg is much shorter than the serve->receiver one) the ramp
            // couldn't finish before elapsed reached the pause point: the ball was still
            // moving at a large fraction of full speed right as the hold "started," and
            // only actually stopped once the hold-settle decay below finished catching up
            // -- a real, visible overshoot past the intended pause point. Computing the
            // scale directly from remaining/_slowdownWindow guarantees it reaches
            // pausedTimeScale exactly by the moment elapsed reaches the pause point, no
            // matter how short the leg is.
            if (_slowdownWindow > 0f)
            {
                float remaining = _pauseAtFlightTime - _elapsedFlightTime;
                float rampProgress = 1f - Mathf.Clamp01(remaining / _slowdownWindow);
                _localTimeScale = Mathf.Lerp(1f, pausedTimeScale, rampProgress);
            }
            else
            {
                // No pause point on this flight at all (_slowdownWindow is 0 precisely
                // for that case -- see FlyTo) -- stay at full speed the whole way, don't
                // ease down.
                _localTimeScale = 1f;
            }
        }
        else
        {
            _localTimeScale = Mathf.MoveTowards(_localTimeScale, 1f, timeScaleRampSpeed * Time.fixedDeltaTime);
        }

        // Once genuinely at the hold cap (not yet resumed), _velocity itself must
        // stop accumulating gravity too, not just the elapsed-time bookkeeping --
        // otherwise it keeps growing every real frame no matter how long the human
        // takes to decide, turning "barely moving" into an ever-accelerating
        // freefall the longer the hold lasts. Frozen at whatever it naturally was
        // the instant the cap was first reached, scaled by the tiny time scale, it
        // reads as a small constant creep instead -- genuinely "barely moving," not
        // "still in freefall, just slower."
        bool atHoldCap = !_resumeRequested && _elapsedFlightTime >= _pauseAtFlightTime;
        if (!atHoldCap)
        {
            _holdTime = 0f;
            float dt = Time.fixedDeltaTime * _localTimeScale;
            _velocity += Vector3.down * (_currentGravity * dt);
            _elapsedFlightTime = _resumeRequested
                ? _elapsedFlightTime + dt
                : Mathf.Min(_elapsedFlightTime + dt, _pauseAtFlightTime);
            _rb.linearVelocity = _velocity * _localTimeScale;
        }
        else
        {
            // A human's decision time is unbounded, so the frozen velocity can't just
            // stay constant forever, or the ball drifts arbitrarily far the longer
            // they take -- ease it down to a true rest over holdSettleDuration instead,
            // so a long hold settles rather than keeps creeping.
            _holdTime += Time.fixedDeltaTime;
            float settle = Mathf.Clamp01(_holdTime / holdSettleDuration);
            _rb.linearVelocity = Vector3.Lerp(_velocity * _localTimeScale, Vector3.zero, settle);
        }
    }

    /// <summary>
    /// Lets a held flight ease back up to full speed and finish. Safe to call at
    /// any time, including when nothing is in flight or the current flight never
    /// pauses -- it only has an effect if a hold is active or about to start.
    /// </summary>
    public void Resume() => _resumeRequested = true;

    /// <summary>
    /// How long TossVertical(peakOffset) will take to complete, so a caller (the serve
    /// toss's accompanying player hop, currently) can size its own animation to finish at
    /// exactly the same moment.
    /// </summary>
    public float TossDuration(float peakOffset) => 2f * Mathf.Sqrt(2f * peakOffset / tossGravity);

    /// <summary>
    /// A simple, presentation-only vertical toss for the serve: eases straight up by
    /// peakOffset above the ball's current height, then falls back down through that same
    /// starting height. Deliberately NOT routed through FlyTo/FixedUpdate -- that state
    /// machine is tightly coupled to the lateral-flight pause/hold/slow-motion fields
    /// (_pauseAtFlightTime, _slowdownWindow, etc.), none of which apply to a toss with no
    /// lateral component and no hold, so reusing it would just be unnecessary risk to
    /// logic that took real effort to get right. The ball stays isKinematic the whole
    /// time (moved by hand via transform.position, exactly like it already sits at rest
    /// between touches) rather than becoming a real simulated Rigidbody for this.
    /// </summary>
    public IEnumerator TossVertical(float peakOffset)
    {
        float startY = transform.position.y;
        float vy = Mathf.Sqrt(2f * tossGravity * peakOffset);
        while (true)
        {
            vy -= tossGravity * Time.deltaTime;
            Vector3 pos = transform.position;
            pos.y += vy * Time.deltaTime;
            transform.position = pos;
            if (vy < 0f && pos.y <= startY)
            {
                break;
            }
            yield return null;
        }
        // Correct the last step's small overshoot below startY -- the loop above stops
        // the frame it first crosses back under the starting height, not exactly at it.
        Vector3 finalPos = transform.position;
        finalPos.y = startY;
        transform.position = finalPos;
    }

    /// <summary>
    /// Launches the ball on a real arc toward destination, peaking at peakHeight
    /// above the higher of the start/destination points. If pauseAtFraction is in
    /// (0,1), the flight eases into a slow-motion hold once it's covered that
    /// fraction of the distance and waits there until Resume() is called before
    /// finishing. Snaps precisely to destination once done -- physics integration
    /// accumulates small error over a flight, so this corrects it rather than
    /// trusting wherever the simulation actually ended up.
    /// </summary>
    public IEnumerator FlyTo(Vector3 destination, float peakHeight = -1f, float lateralSpeed = -1f, float pauseAtFraction = -1f)
    {
        if (peakHeight < 0f)
        {
            peakHeight = defaultPeakHeight;
        }
        if (lateralSpeed < 0f)
        {
            lateralSpeed = defaultLateralSpeed;
        }

        Vector3 start = transform.position;
        Vector3 startXZ = new(start.x, 0f, start.z);
        Vector3 destXZ = new(destination.x, 0f, destination.z);
        float lateralDist = Vector3.Distance(startXZ, destXZ);

        if (lateralDist < 0.01f)
        {
            // No real lateral travel (e.g. a dig that stays with the same player) --
            // an arc has nothing to arc over, so just place it directly.
            transform.position = destination;
            yield break;
        }

        // fts's max_height is an absolute world-space Y, not a relative offset -- it
        // must be strictly above the higher of the two endpoints or its own internal
        // assert fires (Debug.Assert logs but doesn't stop execution, so a bad call
        // here silently produced a nonsensical arc instead of a clean failure).
        float absolutePeakHeight = Mathf.Max(start.y, destination.y) + peakHeight;
        bool solved = fts.solve_ballistic_arc_lateral(
            start, lateralSpeed, destination, absolutePeakHeight, out Vector3 fireVelocity, out float gravity);
        if (!solved)
        {
            transform.position = destination;
            yield break;
        }

        _rb.isKinematic = false;
        _velocity = fireVelocity;
        _currentGravity = gravity;
        _totalFlightTime = lateralDist / lateralSpeed;
        _pauseAtFlightTime = pauseAtFraction > 0f && pauseAtFraction < 1f
            ? _totalFlightTime * pauseAtFraction
            : float.PositiveInfinity;
        // Scaled to THIS flight's own pre-pause segment (not a fixed absolute duration) --
        // otherwise a short leg's whole pre-pause segment could be shorter than the window,
        // and the ease-down would start almost immediately instead of near the pause point.
        // A flight with no pause point at all (_pauseAtFlightTime = +infinity) must get a
        // finite (zero) window here -- infinity * fraction is still infinity, and
        // "remaining <= window" with both sides infinite would otherwise read as true,
        // wrongly slow-motioning a flight that was never supposed to pause.
        _slowdownWindow = float.IsPositiveInfinity(_pauseAtFlightTime) ? 0f : _pauseAtFlightTime * slowdownWindowFraction;
        _elapsedFlightTime = 0f;
        _localTimeScale = 1f;
        _resumeRequested = false;
        _holdTime = 0f;
        _inFlight = true;

        yield return new WaitUntil(() => _elapsedFlightTime >= _totalFlightTime);

        _inFlight = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        transform.position = destination;
    }
}
