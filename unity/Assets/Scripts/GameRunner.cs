using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VolleyballCore;
using VolleyballData;
using Random = System.Random;

/// <summary>
/// Runs a full interactive game: a human (via HumanStrategy) against an AI
/// SmartStrategy, rally after rally until someone reaches GameConstants.PointsToWin,
/// with serve going to whoever won the previous rally (first server picked at
/// random) -- same rules as Core's own Game class, just run rally-by-rally here
/// instead of via Game.Play() so this script gets a checkpoint between rallies to
/// update score/serve without adding a Unity-facing hook to Core.
///
/// The whole loop runs on a background Task -- Core has zero UnityEngine
/// dependency by design, so that's safe -- and blocks inside HumanStrategy's
/// methods via a HumanDecisionChannel until this script (main thread, driven by
/// Update()/OnGUI()) supplies an answer from a button click.
///
/// All ~12 IStrategy decision points are wired through the channel. UI is
/// deliberately ugly OnGUI() immediate-mode buttons -- this proves the
/// interaction loop, not the final look.
///
/// Ball/camera behavior is re-triggered off narrative "checkpoints" (whenever
/// the background thread is confirmed blocked on a pending decision, or once
/// it's fully finished) instead of a fixed timer -- see HumanDecisionChannel's
/// class doc for why that's the only point it's safe to read the shared
/// narrative list from this thread.
/// </summary>
public class GameRunner : MonoBehaviour
{
    [SerializeField] private string matName = "Blitz";
    [SerializeField] private float ballHeight = 1.5f; // destination height above a player's position for the ball to arrive at -- the default, used everywhere that doesn't authored its own (see serveContactHeight/attackContactHeight below)
    public float BallHeight => ballHeight; // read by FormationSetupWindow's Set-phase preview start point

    [Header("Ball contact points")]
    // Where the ball actually sits at the moment of contact, per phase -- overrides the
    // generic ballHeight default above for the moments explicitly tuned so far.
    [SerializeField] private float serveContactHeight = 0.75f;
    // A forearm pass is played at roughly waist/chest center, well below setContactHeight
    // (which is closer to a two-handed overhead set) -- previously the reception leg had
    // no dedicated height/offset at all and fell through to the generic ballHeight
    // default with zero forward reach, landing the ball dead-center on top of the
    // receiver at head height instead of a real bump's actual contact point.
    [SerializeField] private float receiveContactHeight = 1f;
    // How far IN FRONT of the receiver's own position (toward the net, along the team's
    // local -Z -- the opposite sign from setContactBackOffset/attackContactBackOffset's
    // "backward" convention, since a receiver reaches forward to play the ball before
    // it's on top of them, rather than contacting it behind their own landing spot).
    [SerializeField] private float receiveContactForwardOffset = 0.5f;
    [SerializeField] private float setContactHeight = 1.5f;
    [SerializeField] private float attackContactHeight = 2.25f;
    // A dig (and the failed-reception scramble recovery, which is really the same
    // motion) is played low, from an athletic stance -- previously had no dedicated
    // height at all and fell through to the generic ballHeight default (1.5, the same
    // as a two-handed overhead SET), landing every dig at chest height instead of the
    // low, reaching pass it's supposed to be.
    [SerializeField] private float digContactHeight = 0.7f;
    // How far behind the Setter's own Set-phase anchor (away from the net, along the
    // team's local +Z -- same "backward" convention serveTossForwardDistance/deadBallLateralOffset
    // already use) the set is actually released at.
    [SerializeField] private float setContactBackOffset = 0.25f;
    // How far behind the hitter's own Attack-phase anchor (away from the net, along the
    // team's local +Z) the ball is actually contacted -- a real swing's contact point
    // sits behind the attacker's own approach/landing spot, not exactly on top of it.
    [SerializeField] private float attackContactBackOffset = 0.48f;
    // Read by FormationSetupWindow's Scene-view preview so it matches these exactly,
    // rather than duplicating the numbers there and risking drift.
    public float SetContactHeight => setContactHeight;
    public float SetContactBackOffset => setContactBackOffset;
    public float AttackContactBackOffset => attackContactBackOffset;
    public float AttackContactHeight => attackContactHeight;

    [Header("Serve toss")]
    // The court floor -- its bounds define where each team's baseline is, which is
    // where every serve's contact point (and the server's approach) ends. Falls back
    // to a scene object named "Court" if left unassigned.
    [SerializeField] private Renderer court;
    // Height (above the server's own position) the ball leaves the tossing hand from --
    // below serveContactHeight, so the toss rises past contact, peaks, and the serve
    // launches the moment it drops back down to contact height.
    [SerializeField] private float serveTossReleaseHeight = 1.4f;
    [SerializeField] private float serveTossPeakOffset = 1.75f; // how high above serveContactHeight the toss peaks
    // Length of the server's run-up: the toss is released this far behind the
    // baseline (away from the net, local +Z) and travels forward with the server, who
    // runs to meet it -- both arriving at the baseline contact point together.
    [SerializeField] private float serveTossForwardDistance = 2.5f;
    // Where along the team's own depth axis the contact point sits relative to the
    // baseline itself -- 0 is exactly on the line, positive is behind it (away from
    // the net), negative inside the court.
    [SerializeField] private float serveContactBaselineOffset = 0f;

    [Header("Decision-hold pause points (fraction of that leg's own flight)")]
    // How far through the serve->receiver leg the ball gets before easing into the
    // slow-motion hold. Only applies when the human is the one receiving -- see
    // ServeRegex's own stillPending check. This leg's reveal holds its own "Serve:"
    // line back from the generic backlog flush specifically so this pause has an
    // answer to actually wait for by the time it engages (see FlushNarrative's
    // holdTrailingServeLine doc comment) -- without that, the UI couldn't show until
    // this line's own processing (including the pause) already finished, defeating it.
    [SerializeField] private float serveReceptionPauseFraction = 0.55f;
    // Same idea for the receiver->setter leg (Team A's own set) -- this one's reveal is
    // explicitly triggered post-gate (see RevealActiveRequest's own SetCardRequest
    // case), with _activeRequest already correctly set to it, so the pause genuinely
    // applies with no special handling needed.
    // Late in the arc on purpose -- the pass's rebound off the receiver always plays
    // out in full (it'll carry the reception animation), and the ball holds in flight
    // just short of the setter's hands, never on anyone's head.
    [SerializeField] private float setPauseFraction = 0.9f;
    // Same idea for the failed-reception -> chaser leg -- this one's reveal ALSO holds
    // its own trailing "Chase:" line back (see FlushNarrative's holdTrailingFlightLine
    // doc comment), same reasoning as serveReceptionPauseFraction above.
    [SerializeField] private float chaseRecoveryPauseFraction = 0.55f;
    [SerializeField] private float freeBallBouncePeakHeight = 0.5f;
    // The rest of the "never pause on someone's head" holds -- each is the fraction of
    // the flight INTO the touch that the decision is about, same as the two above:
    // setter->hitter while TipOrHit is pending, hitter->digger while the human's own
    // Dig (and any Cover attempt before it) is pending, chaser->teammate while the
    // free-ball discard is pending. The AI's own receive->setter leg reuses
    // setPauseFraction for the human's Block decision (same leg type, same hold).
    [SerializeField] private float swingPauseFraction = 0.9f;
    [SerializeField] private float digPauseFraction = 0.5f;
    [SerializeField] private float freeBallDiscardPauseFraction = 0.9f;

    [Header("Decision hold with no ball flight to pause (currently: Block)")]
    // Block is committed BLIND, before the attacker's final lane is even chosen (Core's
    // PhaseBlockCommit runs ahead of ChooseAttackLane) -- confirmed live the ball is
    // already sitting at rest (wherever the attacking team's own Set leg left it) the
    // moment BlockCardsRequest goes active, not mid-flight the way Reception/Set are.
    // There's nothing to hook BallFlight's own pauseAtFraction into here, so this drives
    // Time.timeScale directly and independently instead -- same "ease down while
    // pending, snap back the instant it resolves" feel, just not tied to any flight's
    // own progress. Ramped on Time.unscaledDeltaTime, not scaled -- using scaled time
    // here would mean the ramp's own progress slows down as timeScale drops, so it'd
    // approach its target ever more slowly instead of actually finishing.
    [SerializeField] private float decisionHoldRampDuration = 0.4f;
    [SerializeField] private float decisionHoldTimeScale = 0.05f;
    private Coroutine _decisionHoldCoroutine;

    [Header("Rally-ending floor landings (so a point genuinely reads as over, not just stopped)")]
    // Absolute world Y a "landed on the floor" flight targets -- a player's own position
    // (what every other flight targets, offset by ballHeight) is well above the court
    // surface, so these need their own low target height instead.
    [SerializeField] private float floorLandingHeight = 0.15f;
    // Flatter/shorter than the general defaults (BallFlight's own defaultPeakHeight) --
    // the ball dying fast right where it's already at, not a full cross-court arc, so
    // a tall lazy peak would read wrong. (A missed dig no longer needs its own landing
    // arc -- it runs on through the digger along the attack's, see RunThroughToFloor.)
    [SerializeField] private float stuffedLandingPeakHeight = 0.5f;
    // NOT a floor landing despite living in this group (kept here so it stays next to
    // its siblings above) -- this is the peakHeight for the failed-reception -> chaser
    // flight, a real, live, still-being-scrambled-for catch (see ChaseStartRegex
    // below), just a short/flat one, same shape reasoning as the two heights above.
    [SerializeField] private float chaseLandingPeakHeight = 0.6f;
    // How far sideways (randomized left/right) a floor landing kicks away from the
    // player it's landing "near" -- confirmed live that landing exactly at the
    // player's own position clips into their (now half-size) capsule.
    [SerializeField] private float deadBallLateralOffset = 1.2f;

    [Header("Attack/dig placement variety")]
    // How far an attack can land from the defender's home spot -- without this every
    // attack/dig for a given lane+card-parity flies to the exact same fixed point
    // (confirmed the root cause of "the ball is ALWAYS hit to the same spot").
    [SerializeField] private float placementVarianceRadius = 1.2f;
    // Fraction applied to the digger's estimated remaining travel time (see the
    // ResolveRegex branch below) -- less than 1 so the digger settles in just ahead of
    // the ball instead of at the same instant.
    [SerializeField] private float digApproachLeadFraction = 0.85f;
    // Set once per Swing (see SwingRegex below) and reused for both the dig flight's
    // target and the digger's own run destination, so the two always agree on where the
    // ball is actually going. A plain world-space XZ offset rather than one projected
    // through a team's local "right" direction (contrast deadBallLateralOffset) --
    // sampling a uniformly random angle makes the two equivalent for any Y-axis-only
    // court rotation, so there's no need to look up either team's transform to compute
    // it correctly.
    private Vector3 _pendingAttackOffset;

    // -- Arc tuning (serve/receive legs only, for now) --
    //
    // X axis = card value (1-10), Y axis = the literal value handed to BallFlight.FlyTo
    // for that leg. Curves, not a min/max pair, so the whole shape (not just the
    // endpoints) can be dragged around live in the Inspector -- including in Play mode,
    // where the effect on the very next serve is immediate. A card's raw face value
    // drives these, not any ability-adjusted "effective" value -- simplest, and matches
    // what the player actually sees on the card.
    [Header("Serve/receive arc tuning (X = card value 1-10)")]
    // A 10 goes as flat and fast as possible; a 1 lobs high and slow. The ballistic
    // solver's peak naturally lands near the horizontal middle of the flight -- for a
    // serve, that's already right around the net -- so no separate "peak near the net"
    // logic is needed on top of this.
    // Floor raised from the old 0.8-at-card-10 default: absolutePeakHeight in FlyTo is
    // Max(start.y, dest.y) + this value, and start/dest (serveContactHeight,
    // receiveContactHeight) sit around 0.75-1.6 -- confirmed live that 0.8 (and the
    // scene's own separately-drifted-even-lower override) put the peak barely above
    // 1.8, well under the net's real ~1.93 top, so a hard serve visibly clipped
    // through it. 1.0 as the floor guarantees at least ~1.75-2.6 depending on the
    // exact contact heights in play, comfortably clearing the net at every card value.
    [SerializeField] private AnimationCurve serveArcHeightByCardValue = AnimationCurve.EaseInOut(1f, 2.5f, 10f, 1f);
    [SerializeField] private AnimationCurve serveArcSpeedByCardValue = AnimationCurve.EaseInOut(1f, 5f, 10f, 14f);
    // The receiver->setter leg (both teams: MoveBallToSetterWhenReady for the human,
    // the Set: line's own trigger below for the AI) gets its own height/speed rather
    // than reusing the serve's own curve or the generic BallFlight default -- a
    // deliberately slow, floaty bump-to-setter pass, not a quick flat one.
    [SerializeField] private float receiveToSetPeakHeight = 2.4f;
    [SerializeField] private float receiveToSetLateralSpeed = 6f;

    [Header("Attack (hit) arc tuning -- tips excluded, see _lastShotWasTip")]
    // Keyed by the attack's own card value (_lastAttackCardValue), the same way
    // serveArcHeightByCardValue is keyed by the serve's -- a harder-driven hit flies
    // flatter than a soft one, not the same lazy arc for every value. Tips are
    // deliberately NOT run through this curve (see DigRegex below) -- a disguised
    // soft shot reads as a real tip only if it keeps the existing, taller/slower
    // default arc, not a fast flat "hit" trajectory. Every hit is otherwise the
    // SAME plain, symmetric ballistic arc every time (see BallFlight.FlyTo) -- no
    // per-flight skew or pacing remap, just this one magnitude knob, so the rally
    // reads as one consistent, predictable game rather than a different-feeling
    // animation on every swing.
    [SerializeField] private AnimationCurve attackPeakHeightByCardValue = AnimationCurve.EaseInOut(1f, 1.6f, 10f, 0.3f);
    // A flat, fast lateral speed for every hit -- straight and driven across the
    // net, not the generic BallFlight default every other flight falls back to.
    [SerializeField] private float attackLateralSpeed = 11f;

    [Header("Setter positioning")]
    // How long the walk back to defense (home position) takes once the opponent starts
    // setting -- there's no ball-landing deadline to hit here like a run-up would have,
    // so a short fixed duration is enough.
    [SerializeField] private float setterReturnDuration = 0.6f;
    // How long a team's ease into Serve/Receive formation is given, and then explicitly
    // waited for, before the toss/flight that follows is allowed to start -- used by
    // both PrepareServeFormationThenToss (the server's own team) and ServeRegex's own
    // receiving-team ease. A flat, generous default rather than a computed "arrive just
    // ahead of the ball" lead time: both of these explicitly BLOCK on this duration now
    // (see "even if we need to wait for the characters to move" in each), so there's no
    // real flight to race against arriving early or late -- just a real settle time
    // that always fully elapses before anything else can happen.
    [SerializeField] private float receiveFormationLeadDuration = 1f;

    [Header("Attack formation")]
    // Fraction of the estimated Set->Hitter flight time (see EstimateSetToHitterDuration)
    // the attacking team's Attack-phase move is given -- same "arrive with a bit of room
    // to spare" idea as digApproachLeadFraction, so the swinging hitter's own authored
    // Attack anchor doubles as their old step-back position, timed to the ball instead
    // of a flat guess.
    [SerializeField] private float hitterMoveLeadFraction = 0.85f;

    [Header("Trajectory preview (Set -> every live hitting option)")]
    // Default color for a still-open, not-yet-committed option.
    [SerializeField] private Color trajectoryLineColor = new(1f, 1f, 1f, 0.6f);
    // The "you've actually committed to this one" color -- the currently-pending slot,
    // any already-placed hit card, the AI's own narrated (and therefore real)
    // "Attack:" commitments, and the final swinging lane all use this. Safe to reveal
    // as early as a lane's committed: Block always resolves before Swing narrates
    // (confirmed in Core/Rally.cs), so nothing shown here can leak information that
    // could still change a pending Block decision.
    [SerializeField] private Color trajectorySelectedColor = Color.yellow;
    [SerializeField] private float trajectoryLineWidth = 0.05f;
    [SerializeField] private int trajectoryLineSegments = 20;
    // At most one arc is ever relevant per role at a time (an open option, the pending
    // selection, or a committed placement, never more than one simultaneously), so one
    // pooled LineRenderer per role -- created lazily, never destroyed, just toggled.
    private readonly Dictionary<PlayerRole, LineRenderer> _trajectoryLines = new();
    // Tracks whichever eased motion (setter run-out/run-back, hitter step-back, digger
    // approach) is currently in flight per player transform, so a new one can cleanly
    // preempt an old one instead of silently no-oping -- see MovePlayerTo. A single
    // HashSet-style "already running" guard doesn't work here: confirmed live that a
    // still-finishing motion from the previous exchange can still be holding such a guard
    // at the exact moment the next one needs to start, silently skipping it and
    // stranding the player mid-transition.
    private readonly Dictionary<Transform, Coroutine> _playerMotionCoroutines = new();

    [Header("World-anchored decision buttons (piloting a move off the OnGUI panels)")]
    // Real UGUI buttons projected to a world position via WorldToScreenPoint, parented
    // under the existing "Card Canvas" (Screen Space Overlay, so transform.position
    // doubles as screen position -- same trick HandCardView's own drag already relies
    // on). Built once per request (see ShowWorldButtonsForRequest, called from Update()'s
    // single-shot request-capture block) rather than every OnGUI frame like the legacy
    // panels -- these don't need to move once the phase camera is locked in for a
    // pending human decision.
    [SerializeField] private Vector2 worldButtonSize = new(150, 44);
    [SerializeField] private float worldButtonYOffset = 70f; // screen pixels above the target's projected position
    // Horizontal screen-space gap between multiple AttackLaneRequest buttons
    // (ShowAttackLaneButtons) -- their world anchors (different hitters) can project
    // close together, or even overlap, depending on the active phase camera's angle;
    // spreading them apart in screen space (not world space) fixes that regardless
    // of which camera is active, rather than tuning a world-space offset per angle.
    [SerializeField] private float laneButtonSpacing = 260f;
    private Transform _worldButtonParent;
    private readonly List<GameObject> _activeWorldButtons = new();
    // Plain informational text (no buttons) for whichever request just switched off its
    // OnGUI panel -- highlight + the real hand strip/drag already carry the interaction,
    // this just keeps the same context ("choose a card to receive, serve = 8") visible
    // without a clickable list.
    private TextMeshProUGUI _promptLabel;

    // How long to hold on an informational narrative line (a card placement, reveal,
    // resolve/shot/outcome, etc.) that doesn't already involve a real wait of its own
    // (a ball flight or the serve toss) -- without this, once camera cuts start
    // reacting to these same lines, an AI-only sequence would flash through several
    // cuts in a single frame with no time to actually see any of them. Applies equally
    // to both teams and to every such line, not just AI ones -- a human's own lines get
    // exactly the same beat, it's just usually masked by their own decision time.
    [SerializeField] private float narrativeBeatDelay = 0.35f;
    private bool _lineHadRealWait;

    // Fallback camera for any decision without its own dedicated phase camera (and the
    // camera shown at startup, before any decision is pending). Auto-found as
    // "Player Main Camera" if not assigned.
    [SerializeField] private Camera defaultCamera;

    // Hand-strip UI (Card Canvas/Card Parent + the HandCardView prefab). Visual only
    // for now -- it shows the current decision's hand, but clicking a card doesn't do
    // anything yet; the OnGUI buttons below are still what actually resolves a decision
    // until drag-and-drop lands. cardPrefab must be assigned in the Inspector (drag
    // Assets/Prefabs/Card 1.prefab onto it) -- it's a prefab asset, not something this
    // script can find in the scene the way it finds Ball/cameras below.
    [SerializeField] private HandCardView cardPrefab;
    [SerializeField] private Transform cardParent;

    // -- Panel layout: every Draw*Request method below builds its panel's Rect from
    // one of these three sizes plus a shared top-left offset, so resizing/repositioning
    // the whole prototype UI is a one-place Inspector edit, not a hunt through methods.
    // Writing an actual *new* panel shape/template means adding a new DrawXxxRequest
    // method that follows the same GUILayout.BeginArea(PanelRect(...), GUI.skin.box)
    // pattern -- see DrawServeRequest for the simplest example.
    [Header("Decision panel layout")]
    [SerializeField] private Vector2 panelOffset = new(20, 20);
    [SerializeField] private Vector2 standardPanelSize = new(320, 400); // card-list prompts (serve, exchange, cover)

    // OnGUI (unlike the Card Canvas's UGUI, which has its own CanvasScaler) has no
    // built-in DPI/resolution scaling at all -- every Rect/font size below is normally
    // interpreted as literal screen pixels, so the exact same panel reads as a
    // completely different physical size on a phone-resolution screen vs. whatever this
    // was tuned against. referenceGuiHeight is "the Screen.height these panel/font sizes
    // above were designed to look right at" -- OnGUI() scales everything by
    // Screen.height / referenceGuiHeight so panels stay legible at any resolution,
    // without needing to hand-retune every Rect each time the target resolution changes.
    [SerializeField] private float referenceGuiHeight = 800f;

    [Header("Player highlight colors")]
    [SerializeField] private Color activeSelectionColor = new(1f, 0.55f, 0f);  // orange: diggers/setters/hitters actively deciding
    [SerializeField] private Color hitterOptionColor = new(0.25f, 0.9f, 0.3f); // green: hitters being offered as options (e.g. during set)
    [SerializeField] private Color blockerSelectionColor = new(0.25f, 0.55f, 1f); // blue: blockers being selected

