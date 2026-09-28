using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the ball on a real projectile arc, not a flat Vector3.Lerp, using Forrest
/// Smith's public-domain ballistic solver (fts.cs, see Assets/Scripts/fts.cs). Given a
/// destination and a desired peak height, FlyTo() computes the launch velocity and a
/// *per-shot* gravity value -- every shot needs its own arc shape (a serve arcs
/// differently than a hard-driven hit), not a fixed constant -- then, every Update,
/// evaluates that same closed-form parabola directly at the flight's current elapsed
/// time and moves the (always-kinematic, never physics-simulated) transform there
/// directly. Deliberately NOT simulated by feeding gravity into a Rigidbody's own
/// velocity step by step: a short, steep flight (small lateral distance relative to
/// lateral speed) can solve for a total flight time shorter than a single physics
/// step, and integrating velocity one full step at a time on a flight that short
/// overshot the landing point by several meters before FlyTo's own final
/// snap-to-destination corrected it -- confirmed live as the cause of the ball
/// visibly clipping through the floor/net. Evaluating position analytically has no
/// such step-size dependency, since it's exactly on-curve (clamped to the flight's own
/// time domain) regardless of how short the flight or how coarse the step. Driven from
/// Update() rather than FixedUpdate specifically so the position update itself doesn't
/// slow down (in real-time call frequency) the way FixedUpdate's own would once
/// Time.timeScale drops during the hold below -- see Update()'s own doc comment.
///
/// Deliberately just ONE arc shape (fts's exactly-symmetric solve) for every flight,
/// tuned only by peakHeight and lateralSpeed -- an earlier pass added an asymmetric
/// skewed-apex mode and a separate pacing-remap curve on top of this, and pulled them
/// back out: with both layered on top of each other across half a dozen legs, each
/// tuned differently, the rally stopped reading as one consistent, predictable game
/// and started reading as a different, unrelated animation every time. Same symmetric
/// arc, every leg, every time -- a real rebuild if it turns out this needs those two
/// mechanisms back, not a quiet re-add.
///
/// Optionally, a flight can pause partway through: once it's covered
/// pauseAtFraction of the distance, it eases into a genuine slow-motion hold by
/// ramping the GLOBAL Time.timeScale down (see slowMotionByPostPauseProgress),
/// not a local per-object clock -- ported from an earlier prototype (VBall_Test_1)
/// whose distance-staged, everything-slows-together hold was the whole point of
/// porting this over. Global timeScale means player movement (setter runs, hitter
/// steps -- anything driven by Time.deltaTime or WaitForSeconds) eases toward
/// frozen right alongside the ball for free, with no extra wiring needed on their
/// end. UI input is untouched either way: Unity's Update()/EventSystem loop that
/// drives OnGUI buttons and UGUI drag-and-drop runs every real frame regardless of
/// Time.timeScale -- only physics and Time.deltaTime-scaled code slows down.
/// Resume() snaps straight back to full speed (matching the old prototype's own
/// instant un-pause on click), not an eased recovery. This pause is the ONLY thing
/// that should ever hold a flight up -- every call site is expected to pass
/// pauseAtFraction/stillPending only where a real player decision is genuinely
/// pending, never as a pacing/cosmetic device.
///
/// The ball stays isKinematic always -- at rest between touches and during every
/// flight alike -- so it's never nudged by stray physics and nothing here depends on
/// Unity's physics engine actually simulating it. Its collider is a trigger (set once
/// in the Editor, and unused -- nothing in this codebase has an OnTrigger callback on
/// the ball) so it never physically bounces off a player or the net mid-flight -- the
/// outcome of each touch is already decided by the card resolution before the ball
/// ever moves; this is presentation, not gameplay-affecting physics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BallFlight : MonoBehaviour
{
    [SerializeField] private float defaultLateralSpeed = 8f;
    [SerializeField] private float defaultPeakHeight = 2.5f;

    // Exposed so external callers (GameRunner's hitter-arrival timing estimate, both
    // the runtime and editor trajectory previews) can match a flight that doesn't
    // override these explicitly, rather than duplicating/guessing the values.
    public float DefaultLateralSpeed => defaultLateralSpeed;
    public float DefaultPeakHeight => defaultPeakHeight;

    [Header("Slow-motion hold (when FlyTo is given a pauseAtFraction)")]
    // X = progress from the pause point (0) to this flight's own nominal completion
    // (1), Y = the (global) Time.timeScale to hold at -- evaluated every FixedUpdate
    // once the flight passes its pause point, so the whole cascading shape (not just
    // one endpoint) is tunable live, including in Play mode. Never quite reaches a
    // literal 0 in the default curve -- like the prototype this was ported from, it
    // stays a genuine (if tiny) creep, not a hard freeze, so a very long decision
    // doesn't read as the game having hung.
    //
    // A plain EaseInOut(0,1, 1,0.02) has zero slope at BOTH ends -- confirmed live
    // that this leaves it near full speed for a good while right after the pause point,
    // reading as "the ball hits just after the pause" instead of genuinely suspended
    // with room still left to travel. The steep initial outSlope on the first key below
    // makes it drop hard immediately instead of easing into the slowdown.
    //
    // The floor also matters more than it looks: this isn't a true pause, just a very
    // slow creep, so simulated flight time still accumulates the whole time a decision
    // sits unanswered. Confirmed live that a ~0.02-0.03 floor let the flight fully
    // complete ON ITS OWN after roughly 20 real seconds of an unanswered decision --
    // easily within normal thinking time, and exactly what "still pausing too close to
    // the target" was actually seeing once a few seconds had passed. Dropping the floor
    // this much further pushes that same full-creep time out past several minutes,
    // while keeping the very-long-idle "not a hang" comfort the original design wanted.
    [SerializeField] private AnimationCurve slowMotionByPostPauseProgress = new(
        new Keyframe(0f, 1f, 0f, -15f),
        new Keyframe(0.08f, 0.01f, 0f, 0f),
        new Keyframe(1f, 0.001f, 0f, 0f));

    // The furthest into a flight (as a fraction of its own total, NOT of the
    // post-pause remainder) the slow-motion creep may carry the ball during a hold,
    // whatever pauseAtFraction that flight was given -- a hard ceiling, independent of
    // each leg's own default pause point (GameRunner's serveReceptionPauseFraction and
    // friends), so a long-held decision can never creep the ball into someone's hands
    // no matter how that default is tuned later. Confirmed live that without ANY cap, a
    // short, fast flight's creep (the receive->set leg holds for several chained
    // decisions: Set, HitCards, AttackLane) carried the ball from its hold point all
    // the way into the setter's hands within a few seconds -- the curve's creep is a
    // rate in flight-seconds, and that leg's last stretch is only ~0.025s of flight.
    [SerializeField, Range(0f, 1f)] private float absoluteMaxHoldFraction = 0.9f;

    [Header("Serve toss (TossTo)")]
    [SerializeField] private float tossGravity = 20f; // independent of each lateral flight's own solved gravity -- this is a fixed, presentation-only constant for the toss's up/down motion

    [Header("Bounce on arrival")]
    // A small scripted hop right where the ball lands, not a real physics collision --
    // ported from an earlier prototype (VBall_Test_1) that gave the ball a genuinely
    // bouncy PhysicMaterial. Doing that here would mean giving up the trigger collider
    // and isKinematic-between-touches setup that keeps every outcome fully decided by
    // card resolution rather than by physics, which isn't worth it for a cosmetic
    // touch -- so this fakes the same feel with a quick vertical hop in place instead.
    [SerializeField] private float bounceHeightPerSpeed = 0.03f; // scales with the ball's own speed at arrival, so a harder incoming shot bounces a little higher
    [SerializeField] private float maxBounceHeight = 0.6f;
    [SerializeField] private float bounceGravity = 30f; // independent of tossGravity/each flight's own solved gravity -- a quick, snappy little hop, not a lazy toss

    [Header("Trail")]
    // A plain TrailRenderer, always emitting -- no per-flight on/off wiring needed:
    // it only ever draws behind actual motion, so a serve toss, a receive, a set, an
    // attack, a dig, all read the same way for free, and it fades to nothing on its
    // own the moment the ball settles (once `trailTime` seconds pass with no
    // movement). Built in code rather than authored on the prefab so every ball in
    // the scene gets a consistent trail with no per-object setup.
    [SerializeField] private float trailTime = 0.3f; // how long (seconds) the trail persists behind the ball
    [SerializeField] private float trailStartWidth = 0.18f;
    [SerializeField] private float trailEndWidth = 0f; // tapers to a point
    [SerializeField] private Color trailColor = new(1f, 1f, 1f, 0.85f);

    private Rigidbody _rb;

    // Per-flight state, reset at the start of each FlyTo call. Position is computed
    // analytically from these (same closed-form parabola fts solved, and the same one
    // fts.ComputeArcPreviewPoints samples for the editor/runtime preview arcs) rather
    // than integrated step-by-step via Rigidbody velocity -- confirmed live that a
    // short, steep flight (small lateralDist relative to lateralSpeed, e.g. a nearby
    // dig/chase) can solve for a very short _totalFlightTime, sometimes shorter than a
    // single Time.fixedDeltaTime step. Integrating velocity one full fixed step at a
    // time on a flight that short blew straight through the landing point -- ball
    // observed many meters below the floor for a frame before FlyTo's own final
    // "snap to destination" corrected it. Evaluating the exact parabola at the current
    // elapsed time has no such step-size dependency: it's exactly on-curve (and clamped
    // to the [0, _totalFlightTime] domain) no matter how short the flight or how coarse
    // the physics step, so it can't overshoot past the destination.
    private Vector3 _flightStart;
    private Vector3 _fireVelocity;
    private float _currentGravity;
    private bool _inFlight;
    private float _elapsedFlightTime;
    private float _totalFlightTime;
    private float _pauseAtFlightTime; // +infinity if this flight never pauses
    private bool _resumeRequested;
    // Where the current flight ends and whether it bounces there -- fields rather than
    // FlyTo locals so RunThroughToFloor can extend a flight already underway.
    private Vector3 _flightDestination;
    private bool _flightAllowBounce;
    // Evaluated live, every frame from the pause point on: the flight holds there for
    // as long as this returns true (checked AT the pause point, not at launch, so a
    // decision that only goes live mid-flight still catches it), and releases the
    // instant it goes false -- no separate Resume() signal to race. slowMoWhile picks
    // HOW it holds: true (a decision is on screen) eases the whole game into the
    // global slow-motion creep below; false (between two chained decisions, e.g. Set
    // -> HitCards -> AttackLane) hovers the ball dead still in the air while
    // everything else keeps running at full speed.
    private System.Func<bool> _holdWhile;
    private System.Func<bool> _slowMoWhile;
    private bool _holding;

    /// <summary>True while the current flight is parked at its pause point.</summary>
    public bool IsHolding => _inFlight && _holding;

    /// <summary>
    /// True from the start of FlyTo until it finishes (including while held mid-flight).
    /// A caller that wants to start a new flight the moment a decision goes live -- before
    /// the *previous* flight's resumed tail end has necessarily finished playing out --
    /// needs to wait for this to go false first: FlyTo isn't reentrant-safe, since a second
    /// call would overwrite the first's in-progress state on the same Rigidbody.
    /// </summary>
    public bool IsInFlight => _inFlight;

    /// <summary>
    /// Seconds left until the current flight lands, 0 when not in flight. Used to size a
    /// defender's run-to-meet-the-ball duration against the flight it's racing, without
    /// needing to re-derive the flight's own lateralDist/lateralSpeed math.
    /// </summary>
    public float RemainingFlightTime => _inFlight ? Mathf.Max(0f, _totalFlightTime - _elapsedFlightTime) : 0f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // we apply our own per-shot gravity analytically in Update instead
        _rb.isKinematic = true;
        // Defensive reset in case a previous Play session ended mid-hold without ever
        // calling Resume() (e.g. Play Mode stopped while a human decision was still
        // pending) -- confirmed this session that stray global state can otherwise
        // linger in ways that are surprising to debug later.
        Time.timeScale = 1f;

        SetUpTrail();
    }

    /// <summary>
    /// Same shader as the trajectory-preview arcs (GameRunner.GetOrCreateTrajectoryLine)
    /// -- already confirmed rendering correctly in this project's URP setup, so reusing
    /// it here avoids guessing at a new material. Built fresh each Awake rather than
    /// relying on a component authored in the scene, so the trail's own tuning lives in
    /// one place (this script) instead of drifting between the prefab and here.
    /// </summary>
    private void SetUpTrail()
    {
        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.time = trailTime;
        trail.startWidth = trailStartWidth;
        trail.endWidth = trailEndWidth;
        trail.minVertexDistance = 0.05f;
        trail.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(trailColor.a, 0f), new GradientAlphaKey(0f, 1f) },
        };
        trail.emitting = true;
        trail.autodestruct = false;
    }

    /// <summary>
    /// Driven from Update(), not FixedUpdate -- confirmed live this was the actual
    /// cause of the pause hold reading as "herky-jerky," and it's a distinct issue
    /// from why FixedUpdate was avoided for the ANALYTIC MATH (see the class-level
    /// comment on the per-flight state fields above; that reasoning is about
    /// step-size-dependent overshoot, not this). Time.timeScale controls how often
    /// FixedUpdate is called *in real time* -- its simulated step (Time.fixedDeltaTime)
    /// never shrinks, so at the hold's ~0.01 floor, FixedUpdate's real-world interval
    /// stretches from 20ms out to roughly 2 real SECONDS, and since position was only
    /// recomputed there, the ball visibly sat frozen that whole span and then snapped.
    /// Update() runs every real rendered frame regardless of timeScale, and
    /// Time.deltaTime already shrinks proportionally with it -- accumulating that
    /// instead gives a position update every real frame, however tiny each individual
    /// step is during the hold, which is exactly what a smooth creep needs. Safe with
    /// the analytic formula specifically because it's evaluated fresh from elapsed
    /// time each call (no step-by-step integration to accumulate error from), so a
    /// variable per-frame step size (Update's, unlike FixedUpdate's fixed one) isn't a
    /// problem here the way it would be for real physics integration.
    /// </summary>
    private void Update()
    {
        if (!_inFlight)
        {
            return;
        }

        bool pauseArmed = !_resumeRequested && _holdWhile != null && !float.IsPositiveInfinity(_pauseAtFlightTime);
        if (pauseArmed && _elapsedFlightTime >= _pauseAtFlightTime && _holdWhile())
        {
            _holding = true;
            if (_slowMoWhile == null || _slowMoWhile())
            {
                // A decision is on screen: slow-motion creep. The slow-down is
                // evaluated in small slices of this frame's REAL time rather than once
                // per frame -- confirmed live that a fast, short flight (the scene's
                // receive->set leg: ~5m in ~0.25s, so its last 10% is ~0.024s) spent
                // one whole full-speed frame (~0.016s) before the first per-frame
                // timeScale drop could apply, crossing most of the post-pause span and
                // freezing the ball in the setter's hands instead of in flight.
                // Sub-stepping lets the curve bite within that first frame, so the ball
                // settles just past the pause point regardless of the flight's speed.
                const int subSteps = 16;
                // Clamped: a single hitched frame (confirmed live with the editor
                // briefly stalling) otherwise carried the ball most of the way into
                // the player's hands in its very first full-speed slice.
                float subRealDt = Mathf.Min(Time.unscaledDeltaTime, 1f / 60f) / subSteps;
                float postPauseSpan = _totalFlightTime - _pauseAtFlightTime;
                float scale = 1f;
                for (int i = 0; i < subSteps; i++)
                {
                    float postPauseProgress = postPauseSpan > 0f
                        ? Mathf.Clamp01((_elapsedFlightTime - _pauseAtFlightTime) / postPauseSpan)
                        : 1f;
                    scale = slowMotionByPostPauseProgress.Evaluate(postPauseProgress);
                    _elapsedFlightTime += subRealDt * scale;
                }
                _elapsedFlightTime = Mathf.Min(_elapsedFlightTime, _totalFlightTime * absoluteMaxHoldFraction);
                Time.timeScale = scale;
            }
            else
            {
                // Between chained decisions: the ball hovers exactly where it is while
                // the rest of the game runs at full speed.
                Time.timeScale = 1f;
            }
        }
        else if (pauseArmed && _elapsedFlightTime < _pauseAtFlightTime
            && _elapsedFlightTime + Time.deltaTime >= _pauseAtFlightTime && _holdWhile())
        {
            // Land exactly ON the pause point the frame that crosses it, rather than
            // stepping past it -- the hold above takes over from there next frame.
            _elapsedFlightTime = _pauseAtFlightTime;
        }
        else
        {
            if (_holding)
            {
                // Released -- snap straight back to full speed (the old Resume()'s own
                // instant un-pause) and finish the flight.
                _holding = false;
                Time.timeScale = 1f;
            }
            _elapsedFlightTime += Time.deltaTime;
        }

        // Clamp to the flight's own domain -- FlyTo's WaitUntil notices completion on
        // the very next Update at the latest, and this parabola isn't valid to
        // extrapolate past _totalFlightTime (see class-level comment on the per-flight
        // state fields above).
        float t = Mathf.Min(_elapsedFlightTime, _totalFlightTime);
        transform.position = _flightStart + _fireVelocity * t + 0.5f * Vector3.down * _currentGravity * t * t;
    }

    /// <summary>
    /// Unpauses a held flight (snapping Time.timeScale straight back to 1, not an eased
    /// recovery) and lets it finish. Safe to call at any time, including when nothing is
    /// in flight or the current flight never pauses -- it only has an effect if a hold
    /// is active or about to start. Sets Time.timeScale synchronously rather than just
    /// flagging it for FixedUpdate to notice -- FixedUpdate calls are rare while
    /// genuinely held at a low timeScale, so waiting for one to notice could add a real,
    /// visible delay after the player has already answered.
    /// </summary>
    /// <summary>
    /// Turns the current flight into a miss: releases any hold and carries the ball on
    /// along the SAME parabola, straight past its intended destination, until it
    /// reaches floorY -- then lands with the normal bounce. For a failed dig: the ball
    /// goes through the passer into the court instead of stopping mid-air and
    /// launching a second flight in a new direction (confirmed live that the old
    /// abort-and-refly read as the ball taking a weird turn). No-op when nothing is in
    /// flight or the flight already ends at/below floorY.
    /// </summary>
    public void RunThroughToFloor(float floorY)
    {
        if (!_inFlight || _currentGravity <= 0f)
        {
            return;
        }
        float vy = _fireVelocity.y;
        float drop = _flightStart.y - floorY;
        float disc = vy * vy + 2f * _currentGravity * drop;
        if (disc < 0f)
        {
            return;
        }
        float tFloor = (vy + Mathf.Sqrt(disc)) / _currentGravity;
        if (tFloor <= _totalFlightTime)
        {
            return;
        }
        _totalFlightTime = tFloor;
        _flightDestination = _flightStart + _fireVelocity * tFloor + 0.5f * Vector3.down * _currentGravity * tFloor * tFloor;
        _flightAllowBounce = true;
        _holdWhile = null;
        if (_holding)
        {
            _holding = false;
            Time.timeScale = 1f;
        }
    }

    public void Resume()
    {
        _resumeRequested = true;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// How long a TossTo from releaseY (up to peakY, then back down to contactY) takes,
    /// split into its rising and falling halves -- a serve's contact point sits well
    /// above the hand that released it, so the two halves aren't equal, and a caller
    /// (the server's accompanying run, currently) needs the peak's own timing to hold
    /// in lockstep with the toss's pause-at-the-peak.
    /// </summary>
    public (float TimeToPeak, float Total) TossTiming(float releaseY, float peakY, float contactY)
    {
        float timeUp = Mathf.Sqrt(2f * Mathf.Max(0f, peakY - releaseY) / tossGravity);
        float timeDown = Mathf.Sqrt(2f * Mathf.Max(0f, peakY - contactY) / tossGravity);
        return (timeUp, timeUp + timeDown);
    }

    /// <summary>
    /// A presentation-only serve toss: from wherever the ball already is (the server's
    /// release hand), up to peakY, then back down until it reaches contactPoint --
    /// ending EXACTLY there, at the serve's contact height on the way down, so the
    /// serve flight that follows launches from the moment/place the ball actually
    /// drops into the server's hitting zone rather than from the release height.
    /// Horizontal travel is linear over the whole toss (a real toss drifts out in
    /// front of the server at a constant rate). Position is evaluated analytically from
    /// elapsed time, same reasoning as FlyTo's own Update -- no step-size overshoot.
    /// Deliberately NOT routed through FlyTo/Update: that state machine is tightly
    /// coupled to the lateral-flight pause/hold fields, none of which apply here. The
    /// ball stays isKinematic the whole time (moved by hand via transform.position).
    ///
    /// stillPending, if given, is checked the instant the toss reaches its own peak --
    /// if it still returns true there, the toss holds (ball motionless, not just slow)
    /// until it returns false, then finishes the descent. The human's own ServeRequest
    /// decision point; left null (AI serve), the toss plays straight through.
    /// </summary>
    public IEnumerator TossTo(Vector3 contactPoint, float peakY, System.Func<bool> stillPending = null)
    {
        Vector3 startPos = transform.position;
        peakY = Mathf.Max(peakY, startPos.y, contactPoint.y);
        var (timeToPeak, total) = TossTiming(startPos.y, peakY, contactPoint.y);
        float vy0 = tossGravity * timeToPeak;
        float elapsed = 0f;
        bool heldAtPeak = false;
        while (elapsed < total)
        {
            if (!heldAtPeak && elapsed >= timeToPeak && stillPending != null && stillPending())
            {
                heldAtPeak = true;
                elapsed = timeToPeak; // hold exactly at the peak, not a frame past it
            }
            if (heldAtPeak)
            {
                if (stillPending())
                {
                    yield return null;
                    continue;
                }
                heldAtPeak = false;
            }

            float t = Mathf.Min(elapsed, total);
            Vector3 pos = Vector3.Lerp(startPos, contactPoint, total > 0f ? t / total : 1f);
            pos.y = startPos.y + vy0 * t - 0.5f * tossGravity * t * t;
            transform.position = pos;
            yield return null;
            elapsed += Time.deltaTime;
        }
        transform.position = contactPoint;
    }

    /// <summary>
    /// Launches the ball on a real arc toward destination, peaking at peakHeight
    /// above the higher of the start/destination points -- always fts's exactly
    /// symmetric ballistic solve (same shape every time, only peakHeight/lateralSpeed
    /// tunable), no per-flight skew or pacing remap. If pauseAtFraction is in (0,1),
    /// the flight holds once it's covered that fraction of the distance, for as long as
    /// holdWhile returns true (see _holdWhile's own comment for how slowMoWhile shapes
    /// the hold) -- reserve this for a genuine pending player decision, not as a
    /// pacing device. Snaps
    /// precisely to destination once done -- physics integration accumulates small
    /// error over a flight, so this corrects it rather than trusting wherever the
    /// simulation actually ended up. allowBounce disables the landing bounce below for
    /// arrivals that should read as a smooth catch rather than a ball hitting a hard
    /// surface (the setter catching a set, specifically).
    /// </summary>
    public IEnumerator FlyTo(Vector3 destination, float peakHeight = -1f, float lateralSpeed = -1f, float pauseAtFraction = -1f, bool allowBounce = true,
        System.Func<bool> holdWhile = null, System.Func<bool> slowMoWhile = null, float? throughToFloorY = null)
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
        _totalFlightTime = lateralDist / lateralSpeed;

        bool solved = fts.solve_ballistic_arc_lateral(
            start, lateralSpeed, destination, absolutePeakHeight, out Vector3 fireVelocity, out float gravity);
        if (!solved)
        {
            transform.position = destination;
            yield break;
        }
        _flightStart = start;
        _fireVelocity = fireVelocity;
        _currentGravity = gravity;

        _pauseAtFlightTime = pauseAtFraction > 0f && pauseAtFraction < 1f
            ? _totalFlightTime * pauseAtFraction
            : float.PositiveInfinity;
        _elapsedFlightTime = 0f;
        _resumeRequested = false;
        _holding = false;
        _flightDestination = destination;
        _flightAllowBounce = allowBounce;
        _holdWhile = holdWhile;
        _slowMoWhile = slowMoWhile;
        _inFlight = true;
        if (throughToFloorY.HasValue)
        {
            RunThroughToFloor(throughToFloorY.Value);
        }

        yield return new WaitUntil(() => _elapsedFlightTime >= _totalFlightTime);

        // Same closed-form velocity the solve above implies at t = _totalFlightTime,
        // evaluated directly rather than carried step-by-step -- only used to size the
        // cosmetic landing bounce below, not the flight itself.
        float arrivalSpeed = (_fireVelocity + Vector3.down * (_currentGravity * _totalFlightTime)).magnitude;
        _inFlight = false;
        transform.position = _flightDestination;

        float bouncePeakOffset = Mathf.Min(arrivalSpeed * bounceHeightPerSpeed, maxBounceHeight);
        if (_flightAllowBounce && bouncePeakOffset > 0.01f)
        {
            yield return BounceInPlace(bouncePeakOffset);
        }
    }

    /// <summary>
    /// A quick vertical hop right where the ball already is, rising by peakOffset then
    /// settling back to exactly the height it started at -- same up-then-down shape as
    /// TossTo, just with its own (snappier) gravity constant and no lateral
    /// component. Purely cosmetic: transform.position's XZ never changes, so this can't
    /// disturb wherever FlyTo just placed the ball.
    /// </summary>
    private IEnumerator BounceInPlace(float peakOffset)
    {
        float startY = transform.position.y;
        float vy = Mathf.Sqrt(2f * bounceGravity * peakOffset);
        while (true)
        {
            vy -= bounceGravity * Time.deltaTime;
            Vector3 pos = transform.position;
            pos.y += vy * Time.deltaTime;
            transform.position = pos;
            if (vy < 0f && pos.y <= startY)
            {
                break;
            }
            yield return null;
        }
        Vector3 finalPos = transform.position;
        finalPos.y = startY;
        transform.position = finalPos;
    }
}