    [Header("Floating label colors")]
    // Reception/Chase/Dig's own card value, colored by whether that specific card beat
    // its target (a clean pass, a chase reaching its total, a successful dig).
    [SerializeField] private Color floatingSuccessColor = new(0.3f, 0.9f, 0.35f);
    [SerializeField] private Color floatingFailureColor = new(0.95f, 0.25f, 0.25f);
    // Set's own tempo label/color -- matches FormationSetupWindow's Tempos ordering
    // (Quick/Mid/High), see GetTempoColor.
    [SerializeField] private Color quickTempoColor = new(0.3f, 0.55f, 1f);   // blue
    [SerializeField] private Color midTempoColor = new(0.95f, 0.85f, 0.2f);  // yellow
    [SerializeField] private Color highTempoColor = new(0.7f, 0.35f, 0.9f); // purple

    // Permanent, never cleared -- applied once at startup so the two teams are always
    // visually distinguishable regardless of whose turn it is. Uses the same
    // MaterialPropertyBlock mechanism as the temporary highlights above, but on the
    // "AIPlayers" group, which the temporary highlight/clear logic never touches.
    [SerializeField] private Color aiTeamColor = new(0.6f, 0.2f, 0.8f);

    private static readonly Regex ServeRegex = new(@"Serve:\s+(\S+) card (\d+).*targeting (\w+)");
    private static readonly Regex ReceiveRegex = new(@"Receive:\s+(\S+) card (\d+)");
    private static readonly Regex SetRegex = new(@"Set:\s+(\S+) card (\d+)");
    private static readonly Regex AttackRegex = new(@"Attack:\s+(\S+) lane (\d+) \S+\s+card (\d+)");
    private static readonly Regex SwingRegex = new(@"Swing:\s+(\S+) lane (\d+) (\w+)");
    private static readonly Regex RevealRegex = new(@"Reveal:\s+lane (\d+) blind draw.*card (\d+)");
    private static readonly Regex BlockQuicksetRegex = new(@"Block:\s+Quick set lane (\d+).*card (\d+)");
    // Captures both the receiving team's name and the actual back-row role Core chose
    // for it (ChooseFreeBallTarget -- e.g. "Plain (Libero)") so the crossing can fly
    // there for real instead of skipping straight to the next setter.
    private static readonly Regex FreeBallRegex = new(@"mandatory free ball to (\S+) \((\w+)\)");
    private static readonly Regex ResolveRegex = new(@"Resolve:\s+(\S+) lane (\d+) (\w+)\s+atk (\d+)");
    // Narrated between Resolve: and Dig: (see Core/Rally.cs -- "hit", "tip", "roll",
    // "heavy_spin", or "seam") -- read here so DigRegex's own flight below knows
    // whether to fly the disguised-soft-shot arc (tip) or the flatter, harder one
    // (everything else). See _lastShotWasTip's own comment.
    private static readonly Regex ShotRegex = new(@"Shot:\s+(\w+)");
    private static readonly Regex DigRegex = new(@"Dig:\s+(\S+) card (\d+)");
    // The Chase *header* line specifically ("Chase:   Plain  need 8  starting at 3"),
    // not the per-attempt "Chase 1: card N -> total X/Y" lines that follow it -- this is
    // the only place Core narrates "need"/"starting at", so it can't collide with those.
    // Narrated before ChooseChaseCard blocks (confirmed in Core/Rally.cs's PhaseChase),
    // same ordering as Serve/Receive/Set -- see HandleLine's own branch for why that
    // matters here.
    private static readonly Regex ChaseStartRegex = new(@"Chase:\s+\S+\s+need\s+\d+\s+starting at\s+\d+");
    // Each individual attempt's own line ("Chase 1: card 8  ->  total 17 / 10") -- the
    // running total/target it already carries is enough to color that attempt's card
    // green/red without waiting for the separate SUCCEEDED/FAILED line a few lines later.
    private static readonly Regex ChaseAttemptRegex = new(@"Chase\s+\d+:\s+card\s+(\d+)\s+→\s+total\s+(\d+)\s*/\s*(\d+)");
    // An exact-tie hit deflects off the block back onto the ATTACKER's own side, and
    // that team digs it (Rally.ResolveOwnSideDeflect). Core names no digging role --
    // presentation always sends it to that team's Libero (DeflectDigRole).
    private static readonly Regex DeflectRegex = new(@"Deflect:\s+attacker side, (\S+) card (\d+)");
    private const PlayerRole DeflectDigRole = PlayerRole.Libero;
    private static readonly Regex StuffedRegex = new(@"Outcome:\s+STUFFED");

    private Transform _ball;
    private BallFlight _ballFlight;
    private Dictionary<string, Dictionary<PlayerRole, Transform>> _teamPositions;
    private Dictionary<Transform, (string Team, PlayerRole Role)> _transformOwner;
    private Dictionary<string, Transform> _teamRoots;
    private Dictionary<string, Dictionary<PlayerRole, Vector3>> _basePositions;
    private string _teamAName;
    private string _teamBName;
    private int _lastLane = -1;
    private int _lastAttackCardValue = -1;
    // Set by ShotRegex, read by DigRegex's own flight -- tips keep the original,
    // taller/slower default arc (a disguised soft shot shouldn't fly like a hard hit);
    // everything else ("hit", "roll", "heavy_spin", "seam" -- see Core/Rally.cs's own
    // shot-type strings) is a real driven attack and gets the flatter,
    // apex-skewed treatment. Defaults false so an early/unexpected Dig: line (should
    // never happen, but see every other defensive default in this file) reads as a
    // normal hit rather than a tip.
    private bool _lastShotWasTip;
    private int _lastServeCardValue = 5; // default (mid-value) until the first serve of the game
    private int _lastSetCardValue = 5; // default (mid tempo) until the first set of the game
    private PlayerRole? _lastServeTargetRole;
    private int? _lastChosenAttackLane;
    private string _pendingReceiveTeam;
    private PlayerRole? _pendingReceiveRole;
    private string _lastAttackingTeam;
    // Which of the receiving team's two eligible-receiver roles (Ds/Libero -- the only
    // roles EligibleReceivers() ever returns) is chasing down a failed serve reception.
    // Determined the instant Core's "Chase:" header line narrates (see HandleLine),
    // well before ChaseCardRequest itself goes active, so the highlight/hand-strip
    // reveal and the drag target are both already correct the moment the human sees it.
    private PlayerRole? _chaseRole;

    private readonly Dictionary<Transform, MaterialPropertyBlock> _highlightBlocks = new();

    // Card values floating above whoever just played them, cleared every time the ball
    // crosses the net (a fresh serve, or an attack coming over for a dig) so the display
    // always reflects only the current exchange in flight.
    // Ball included as a valid key (alongside every player role's Transform) -- Serve's
    // own value, a successful dig's transferred attack value, and "Free Ball" all show
    // attached to the ball itself rather than above a person, so DrawFloatingNumbers
    // reads off whichever Transform is present with no special-casing.
    private readonly Dictionary<Transform, (string Text, Color Color)> _floatingNumbers = new();

    // -- Per-phase cameras: convention-based, not a fixed list -- a camera named
    // "Player {Phase} Camera" is used automatically for that decision type the moment
    // it exists in the scene, falling back to defaultCamera otherwise. Several request
    // types deliberately share one camera name rather than each getting its own: Hit/
    // AttackLane/TipOrHit all need the same "every hitter in frame" shot (Attack), Dig/
    // Chase/FreeBall are the same scrambled-defense framing (Dig) -- one distinct camera per *kind of
    // moment*, not one per request type. Exchange/Cover have no player tied to them at
    // all (see ApplyHighlightsForRequest's comment on those two) and intentionally have
    // no entry here, always falling back to defaultCamera.
    private static readonly Dictionary<Type, string> PhaseCameraNames = new()
    {
        { typeof(ServeRequest), "Serve" },
        { typeof(ReceiveRequest), "Receive" },
        { typeof(SetCardRequest), "Set" },
        { typeof(HitCardsRequest), "Attack" },
        { typeof(BlockCardsRequest), "Block" },
        { typeof(AttackLaneRequest), "Attack" },
        { typeof(TipOrHitRequest), "Attack" },
        { typeof(DigCardRequest), "Dig" },
        { typeof(ChaseCardRequest), "Dig" },
        { typeof(FreeBallDiscardRequest), "Dig" },
    };

    // Guards PlayServeToss against running twice concurrently for the same team -- if a
    // second Serve line for the same server got processed before the first toss finished
    // (a genuine, confirmed possibility: multiple PlayLines batches can be in flight at
    // once), both coroutines would fight over the same setter Transform, and neither's
    // final restore would reliably win -- leaving the setter stuck away from its normal
    // position. See PlayServeToss.
    private readonly HashSet<string> _tossingTeams = new();
    // Set the instant a human ServeRequest goes live (well before the serve card/target
    // are chosen -- see the switch in RevealActiveRequest), so ServeRegex's own later
    // handling of the "Serve:" line can just await this already-started toss instead of
    // kicking off a second one. Cleared once ServeRegex consumes it. Stays null for an
    // AI-served rally (no request, no early trigger), so ServeRegex's existing fallback
    // -- starting the toss itself -- is unchanged for that path.
    private Coroutine _pendingServeToss;
    // True from the moment a "Serve:" line starts processing until the serve has
    // actually reached its receiver. For an AI serve, the human's ReceiveRequest reveals
    // alongside the toss (see FlushNarrative's holdTrailingFlightLine), so a quick
    // answer can put SetCardRequest live before the serve is even struck -- and
    // BallFlight.IsInFlight doesn't cover the toss, so MoveBallToSetterWhenReady would
    // otherwise launch the set straight out of the server's toss. See that method.
    private bool _serveInProgress;

    // "Hold the ball in flight while this is true" conditions for every leg that leads
    // into a human decision (see BallFlight._holdWhile). Flags rather than
    // "_activeRequest is X" wherever the hold has to survive the gaps BETWEEN several
    // chained decisions (Set -> Exchange -> HitCards -> AttackLane all happen before the
    // setter touches the ball, the chase can take several attempts, a Cover attempt
    // precedes its Dig) or has to be armed before the decision is actually revealed.
    // Set as each request is taken off the channel (Update), cleared by the narrative
    // line that proves the decision chain is over (ScanForStateUpdates) or by the
    // answer itself (ResolveActiveRequest), and always by a rally-ending [Score] line.
    private bool _holdForHumanAttack;
    private bool _holdForBlock;
    private bool _holdForTip;
    private bool _holdForChase;
    private bool _holdForDig;
    // Set once the human's own dig flight has been launched early (at the Dig/Cover
    // reveal, see PreLaunchHumanDig) so the "Dig:" line's own handler knows not to
    // launch a second one.
    private bool _digPreLaunched;
    // Which team is attacking, as of the most recently FLUSHED (not yet necessarily
    // played) narrative -- _lastAttackingTeam only updates at playback, too late for
    // Update()'s own request-take bookkeeping.
    private string _scanAttackingTeam;
    // Whether the human's own receive/dig->setter leg has already been launched for
    // the current set -- normally by SetCardRequest's reveal, but Core skips that
    // request entirely when the set card is drawn blind (empty hand), in which case
    // the "Set:" line itself has to launch it (see SetRegex).
    private bool _humanSetLegStarted;

    private HumanDecisionChannel _channel;
    private List<string> _narrative;
    // Typed, ordered record of the whole game -- Rally's own events plus the human's
    // decision prompts (DecisionRecordingStrategy) and each rally's score, all in the
    // order Core actually ran them. Written on the game's background thread. Not read
    // by presentation yet: it's the replacement being built for narrative parsing.
    private RallyEventLog _events;
    private int _narrativeReadIndex;
    private Task<string> _gameTask;
    private bool _resultLogged;

    // Written only by the background game loop, read every frame by OnGUI for the
    // scoreboard. Plain ints are atomic to read/write in .NET, and a display that's
    // one frame stale is harmless, so no lock is needed just for this.
    private volatile int _scoreA;
    private volatile int _scoreB;

    // Currently-displayed decision (null = none) and its in-progress sub-state.
    private object _activeRequest;
    // Every request taken off the channel stashes here first and waits for whatever
    // narrative was already sitting unflushed to finish animating before its own UI
    // shows -- see Update() for why this applies unconditionally, and RevealAfterFlush
    // for the actual wait.
    private object _pendingReveal;
    private Card? _servePendingCard;
    private List<(int Lane, Card Card, AttackPosition Position)> _hitPlacements;
    private Dictionary<PlayerRole, (int Lane, Card Card)> _blockPlacements;
    // One-shot guard for the Set->Attack floating-label wipe ("all other numbers
    // disappear" the instant the attack phase itself begins) -- reset the moment a
    // fresh "Set:" line narrates, consumed the first time either side's attack phase
    // actually starts (the human's own HitCardsRequest reveal, or the AI's first
    // "Attack:" line), so a multi-attacker set's later placements don't each re-wipe
    // the X's the earlier ones just put up.
    private bool _attackPhaseCleared;

    private Rect PanelRect(Vector2 size) => new(panelOffset.x, panelOffset.y, size.x, size.y);

    private void Start()
    {
        GameObject ballObj = GameObject.Find("Ball");
        if (ballObj == null)
        {
            Debug.LogWarning("GameRunner: no 'Ball' GameObject found in the scene -- ball tracking disabled.");
        }
        else
        {
            _ball = ballObj.transform;
            _ballFlight = ballObj.GetComponent<BallFlight>();
            if (_ballFlight == null)
            {
                Debug.LogWarning("GameRunner: 'Ball' has no BallFlight component -- ball movement disabled.");
            }
        }

        if (defaultCamera == null)
        {
            defaultCamera = GameObject.Find("Player Main Camera")?.GetComponent<Camera>();
        }
        SetActiveCamera(defaultCamera);

        if (cardParent == null)
        {
            cardParent = GameObject.Find("Card Parent")?.transform;
        }
        ClearHandStrip(); // wipes the leftover placeholder "Card 1" instance sitting in the scene

        var mat = MatLoader.LoadMat(
            matName,
            DefaultPaths.TeamsCsv,
            DefaultPaths.PlayerCardsCsv,
            DefaultPaths.TeamPassivesCsv,
            DefaultPaths.SetTemplatesCsv);

        var seedRng = new Random();
        Team teamA = TeamFactory.BuildTeam(mat, new Random(seedRng.Next()));
        Team teamB = new Team("Plain", new Random(seedRng.Next()));
        _teamAName = teamA.Name;
        _teamBName = teamB.Name;

        _teamPositions = new Dictionary<string, Dictionary<PlayerRole, Transform>>
        {
            [_teamAName] = FindTeamPositions("Players"),
            [_teamBName] = FindTeamPositions("AIPlayers"),
        };

        // Team root transforms (for Formations/ lookups -- see GetFormationPosition)
        // and a snapshot of each role's own original position, taken before anything
        // has a chance to move -- NOT the same as teamRoot.position: the root happens
        // to sit exactly at the Setter's own spot (zero local offset), which made
        // "teamRoot.position" a tempting but WRONG stand-in for "this role's home" for
        // every other role (confirmed live -- a Libero/DS "go home" resolved to the
        // Setter's spot instead of its own). This snapshot is the real fix, and doubles
        // as the fallback for any phase/role with no authored Formations/ anchor.
        _teamRoots = new Dictionary<string, Transform>
        {
            [_teamAName] = GameObject.Find("Players").transform,
            [_teamBName] = GameObject.Find("AIPlayers").transform,
        };
        _basePositions = new Dictionary<string, Dictionary<PlayerRole, Vector3>>();
        foreach (var teamKv in _teamPositions)
        {
            var snapshot = new Dictionary<PlayerRole, Vector3>();
            foreach (var roleKv in teamKv.Value)
            {
                snapshot[roleKv.Key] = roleKv.Value.position;
            }
            _basePositions[teamKv.Key] = snapshot;
        }

        // Reverse lookup for drag-and-drop: "which (team, role) owns this Transform" --
        // built once here since _teamPositions never changes after this point.
        _transformOwner = new Dictionary<Transform, (string Team, PlayerRole Role)>();
        foreach (var teamKv in _teamPositions)
        {
            foreach (var roleKv in teamKv.Value)
            {
                _transformOwner[roleKv.Value] = (teamKv.Key, roleKv.Key);
            }
        }

        // Permanent team-color tint -- applied once, never cleared, independent of the
        // temporary orange/green/blue decision highlighting above.
        foreach (Transform t in _teamPositions[_teamBName].Values)
        {
            ApplyHighlight(t, aiTeamColor);
        }

        _channel = new HumanDecisionChannel();
        _narrative = new List<string>();
        _events = new RallyEventLog();
        var humanStrategy = new DecisionRecordingStrategy(new HumanStrategy(_channel), teamA.Name, _events);
        var aiStrategy = new SmartStrategy(new Random(seedRng.Next()));

        // Core has no UnityEngine dependency (VolleyballCore.asmdef: noEngineReferences)
        // so it's safe to run the whole game loop, including HumanStrategy's
        // blocking waits, off the main thread.
        _gameTask = Task.Run(() => RunGame(teamA, teamB, humanStrategy, aiStrategy, seedRng));
    }

    /// <summary>
    /// Runs rallies back-to-back until someone reaches GameConstants.PointsToWin,
    /// mirroring Core's own Game.Play() (score, serve-follows-the-winner) but with a
    /// checkpoint between rallies here instead of inside Core. Runs entirely on the
    /// background task -- must never touch a UnityEngine API.
    /// </summary>
    private string RunGame(Team teamA, Team teamB, IStrategy strategyA, IStrategy strategyB, Random rng)
    {
        teamA.DrawStartingHand();
        teamB.DrawStartingHand();
        Team server = rng.Choice(new[] { teamA, teamB });

        while (Math.Max(_scoreA, _scoreB) < GameConstants.PointsToWin)
        {
            Team serving = server;
            Team receiving = ReferenceEquals(serving, teamA) ? teamB : teamA;
            IStrategy srvStrat = ReferenceEquals(serving, teamA) ? strategyA : strategyB;
            IStrategy rcvStrat = ReferenceEquals(serving, teamA) ? strategyB : strategyA;

            var rally = new Rally(serving, receiving, srvStrat, rcvStrat, rng, _narrative, _events);
            RallyResult result = rally.Play();

            if (result.WinnerName == teamA.Name)
            {
                _scoreA++;
                server = teamA;
            }
            else
            {
                _scoreB++;
                server = teamB;
            }
            _narrative.Add($"[Score] {result.WinnerName} wins the rally ({result.Reason}) — {teamA.Name} {_scoreA}, {teamB.Name} {_scoreB}");
            _events.Emit(new GameScoreEvent(teamA.Name, _scoreA, teamB.Name, _scoreB));
        }

        return _scoreA > _scoreB ? teamA.Name : teamB.Name;
    }

    private void OnDestroy() => _channel?.Cancel();

    private void OnApplicationQuit() => _channel?.Cancel();

    private void Update()
    {
        if (_activeRequest == null && _pendingReveal == null && _channel != null && _channel.TryTakePending(out object req))
        {
            // Every request type goes through the same stash-flush-wait-then-reveal
            // path, unconditionally -- confirmed live, one request type at a time
            // (Block, then Serve, then SetCard, then ChaseCard), that whichever type
            // gets special-cased as "reveal immediately" is exactly the one that
            // eventually turns out to have SOME path where real narrative (and the
            // ball flight tied to it) is still sitting unflushed the instant this
            // request is taken off the channel: Core's background thread narrates each
            // phase's own header/result line and then, whenever the NEXT decision
            // belongs to the AI (or the same team that just acted), keeps running
            // synchronously with no yield -- so an arbitrarily long backlog of
            // AI-only lines (and their flights: chase recoveries, free-ball crossings,
            // reception/set legs) can pile up between one human decision and the next.
            // Revealing the next request immediately raced that backlog's own
            // animation every time. FlushNarrative() itself is always safe to call
            // unconditionally (a no-op, returning a null Coroutine, when there's
            // nothing new) -- so gating universally costs nothing when there's no
            // backlog (RevealAfterFlush reveals on the very next tick, same as the old
            // immediate path effectively did) and fixes every future case of this same
            // bug at once instead of chasing it request type by request type.
            //
            // ReceiveRequest holds back its own trailing "Serve:" line specifically
            // (see FlushNarrative's own doc comment) -- unlike every other type, its
            // own triggering line carries a flight that's meant to pause and visibly
            // wait for this very answer, which can only happen if that line's
            // animation runs AFTER (not as a prerequisite to) revealing.
            _pendingReveal = req;
            Coroutine playback = FlushNarrative(holdTrailingFlightLine: req is ReceiveRequest or ChaseCardRequest);
            // Armed right after the flush's own synchronous scan (which may clear
            // stale flags off a [Score] line) and before any flight it just started
            // can reach its pause point.
            ArmHoldForRequest(req);
            StartCoroutine(RevealAfterFlush(playback));
        }

        if (_gameTask != null && _gameTask.IsCompleted && !_resultLogged)
        {
            _resultLogged = true;
            FlushNarrative(); // safe: background thread has fully finished
            ClearAllHighlights();
            ClearFloatingNumbers();
            ClearHandStrip();
            if (_gameTask.IsFaulted)
            {
                Debug.LogError($"Game faulted: {_gameTask.Exception}");
            }
            else
            {
                Debug.Log($"<b>Game over! Winner: {_gameTask.Result} — {_teamAName} {_scoreA}, {_teamBName} {_scoreB}</b>");
            }
        }
    }

    /// <summary>
    /// Does everything that makes a decision actually visible/actionable: claims
    /// _activeRequest, flushes narrative, applies highlights, cuts the phase camera,
    /// populates the hand strip, sets prompt text / shows buttons, and (for BlockCards)
    /// starts the slow-motion hold. Called immediately from Update() for every request
    /// type except BlockCardsRequest and ServeRequest, which stash themselves in
    /// _pendingReveal and call this later instead, once whatever narrative had already
    /// piled up by the time they were taken off the channel has actually finished
    /// playing -- see Update()'s own comment on each for why.
    /// </summary>
    private void RevealActiveRequest(object req)
    {
        _activeRequest = req;
        ResetPerRequestState();
        FlushNarrative(); // safe: background thread is now confirmed blocked in Post() (no-op if already flushed, e.g. for a deferred Block reveal)
        ApplyHighlightsForRequest(req);
        // Must run before PopulateHandStrip -- it switches to this decision's phase
        // camera, and PopulateHandStrip reads whichever camera is active right now
        // to assign each spawned card's DropCamera (used for the drop raycast). In
        // the old order, cards were handed a stale camera from the previous
        // decision (or none at all), silently breaking every drag-and-drop drop.
        ApplyPhaseCameraForRequest(req);
        PopulateHandStrip(req);

        // Piloting a move off the OnGUI panels for the decisions that already fully
        // resolve some other way -- Receive/Set/Dig/Chase via the existing highlight +
        // hand-strip drag (HandleCardDropped); FreeBallTarget, HitCards' own slot
        // picker, AttackLane, and TipOrHit via real world-anchored buttons; Block via
        // drag-and-drop plus a world button when the lane's ambiguous
        // (HandleBlockCardDrop). Only Serve, Exchange, and Cover still use an OnGUI
        // panel -- Exchange/Cover have no natural world target at all (no card is
        // played TO anyone), and Serve's panel coexists with its own drag support.
        switch (req)
        {
            case ReceiveRequest receiveReq:
                SetPromptText($"Choose a card to receive (serve = {receiveReq.ServeValue}):");
                break;
            case SetCardRequest setReq:
                SetPromptText(setReq.BrokenPlay ? "Choose a set card (BROKEN PLAY):" : "Choose a set card:");
                break;
            case DigCardRequest digReq:
                SetPromptText($"Choose a dig card (target {digReq.TargetValue}, {digReq.DigType}):");
                break;
            case ChaseCardRequest chaseReq:
                SetPromptText($"Choose a chase card (need {chaseReq.TargetValue - chaseReq.RunningTotal} more, total {chaseReq.RunningTotal}):");
                break;
            case FreeBallDiscardRequest:
                // Purely cosmetic -- Core itself never tracks who on the recovering
                // side actually redirects the ball before it crosses, so this invents
                // a target (any teammate other than the chaser, chosen at random) so
                // the ball visibly moves off the chaser instead of sitting dead.
                // Holds late in the bounce (freeBallDiscardPauseFraction) for this
                // decision rather than landing on the teammate and sitting there --
                // the "never pause on someone's head" rule overrides the earlier
                // choice to play this bounce straight through.
                StartCoroutine(PlayFreeBallDiscardBounce());
                break;
            case HitCardsRequest:
                SetPromptText("Drag cards onto your attackers:");
                ShowDoneButton("Done", () => (object)(_hitPlacements ?? new List<(int, Card, AttackPosition)>()));
                break;
            case BlockCardsRequest:
                // Blind commit, ahead of the attacker's final lane. Normally the AI's
                // own pass is still held in flight toward its setter (SetRegex,
                // _holdForBlock), and that held flight drives the slow-motion itself;
                // only if the ball genuinely isn't moving does this need its own
                // standalone hold (see the header comment above decisionHoldRampDuration).
                if (_ballFlight == null || !_ballFlight.IsInFlight)
                {
                    StartDecisionHold();
                }
                SetPromptText("Drag cards onto your blockers:");
                ShowDoneButton("Done", () => (object)(_blockPlacements ?? new Dictionary<PlayerRole, (int, Card)>()));
                break;
            case AttackLaneRequest laneReq:
                ShowAttackLaneButtons(laneReq);
                break;
            case TipOrHitRequest tipReq:
                ShowTipOrHitButtons(tipReq);
                break;
        }

        // Unlike the Serve->Receive leg (whose "Serve:" narrative line is written
        // BEFORE ChooseReceiveCard runs in Core, so HandleLine's regex-driven
        // MoveBallTo naturally starts while the decision is still pending), PhaseSet
        // calls ChooseSetCard BEFORE appending its "Set:" line -- by the time that
        // line exists and HandleLine would see it, the human has already answered,
        // too late to still pause and wait for it. Trigger the flight here instead,
        // the moment the request itself goes live, so the pause genuinely happens
        // before the answer exists. Only team A ever produces a request (the AI
        // resolves synchronously with no request), so this is always the human's own
        // setter -- the AI's own Set leg still relies on the narrative line below.
        if (req is DigCardRequest or CoverAttemptRequest && _holdForDig && !_digPreLaunched)
        {
            _digPreLaunched = true;
            StartCoroutine(PreLaunchHumanDig());
        }

        if (req is SetCardRequest)
        {
            _humanSetLegStarted = true;
            StartCoroutine(MoveBallToSetterWhenReady());
        }

        // Same "trigger the instant the request itself goes live" reasoning as
        // SetCardRequest above, for the opposite end of a rally: ServeRequest only ever
        // exists for a human server (team A -- the AI resolves ChooseServe synchronously
        // with no request at all), and it's posted the moment PhaseServe calls
        // ChooseServe, well before Core's "Serve:" line is narrated (that only happens
        // once this request is answered). Waiting for that line, like the rest of the
        // Serve handling in HandleLine still does, meant the receiving team sat frozen
        // in last rally's positions for however long the human took to actually choose
        // their serve card/target -- exactly the "nobody has moved" complaint this
        // fixes. Neither the toss nor the receiving team's formation needs to know the
        // eventual card/target: the toss is just a vertical hop-and-catch at the
        // server's own spot, and Receive formation is a fixed team shape, not aimed at
        // whichever role ends up targeted.
        if (req is ServeRequest)
        {
            // Same early-trigger reasoning as the formation/toss lines above -- the
            // previous rally's floating labels (a dig's transferred number, lingering
            // "X" attackers, etc.) would otherwise sit on screen through this entire
            // decision, since HandleLine's own ClearFloatingNumbers doesn't run until
            // the "Serve:" line exists, which requires this very choice to already be
            // answered. Confirmed live: without this, "Choose a card to serve" showed
            // straight through last rally's leftover numbers.
            ClearFloatingNumbers();
            // The RECEIVING team already eased into shape here -- but the SERVING team
            // itself only ever got an instant SnapTeamToFormation, and only once
            // "Serve:" narrates, which (per the reasoning above) is well after this
            // reveal and entirely at the mercy of the human's own answer speed. Every
            // player who isn't the setter -- the ones PlayServeToss itself repositions
            // -- just sat wherever the PREVIOUS rally's Attack/Dig formation left them
            // for the whole "Choose a card to serve" decision, then teleported the
            // instant the choice was made. Confirmed live as exactly that: a team
            // frozen in last rally's shape, then snapping. Fixed by starting BOTH
            // teams' proper eases here, and -- since "even if we need to wait for the
            // characters to move" is the whole point -- explicitly holding the toss
            // until that settle time has genuinely elapsed, not just hoping it beats
            // the toss+decision time by luck.
            _pendingServeToss = StartCoroutine(PrepareServeFormationThenToss());
        }
    }

    /// <summary>
    /// Waits for a just-flushed narrative batch to finish animating (playback is null
    /// when there was nothing new to wait for), then reveals whichever request was
    /// staged alongside it -- see Update()'s own comment on why every request type
    /// goes through this unconditionally.
    /// </summary>
    private void ArmHoldForRequest(object req)
    {
        switch (req)
        {
            case SetCardRequest:
                _holdForHumanAttack = true;
                break;
            case BlockCardsRequest:
                _holdForBlock = true;
                break;
            case TipOrHitRequest:
                _holdForTip = true;
                break;
            case ChaseCardRequest:
                _holdForChase = true;
                break;
            case DigCardRequest or CoverAttemptRequest:
                // Either the opponent's attack coming over, or (human attacking) the
                // human's own hit deflecting back off the block -- both get a flight
                // that holds partway (PreLaunchHumanDig).
                _holdForDig = true;
                break;
        }
    }

    private IEnumerator RevealAfterFlush(Coroutine playback)
    {
        if (playback != null)
        {
            yield return playback;
        }
        if (_pendingReveal != null)
        {
            object toReveal = _pendingReveal;
            _pendingReveal = null;
            RevealActiveRequest(toReveal);
        }
    }

    // -- Hand-strip population: shows the real hand for whichever decision is active.
    // Purely visual for now -- these spawned cards don't respond to clicks yet, the
    // OnGUI buttons are still what actually resolves a decision.

    private void PopulateHandStrip(object request)
    {
        ClearHandStrip();
        List<Card> hand = GetHandForRequest(request);
        if (hand == null || cardParent == null || cardPrefab == null)
        {
            return;
        }
        Camera decisionCamera = GetActiveGameplayCamera();
        foreach (Card card in hand)
        {
            HandCardView view = Instantiate(cardPrefab, cardParent);
            view.SetCard(card);
            view.DropCamera = decisionCamera;
            view.OnDroppedOnTransform = HandleCardDropped;
        }
    }

    /// <summary>
    /// Whichever camera is actually rendering the 3D scene right now, for the drop
    /// raycast. Deliberately not hardcoded to defaultCamera/followCamera specifically --
    /// as more per-phase cameras get added, this keeps working as long as exactly one
    /// gameplay camera is enabled at a time (same convention SetActiveCamera already
    /// relies on for main/follow).
    /// </summary>
    private static Camera GetActiveGameplayCamera()
    {
        Camera[] cameras = Camera.allCameras;
        return cameras.Length > 0 ? cameras[0] : null;
    }

    private void ClearHandStrip()
    {
        if (cardParent == null)
        {
            return;
        }
        for (int i = cardParent.childCount - 1; i >= 0; i--)
        {
            Destroy(cardParent.GetChild(i).gameObject);
        }
    }

    /// <summary>Null for AttackLaneRequest/TipOrHitRequest -- those pick among lanes/shot
    /// type, not a card from hand, so there's nothing to show in the strip for them.</summary>
    private static List<Card> GetHandForRequest(object request) => request switch
    {
        ServeRequest r => r.Hand,
        ReceiveRequest r => r.Hand,
        SetCardRequest r => r.Hand,
        HitCardsRequest r => r.Hand,
        BlockCardsRequest r => r.Hand,
        DigCardRequest r => r.Hand,
        ChaseCardRequest r => r.Hand,
        FreeBallDiscardRequest r => r.Hand,
        ExchangeCardRequest r => r.Hand,
        CoverAttemptRequest r => r.Hand,
        _ => null,
    };

    // -- Drag-and-drop resolution: a card dropped on a player Transform resolves the
    // active decision if that player is a legal target for it. Covers the decisions
    // with an unambiguous single-player (or single-lane) target: Serve, Receive, Set,
    // Dig, Chase, Block. HitCards is deliberately NOT wired here yet -- a lane's front
    // vs. back slot can't be disambiguated from "which player capsule got hit" alone,
    // that needs its own design pass. Exchange/Cover have no target player at all (see
    // ApplyHighlightsForRequest's comment on those two). Those four stay OnGUI-only;
    // the OnGUI panels for the drag-covered types also still work, unchanged -- drag is
    // an additional path to the same ResolveActiveRequest/_blockPlacements state, not a
    // replacement, so the game stays fully playable if a drop ever misses.
    private void HandleCardDropped(HandCardView view, Transform hit)
    {
        if (hit == null || _transformOwner == null || !_transformOwner.TryGetValue(hit, out var owner))
        {
            return;
        }
        (string team, PlayerRole role) = owner;
        Card card = view.CardValue;

        switch (_activeRequest)
        {
            case ServeRequest serveReq when team == _teamBName:
                foreach (GridPlayer candidate in serveReq.EligibleReceivers)
                {
                    if (candidate.Role == role)
                    {
                        ResolveActiveRequest((card, candidate));
                        break;
                    }
                }
                break;
            case ReceiveRequest when team == _teamAName && role == _lastServeTargetRole:
                ResolveActiveRequest(card);
                break;
            case SetCardRequest when team == _teamAName && role == PlayerRole.Setter:
                ResolveActiveRequest(card);
                break;
            case DigCardRequest when team == _teamAName && role == GetCurrentDefenderRole():
                ResolveActiveRequest(card);
                break;
            case ChaseCardRequest when team == _teamAName && _chaseRole.HasValue && role == _chaseRole.Value:
                ResolveActiveRequest(card);
                break;
            case BlockCardsRequest blockReq when team == _teamAName:
                HandleBlockCardDrop(blockReq, role, card, view);
                break;
            case HitCardsRequest hitReq when team == _teamAName:
                HandleHitCardDrop(hitReq, role, card, view);
                break;
        }
    }

    /// <summary>
    /// Drops a card directly onto a blocker's own capsule. When only one lane is
    /// actually legal for them (their own reach intersected with what's attacked),
    /// there's no real choice to make -- commit immediately. When more than one lane
    /// is legal (MB reaching all three, or an outside blocker whose own lane and MB's
    /// are both attacked), ask which one via a world button per legal lane, positioned
    /// above this blocker -- same "ask, don't guess" shape HandleHitCardDrop uses for
    /// DS's own multiple open back lanes.
    /// </summary>
    private void HandleBlockCardDrop(BlockCardsRequest request, PlayerRole role, Card card, HandCardView view)
    {
        if (!PlayerRoleExtensions.BlockableLanes.TryGetValue(role, out var reachableLanes))
        {
            return; // not a front-row blocking role at all
        }
        if (_blockPlacements != null && _blockPlacements.ContainsKey(role))
        {
            return; // this blocker already committed via an earlier drop
        }
        if (_blockPlacements != null && _blockPlacements.Values.Any(v => v.Card.Equals(card)))
        {
            return; // this exact card already committed to another blocker
        }
        var legalLanes = reachableLanes.Where(request.AttackLanes.Contains).ToList();
        if (legalLanes.Count == 0)
        {
            return;
        }

        Destroy(view.gameObject); // committed to this blocker either way -- remove from the draggable strip

        if (legalLanes.Count == 1)
        {
            CommitBlockPlacement(role, legalLanes[0], card);
            return;
        }

        // Positioned at each candidate lane's own physical spot -- the attacking
        // team's own hitter for that lane -- rather than stacked above the blocker,
        // same "DS lane disambiguation" fix as HandleHitCardDrop: a stack above one
        // fixed blocker doesn't relate to which attacker each lane actually belongs
        // to, and reads as an arbitrary vertical pile that overlaps at most camera
        // angles. Same screen-space sort-then-spread approach.
        Camera cam = GetActiveGameplayCamera();
        string tempo = GetTempoLabel(_lastSetCardValue);
        var entries = new List<(int Lane, Vector2 ScreenPos)>();
        if (cam != null && _lastAttackingTeam != null)
        {
            foreach (int lane in legalLanes)
            {
                if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole attackerRole)
                    && TryWorldToScreenPoint(cam, GetAttackContactPoint(_lastAttackingTeam, attackerRole, tempo) + Vector3.up * ballHeight, out Vector2 screenPos))
                {
                    entries.Add((lane, screenPos));
                }
            }
        }
        entries.Sort((a, b) => a.ScreenPos.x.CompareTo(b.ScreenPos.x));

        // All of this lane's buttons need to disappear together the instant ANY one of
        // them is clicked -- only one lane can ever apply to this single dropped card.
        var group = new List<GameObject>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float xOffset = (i - (entries.Count - 1) / 2f) * laneButtonSpacing;
            Vector2 screenPos = new(entry.ScreenPos.x + xOffset, entry.ScreenPos.y + worldButtonYOffset);
            GameObject go = ShowScreenButton(screenPos, $"Lane {entry.Lane}", () =>
            {
                CommitBlockPlacement(role, entry.Lane, card);
                foreach (GameObject sibling in group)
                {
                    if (sibling != null)
                    {
                        Destroy(sibling);
                    }
                }
            });
            if (go != null)
            {
                group.Add(go);
            }
        }
    }

    private void CommitBlockPlacement(PlayerRole role, int lane, Card card)
    {
        _blockPlacements ??= new Dictionary<PlayerRole, (int, Card)>();
        _blockPlacements[role] = (lane, card);
        // Shown immediately, per blocker -- no reason to make it wait for "Done" now
        // that there's no OnGUI list to reveal it all at once from.
        SetFloatingNumber(_teamAName, role, card.Value);
    }

    /// <summary>
    /// Drops a card directly onto a hitter's own capsule -- same "ask, don't guess"
    /// shape as HandleBlockCardDrop. A front-row role (OH/MB/OPP) has at most one
    /// legal slot (their own lane, if this tempo's Template.FrontLanes includes it
    /// and it isn't already used) -- commit immediately, nothing to ask. DS is the
    /// only role with more than one possible slot (every still-open back lane) --
    /// ask which one via a world button per open lane, positioned above DS.
    /// </summary>
    private void HandleHitCardDrop(HitCardsRequest request, PlayerRole role, Card card, HandCardView view)
    {
        if ((_hitPlacements?.Count ?? 0) >= request.Template.MaxAttackers)
        {
            return; // every attacker slot already filled
        }
        if (_hitPlacements != null && _hitPlacements.Any(p => p.Card.Equals(card)))
        {
            return; // this exact card already committed to another slot
        }

        var usedSlots = _hitPlacements != null
            ? new HashSet<(int Lane, AttackPosition Position)>(_hitPlacements.Select(p => (p.Lane, p.Position)))
            : new HashSet<(int, AttackPosition)>();
        var candidates = new List<(int Lane, AttackPosition Position)>();
        if (role == PlayerRole.Ds)
        {
            foreach (int lane in request.Template.BackLanes)
            {
                if (!usedSlots.Contains((lane, AttackPosition.Back)))
                {
                    candidates.Add((lane, AttackPosition.Back));
                }
            }
        }
        else
        {
            foreach (var kv in PlayerRoleExtensions.LaneToRole)
            {
                if (kv.Value == role && request.Template.FrontLanes.Contains(kv.Key)
                    && !usedSlots.Contains((kv.Key, AttackPosition.Front)))
                {
                    candidates.Add((kv.Key, AttackPosition.Front));
                }
            }
        }
        if (candidates.Count == 0)
        {
            return; // not a legal attacker for this tempo, or their only slot's already used
        }

        Destroy(view.gameObject); // committed to this hitter either way -- remove from the draggable strip

        if (candidates.Count == 1)
        {
            CommitHitPlacement(candidates[0].Lane, candidates[0].Position, card, role, request);
            return;
        }

        // Positioned at each candidate lane's own physical spot (the front-row
        // hitter who'd normally occupy it) rather than stacked above DS -- DS's own
        // back-row position doesn't move based on which lane they're attacking into,
        // so stacking buttons above it read as an arbitrary vertical pile with no
        // relation to the actual choice, and confirmed live to overlap/read as
        // unreadable from most camera angles. Same screen-space sort-then-spread
        // approach as ShowAttackLaneButtons, for the same "Camera-pixel vs
        // Screen-pixel" and "iteration order vs screen order" reasons that fixed
        // that one.
        Camera cam = GetActiveGameplayCamera();
        string tempo = GetTempoLabel(_lastSetCardValue);
        var entries = new List<(int Lane, AttackPosition Position, Vector2 ScreenPos)>();
        if (cam != null)
        {
            foreach (var slot in candidates)
            {
                if (PlayerRoleExtensions.LaneToRole.TryGetValue(slot.Lane, out PlayerRole laneRole)
                    && TryWorldToScreenPoint(cam, GetAttackContactPoint(_teamAName, laneRole, tempo) + Vector3.up * ballHeight, out Vector2 screenPos))
                {
                    entries.Add((slot.Lane, slot.Position, screenPos));
                }
            }
        }
        entries.Sort((a, b) => a.ScreenPos.x.CompareTo(b.ScreenPos.x));

        // All of this hitter's lane buttons need to disappear together the instant
        // ANY one of them is clicked -- only one lane can ever apply to this drop.
        var group = new List<GameObject>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float xOffset = (i - (entries.Count - 1) / 2f) * laneButtonSpacing;
            Vector2 screenPos = new(entry.ScreenPos.x + xOffset, entry.ScreenPos.y + worldButtonYOffset);
            GameObject go = ShowScreenButton(screenPos, $"Lane {entry.Lane}", () =>
            {
                CommitHitPlacement(entry.Lane, entry.Position, card, role, request);
                foreach (GameObject sibling in group)
                {
                    if (sibling != null)
                    {
                        Destroy(sibling);
                    }
                }
            });
            if (go != null)
            {
                group.Add(go);
            }
        }
    }

    private void CommitHitPlacement(int lane, AttackPosition position, Card card, PlayerRole role, HitCardsRequest request)
    {
        _hitPlacements ??= new List<(int, Card, AttackPosition)>();
        _hitPlacements.Add((lane, card, position));
        // Live, the instant the card lands -- not the real value (see AttackRegex's
        // own "X" for why), and not waiting on Core's own "Attack:" narrative line,
        // which only exists once every placement in this HitCardsRequest is
        // submitted together.
        SetFloatingLabel(_teamAName, role, "X", Color.white);
        HighlightHitOptions(request);
        UpdateHitTrajectoryPreviews(request);
    }

    // -- Player highlighting: orange = the single player actively making this decision
    // (digger/setter/hitter), green = the set of hitter options being chosen among
    // (e.g. every eligible attack lane while the setter places hit cards), blue = the
    // blockers currently being selected. Almost always the human's own players
    // ("Players" group) -- the one exception is ServeRequest, which only ever exists
    // while team A is serving (the AI decides instantly, no request), so its own
    // "who can this target" options are necessarily on the AI's side instead.

    private void ApplyHighlightsForRequest(object request)
    {
        ClearAllHighlights();
        switch (request)
        {
            case ServeRequest serveReq:
                HighlightServeTargets(serveReq);
                break;
            case SetCardRequest:
                HighlightRole(PlayerRole.Setter, activeSelectionColor);
                break;
            case DigCardRequest:
                HighlightCurrentDefender(activeSelectionColor);
                break;
            case ChaseCardRequest:
                // Not HighlightCurrentDefender -- GetCurrentDefenderRole() is a dig-lane
                // formula (needs _lastLane, only ever set by an attack's own Resolve:
                // line) that's meaningless here: a chase only ever follows a failed
                // SERVE reception, before any attack has happened this rally at all.
                // _chaseRole (set off the "Chase:" header line -- see HandleLine) is the
                // real answer.
                if (_chaseRole.HasValue)
                {
                    HighlightRole(_chaseRole.Value, activeSelectionColor);
                }
                break;
            case ReceiveRequest:
                if (_lastServeTargetRole.HasValue)
                {
                    HighlightRole(_lastServeTargetRole.Value, activeSelectionColor);
                }
                break;
            case HitCardsRequest hitReq:
                // The human's own attack phase starting -- same one-shot "all other
                // numbers disappear" wipe AttackRegex does for the AI's side, just
                // triggered here instead since these placements happen live via drag,
                // well before any "Attack:" narrative line exists for them.
                if (!_attackPhaseCleared)
                {
                    ClearFloatingNumbers();
                    _attackPhaseCleared = true;
                }
                HighlightHitOptions(hitReq);
                UpdateHitTrajectoryPreviews(hitReq);
                break;
            case AttackLaneRequest laneReq:
                // Same one-shot "all other numbers disappear" wipe AttackRegex/
                // HitCardsRequest's own reveal already do -- needed as a fallback here
                // too for a fully forced-blind exchange (every lane blind-drawn, no
                // hand cards left on either side): neither of those two triggers ever
                // fires then (no HitCardsRequest goes out, and blind-drawn Attack:
                // lines don't match AttackRegex at all -- see its own comment), so
                // without this a stale label (confirmed live: the Set's own tempo
                // name) can still be sitting on screen once lane selection begins.
                if (!_attackPhaseCleared)
                {
                    ClearFloatingNumbers();
                    _attackPhaseCleared = true;
                }
                foreach (int lane in laneReq.AttackCards.Keys)
                {
                    HighlightLane(lane, hitterOptionColor);
                }
                UpdateAttackLaneTrajectoryPreviews(laneReq);
                break;
            case TipOrHitRequest:
                if (_lastChosenAttackLane.HasValue)
                {
                    HighlightLane(_lastChosenAttackLane.Value, activeSelectionColor);
                }
                break;
            case BlockCardsRequest blockReq:
                // Highlight every blocker who has at least one attacked lane within
                // reach -- not just each lane's nominal "owner" -- since more than one
                // blocker can now legally help cover the same lane.
                foreach (PlayerRole blockerRole in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
                {
                    if (PlayerRoleExtensions.BlockableLanes[blockerRole].Any(blockReq.AttackLanes.Contains))
                    {
                        HighlightRole(blockerRole, blockerSelectionColor);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Which physical player a given hit-placement slot represents. Front lanes map 1:1
    /// to their own role via LaneToRole; back lanes have no such fixed mapping in Core at
    /// all (it attributes every attack in a lane to that lane's *front* role for ability
    /// math, regardless of position -- confirmed in Rally.cs) and per the user, back-row
    /// hitting is DS-only for now (Setter never attacks, Libero can't attack at all).
    /// </summary>
    private static PlayerRole HitSlotRole(int lane, AttackPosition position) =>
        position == AttackPosition.Front ? PlayerRoleExtensions.LaneToRole[lane] : PlayerRole.Ds;

    /// <summary>
    /// Green on every still-open hitting option -- called both when a HitCardsRequest
    /// first goes active and again every time _hitPlacements changes, so the
    /// highlight always reflects the live selection state.
    /// </summary>
    private void HighlightHitOptions(HitCardsRequest request)
    {
        // Without this, a hitter's green highlight would stay stuck forever once
        // their slot gets filled -- this method only ever adds colors for
        // currently-relevant roles below, it never removes one for a role that's no
        // longer under consideration.
        ClearAllHighlights();
        var usedSlots = _hitPlacements != null
            ? new HashSet<(int Lane, AttackPosition Position)>(_hitPlacements.Select(p => (p.Lane, p.Position)))
            : new HashSet<(int, AttackPosition)>();
        var colors = new Dictionary<PlayerRole, Color>();

        void Consider(int lane, AttackPosition position)
        {
            if (usedSlots.Contains((lane, position)))
            {
                return;
            }
            PlayerRole role = HitSlotRole(lane, position);
            colors[role] = hitterOptionColor;
        }

        foreach (int lane in request.Template.FrontLanes)
        {
            Consider(lane, AttackPosition.Front);
        }
        foreach (int lane in request.Template.BackLanes)
        {
            Consider(lane, AttackPosition.Back);
        }
        foreach (var kv in colors)
        {
            HighlightRole(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// The trajectory-preview counterpart to HighlightHitOptions -- same Template-driven
    /// iteration over every legal (lane, position) this set's tempo allows, called from
    /// every place _hitPlacements changes so the arcs always reflect the live
    /// selection. Unlike HighlightHitOptions (which only ever colors a player
    /// capsule, so skipping an already-used slot is fine -- the capsule's already been
    /// dealt with elsewhere), this one draws an arc for used slots too: once a card's
    /// actually been placed on a lane, its arc should stay visible and turn yellow, not
    /// disappear. Once every attacker slot is filled, every non-committed option's arc
    /// is hidden -- "when all hitters have been selected, all others disappear."
    /// </summary>
    private void UpdateHitTrajectoryPreviews(HitCardsRequest request)
    {
        var usedSlots = _hitPlacements != null
            ? new HashSet<(int Lane, AttackPosition Position)>(_hitPlacements.Select(p => (p.Lane, p.Position)))
            : new HashSet<(int, AttackPosition)>();
        bool allSlotsFilled = (_hitPlacements?.Count ?? 0) >= request.Template.MaxAttackers;
        string tempo = GetTempoLabel(_lastSetCardValue);
        Vector3 setterPos = GetSetContactPoint(_teamAName);
        var shownRoles = new HashSet<PlayerRole>();

        void Consider(int lane, AttackPosition position)
        {
            bool isUsed = usedSlots.Contains((lane, position));
            if (allSlotsFilled && !isUsed)
            {
                return; // once everyone's picked, only the committed lanes stay visible
            }
            PlayerRole role = HitSlotRole(lane, position);
            if (!shownRoles.Add(role))
            {
                return; // DS can appear via multiple back lanes -- only draw it once
            }
            Color color = isUsed ? trajectorySelectedColor : trajectoryLineColor;
            ShowTrajectoryPreview(role, setterPos + Vector3.up * setContactHeight,
                GetAttackContactPoint(_teamAName, role, tempo) + Vector3.up * attackContactHeight, color,
                GetFormationPeakHeight(_teamAName, role, "Attack", tempo));
        }

        foreach (int lane in request.Template.FrontLanes)
        {
            Consider(lane, AttackPosition.Front);
        }
        foreach (int lane in request.Template.BackLanes)
        {
            Consider(lane, AttackPosition.Back);
        }

        foreach (PlayerRole role in _trajectoryLines.Keys.Where(r => !shownRoles.Contains(r)).ToList())
        {
            HideTrajectoryPreview(role);
        }
    }

    /// <summary>
    /// Narrows the trajectory pool down to exactly the lanes AttackLaneRequest is
    /// choosing among (laneReq.AttackCards.Keys) -- by construction this should already
    /// match what HitCards left committed, but this makes it authoritative rather than
    /// assuming the two never drift.
    /// </summary>
    private void UpdateAttackLaneTrajectoryPreviews(AttackLaneRequest request)
    {
        string tempo = GetTempoLabel(_lastSetCardValue);
        Vector3 setterPos = GetSetContactPoint(_teamAName);
        var shownRoles = new HashSet<PlayerRole>();
        foreach (int lane in request.AttackCards.Keys)
        {
            if (!PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role))
            {
                continue;
            }
            shownRoles.Add(role);
            ShowTrajectoryPreview(role, setterPos + Vector3.up * setContactHeight,
                GetAttackContactPoint(_teamAName, role, tempo) + Vector3.up * attackContactHeight, trajectorySelectedColor,
                GetFormationPeakHeight(_teamAName, role, "Attack", tempo));
        }
        foreach (PlayerRole role in _trajectoryLines.Keys.Where(r => !shownRoles.Contains(r)).ToList())
        {
            HideTrajectoryPreview(role);
        }
    }

    private void HighlightCurrentDefender(Color color)
    {
        PlayerRole? role = GetCurrentDefenderRole();
        if (role.HasValue)
        {
            HighlightRole(role.Value, color);
        }
    }

    private PlayerRole? GetCurrentDefenderRole() =>
        _lastLane < 0 ? null : AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);

    /// <summary>
    /// A failed serve reception's chase is never the original receiver -- Team.
    /// EligibleReceivers() only ever returns Ds/Libero (the only two back-row roles
    /// that can receive a serve at all), so "the other one of those two" is always a
    /// real, adjacent back-row teammate, with no need for a live-position distance
    /// search.
    /// </summary>
    private static PlayerRole GetAdjacentChaseRole(PlayerRole receiverRole) =>
        receiverRole == PlayerRole.Ds ? PlayerRole.Libero : PlayerRole.Ds;

    private void HighlightLane(int lane, Color color)
    {
        if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role))
        {
            HighlightRole(role, color);
        }
    }

    private void HighlightRole(PlayerRole role, Color color)
    {
        if (_teamPositions.TryGetValue(_teamAName, out var positions) && positions.TryGetValue(role, out Transform t))
        {
            ApplyHighlight(t, color);
        }
    }

    private void ApplyHighlight(Transform t, Color? color)
    {
        var renderer = t.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }
        if (!_highlightBlocks.TryGetValue(t, out MaterialPropertyBlock block))
        {
            block = new MaterialPropertyBlock();
            _highlightBlocks[t] = block;
        }
        if (color.HasValue)
        {
            block.SetColor("_BaseColor", color.Value);
        }
        else
        {
            block.Clear();
        }
        renderer.SetPropertyBlock(block);
    }

    private void ClearAllHighlights()
    {
        if (!_teamPositions.TryGetValue(_teamAName, out var positions))
        {
            return;
        }
        foreach (Transform t in positions.Values)
        {
            ApplyHighlight(t, null);
        }
        ClearServeTargetHighlights();
    }

    /// <summary>
    /// Green on every role a live ServeRequest can target -- request.EligibleReceivers
    /// is already exactly that list (Team.EligibleServeReceivers, see Core/Team.cs),
    /// so this just colors each of them on the AI team ("who can this target" is
    /// always about the opponent, since ServeRequest only exists while team A serves).
    /// </summary>
    private void HighlightServeTargets(ServeRequest request)
    {
        if (!_teamPositions.TryGetValue(_teamBName, out var positions))
        {
            return;
        }
        foreach (GridPlayer receiver in request.EligibleReceivers)
        {
            if (positions.TryGetValue(receiver.Role, out Transform t))
            {
                ApplyHighlight(t, hitterOptionColor);
            }
        }
    }

    /// <summary>
    /// Restores the AI team's permanent tint (aiTeamColor) rather than a bare null --
    /// HighlightServeTargets shares the exact same MaterialPropertyBlock mechanism
    /// that tint uses (see Start(), which applies it once via this same ApplyHighlight),
    /// so clearing outright would revert a just-highlighted role to the material's raw
    /// default color instead of back to purple. Called unconditionally from
    /// ClearAllHighlights (not gated on the previous request having been a
    /// ServeRequest) so it's correct no matter what the last decision was.
    /// </summary>
    private void ClearServeTargetHighlights()
    {
        if (!_teamPositions.TryGetValue(_teamBName, out var positions))
        {
            return;
        }
        foreach (Transform t in positions.Values)
        {
            ApplyHighlight(t, aiTeamColor);
        }
    }

    // -- Floating card labels: a short text (usually a card value, sometimes a word --
    // "Free Ball", a tempo name) hovering above whichever player or the ball just
    // became relevant, colored to carry meaning (green/red for success/failure, a
    // tempo's own color for a set, plain white otherwise) -- driven off the same
    // narrative lines that already drive ball movement, see HandleLine below. Cleared
    // at each phase boundary the user's own spec calls out (a fresh serve, an attack
    // crossing over for a dig, a free ball crossing, and the Set->Attack transition),
    // so only the exchange currently in flight is ever showing.

    private void SetFloatingLabel(string team, PlayerRole role, string text, Color color)
    {
        if (_teamPositions.TryGetValue(team, out var positions) && positions.TryGetValue(role, out Transform t))
        {
            _floatingNumbers[t] = (text, color);
        }
    }

    private void SetFloatingNumber(string team, PlayerRole role, int value) =>
        SetFloatingLabel(team, role, value.ToString(), Color.white);

    private void SetFloatingLabelOnBall(string text, Color color)
    {
        if (_ball != null)
        {
            _floatingNumbers[_ball] = (text, color);
        }
    }

    private void ClearFloatingNumbers() => _floatingNumbers.Clear();

    private void ResetPerRequestState()
    {
        _servePendingCard = null;
        _hitPlacements = null;
        _blockPlacements = null;
        ClearWorldButtons();
        SetPromptText(string.Empty);
        EndDecisionHold();
        // Safety net for a rally that ends before a fresh Swing (stuffed, killed) --
        // without this a stale offset from the previous exchange could leak into the
        // next one's dig target.
        _pendingAttackOffset = Vector3.zero;
    }

    /// <summary>
    /// Starts easing Time.timeScale down toward decisionHoldTimeScale and holds it there
    /// -- for a decision with no ball flight of its own to pause (see the header comment
    /// above decisionHoldRampDuration). Stops any previous hold first so a second call
    /// cleanly replaces it rather than running two ramps at once.
    /// </summary>
    private void StartDecisionHold()
    {
        if (_decisionHoldCoroutine != null)
        {
            StopCoroutine(_decisionHoldCoroutine);
        }
        _decisionHoldCoroutine = StartCoroutine(RampTimeScaleDown());
    }

    /// <summary>Snaps straight back to full speed -- matching BallFlight.Resume()'s own
    /// instant (not eased) un-pause the moment a decision resolves.</summary>
    private void EndDecisionHold()
    {
        if (_decisionHoldCoroutine != null)
        {
            StopCoroutine(_decisionHoldCoroutine);
            _decisionHoldCoroutine = null;
        }
        Time.timeScale = 1f;
    }

    private IEnumerator RampTimeScaleDown()
    {
        float start = Time.timeScale;
        float elapsed = 0f;
        while (elapsed < decisionHoldRampDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(start, decisionHoldTimeScale, elapsed / decisionHoldRampDuration);
            yield return null;
        }
        Time.timeScale = decisionHoldTimeScale;
    }

    /// <summary>Answers the currently-active request and clears it. Call exactly once per request.</summary>
    private void ResolveActiveRequest(object response)
    {
        // Captured here (not in ShowAttackLaneButtons' onClick) so it's correct no
        // matter how the response was produced -- TipOrHitRequest's highlight depends on it.
        if (_activeRequest is AttackLaneRequest && response is int lane)
        {
            _lastChosenAttackLane = lane;
        }
        // Held flights release themselves off their own holdWhile conditions (see
        // BallFlight._holdWhile) -- these two are the ones answered directly.
        if (_activeRequest is BlockCardsRequest)
        {
            _holdForBlock = false;
        }
        if (_activeRequest is TipOrHitRequest)
        {
            _holdForTip = false;
        }
        _channel.Resolve(response);
        _activeRequest = null;
        ResetPerRequestState();
    }

    // -- OnGUI dispatch --

    private void OnGUI()
    {
        // DrawFloatingNumbers computes real screen-space positions itself (via
        // Camera.WorldToScreenPoint), so it stays outside the scale transform below --
        // wrapping it too would double-transform those positions. It scales its own
        // font/label size directly instead.
        float guiScale = Screen.height / referenceGuiHeight;
        DrawFloatingNumbers(guiScale);

        Matrix4x4 previousMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(guiScale, guiScale), Vector2.zero);

        DrawScoreboard(guiScale);

        switch (_activeRequest)
        {
            case ServeRequest serveReq:
                DrawServeRequest(serveReq);
                break;
            case ExchangeCardRequest exchangeReq:
                DrawCardChoice(exchangeReq.Hand, $"Exchange a card for deck top ({exchangeReq.DeckTop})? (or Decline)",
                    allowDecline: true, card => ResolveActiveRequest(card));
                break;
            case CoverAttemptRequest coverReq:
                DrawCardChoice(coverReq.Hand, $"Attempt cover (need >= {coverReq.Threshold})? (or Decline)",
                    allowDecline: true, card => ResolveActiveRequest(card));
                break;
            case FreeBallDiscardRequest discardReq:
                // No natural world target for a pure discard (it isn't going TO any
                // specific player), same reasoning that kept Exchange/Cover on this
                // panel rather than converting them -- any card is valid, so there's
                // nothing to highlight either.
                DrawCardChoice(discardReq.Hand, "Chase succeeded! Discard a card to send the free ball:",
                    allowDecline: false, card => ResolveActiveRequest(card.Value));
                break;
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawScoreboard(float guiScale)
    {
        // Screen.width is real screen pixels, but everything drawn after
        // GUIUtility.ScaleAroundPivot is interpreted in "reference space" (i.e. as if
        // the screen were referenceGuiHeight tall) -- dividing by guiScale converts
        // this one screen-relative position into that same reference space.
        GUILayout.BeginArea(new Rect(Screen.width / guiScale - 220, 20, 200, 60), GUI.skin.box);
        GUILayout.Label($"{_teamAName}: {_scoreA}   {_teamBName}: {_scoreB}");
        GUILayout.EndArea();
    }

    private GUIStyle _floatingNumberStyle;

    private void DrawFloatingNumbers(float guiScale)
    {
        if (_floatingNumbers.Count == 0)
        {
            return;
        }
        Camera cam = GetActiveGameplayCamera();
        if (cam == null)
        {
            return;
        }
        // GUIStyle must be constructed inside an IMGUI callback (OnGUI), not earlier,
        // so this is built lazily here rather than as a static/field initializer. This
        // method draws outside the GUI.matrix scale transform (its positions are real
        // screen coordinates from WorldToScreenPoint, already correct as-is), so its
        // own font/label size are scaled directly here instead.
        _floatingNumberStyle ??= new GUIStyle(GUIStyle.none)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        _floatingNumberStyle.fontSize = Mathf.RoundToInt(22 * guiScale);
        float halfHeight = 15 * guiScale;
        foreach (var kv in _floatingNumbers)
        {
            if (kv.Key == null)
            {
                continue;
            }
            // The ball's own label (Serve's value, a dig's transferred attack value,
            // "Free Ball") sits much closer to it than a player's -- ballHeight + 1f is
            // calibrated for a standing capsule's head, way too high above the ball itself.
            float aboveOffset = kv.Key == _ball ? 0.6f : ballHeight + 1f;
            Vector3 worldPos = kv.Key.position + Vector3.up * aboveOffset;
            if (!TryWorldToScreenPoint(cam, worldPos, out Vector2 screenPos))
            {
                continue;
            }
            float guiY = Screen.height - screenPos.y;
            _floatingNumberStyle.normal.textColor = kv.Value.Color;
            // CalcSize (not a fixed box) since labels now range from a 1-digit number up
            // to a word like "Free Ball" -- a fixed width sized for numbers would clip text.
            Vector2 size = _floatingNumberStyle.CalcSize(new GUIContent(kv.Value.Text));
            float halfWidth = size.x / 2f + 4f * guiScale;
            GUI.Label(new Rect(screenPos.x - halfWidth, guiY - halfHeight, halfWidth * 2, halfHeight * 2), kv.Value.Text, _floatingNumberStyle);
        }
    }

    private void DrawServeRequest(ServeRequest request)
    {
        GUILayout.BeginArea(PanelRect(standardPanelSize), GUI.skin.box);
        if (_servePendingCard == null)
        {
            GUILayout.Label("Choose a card to serve:");
            foreach (Card card in request.Hand)
            {
                if (GUILayout.Button($"{card}"))
                {
                    _servePendingCard = card;
                }
            }
        }
        else
        {
            GUILayout.Label($"Serving {_servePendingCard.Value} — choose a target:");
            foreach (GridPlayer receiver in request.EligibleReceivers)
            {
                if (GUILayout.Button(receiver.Role.DisplayName()))
                {
                    ResolveActiveRequest((_servePendingCard.Value, receiver));
                }
            }
        }
        GUILayout.EndArea();
    }

    /// <summary>
    /// The "Card Canvas" GameObject already in the scene (Screen Space Overlay, with a
    /// CanvasScaler + GraphicRaycaster already set up for HandCardView's own drag) is
    /// reused here rather than creating a whole new Canvas -- one child container,
    /// "World Buttons", holds every button spawned by ShowWorldButton below.
    /// </summary>
    private Transform GetWorldButtonParent()
    {
        if (_worldButtonParent != null)
        {
            return _worldButtonParent;
        }
        GameObject canvasObj = GameObject.Find("Card Canvas");
        if (canvasObj == null)
        {
            return null;
        }
        Transform existing = canvasObj.transform.Find("World Buttons");
        if (existing != null)
        {
            _worldButtonParent = existing;
            return _worldButtonParent;
        }
        var go = new GameObject("World Buttons", typeof(RectTransform));
        go.transform.SetParent(canvasObj.transform, worldPositionStays: false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _worldButtonParent = go.transform;
        return _worldButtonParent;
    }

    /// <summary>
    /// Destroys every button ShowWorldButton has spawned since the last clear. Called
    /// from ResetPerRequestState, so it runs both when a new request goes active and
    /// right after the previous one resolves -- either way, whatever was showing is
    /// stale.
    /// </summary>
    private void ClearWorldButtons()
    {
        foreach (GameObject go in _activeWorldButtons)
        {
            if (go != null)
            {
                Destroy(go);
            }
        }
        _activeWorldButtons.Clear();
    }

    /// <summary>
    /// Spawns one real UGUI button projected onto worldAnchor's current screen position
    /// (plus worldButtonYOffset), labeled label, firing onClick when pressed. Built once
    /// per request rather than redrawn every frame like the OnGUI panels it's replacing
    /// -- safe because the phase camera is locked for the duration of a pending human
    /// decision (ApplyPhaseCameraForRequest), so the projected position won't drift.
    /// Returns null (no button spawned) if worldAnchor projects behind the camera.
    /// </summary>
    private GameObject ShowWorldButton(Vector3 worldAnchor, string label, Action onClick)
    {
        Camera cam = GetActiveGameplayCamera();
        if (cam == null || !TryWorldToScreenPoint(cam, worldAnchor, out Vector2 screenPos))
        {
            return null;
        }
        return CreateButton(new Vector2(screenPos.x, screenPos.y + worldButtonYOffset), label, onClick);
    }

    /// <summary>
    /// Camera.WorldToScreenPoint returns coordinates in the camera's own pixelWidth/
    /// pixelHeight terms, which can genuinely diverge from Screen.width/height under
    /// certain Editor Game View resolution-simulation setups -- confirmed live (a
    /// "Player Attack Camera" reporting 1920x1080 while Screen.width/height read
    /// 1076x1111), the same divergence HandCardView.OnEndDrag already corrects for in
    /// the opposite direction. Every caller here ultimately positions a Screen Space
    /// Overlay UI element, which IS in Screen.width/height terms, so this rescales to
    /// match -- without it, world-anchored buttons/labels land at systematically wrong
    /// (and, for multiple buttons spread apart by a Screen-space offset, sometimes
    /// nearly coincident) positions whenever the two resolutions differ. Returns false
    /// (screenPos left at default) if worldPos projects behind the camera.
    /// </summary>
    private static bool TryWorldToScreenPoint(Camera cam, Vector3 worldPos, out Vector2 screenPos)
    {
        Vector3 raw = cam.WorldToScreenPoint(worldPos);
        if (raw.z <= 0f)
        {
            screenPos = default;
            return false;
        }
        screenPos = new Vector2(
            raw.x * (Screen.width / (float)cam.pixelWidth),
            raw.y * (Screen.height / (float)cam.pixelHeight));
        return true;
    }

    /// <summary>Same button, anchored to a fixed screen position instead of a world
    /// point -- for a control tied to the decision as a whole ("Done") rather than to
    /// any one player.</summary>
    private GameObject ShowScreenButton(Vector2 screenPos, string label, Action onClick) =>
        CreateButton(screenPos, label, onClick);

    /// <summary>
    /// Spawns one real UGUI button at screenPos, labeled label, firing onClick when
    /// pressed. Built once per request rather than redrawn every frame like the OnGUI
    /// panels it's replacing -- safe because the phase camera is locked for the
    /// duration of a pending human decision (ApplyPhaseCameraForRequest), so a
    /// world-derived screenPos won't drift.
    /// </summary>
    private GameObject CreateButton(Vector2 screenPos, string label, Action onClick)
    {
        Transform parent = GetWorldButtonParent();
        if (parent == null)
        {
            return null;
        }

        var go = new GameObject($"Button: {label}", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = worldButtonSize;
        // Screen Space Overlay canvas: setting transform.position directly to a screen
        // coordinate works the same way HandCardView.OnDrag already relies on, sidestepping
        // any anchor/CanvasScaler unit-conversion entirely.
        go.transform.position = new Vector3(screenPos.x, screenPos.y, 0f);
        go.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var textRt = (RectTransform)textGo.transform;
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        // Auto-size down instead of wrapping -- a longer label (AttackLaneRequest's
        // "Lane 1: 8B vs 6") on the same fixed-width box as a short one ("Done")
        // wrapped to two cramped lines and pushed neighboring buttons into overlap
        // instead of just shrinking to fit. Confirmed live this was the main driver
        // of the illegible stacked-text look on a 3-lane choice.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12;
        tmp.fontSizeMax = 22;
        tmp.color = Color.white;

        go.GetComponent<Button>().onClick.AddListener(() => onClick());
        _activeWorldButtons.Add(go);
        return go;
    }

    /// <summary>
    /// The persistent (created-once) context label for whichever request has moved off
    /// its OnGUI panel -- Receive/Set/Dig/Chase resolve purely via the existing
    /// highlight + hand-strip drag (see HandleCardDropped), so this is pure information,
    /// not a clickable control. Positioned to roughly match where the old OnGUI panels
    /// used to sit.
    /// </summary>
    private void EnsurePromptLabel()
    {
        if (_promptLabel != null)
        {
            return;
        }
        GameObject canvasObj = GameObject.Find("Card Canvas");
        if (canvasObj == null)
        {
            return;
        }
        var go = new GameObject("Prompt Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(canvasObj.transform, worldPositionStays: false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(panelOffset.x, -panelOffset.y);
        rt.sizeDelta = new Vector2(standardPanelSize.x, 60f);
        _promptLabel = go.GetComponent<TextMeshProUGUI>();
        _promptLabel.fontSize = 22;
        _promptLabel.color = Color.white;
    }

    private void SetPromptText(string text)
    {
        EnsurePromptLabel();
        if (_promptLabel != null)
        {
            _promptLabel.text = text;
        }
    }

    /// <summary>Shared "pick one card from hand" prompt, reused by every single-card decision.</summary>
    private void DrawCardChoice(List<Card> hand, string prompt, bool allowDecline, Action<Card?> onChosen)
    {
        GUILayout.BeginArea(PanelRect(standardPanelSize), GUI.skin.box);
        GUILayout.Label(prompt);
        foreach (Card card in hand)
        {
            if (GUILayout.Button($"{card}"))
            {
                onChosen(card);
            }
        }
        if (allowDecline && GUILayout.Button("Decline"))
        {
            onChosen(null);
        }
        GUILayout.EndArea();
    }

    /// <summary>
    /// One world button per legal lane, positioned above that lane's own front-row
    /// role -- replaces the old left-side OnGUI list so the court stays visible.
    /// </summary>
    private void ShowAttackLaneButtons(AttackLaneRequest request)
    {
        SetPromptText("Choose an attack lane:");
        if (!_teamPositions.TryGetValue(_teamAName, out var positions))
        {
            return;
        }
        Camera cam = GetActiveGameplayCamera();
        if (cam == null)
        {
            return;
        }
        // Sorted by each lane's own natural screen X, not by AttackCards' dictionary
        // iteration order -- offsetting by an arbitrary key order risks pushing two
        // already-separated buttons toward each other instead of apart whenever
        // iteration order doesn't happen to match left-to-right screen order.
        // Confirmed live: lanes enumerated as [3, 2] while lane 3's own hitter
        // projects further RIGHT on screen than lane 2's, so offsetting lane 3 (index
        // 0) left and lane 2 (index 1) right collapsed them almost on top of each other
        // instead of spreading them apart.
        var entries = new List<(int Lane, Vector2 ScreenPos)>();
        foreach (int lane in request.AttackCards.Keys)
        {
            if (!PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role)
                || !positions.TryGetValue(role, out Transform anchor)
                || !TryWorldToScreenPoint(cam, anchor.position + Vector3.up * ballHeight, out Vector2 screenPos))
            {
                continue;
            }
            entries.Add((lane, screenPos));
        }
        entries.Sort((a, b) => a.ScreenPos.x.CompareTo(b.ScreenPos.x));

        for (int i = 0; i < entries.Count; i++)
        {
            (int lane, Vector2 basePos) = entries[i];
            // Centered around the group's own natural spread, not stacked to one side
            // -- with 2 buttons this puts one left, one right of where they'd
            // otherwise sit on top of each other.
            float xOffset = (i - (entries.Count - 1) / 2f) * laneButtonSpacing;
            Vector2 screenPos = new(basePos.x + xOffset, basePos.y + worldButtonYOffset);
            string cardLabel = request.AttackCards[lane].Value > 0 ? request.AttackCards[lane].ToString() : "??";
            int blockValue = request.BlockLayout.GetValueOrDefault(lane, 0);
            ShowScreenButton(screenPos, $"Lane {lane}: {cardLabel} vs {blockValue}", () => ResolveActiveRequest(lane));
        }
    }

    /// <summary>Tip/Hit, positioned above whichever role AttackLaneRequest just
    /// committed to (_lastChosenAttackLane, captured in ResolveActiveRequest).</summary>
    private void ShowTipOrHitButtons(TipOrHitRequest request)
    {
        SetPromptText($"Attack {request.AttackValue} vs block {request.BlockValue} — tip or hit?");
        if (!_lastChosenAttackLane.HasValue
            || !PlayerRoleExtensions.LaneToRole.TryGetValue(_lastChosenAttackLane.Value, out PlayerRole role)
            || !_teamPositions.TryGetValue(_teamAName, out var positions)
            || !positions.TryGetValue(role, out Transform anchor))
        {
            return;
        }
        Camera cam = GetActiveGameplayCamera();
        if (cam == null || !TryWorldToScreenPoint(cam, anchor.position + Vector3.up * (ballHeight + 1f), out Vector2 basePos))
        {
            return;
        }
        // Same screen-space spread as every other multi-option button group in this
        // file -- stacking both options directly above one anchor (the old approach
        // here) put "Tip" and "Hit" on top of each other, unreadable from most camera
        // angles.
        ShowScreenButton(new Vector2(basePos.x - laneButtonSpacing / 2f, basePos.y + worldButtonYOffset), "Tip", () => ResolveActiveRequest("tip"));
        ShowScreenButton(new Vector2(basePos.x + laneButtonSpacing / 2f, basePos.y + worldButtonYOffset), "Hit", () => ResolveActiveRequest("hit"));
    }

    /// <summary>
    /// The one control HitCards/Block still need beyond drag-and-drop: a way to say
    /// "that's everyone I'm placing" once ready (both allow committing fewer than the
    /// maximum, including zero). Fixed screen position, not tied to any one player.
    /// </summary>
    private void ShowDoneButton(string label, Func<object> getResult) =>
        ShowScreenButton(new Vector2(Screen.width / 2f, 90f), label, () => ResolveActiveRequest(getResult()));

    // -- Scene lookup, narrative playback, ball/camera (unchanged in spirit from the non-interactive version) --

    private Dictionary<PlayerRole, Transform> FindTeamPositions(string groupName)
    {
        var map = new Dictionary<PlayerRole, Transform>();
        GameObject group = GameObject.Find(groupName);
        if (group == null)
        {
            Debug.LogWarning($"GameRunner: no '{groupName}' GameObject found -- ball won't move for that team.");
            return map;
        }
        foreach (Transform child in group.transform)
        {
            if (Enum.TryParse(child.name, ignoreCase: true, out PlayerRole role))
            {
                map[role] = child;
            }
        }
        return map;
    }

    /// <summary>
    /// Returns the Coroutine playing the freshly-flushed batch back (or null if there
    /// was nothing new) -- callers that need to know when this batch's animation has
    /// actually finished (see _pendingReveal in Update()) can yield on it directly.
    /// </summary>
    /// <summary>
    /// holdTrailingFlightLine: for ReceiveRequest and ChaseCardRequest's own gate
    /// cycle only (see Update()) -- each is always posted the instant Core narrates
    /// THIS rally's own "Serve: ..." / "Chase:   ... starting at ..." line (PhaseServe/
    /// PhaseChase narrates it, then immediately calls ChooseReceiveCard/ChooseChaseCard
    /// with no yield in between), so that line is always the very last thing sitting
    /// unflushed by the time this fires. Flushing it here, as part of the SAME backlog
    /// batch this gate awaits before revealing, would mean the whole
    /// Serve+Reception/Chase-start+recovery sequence -- formation settle (Serve only),
    /// toss, the flight itself -- has to fully finish before the human ever sees the
    /// question, starving that flight's own mid-air pause-and-wait-for-an-answer hold
    /// of anything to actually wait for. So: hold that one trailing line back (don't
    /// advance _narrativeReadIndex past it) when set, leaving it for
    /// RevealActiveRequest's own unconditional FlushNarrative() call (which runs
    /// immediately after _activeRequest is finally set to this request) to pick up and
    /// animate -- concurrently with the now-revealed decision UI, exactly like
    /// ServeRequest/SetCardRequest's own early-triggered legs already work. Genuine
    /// prior backlog (the previous rally's own tail) still flushes and awaits normally
    /// either way.
    /// </summary>
    private Coroutine FlushNarrative(bool holdTrailingFlightLine = false)
    {
        if (_narrativeReadIndex >= _narrative.Count)
        {
            return null;
        }
        var newLines = _narrative.Skip(_narrativeReadIndex).ToList();
        _narrativeReadIndex = _narrative.Count;
        if (holdTrailingFlightLine && newLines.Count > 0
            && (ServeRegex.IsMatch(newLines[^1].Trim()) || ChaseStartRegex.IsMatch(newLines[^1].Trim())))
        {
            _narrativeReadIndex--;
            newLines.RemoveAt(newLines.Count - 1);
        }
        if (newLines.Count == 0)
        {
            return null;
        }
        foreach (string line in newLines)
        {
            Debug.Log(line);
        }
        ScanForStateUpdates(newLines); // synchronous, so highlighting can use the result immediately -- ball movement below is animated instead
        return StartCoroutine(PlayLines(newLines));
    }

    /// <summary>
    /// Cheap, synchronous pass over freshly-flushed narrative lines to keep the
    /// dig-role/serve-target tracking fields current *immediately*, since
    /// ApplyHighlightsForRequest needs them the moment a request is taken -- it can't
    /// wait on PlayLines' animated coroutine, which spreads the same lines out over
    /// several frames for ball movement.
    /// </summary>
    private void ScanForStateUpdates(List<string> lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            Match m;
            if ((m = ServeRegex.Match(line)).Success)
            {
                string serverTeam = m.Groups[1].Value;
                if (serverTeam != _teamAName && Enum.TryParse(m.Groups[3].Value, ignoreCase: true, out PlayerRole role))
                {
                    _lastServeTargetRole = role;
                }
            }
            else if ((m = ResolveRegex.Match(line)).Success)
            {
                _lastLane = int.Parse(m.Groups[2].Value);
                _lastAttackCardValue = int.Parse(m.Groups[4].Value);
            }
            else if ((m = SetRegex.Match(line)).Success || (m = AttackRegex.Match(line)).Success)
            {
                _scanAttackingTeam = m.Groups[1].Value;
                if (line.StartsWith("Set:") && _scanAttackingTeam == _teamAName)
                {
                    // Covers the blind-drawn set too (no SetCardRequest to arm this);
                    // a Swing: later in this same flush clears it again.
                    _holdForHumanAttack = true;
                }
            }
            else if ((m = SwingRegex.Match(line)).Success)
            {
                _scanAttackingTeam = m.Groups[1].Value;
                if (_scanAttackingTeam == _teamAName)
                {
                    // The human's lane is final -- the set can finally reach the setter.
                    _holdForHumanAttack = false;
                }
            }
            else if (line.StartsWith("Chase:") && (line.Contains("SUCCEEDED") || line.Contains("FAILED")))
            {
                _holdForChase = false;
            }
            else if ((m = DigRegex.Match(line)).Success && m.Groups[1].Value == _teamAName && _holdForDig)
            {
                _holdForDig = false;
                if (_digPreLaunched && line.Contains("NOT DUG"))
                {
                    // A miss -- the held flight carries straight on through the
                    // digger to the floor along its own arc (no second flight, so no
                    // change of direction).
                    _ballFlight?.RunThroughToFloor(floorLandingHeight);
                }
            }
            else if ((m = DeflectRegex.Match(line)).Success && m.Groups[1].Value == _teamAName && _holdForDig)
            {
                _holdForDig = false;
                if (_digPreLaunched && line.Contains("NOT DUG"))
                {
                    _ballFlight?.RunThroughToFloor(floorLandingHeight);
                }
            }
            else if (line.StartsWith("[Score]"))
            {
                _holdForHumanAttack = false;
                _holdForBlock = false;
                _holdForTip = false;
                _holdForChase = false;
                _holdForDig = false;
                // A miss's own Dig:/Deflect: line (always flushed alongside this one)
                // drops the ball to the floor the same way whether or not it was
                // pre-launched, so this safety reset can't strand anything.
                _digPreLaunched = false;
            }
        }
    }

    private IEnumerator PlayLines(List<string> lines)
    {
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length > 0)
            {
                // A human receiver can answer mid-toss (the ReceiveRequest reveals
                // alongside an AI serve), and every decision after that narrates in a
                // NEW batch running concurrently with the still-unfinished Serve line's
                // own -- confirmed live that the Set/Chase/free-ball lines then played
                // out before the serve was even struck (whole team leaving for Set
                // shape, the server leaving the baseline mid-toss). Nothing that comes
                // after a serve may start until that serve reaches its receiver. The
                // Serve line's own batch never waits on itself: its later lines only
                // run once HandleLine below has already cleared the flag.
                if (_serveInProgress)
                {
                    yield return new WaitUntil(() => !_serveInProgress);
                }
                yield return HandleLine(line);
                // The ball is never left sitting on someone while text plays out --
                // only a finished rally ([Score]) gets a narrative beat.
                if (!_lineHadRealWait && line.StartsWith("[Score]"))
                {
                    yield return new WaitForSeconds(narrativeBeatDelay);
                }
            }
        }
    }

    private IEnumerator HandleLine(string line)
    {
        _lineHadRealWait = false;
        Match m;
        if ((m = ServeRegex.Match(line)).Success)
        {
            string serverTeam = m.Groups[1].Value;
            string receiverTeam = serverTeam == _teamAName ? _teamBName : _teamAName;
            _serveInProgress = true;
            int serveCardValue = int.Parse(m.Groups[2].Value);
            _lastServeCardValue = serveCardValue; // read later by the receiver->setter leg's reception-height lookup
            float serveHeight = serveArcHeightByCardValue.Evaluate(serveCardValue);
            float serveSpeed = serveArcSpeedByCardValue.Evaluate(serveCardValue);
            ClearFloatingNumbers(); // the ball crosses the net on every serve
            SetFloatingLabelOnBall(serveCardValue.ToString(), Color.white);
            CutToPhaseCameraIfIdle("Serve");
            _lineHadRealWait = true;

            if (Enum.TryParse(m.Groups[3].Value, ignoreCase: true, out PlayerRole role))
            {
                _pendingReceiveTeam = receiverTeam;
                _pendingReceiveRole = role;
                _chaseRole = null; // a new rally's own Chase: line (if any) sets this fresh

                // Snap BOTH teams instantly into their pre-serve shape (Serve/Receive, or
                // base if nothing's been authored -- see GetFormationPosition's
                // fallback) before this serve's own toss -- PlayServeToss records
                // "normal position to restore to" off the server's Setter transform the
                // moment it starts, so if anything is still mid-transition from the
                // previous rally (e.g. the last exchange's Receive/Attack shape), that
                // capture would be wrong. An instant snap, not an eased eye-catching
                // move: confirmed live that skipping the receiver's own snap here (on
                // the theory that "nobody moves until contact" should cover them too)
                // left them sitting in whatever formation the PREVIOUS rally ended in
                // -- scattered and wrong -- for the entire serve card decision, since
                // nothing eases them into Receive until after contact. Real volleyball
                // receivers are already standing in their reception stance long before
                // the toss; this reproduces that instantly rather than animating it, so
                // the ONLY visible movement before contact is still nothing at all --
                // the receiving team's own eased eases after AwaitServeToss below (and
                // the Setter's peel-off within it) become a no-op tween from an anchor
                // to itself for everyone except the Setter, who's the one real motion
                // this sequence is actually about.
                SnapTeamToFormation(serverTeam, "Serve");
                SnapTeamToFormation(receiverTeam, "Receive");
                // Sweeps up anything left over from the previous rally regardless of how
                // it ended (e.g. a stuffed block, which ends the rally before Swing ever
                // gets a chance to cull down to one) -- a new rally always starts with a
                // clean slate. Deliberately not in ResetPerRequestState: that fires on
                // every single decision reveal, including the human's own Block reveal
                // mid-rally, which would wipe the AI's committed-lane arcs at exactly the
                // moment they're needed.
                HideAllTrajectoryPreviewsExcept(null);

                // Nobody on the receiving side EASES anywhere until the server actually
                // makes contact -- they're already correctly positioned (the instant
                // snap above), so there's nothing left to visibly animate until then
                // anyway. AwaitServeToss (PlayServeToss/TossTo) is what carries the
                // toss's own pause-at-the-peak for a pending human ServeRequest, so
                // waiting for it here means the one real move left (the Setter's
                // peel-off to Set, below) can't even START until that decision is
                // answered and contact is made.
                yield return AwaitServeToss(serverTeam);

                // The serve launches the instant the toss reaches contact -- the ball
                // must never hang in the air at the contact point. The receiving team
                // was already snapped into Receive shape before the toss (above, or in
                // PrepareServeFormationThenToss for a human serve), so this ease is a
                // no-op for everyone except the Setter's peel-off below, which plays
                // out alongside the serve's own flight rather than ahead of it.
                ApplyTeamFormation(receiverTeam, "Receive", null, receiveFormationLeadDuration);
                if (_teamPositions.TryGetValue(receiverTeam, out var earlyPositions) && earlyPositions.TryGetValue(PlayerRole.Setter, out Transform earlySetterT))
                {
                    // The Setter doesn't linger at its Receive spot -- it's not the one
                    // receiving, so its actual job (setting) starts right after this same
                    // reception. Send it straight on to its Set anchor instead, on the
                    // same wait budget, overriding the bulk move the line above just
                    // started for it specifically (MovePlayerTo preempts cleanly
                    // per-transform). This is "the setter can start to move the instant
                    // contact is made" -- it's already moving by the time this line even
                    // runs, since AwaitServeToss above only just returned.
                    MovePlayerTo(earlySetterT, GetFormationPosition(receiverTeam, PlayerRole.Setter, "Set"), receiveFormationLeadDuration);
                }

                // Pause partway there and hold (slow-motion, not a stop) until the
                // receive card is actually chosen -- see ResolveActiveRequest. Only when
                // the human is the one receiving -- the AI decides instantly and never
                // resolves through that path, so pausing for it would hang forever. This
                // leg's reveal deliberately runs concurrently with this animation (see
                // FlushNarrative's holdTrailingFlightLine doc comment), so
                // _activeRequest is already correctly set to this ReceiveRequest by the
                // time holdWhile checks it here -- no more racing the reveal the way
                // this used to when every request's own triggering line was fully
                // animated BEFORE it could reveal. Same "controlled contact, not a
                // ground bounce" reasoning already applied to the Setter's and hitter's
                // own arrivals for allowBounce; destinationOverride/contactHeight route
                // this through the same real reception point (reaching forward,
                // waist-height) GetSetContactPoint/GetAttackContactPoint already use for
                // their own phases, instead of the generic ballHeight fallback landing
                // dead-center over the receiver's head.
                float servePause = receiverTeam == _teamAName ? serveReceptionPauseFraction : -1f;
                yield return MoveBallToWhenReady(receiverTeam, role, peakHeight: serveHeight, lateralSpeed: serveSpeed,
                    pauseAtFraction: servePause, holdWhile: () => _activeRequest is ReceiveRequest,
                    destinationOverride: GetReceiveContactPoint(receiverTeam, role), contactHeight: receiveContactHeight,
                    allowBounce: false);
            }
            else
            {
                yield return AwaitServeToss(serverTeam);
            }
            _serveInProgress = false;
        }
        else if ((m = ReceiveRegex.Match(line)).Success)
        {
            // The Receive line itself doesn't name a role -- it's whoever the
            // preceding Serve line targeted, captured above. Success/failure isn't in
            // a capture group -- "clean pass" vs "FAILED" is literal text on the same
            // line, same convention the Dig:/Outcome: STUFFED lines already use elsewhere.
            if (_pendingReceiveRole.HasValue)
            {
                bool receiveSuccess = line.Contains("clean pass");
                SetFloatingLabel(_pendingReceiveTeam, _pendingReceiveRole.Value, m.Groups[2].Value,
                    receiveSuccess ? floatingSuccessColor : floatingFailureColor);
            }
            // No narrative beat after this line: the pass rebounds straight off the
            // receiver toward the setter (Set line) or the chaser (Chase line) -- the
            // ball never sits on the receiver's head.
            _lineHadRealWait = true;
        }
        else if (ChaseStartRegex.IsMatch(line))
        {
            CutToPhaseCameraIfIdle("Dig");
            _lineHadRealWait = true;
            if (_pendingReceiveRole.HasValue && _pendingReceiveTeam != null)
            {
                _chaseRole = GetAdjacentChaseRole(_pendingReceiveRole.Value);
                // Genuinely pause mid-flight for the chase decision, same as every
                // other live-decision flight (Serve->Receive, Set->Attack) -- this
                // used to fly the ball to MoveBallToFloorPosition, a DEAD-ball
                // landing (offset away from the chaser, never caught) that also
                // fully completed BEFORE ChaseCardRequest even revealed (this
                // line's own reveal now holds it back -- see FlushNarrative's
                // holdTrailingFlightLine doc comment), instead of pausing
                // concurrently with the decision the way it's meant to read: the
                // ball is still live, still being scrambled for, not already dead
                // on the floor.
                float chasePause = _pendingReceiveTeam == _teamAName ? chaseRecoveryPauseFraction : -1f;
                yield return MoveBallToWhenReady(_pendingReceiveTeam, _chaseRole.Value, peakHeight: chaseLandingPeakHeight,
                    contactHeight: digContactHeight, pauseAtFraction: chasePause,
                    holdWhile: () => _holdForChase, allowBounce: false);
            }
        }
        else if ((m = ChaseAttemptRegex.Match(line)).Success)
        {
            // Each attempt's own card value, colored by whether THIS attempt's running
            // total already reached the target -- no need to wait for the separate
            // "Chase: SUCCEEDED/FAILED" line a couple lines later, it's the exact same
            // comparison Core itself makes.
            if (_pendingReceiveTeam != null && _chaseRole.HasValue)
            {
                bool chaseSuccess = int.Parse(m.Groups[2].Value) >= int.Parse(m.Groups[3].Value);
                SetFloatingLabel(_pendingReceiveTeam, _chaseRole.Value, m.Groups[1].Value,
                    chaseSuccess ? floatingSuccessColor : floatingFailureColor);
            }
        }
        else if ((m = SetRegex.Match(line)).Success)
        {
            string team = m.Groups[1].Value;
            _lastAttackingTeam = team;
            int setValue = int.Parse(m.Groups[2].Value);
            _lastSetCardValue = setValue;
            string setTempoLabel = GetTempoLabel(setValue);
            SetFloatingLabel(team, PlayerRole.Setter, setTempoLabel, GetTempoColor(setTempoLabel));
            // The attack phase itself hasn't visually started yet -- the next thing
            // either team does (the human's own HitCardsRequest reveal, or the AI's
            // first "Attack:" line) wipes this and every other lingering label, exactly
            // once. See _attackPhaseCleared's own comment.
            _attackPhaseCleared = false;

            // Team X setting means team Y is about to defend (block/dig) -- release
            // whichever of team Y's players are still out of position from their own
            // last turn on offense back to a defensive-ready (Dig) formation.
            string opponentTeam = team == _teamAName ? _teamBName : _teamAName;
            ApplyTeamFormation(opponentTeam, "Dig", null, setterReturnDuration);

            // Team X itself (who just received/dug and is now setting) eases into its
            // own Set formation -- covers both the initial post-serve-reception set and
            // any later mid-rally set after a dig, and subsumes the old digger-only
            // reset (that role just gets its Set-phase position like everyone else).
            ApplyTeamFormation(team, "Set", null, setterReturnDuration);

            // This team's own eligible attackers (everyone except Setter/Libero, same
            // exclusion as GridPlayer.CanAttack()) start easing toward this set's own
            // tempo's approach waypoint the instant the set itself is called -- well
            // before the specific attack lane is even chosen -- instead of sitting at
            // their generic Set-phase spot until the real Attack-phase anchor snaps
            // them into their final swing position later (see AttackRegex below).
            // Overrides the Set-phase move just above for these specific roles
            // (MovePlayerTo preempts cleanly per-transform, same early-release pattern
            // as the Setter's own moves elsewhere in this file).
            if (_teamPositions.TryGetValue(team, out var attackPrepPositions))
            {
                foreach (var kv in attackPrepPositions)
                {
                    if (kv.Key == PlayerRole.Setter || kv.Key == PlayerRole.Libero)
                    {
                        continue;
                    }
                    MovePlayerTo(kv.Value, GetFormationPosition(team, kv.Key, "AttackPrep", setTempoLabel), setterReturnDuration);
                }
            }

            // Genuinely await both eases before anything downstream (HitCardsRequest's
            // own gate, in particular) can reveal -- previously fire-and-forget. That
            // was mostly hidden for the AI's own Set leg below, whose real flight yield
            // happens to run about as long as setterReturnDuration, but the human's own
            // Set leg has no flight here at all (that one's already well underway via
            // SetCardRequest's own early-triggered MoveBallToSetterWhenReady -- see
            // RevealActiveRequest), so this line's own processing used to finish
            // instantly, letting HitCardsRequest reveal before either team's tween had
            // time to settle -- exactly "blockers should be ready already" not holding
            // true. Same "even if we need to wait for the characters to move" fix as
            // every other phase transition this pass.
            _lineHadRealWait = true;
            if (team == _teamAName)
            {
                if (!_humanSetLegStarted)
                {
                    // Blind-drawn set: no SetCardRequest ever launched the pass to the
                    // setter -- confirmed live that the ball otherwise sat on the
                    // digger through the whole AttackLane decision and then flew
                    // straight to the hitter, skipping the setter entirely.
                    StartCoroutine(MoveBallToSetterWhenReady(applyFormations: false));
                }
                _humanSetLegStarted = false;
                yield return new WaitForSeconds(setterReturnDuration);
            }
            else
            {
                // Team A's flight to the setter was already kicked off the moment the
                // SetCardRequest went live (see Update()) -- by the time this line
                // exists, the human has already answered. The AI team has no such
                // request, so this narrative line remains its only trigger, and it
                // never pauses (the AI decides instantly). The flight starts right
                // away, alongside the formation eases rather than after them -- the
                // pass rebounds straight off the receiver, never sitting on their head
                // (the Setter itself already peeled off toward its Set spot at serve
                // contact / dig, so it isn't racing this flight). Whatever's left of
                // the ease budget is still waited out afterward, so the settle
                // guarantee above holds for anything downstream.
                //
                // Launched in the background, not awaited: if the human's Block is the
                // next decision (_holdForBlock, armed when it was taken), this pass
                // holds just short of the AI setter's hands for the whole decision
                // rather than landing and sitting there -- and the rest of this batch
                // (the AI's own Attack: lines, whose committed-lane arcs the Block
                // decision needs on screen) has to keep playing meanwhile so the Block
                // can actually reveal. Anything that flies the ball next (Swing) waits
                // for this flight to finish on its own (MoveBallToWhenReady).
                CutToPhaseCameraIfIdle("Set");
                StartCoroutine(MoveBallToWhenReady(team, PlayerRole.Setter,
                    peakHeight: receiveToSetPeakHeight, lateralSpeed: receiveToSetLateralSpeed,
                    pauseAtFraction: setPauseFraction, holdWhile: () => _holdForBlock,
                    destinationOverride: GetSetContactPoint(team), allowBounce: false, contactHeight: setContactHeight));
                if (_holdForBlock)
                {
                    // Only worth waiting out the formation settle when the ball is
                    // genuinely held for the Block -- the human's own blockers should be
                    // in place by the time it reveals. Otherwise the ball would just
                    // land on the setter and sit through this wait.
                    yield return new WaitForSeconds(setterReturnDuration);
                }
            }
        }
        else if ((m = AttackRegex.Match(line)).Success)
        {
            // Blind-drawn attack cards don't match this regex (no "card N" in that
            // line) -- their number only appears later, off the Reveal line below,
            // matching "as they're selected or revealed."
            string team = m.Groups[1].Value;
            _lastAttackingTeam = team;
            int lane = int.Parse(m.Groups[2].Value);
            CutToPhaseCameraIfIdle("Attack"); // first sign this exchange has reached the hitting phase
            if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role))
            {
                // "All other numbers disappear" the instant the attack phase itself
                // begins -- exactly once per exchange (a multi-attacker set narrates
                // several of these Attack: lines in a row, only the first should wipe
                // anything). The human's own side reaches this same one-shot clear via
                // HitCardsRequest's own reveal instead (see ApplyHighlightsForRequest),
                // since their placements happen live via drag, well before any of these
                // narrative lines exist.
                if (!_attackPhaseCleared)
                {
                    ClearFloatingNumbers();
                    _attackPhaseCleared = true;
                }
                // Not the real card value -- an attacker's actual strength is exactly
                // what Block is about to guess at; showing "X" (both teams, for visual
                // consistency) marks a committed hitter without revealing it.
                SetFloatingLabel(team, role, "X", Color.white);

                // The AI's own committed lanes -- shown the instant each one narrates,
                // not hypothetically like the human's own open HitCards options, since
                // these are real cards the AI has actually played. This is exactly the
                // information the human needs to make their own Block decision, and
                // it's safe to reveal now: Block always resolves before Swing narrates
                // (Core/Rally.cs), so nothing here can still change by the time this is
                // seen. The human's own committed lanes get the same treatment via
                // UpdateHitTrajectoryPreviews instead, so this is AI-only.
                if (team != _teamAName)
                {
                    string tempoLabel = GetTempoLabel(_lastSetCardValue);
                    ShowTrajectoryPreview(role,
                        GetSetContactPoint(team) + Vector3.up * setContactHeight,
                        GetAttackContactPoint(team, role, tempoLabel) + Vector3.up * attackContactHeight,
                        trajectorySelectedColor, GetFormationPeakHeight(team, role, "Attack", tempoLabel));
                }
            }
        }
        else if ((m = SwingRegex.Match(line)).Success)
        {
            // Fires the instant the final (post-SlideLanes) attack lane/role is known --
            // well before Resolve: (which only exists once tip-or-hit and block/dig
            // resolution already happened) -- so the ball leaves the setter's head and
            // starts flying to the actual hitter right away, for both teams, instead of
            // sitting static through the whole hitter-choice sequence.
            string team = m.Groups[1].Value;
            CutToPhaseCameraIfIdle("Attack"); // harmless if Attack: already set this -- blind-drawn attacks skip that line entirely
            // Fresh placement offset for this swing -- reused unchanged for both the dig
            // flight's target (DigRegex below) and the digger's own run destination, so
            // the ball and the defender are always aiming at the identical point.
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(UnityEngine.Random.value) * placementVarianceRadius; // sqrt for uniform area density, not just uniform radius
            _pendingAttackOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (Enum.TryParse(m.Groups[3].Value, ignoreCase: true, out PlayerRole role))
            {
                string tempoLabel = GetTempoLabel(_lastSetCardValue);
                Vector3 setterPos = GetSetContactPoint(team);
                Vector3 hitterDestination = GetAttackContactPoint(team, role, tempoLabel);
                float hitterPeakHeight = GetFormationPeakHeight(team, role, "Attack", tempoLabel);

                // Whole team eases into its Attack-phase formation for this set's tempo
                // -- the swinging hitter's own authored anchor IS their approach/
                // step-back position now (author it a bit further back and it does the
                // same job the old single-hitter step-back mechanism did). Timed off
                // how long the ball will actually take to get there (same lead-time
                // treatment as the Setter's own serve-reception run and the digger's
                // run to the ball), not a flat guess, so the hitter lands on the set
                // instead of arriving early or late.
                float hitterDuration = EstimateSetToHitterDuration(team, role, tempoLabel) * hitterMoveLeadFraction;
                ApplyTeamFormation(team, "Attack", tempoLabel, hitterDuration);

                // The Setter's job for this exchange ends the instant the ball leaves
                // for the hitter -- peel off toward defense right away instead of
                // waiting for the whole team's later Dig-formation trigger (which only
                // fires once the opponent sets again, well after this exchange has
                // already resolved). Overrides the Attack-phase move the line above
                // just started for the Setter specifically.
                if (_teamPositions.TryGetValue(team, out var releasedPositions) && releasedPositions.TryGetValue(PlayerRole.Setter, out Transform releasedSetterT))
                {
                    MovePlayerTo(releasedSetterT, GetFormationPosition(team, PlayerRole.Setter, "Dig"), setterReturnDuration);
                }

                _lineHadRealWait = true;
                // The final lane's chosen -- drop every other option this exchange was
                // showing (open hit-card options, the AI's other committed lanes,
                // whatever's left from AttackLaneRequest) down to just this one.
                HideAllTrajectoryPreviewsExcept(role);
                ShowTrajectoryPreview(role, setterPos + Vector3.up * setContactHeight, hitterDestination + Vector3.up * attackContactHeight,
                    trajectorySelectedColor, hitterPeakHeight);
                // Same reasoning as the Setter's own two arrivals (Serve->Setter,
                // Set->Setter) -- the hitter doesn't catch and hold either, they swing
                // on it essentially the instant it arrives, so a landing bounce here
                // reads as wrong/jarring rather than a clean strike.
                //
                // Launched in the background, not awaited: the human's TipOrHit
                // decision (if it comes) is posted only after the Resolve: line that
                // follows this one in the same batch, and can't reveal until the batch
                // finishes -- awaiting here would either deadlock against its own hold
                // or land the ball on the hitter first. Anything that flies the ball
                // next (a dig, a stuff, a kill) waits for this flight on its own.
                StartCoroutine(FlySwing(team, role, hitterDestination, hitterPeakHeight));
            }
        }
        else if ((m = RevealRegex.Match(line)).Success)
        {
            int lane = int.Parse(m.Groups[1].Value);
            if (_lastAttackingTeam != null && PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role))
            {
                SetFloatingNumber(_lastAttackingTeam, role, int.Parse(m.Groups[2].Value));
            }
        }
        else if ((m = BlockQuicksetRegex.Match(line)).Success)
        {
            // Only the forced blind single-blocker case narrates an exact per-card
            // value -- a normal multi-card block only ever reports a lane total (see
            // the Resolve line below), so that case isn't shown here at all, and a
            // normal AI block has no earlier narrative signal to cut to Block on.
            CutToPhaseCameraIfIdle("Block");
            int lane = int.Parse(m.Groups[1].Value);
            // LaneToDefendingRole, not LaneToRole -- this number belongs to the
            // DEFENDING team's own physical blocker for this lane, which (courts being
            // mirrored) is a different role than whichever attacker plays that same
            // lane number on their own side.
            if (_lastAttackingTeam != null && PlayerRoleExtensions.LaneToDefendingRole.TryGetValue(lane, out PlayerRole role))
            {
                string defendingTeam = _lastAttackingTeam == _teamAName ? _teamBName : _teamAName;
                SetFloatingNumber(defendingTeam, role, int.Parse(m.Groups[2].Value));
            }
        }
        else if ((m = FreeBallRegex.Match(line)).Success)
        {
            // A broken-dig recovery sends a mandatory free ball across the net without
            // ever narrating a "Dig:" line -- still a real net crossing, so it gets a
            // real flight of its own, same as a serve reception: from wherever the
            // ball currently sits (the chasing team's own recovery spot, left there by
            // ChaseStartRegex's own MoveBallToWhenReady above) to the actual back-row
            // role Core picked (freeBallTarget.Role -- always Ds or Libero, see
            // Team.EligibleReceivers), not a silent teleport straight to next
            // exchange's setter. No specific card value is tied to the crossing
            // itself; the recovering team's own Set/Attack lines add their numbers
            // next.
            ClearFloatingNumbers();
            SetFloatingLabelOnBall("Free Ball", Color.white);
            // A free ball is scrambled defense, not a serve reception -- Dig
            // formation/camera, not Receive. Confirmed live this was wrong: the whole
            // team (Setter included) was easing into its Receive shape here, which
            // reads completely differently from the mid-rally defensive recovery this
            // actually is.
            CutToPhaseCameraIfIdle("Dig");
            string receivingTeam = m.Groups[1].Value;
            if (Enum.TryParse(m.Groups[2].Value, ignoreCase: true, out PlayerRole receiverRole))
            {
                _lineHadRealWait = true;
                // The crossing starts straight away -- the ball never waits on the
                // chaser's side for the receiving team to settle; their Dig ease plays
                // out alongside the flight instead.
                ApplyTeamFormation(receivingTeam, "Dig", null, receiveFormationLeadDuration);
                // The Setter doesn't linger in Dig shape -- it's not the one digging
                // this free ball, so its actual job (setting it right back up) starts
                // right away, same early-release pattern as the Serve->Receive and
                // successful-Dig legs elsewhere in this file. freeBallTarget.Role can
                // never BE the Setter (Team.EligibleReceivers excludes it), so there's
                // no collision to guard against the way the Dig-catch leg needs to.
                if (_teamPositions.TryGetValue(receivingTeam, out var freeBallPositions)
                    && freeBallPositions.TryGetValue(PlayerRole.Setter, out Transform freeBallSetterT))
                {
                    MovePlayerTo(freeBallSetterT, GetFormationPosition(receivingTeam, PlayerRole.Setter, "Set"), receiveFormationLeadDuration);
                }
                yield return MoveBallToWhenReady(receivingTeam, receiverRole, allowBounce: false);
            }
        }
        else if ((m = DeflectRegex.Match(line)).Success)
        {
            string digTeam = m.Groups[1].Value;
            bool deflectDug = !line.Contains("NOT DUG");
            SetFloatingLabel(digTeam, DeflectDigRole, m.Groups[2].Value, deflectDug ? floatingSuccessColor : floatingFailureColor);
            CutToPhaseCameraIfIdle("Dig");
            _lineHadRealWait = true;
            bool preLaunched = _digPreLaunched && digTeam == _teamAName;
            if (preLaunched)
            {
                _digPreLaunched = false;
            }
            if (preLaunched)
            {
                // Already flying -- released into the catch, or (a miss) sent on
                // through to the floor by ScanForStateUpdates.
                yield return new WaitUntil(() => !_ballFlight.IsInFlight);
            }
            else
            {
                // A miss goes straight on through the Libero to the floor along the
                // same arc (see BallFlight.RunThroughToFloor).
                yield return MoveBallToWhenReady(digTeam, DeflectDigRole, contactHeight: digContactHeight, allowBounce: false,
                    throughToFloor: !deflectDug);
            }
        }
        else if ((m = ResolveRegex.Match(line)).Success)
        {
            // Ball movement to the hitter is now handled by Swing: above, which fires
            // much earlier -- this just keeps the lane/attack-value bookkeeping
            // GetCurrentDefenderRole() (dig/chase highlighting) still needs.
            _lastLane = int.Parse(m.Groups[2].Value);
            _lastAttackCardValue = int.Parse(m.Groups[4].Value);

            // This is also the earliest point the defending role is knowable
            // (GetDigDefenderRole needs the final attack card value, only narrated here)
            // -- start the whole defending team moving into its Dig formation right
            // away instead of leaving them standing still until the ball just arrives.
            // The digger's own per-swing placement variance (_pendingAttackOffset)
            // layers on top of their Dig-phase anchor via extraOffsets; every other
            // defending role just gets its plain Dig anchor.
            if (_lastAttackingTeam != null)
            {
                PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);
                string defendingTeam = _lastAttackingTeam == _teamAName ? _teamBName : _teamAName;
                // Estimate of how long until the ball is actually in the digger's
                // hands: whatever's left of the current (Swing-to-hitter) flight,
                // plus one narrative-only beat for the "Shot:" line that always
                // narrates between here and Dig: -- close enough to tune live, same
                // as every other timing constant introduced this session.
                float remainingFlight = _ballFlight != null ? _ballFlight.RemainingFlightTime : 0f;
                float estimatedDuration = (remainingFlight + narrativeBeatDelay) * digApproachLeadFraction;
                ApplyTeamFormation(defendingTeam, "Dig", null, estimatedDuration,
                    extraOffsets: new Dictionary<PlayerRole, Vector3> { [defenderRole] = _pendingAttackOffset });
                // Not awaited: waiting here used to leave the ball sitting on the
                // hitter's hand. The ease is sized off the swing's remaining flight,
                // and the attack flight that follows (or the human's dig, which now
                // launches at its own reveal and holds partway) gives the defense the
                // rest of the time it needs.
                _lineHadRealWait = true;
            }
        }
        else if ((m = ShotRegex.Match(line)).Success)
        {
            _lastShotWasTip = m.Groups[1].Value.Equals("TIP", StringComparison.OrdinalIgnoreCase);
        }
        else if ((m = DigRegex.Match(line)).Success && _lastLane >= 0)
        {
            // The narrative's "Dig:" line doesn't name a role directly -- Core's
            // GetDigDefenderRole is the same public lookup Rally itself uses, so
            // we recompute it here from the last resolved lane/attack value
            // rather than re-parsing something the engine never printed.
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);
            ClearFloatingNumbers(); // the ball crosses the net into this dig
            // The successful attack's own value transfers onto the ball itself -- it's
            // now what the defense has to beat, same number, new meaning.
            SetFloatingLabelOnBall(_lastAttackCardValue.ToString(), Color.white);
            // "DUG" vs "NOT DUG, no chase" is literal text, not a capture group --
            // same convention as Receive's "clean pass"/"FAILED" a few lines up.
            bool digSuccess = !line.Contains("NOT DUG");
            SetFloatingLabel(m.Groups[1].Value, defenderRole, m.Groups[2].Value,
                digSuccess ? floatingSuccessColor : floatingFailureColor);
            CutToPhaseCameraIfIdle("Dig");
            _lineHadRealWait = true;
            bool preLaunched = _digPreLaunched && m.Groups[1].Value == _teamAName;
            if (preLaunched)
            {
                _digPreLaunched = false;
            }
            if (!digSuccess)
            {
                // The point is already decided -- the kill drives straight on through
                // the digger and into the floor behind them, along the very same arc a
                // successful dig would have taken (BallFlight.RunThroughToFloor), so it
                // never changes direction. Previously this flew a separate flight to a
                // spot beside the digger -- which, for a pre-launched human dig already
                // held mid-air, read as the ball taking a weird turn.
                if (preLaunched)
                {
                    yield return new WaitUntil(() => !_ballFlight.IsInFlight);
                }
                else if (_lastShotWasTip)
                {
                    yield return MoveBallToWhenReady(m.Groups[1].Value, defenderRole, targetOffset: _pendingAttackOffset,
                        contactHeight: digContactHeight, allowBounce: false, throughToFloor: true);
                }
                else
                {
                    yield return MoveBallToWhenReady(m.Groups[1].Value, defenderRole, targetOffset: _pendingAttackOffset,
                        peakHeight: attackPeakHeightByCardValue.Evaluate(_lastAttackCardValue), lateralSpeed: attackLateralSpeed,
                        contactHeight: digContactHeight, allowBounce: false, throughToFloor: true);
                }
            }
            else
            {
                // The digging team's own Setter starts toward its next job (setting
                // this recovered ball) the instant the dig succeeds, without waiting
                // for the later whole-team Set-formation batch move (SetRegex) --
                // same early-release pattern as the Serve->Setter and Swing->Setter
                // legs elsewhere in this file. Skipped when the Setter IS the digger
                // (GetDigDefenderRole can return Setter for lane 1/2 on an odd attack
                // value, confirmed in AttackResolution.cs) -- moving them before the
                // ball's own flight below even starts would fly the ball at their
                // new, already-vacated Set-phase spot instead of the real catch point,
                // since MoveBallTo samples this role's LIVE transform at flight-start.
                if (defenderRole != PlayerRole.Setter
                    && _teamPositions.TryGetValue(m.Groups[1].Value, out var digTeamPositions)
                    && digTeamPositions.TryGetValue(PlayerRole.Setter, out Transform diggingSetterT))
                {
                    MovePlayerTo(diggingSetterT, GetFormationPosition(m.Groups[1].Value, PlayerRole.Setter, "Set"), setterReturnDuration);
                }

                // Tips keep BallFlight's own generic default arc (a disguised soft
                // shot should still loop in, not fly flat like a driven hit); every
                // other shot type gets the card-value-driven flat, fast treatment --
                // see attackPeakHeightByCardValue/attackLateralSpeed's own comments.
                if (preLaunched)
                {
                    // Already on its way (PreLaunchHumanDig) and released by this very
                    // line -- just let it arrive.
                    yield return new WaitUntil(() => !_ballFlight.IsInFlight);
                }
                else if (_lastShotWasTip)
                {
                    yield return MoveBallToWhenReady(m.Groups[1].Value, defenderRole, targetOffset: _pendingAttackOffset,
                        contactHeight: digContactHeight, allowBounce: false);
                }
                else
                {
                    yield return MoveBallToWhenReady(m.Groups[1].Value, defenderRole, targetOffset: _pendingAttackOffset,
                        peakHeight: attackPeakHeightByCardValue.Evaluate(_lastAttackCardValue), lateralSpeed: attackLateralSpeed,
                        contactHeight: digContactHeight, allowBounce: false);
                }
            }
        }
        else if (StuffedRegex.IsMatch(line) && _lastAttackingTeam != null && _lastLane >= 0)
        {
            // No further narrative line exists for a stuffed attack at all (Core ends
            // the rally the instant the block wins) -- previously the ball just sat
            // wherever the Swing: leg left it forever, which is exactly the "play
            // doesn't reset, it just sort of stops" problem. Send it down onto the
            // attacker's own side instead, near the lane it was hit from.
            _lineHadRealWait = true;
            if (PlayerRoleExtensions.LaneToRole.TryGetValue(_lastLane, out PlayerRole attackerRole)
                && _teamPositions.TryGetValue(_lastAttackingTeam, out var attackerPositions)
                && attackerPositions.TryGetValue(attackerRole, out Transform stuffTarget))
            {
                yield return MoveBallToFloorPosition(stuffTarget, stuffedLandingPeakHeight);
            }
        }
    }

    private IEnumerator FlySwing(string team, PlayerRole role, Vector3 hitterDestination, float hitterPeakHeight)
    {
        Func<bool> holdWhile = team == _teamAName ? () => _holdForTip : null;
        yield return MoveBallToWhenReady(team, role, destinationOverride: hitterDestination, peakHeight: hitterPeakHeight,
            contactHeight: attackContactHeight, allowBounce: false, pauseAtFraction: swingPauseFraction, holdWhile: holdWhile);
        HideTrajectoryPreview(role);
    }

    /// <summary>
    /// Launches the opponent's attack toward the human's own digger the moment their
    /// Dig (or the Cover attempt before it) goes live, holding partway across
    /// (digPauseFraction) until the "Dig:" line proves the answer is in -- instead of
    /// the attack sitting on the opposing hitter's hand for the whole decision. A hit
    /// flies at the card-value-driven attack arc, a tip at BallFlight's softer default,
    /// same as DigRegex's own flights. A miss is aborted mid-air by
    /// ScanForStateUpdates and dropped to the floor by DigRegex's own handler.
    /// </summary>
    private IEnumerator PreLaunchHumanDig()
    {
        if (_scanAttackingTeam == _teamAName)
        {
            // The human's own attack deflecting back off the block onto their side --
            // same soft, looping arc as a tip.
            yield return MoveBallToWhenReady(_teamAName, DeflectDigRole, contactHeight: digContactHeight, allowBounce: false,
                pauseAtFraction: digPauseFraction, holdWhile: () => _holdForDig);
            yield break;
        }
        PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);
        ClearFloatingNumbers(); // the ball crosses the net into this dig
        SetFloatingLabelOnBall(_lastAttackCardValue.ToString(), Color.white);
        if (_lastShotWasTip)
        {
            yield return MoveBallToWhenReady(_teamAName, defenderRole, targetOffset: _pendingAttackOffset,
                contactHeight: digContactHeight, allowBounce: false,
                pauseAtFraction: digPauseFraction, holdWhile: () => _holdForDig);
        }
        else
        {
            yield return MoveBallToWhenReady(_teamAName, defenderRole, targetOffset: _pendingAttackOffset,
                peakHeight: attackPeakHeightByCardValue.Evaluate(_lastAttackCardValue), lateralSpeed: attackLateralSpeed,
                contactHeight: digContactHeight, allowBounce: false,
                pauseAtFraction: digPauseFraction, holdWhile: () => _holdForDig);
        }
    }

    /// <summary>
    /// Starts the receiver->setter flight for team A's SetCardRequest, but only once any
    /// flight already in progress (the serve->receiver leg's resumed tail end, which may
    /// still be physically finishing when this request goes live) has genuinely
    /// completed -- FlyTo isn't reentrant-safe, since a second call while one is still
    /// running would overwrite its in-progress state on the same Rigidbody.
    /// </summary>
    private IEnumerator MoveBallToSetterWhenReady(bool applyFormations = true)
    {
        // Not before the ball has genuinely reached the passer -- neither the serve
        // (toss included, see _serveInProgress) nor the incoming flight's own resumed
        // tail end.
        yield return new WaitUntil(() => !_serveInProgress && (_ballFlight == null || !_ballFlight.IsInFlight));
        // SetRegex's own Set/Dig eases only run once "Set:" narrates, i.e. after this
        // decision is already answered -- confirmed live that both teams sat in their
        // previous (Receive/Dig) shapes for the whole "Choose a set card" decision.
        // Start them here instead, the moment the pass leaves the passer's hands;
        // SetRegex's later calls become no-op tweens, and only the tempo-dependent
        // AttackPrep approach (which needs the chosen card) still waits for that line.
        // (Skipped when the Set: line itself launches this leg -- it has already
        // applied both formations plus the tempo-specific AttackPrep moves, which a
        // second Set-formation pass here would undo.)
        if (applyFormations)
        {
            ApplyTeamFormation(_teamAName, "Set", null, setterReturnDuration);
            ApplyTeamFormation(_teamBName, "Dig", null, setterReturnDuration);
        }
        yield return MoveBallToWhenReady(_teamAName, PlayerRole.Setter,
            peakHeight: receiveToSetPeakHeight, lateralSpeed: receiveToSetLateralSpeed,
            pauseAtFraction: setPauseFraction, holdWhile: () => _holdForHumanAttack,
            destinationOverride: GetSetContactPoint(_teamAName), allowBounce: false, contactHeight: setContactHeight);
    }

    /// <summary>
    /// MoveBallTo, but waits for any flight already in progress to genuinely finish
    /// first. Needed anywhere a flight can be triggered from outside the narrative's own
    /// strictly-sequential PlayLines processing -- a new FlushNarrative/PlayLines batch
    /// starts on every new active request, independent of whether an EARLIER batch's own
    /// ball movement (or a directly-triggered one, like the Set leg's
    /// MoveBallToSetterWhenReady) has actually finished yet. FlyTo isn't reentrant-safe:
    /// confirmed live that the Swing: line's trigger (the attack-lane -> hitter leg) can
    /// fire while the human's own Set leg is still resuming from its 50% hold, and
    /// without this wait the two flights fight over the same Rigidbody state.
    ///
    /// holdWhile (with pauseAtFraction) holds the flight at that point for as long as it
    /// returns true -- evaluated live at the pause point (see BallFlight._holdWhile), so
    /// a decision answered before the flight even starts simply never holds, and one
    /// that only goes live mid-flight still does.
    /// </summary>
    private IEnumerator MoveBallToWhenReady(string teamName, PlayerRole role, float peakHeight = -1f,
        float lateralSpeed = -1f, float pauseAtFraction = -1f, Func<bool> holdWhile = null, Vector3? targetOffset = null,
        Vector3? destinationOverride = null, bool allowBounce = true, float? contactHeight = null, bool throughToFloor = false)
    {
        if (_ballFlight != null)
        {
            yield return new WaitUntil(() => !_ballFlight.IsInFlight);
        }
        yield return MoveBallTo(teamName, role, peakHeight, lateralSpeed, pauseAtFraction, targetOffset, destinationOverride,
            allowBounce, contactHeight, holdWhile, throughToFloor);
    }

    private IEnumerator MoveBallTo(string teamName, PlayerRole role, float peakHeight = -1f,
        float lateralSpeed = -1f, float pauseAtFraction = -1f, Vector3? targetOffset = null, Vector3? destinationOverride = null,
        bool allowBounce = true, float? contactHeight = null, Func<bool> holdWhile = null, bool throughToFloor = false)
    {
        if (_ball == null || _ballFlight == null)
        {
            yield break;
        }

        Vector3 basePos;
        if (destinationOverride.HasValue)
        {
            // Targets the role's FINAL formation position directly rather than
            // sampling its live transform -- for a role whose formation-phase ease
            // (ApplyTeamFormation) starts in the very same synchronous instant as this
            // flight (the Set and Swing/Attack call sites), the live transform is still
            // sitting at its PREVIOUS phase's spot when this would otherwise sample it,
            // since nothing yields between them. Confirmed live: without this, the
            // ball flew to and settled at the setter's old Receive-phase position while
            // the setter itself had already eased on to its Set-phase spot several
            // units away -- exactly the "ball doesn't end up at the setter" bug.
            basePos = destinationOverride.Value;
        }
        else if (_teamPositions.TryGetValue(teamName, out var positions) && positions.TryGetValue(role, out var target))
        {
            basePos = target.position;
        }
        else
        {
            yield break;
        }

        // No camera switch here -- fixed cameras only now (no more Follow Camera, see
        // PlayServeToss/HandleLine for the same decision). Whichever camera is already
        // showing (set by ApplyPhaseCameraForRequest or CutToPhaseCameraIfIdle) is
        // expected to already frame wherever this flight travels; that's the whole
        // point of auditing each camera's coverage rather than chasing the ball.
        Vector3 dest = basePos + Vector3.up * (contactHeight ?? ballHeight) + (targetOffset ?? Vector3.zero);
        yield return _ballFlight.FlyTo(dest, peakHeight: peakHeight, lateralSpeed: lateralSpeed, pauseAtFraction: pauseAtFraction,
            allowBounce: allowBounce, holdWhile: holdWhile, slowMoWhile: () => _activeRequest != null,
            throughToFloorY: throughToFloor ? floorLandingHeight : null);
    }

    /// <summary>
    /// Bounces the ball from the chaser to a random OTHER teammate the moment
    /// FreeBallDiscardRequest goes live (see RevealActiveRequest's own case) -- Core
    /// never actually tracks who on the recovering side redirects the ball before it
    /// crosses the net (ChooseFreeBallDiscard is a pure cost, no role attached), so
    /// this invents a target purely for presentation, so the ball visibly moves off
    /// the chaser instead of sitting dead through the discard decision. Plays out at
    /// full speed regardless of how long that decision takes -- confirmed live that
    /// pausing this leg (an earlier version did) read as the free ball stalling right
    /// when it should already be launching. The LATER "mandatory free ball to X"
    /// crossing flight (FreeBallRegex) needs no changes to pick this up -- it always
    /// starts from wherever the ball currently sits.
    /// </summary>
    private IEnumerator PlayFreeBallDiscardBounce()
    {
        // Same serve gate as PlayLines -- this reveal-triggered bounce otherwise could
        // start out of a serve still in the air if the receive/chase were answered mid-toss.
        yield return new WaitUntil(() => !_serveInProgress);
        if (_pendingReceiveTeam == null || !_chaseRole.HasValue
            || !_teamPositions.TryGetValue(_pendingReceiveTeam, out var positions))
        {
            yield break;
        }
        var candidates = positions.Keys.Where(role => role != _chaseRole.Value).ToList();
        if (candidates.Count == 0)
        {
            yield break;
        }
        PlayerRole target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        // Holds late in the bounce for the discard decision -- never landed on the
        // teammate and left sitting there.
        yield return MoveBallToWhenReady(_pendingReceiveTeam, target, peakHeight: freeBallBouncePeakHeight,
            contactHeight: digContactHeight, allowBounce: false,
            pauseAtFraction: freeBallDiscardPauseFraction, holdWhile: () => _activeRequest is FreeBallDiscardRequest);
    }

    /// <summary>
    /// Flies the ball to the floor near (not ON) nearTransform -- for a rally-ending
    /// outcome that lands on the court itself (a kill drilled past the dig, a stuffed
    /// attack falling back on the attacker's own side) rather than arriving at a player
    /// who caught it. Confirmed live that landing exactly at the player's own position
    /// clips straight into their capsule, which also reads as "in reach" rather than the
    /// dead, undiggable ball it's supposed to be -- offset laterally (randomized side,
    /// relative to the team's own facing) instead. Waits for any flight already in
    /// progress first, same reentrancy reasoning as MoveBallToWhenReady.
    /// </summary>
    private IEnumerator MoveBallToFloorPosition(Transform nearTransform, float peakHeight)
    {
        if (_ball == null || _ballFlight == null || nearTransform == null)
        {
            yield break;
        }
        yield return new WaitUntil(() => !_ballFlight.IsInFlight);
        Vector3 lateral = nearTransform.parent != null
            ? nearTransform.parent.TransformDirection(Vector3.right)
            : Vector3.right;
        float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        Vector3 landingXZ = nearTransform.position + lateral * (deadBallLateralOffset * side);
        Vector3 dest = new(landingXZ.x, floorLandingHeight, landingXZ.z);
        yield return _ballFlight.FlyTo(dest, peakHeight: peakHeight);
    }

    /// <summary>
    /// Eases the server's OWN team into Serve shape the instant a human ServeRequest
    /// goes live, then genuinely waits for that settle time to elapse before the toss
    /// (and therefore the serve itself) is allowed to start -- an explicit hold, not a
    /// hope that the tween happens to beat the human's own answer speed. Also snaps
    /// the RECEIVING team (always _teamBName -- this only ever runs for team A's own
    /// serve) instantly into Receive shape right alongside it -- confirmed live this
    /// was missing: this method runs DURING the "choose a card to serve" decision,
    /// well before HandleLine's ServeRegex branch (which only fires once that
    /// decision is already answered and the "Serve:" line narrates) ever gets a
    /// chance to touch the receiver at all, so without this snap here specifically,
    /// the receiving team sat in whatever formation the PREVIOUS rally left them in
    /// for the entire serve-card decision. Instant, not eased, same "already
    /// standing in their reception stance, nothing to visibly animate yet" reasoning
    /// as the AI-serve path. Stashed in _pendingServeToss (same field PlayServeToss
    /// alone used to occupy) so AwaitServeToss's existing await-or-start-fresh logic
    /// needs no changes: whatever this method does before the toss itself is now
    /// just as transparently awaited.
    /// </summary>
    private IEnumerator PrepareServeFormationThenToss()
    {
        ApplyTeamFormation(_teamAName, "Serve", null, receiveFormationLeadDuration);
        // The server itself walks straight to its run-up spot behind the baseline
        // rather than its Serve anchor -- PlayServeToss places it there anyway, so
        // easing anywhere else first would just end in a visible pop.
        if (_teamPositions.TryGetValue(_teamAName, out var servePositions)
            && servePositions.TryGetValue(PlayerRole.Setter, out Transform serverT))
        {
            MovePlayerTo(serverT, GetServeApproach(_teamAName, serverT).RunStart, receiveFormationLeadDuration);
        }
        SnapTeamToFormation(_teamBName, "Receive");
        yield return new WaitForSeconds(receiveFormationLeadDuration);
        yield return PlayServeToss(_teamAName);
    }

    /// <summary>
    /// Waits for this serve's toss, starting it fresh only if nothing started it already
    /// -- for a human server, the switch in RevealActiveRequest already kicked one off
    /// the instant ServeRequest went live (long before the "Serve:" line this is called
    /// from even exists), stashing the coroutine in _pendingServeToss so this just awaits
    /// it instead of stacking a second toss on top. For an AI server there's no request,
    /// hence nothing pending, so this starts one itself -- same as before.
    /// </summary>
    private IEnumerator AwaitServeToss(string serverTeam)
    {
        if (_pendingServeToss != null)
        {
            yield return _pendingServeToss;
            _pendingServeToss = null;
        }
        else
        {
            yield return PlayServeToss(serverTeam);
        }
    }

    /// <summary>
    /// The pre-serve toss: places the server at its run-up spot behind the baseline,
    /// releases the ball from hand height, and tosses it up and forward while the
    /// server accelerates along the ground to meet it -- the toss peaks, drops back
    /// down, and ends exactly at the contact point (serveContactHeight, ON the
    /// baseline) at the same moment the server arrives there. The ball is left at the
    /// contact point so the serve arc that follows (HandleLine's Serve branch)
    /// launches from there immediately. The server stays at the baseline afterward --
    /// a real server finishes their approach at the line, not back where they started.
    /// For a human server, the toss holds at its own peak (see BallFlight.TossTo) until
    /// the ServeRequest itself is answered -- "the decision point is the top of the
    /// toss." An AI server has no such request, so its toss plays straight through.
    /// </summary>
    private IEnumerator PlayServeToss(string serverTeam)
    {
        if (_ball == null
            || _ballFlight == null
            || !_teamPositions.TryGetValue(serverTeam, out var positions)
            || !positions.TryGetValue(PlayerRole.Setter, out Transform setterT))
        {
            yield break;
        }
        if (!_tossingTeams.Add(serverTeam))
        {
            // A toss for this same team is already running -- confirmed live (via a
            // deliberately forced double-invocation) that letting a second one start
            // leaves the setter permanently stuck away from its normal position, since
            // both coroutines race to move/restore the same Transform. Skip entirely
            // rather than stack a second one on top.
            yield break;
        }
        // The previous rally's tail-end flight (e.g. a kill/dig) may still be physically
        // finishing when this rally's Serve line gets processed -- same reentrancy
        // concern as MoveBallToWhenReady, since this repositions the ball directly
        // rather than going through FlyTo at all.
        yield return new WaitUntil(() => !_ballFlight.IsInFlight);

        var (runStart, contactGround) = GetServeApproach(serverTeam, setterT);
        StopPlayerMotion(setterT); // PrepareServeFormationThenToss's walk to runStart may still be on its last frame
        setterT.position = runStart;
        _ball.position = runStart + Vector3.up * serveTossReleaseHeight;
        Vector3 contactPoint = contactGround + Vector3.up * serveContactHeight;
        float peakY = contactPoint.y + serveTossPeakOffset;

        Func<bool> stillPending = serverTeam == _teamAName ? () => _activeRequest is ServeRequest : null;
        var (timeToPeak, duration) = _ballFlight.TossTiming(_ball.position.y, peakY, contactPoint.y);
        Coroutine approach = StartCoroutine(RunServerApproach(setterT, contactGround, duration, timeToPeak, stillPending));
        yield return _ballFlight.TossTo(contactPoint, peakY, stillPending);
        // RunServerApproach is sized to the same duration (and holds at the same
        // moment, against the same stillPending check) as the toss, but its own timer
        // can finish a frame after TossTo returns -- wait so the server is genuinely
        // planted at the baseline before the serve launches.
        yield return approach;
        _tossingTeams.Remove(serverTeam);
    }

    /// <summary>
    /// Where this team's server runs up from and where it makes contact: the contact
    /// point sits ON this team's own baseline (plus serveContactBaselineOffset), at the
    /// Serve-formation Setter anchor's lateral position, and the run-up starts
    /// serveTossForwardDistance further back. Both are ground-level (the server's own
    /// height), not ball height. Uses the Serve anchor rather than the server's live
    /// position so a mid-ease transform can't skew it. Without a court to measure,
    /// falls back to treating the Serve anchor itself as the contact spot.
    /// </summary>
    private (Vector3 RunStart, Vector3 ContactGround) GetServeApproach(string team, Transform serverT)
    {
        Transform teamRoot = serverT.parent;
        Vector3 anchor = GetFormationPosition(team, PlayerRole.Setter, "Serve");
        Vector3 backDir = teamRoot.TransformDirection(Vector3.forward); // local +Z = away from the net
        backDir.y = 0f;
        backDir.Normalize();

        if (court == null)
        {
            court = GameObject.Find("Court")?.GetComponent<Renderer>();
        }
        Vector3 contactGround = anchor;
        if (court != null)
        {
            Bounds b = court.bounds;
            float baselineDist = Mathf.Abs(backDir.x) * b.extents.x + Mathf.Abs(backDir.z) * b.extents.z;
            float anchorDepth = Vector3.Dot(anchor - b.center, backDir);
            contactGround = anchor + backDir * (baselineDist + serveContactBaselineOffset - anchorDepth);
        }
        return (contactGround + backDir * serveTossForwardDistance, contactGround);
    }

    /// <summary>
    /// Runs a transform from its current position to contactPos over duration, eased
    /// in (slow start, fast finish) to read as an accelerating run -- the server's own
    /// ground approach to meet BallFlight.TossTo's matching aerial toss at the same
    /// contact point and moment. Holds at holdAtTime (the toss's own peak) against the
    /// same stillPending check, so the two stay in lockstep without needing to
    /// reference each other's internal state directly.
    /// </summary>
    private IEnumerator RunServerApproach(Transform t, Vector3 contactPos, float duration, float holdAtTime, Func<bool> stillPending)
    {
        if (duration <= 0f)
        {
            t.position = contactPos;
            yield break;
        }
        Vector3 basePos = t.position;
        float elapsed = 0f;
        bool heldAtPeak = false;
        while (elapsed < duration)
        {
            if (!heldAtPeak && elapsed >= holdAtTime && stillPending != null && stillPending())
            {
                heldAtPeak = true;
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
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = progress * progress; // ease-in: slow start, fast finish -- an accelerating run, not a linear glide
            t.position = Vector3.Lerp(basePos, contactPos, eased);
            yield return null;
        }
        t.position = contactPos;
    }

    /// <summary>
    /// Physics-free estimate of how long the Set->Hitter flight about to start will
    /// take, used to size the attacking team's Attack-phase move (see the SwingRegex
    /// branch), computed from the Setter's Set-phase anchor to the hitter's Attack-phase
    /// anchor rather than any live (possibly mid-ease) transform, and using BallFlight's
    /// own default lateral speed since the Swing flight doesn't override it.
    /// </summary>
    private float EstimateSetToHitterDuration(string team, PlayerRole role, string tempo)
    {
        if (_ballFlight == null)
        {
            return 0f;
        }
        Vector3 setterPos = GetSetContactPoint(team);
        Vector3 hitterPos = GetAttackContactPoint(team, role, tempo);
        float lateralDist = Vector3.Distance(
            new Vector3(setterPos.x, 0f, setterPos.z),
            new Vector3(hitterPos.x, 0f, hitterPos.z));
        return lateralDist / _ballFlight.DefaultLateralSpeed;
    }

    /// <summary>
    /// Lazily creates (never destroys) the pooled LineRenderer for this role -- at most
    /// one arc is ever relevant per role at a time, so the pool never needs more than
    /// one entry per role.
    /// </summary>
    private LineRenderer GetOrCreateTrajectoryLine(PlayerRole role)
    {
        if (_trajectoryLines.TryGetValue(role, out LineRenderer existing))
        {
            return existing;
        }
        var line = new GameObject($"Trajectory Preview ({role})").AddComponent<LineRenderer>();
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startWidth = trajectoryLineWidth;
        line.endWidth = trajectoryLineWidth;
        line.useWorldSpace = true;
        line.enabled = false;
        _trajectoryLines[role] = line;
        return line;
    }

    /// <summary>
    /// Draws (or updates) this role's pooled arc via the same analytic solve FlyTo
    /// itself uses (fts.ComputeArcPreviewPoints) and this role's own authored peak
    /// height (GetFormationPeakHeight), so the preview always matches what a real
    /// flight to this spot would actually fly.
    /// </summary>
    private void ShowTrajectoryPreview(PlayerRole role, Vector3 start, Vector3 end, Color color, float peakHeight)
    {
        if (_ballFlight == null)
        {
            return;
        }
        LineRenderer line = GetOrCreateTrajectoryLine(role);
        Vector3[] points = fts.ComputeArcPreviewPoints(start, end, peakHeight, _ballFlight.DefaultLateralSpeed, trajectoryLineSegments);
        line.startColor = color;
        line.endColor = color;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.enabled = true;
    }

    private void HideTrajectoryPreview(PlayerRole role)
    {
        if (_trajectoryLines.TryGetValue(role, out LineRenderer line))
        {
            line.enabled = false;
        }
    }

    /// <summary>
    /// Hides every pooled arc except (optionally) one -- pass null to clear everything
    /// (a new rally starting; see the ServeRegex branch), or a role to narrow down to
    /// just that one (the final swinging lane; see the SwingRegex branch).
    /// </summary>
    private void HideAllTrajectoryPreviewsExcept(PlayerRole? keep = null)
    {
        foreach (var kv in _trajectoryLines)
        {
            kv.Value.enabled = keep.HasValue && kv.Key.Equals(keep.Value);
        }
    }

    /// <summary>
    /// Stops whichever motion is currently in flight for this transform, if any, without
    /// starting a new one -- used when something else (a snap, an instant reset) is about
    /// to override the transform directly and needs the in-flight motion to not resume on
    /// its own next frame and clobber that override.
    /// </summary>
    private void StopPlayerMotion(Transform player)
    {
        if (_playerMotionCoroutines.TryGetValue(player, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
            _playerMotionCoroutines.Remove(player);
        }
    }

    /// <summary>
    /// Eases the given player transform to destination over duration, stopping whichever
    /// motion is already in flight for that same transform first. Every player-movement
    /// call site (setter run-out/run-back, hitter step-back, digger approach) shares this
    /// one mechanism so the most recently requested motion always wins cleanly, rather
    /// than a still-finishing motion silently blocking the next one (confirmed live) or
    /// vice versa.
    /// </summary>
    private void MovePlayerTo(Transform player, Vector3 destination, float duration)
    {
        StopPlayerMotion(player);
        _playerMotionCoroutines[player] = StartCoroutine(EasePlayerPosition(player, destination, duration));
    }

    private IEnumerator EasePlayerPosition(Transform player, Vector3 destination, float duration)
    {
        Vector3 start = player.position;
        if (duration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                player.position = Vector3.Lerp(start, destination, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
        }
        player.position = destination;
    }

    /// <summary>
    /// Maps a set card's face value to the same tempo tiers SetTemplate.Universal uses
    /// (Core/SetTemplate.cs) -- 1-3 quickset, 4-7 mid, 8-10 high -- purely for labeling
    /// which Formations/Attack/&lt;tempo&gt; anchor set applies; the actual lane-legality
    /// rules still live in Core and aren't duplicated here.
    /// </summary>
    private static string GetTempoLabel(int setCardValue) => setCardValue switch
    {
        <= 3 => "Quick",
        <= 7 => "Mid",
        _ => "High",
    };

    private Color GetTempoColor(string tempoLabel) => tempoLabel switch
    {
        "Quick" => quickTempoColor,
        "Mid" => midTempoColor,
        _ => highTempoColor,
    };

    /// <summary>
    /// Resolves where a role should stand for a given phase (and, for Attack, tempo):
    /// the position of Formations/{phase}[/{tempo}]/{role} under that team's root if
    /// it's been authored (see the Formation Setup editor tool), else that role's own
    /// snapshotted base position -- never a raw parent/root position (see _basePositions'
    /// comment for why that's specifically wrong).
    /// </summary>
    private Vector3 GetFormationPosition(string team, PlayerRole role, string phase, string tempo = null)
    {
        Vector3 fallback = _basePositions.TryGetValue(team, out var baseByRole) && baseByRole.TryGetValue(role, out Vector3 basePos)
            ? basePos
            : Vector3.zero;
        if (!_teamRoots.TryGetValue(team, out Transform root))
        {
            return fallback;
        }
        string path = tempo != null ? $"Formations/{phase}/{tempo}/{role}" : $"Formations/{phase}/{role}";
        Transform anchor = root.Find(path);
        return anchor != null ? anchor.position : fallback;
    }

    /// <summary>
    /// Resolves the arc peak height a flight to this role/phase/tempo should use --
    /// the anchor's own FormationAnchorHeight override if one's been authored (see the
    /// Formation Setup editor tool), else BallFlight's single global default. Same
    /// lookup shape as GetFormationPosition, kept separate since not every anchor has
    /// (or needs) a height override.
    /// </summary>
    private float GetFormationPeakHeight(string team, PlayerRole role, string phase, string tempo = null)
    {
        float fallback = _ballFlight != null ? _ballFlight.DefaultPeakHeight : 2.5f;
        if (!_teamRoots.TryGetValue(team, out Transform root))
        {
            return fallback;
        }
        string path = tempo != null ? $"Formations/{phase}/{tempo}/{role}" : $"Formations/{phase}/{role}";
        Transform anchor = root.Find(path);
        if (anchor != null && anchor.TryGetComponent(out FormationAnchorHeight heightOverride) && heightOverride.peakHeight >= 0f)
        {
            return heightOverride.peakHeight;
        }
        return fallback;
    }

    /// <summary>
    /// The ground-level XZ point a hitter's swing actually contacts the ball at --
    /// attackContactBackOffset units behind their own Attack-phase anchor (away from the
    /// net), since a real swing's contact point sits behind the attacker's own approach/
    /// landing spot, not exactly on top of it. Ground-level only (no height baked in,
    /// same convention as GetFormationPosition) -- height is added separately wherever
    /// this is consumed (MoveBallTo's contactHeight for the real flight, or explicitly
    /// for a trajectory preview), so the two can never silently drift apart.
    /// </summary>
    private Vector3 GetAttackContactPoint(string team, PlayerRole role, string tempo)
    {
        Vector3 anchorPos = GetFormationPosition(team, role, "Attack", tempo);
        return _teamRoots.TryGetValue(team, out Transform root)
            ? anchorPos + root.TransformDirection(new Vector3(0f, 0f, attackContactBackOffset))
            : anchorPos;
    }

    /// <summary>
    /// The ground-level XZ point the set is actually released at -- setContactBackOffset
    /// units behind the Setter's own Set-phase anchor (away from the net), same
    /// reasoning and ground-level-only convention as GetAttackContactPoint.
    /// </summary>
    private Vector3 GetSetContactPoint(string team)
    {
        Vector3 anchorPos = GetFormationPosition(team, PlayerRole.Setter, "Set");
        return _teamRoots.TryGetValue(team, out Transform root)
            ? anchorPos + root.TransformDirection(new Vector3(0f, 0f, setContactBackOffset))
            : anchorPos;
    }

    /// <summary>
    /// The ground-level XZ point a served ball is actually received at --
    /// receiveContactForwardOffset units IN FRONT of the receiver's own Receive-phase
    /// anchor (toward the net), since a real forearm pass reaches forward to play the
    /// ball before it's on top of them, rather than being caught directly overhead the
    /// way GetSetContactPoint/GetAttackContactPoint's own backward offset works for a
    /// release point. Ground-level only, same convention as those two.
    /// </summary>
    private Vector3 GetReceiveContactPoint(string team, PlayerRole role)
    {
        Vector3 anchorPos = GetFormationPosition(team, role, "Receive");
        return _teamRoots.TryGetValue(team, out Transform root)
            ? anchorPos + root.TransformDirection(new Vector3(0f, 0f, -receiveContactForwardOffset))
            : anchorPos;
    }

    /// <summary>
    /// Eases every role on the given team toward its formation position for this phase
    /// (see GetFormationPosition) in one call -- the single mechanism every phase
    /// transition uses to reshape a whole team at once, rather than the old
    /// one-role-at-a-time movement (MoveSetterToFrontRow/Home, StepHitterBack) it
    /// replaces. extraOffsets, when given, adds a per-role nudge on top of that role's
    /// formation position -- used for the digger's own per-swing placement variance
    /// (_pendingAttackOffset) on top of its Dig-phase anchor.
    /// </summary>
    private void ApplyTeamFormation(string team, string phase, string tempo, float duration, Dictionary<PlayerRole, Vector3> extraOffsets = null)
    {
        if (!_teamPositions.TryGetValue(team, out var positions))
        {
            return;
        }
        foreach (var kv in positions)
        {
            PlayerRole role = kv.Key;
            Transform roleT = kv.Value;
            Vector3 dest = GetFormationPosition(team, role, phase, tempo);
            if (extraOffsets != null && extraOffsets.TryGetValue(role, out Vector3 offset))
            {
                dest += offset;
            }
            MovePlayerTo(roleT, dest, duration);
        }
    }

    /// <summary>
    /// Snaps every role on the given team instantly (no ease) to its formation position
    /// for the given phase (falls back to that role's own snapshotted base position if
    /// nothing's authored for this phase -- see GetFormationPosition -- so an unauthored
    /// phase behaves exactly like the old base-only snap did). Genuinely instant, not
    /// just a zero-duration ease: this fires right before PlayServeToss records "normal
    /// position to restore to" off the server's Setter transform, so it needs to have
    /// already landed by the time that capture happens, not merely be a same-frame
    /// coroutine.
    /// </summary>
    private void SnapTeamToFormation(string team, string phase)
    {
        if (!_teamPositions.TryGetValue(team, out var positions))
        {
            return;
        }
        foreach (var kv in positions)
        {
            StopPlayerMotion(kv.Value);
            kv.Value.position = GetFormationPosition(team, kv.Key, phase);
        }
    }

    /// <summary>
    /// Switches to the camera for the given decision: "Player {Phase} Camera" if one
    /// exists in the scene (see PhaseCameraNames), defaultCamera otherwise. Runs
    /// synchronously the instant a request goes active (see Update()) -- before the
    /// PlayLines coroutine for that same narrative batch has taken its first step -- so
    /// this always wins over CutToPhaseCameraIfIdle for the same moment: a pending human
    /// decision is a stronger signal of what the camera should show than whatever
    /// narrative line happens to be catching up in the background.
    /// </summary>
    private void ApplyPhaseCameraForRequest(object request)
    {
        Camera cam = defaultCamera;
        if (request != null && PhaseCameraNames.TryGetValue(request.GetType(), out string phaseName))
        {
            cam = FindPhaseCamera(phaseName);
        }
        SetActiveCamera(cam);
    }

    /// <summary>
    /// Cuts to the named phase camera in response to a narrative event, but only when
    /// no human decision is currently pending -- when one is, ApplyPhaseCameraForRequest
    /// already claimed the camera for it (and always runs first for the same moment, see
    /// its own comment), so overriding here would just fight that for no reason. This is
    /// what gives the AI's own turn real camera cuts: no request is ever pending during
    /// an AI decision, so every call here freely takes effect, following the action the
    /// same way the human's own turn already does via the request-driven path.
    /// </summary>
    private void CutToPhaseCameraIfIdle(string phaseName)
    {
        if (_activeRequest == null)
        {
            SetActiveCamera(FindPhaseCamera(phaseName));
        }
    }

    private Camera FindPhaseCamera(string phaseName)
    {
        GameObject found = GameObject.Find($"Player {phaseName} Camera");
        return found != null && found.TryGetComponent(out Camera cam) ? cam : defaultCamera;
    }

    /// <summary>
    /// Enables exactly one camera (and its AudioListener) and disables every other
    /// camera in the scene -- deliberately not limited to a fixed set of known cameras,
    /// since the number of phase cameras grows as more get added.
    /// </summary>
    private void SetActiveCamera(Camera active)
    {
        foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SetCameraActive(cam, cam == active);
        }
    }

    private static void SetCameraActive(Camera cam, bool isActive)
    {
        if (cam == null)
        {
            return;
        }
        cam.enabled = isActive;
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = isActive;
        }
    }
}
