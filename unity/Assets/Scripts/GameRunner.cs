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
    // Every hold fraction below is how far into that leg the ball gets before holding
    // -- and it only holds while a human decision is pending before the next touch
    // (see ShouldHoldFlight), so the AI's own touches never pause.
    // serve -> receiver, while the human chooses their receive card:
    [SerializeField] private float serveReceptionPauseFraction = 0.55f;
    // pass -> setter, through the human's whole set/hit/lane chain (and the AI's pass
    // through the human's block). Late in the arc on purpose -- the pass's rebound off the receiver always plays
    // out in full (it'll carry the reception animation), and the ball holds in flight
    // just short of the setter's hands, never on anyone's head.
    [SerializeField] private float setPauseFraction = 0.9f;
    // failed reception -> chaser, through every chase attempt:
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
    // flight, a real, live, still-being-scrambled-for catch (see
    // PresentChaseStarted), just a short/flat one, same shape reasoning as the two heights above.
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
    // Fraction applied to the digger's estimated remaining travel time (see
    // PresentResolve) -- less than 1 so the digger settles in just ahead of
    // the ball instead of at the same instant.
    [SerializeField] private float digApproachLeadFraction = 0.85f;
    // Set once per Swing (see PresentSwing) and reused for both the dig flight's
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
    // deliberately NOT run through this curve (see FlyAttack) -- a disguised
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
    // both PrepareServeFormationThenToss (the server's own team) and PresentServe's own
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

    // An exact-tie hit deflects off the block back onto the ATTACKER's own side, and
    // that team digs it (Rally.ResolveOwnSideDeflect). Core names no digging role --
    // presentation always sends it to that team's Libero.
    private const PlayerRole DeflectDigRole = PlayerRole.Libero;

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
    // Set by the ShotEvent, read by the attack flight (FlyAttack) -- tips keep the original,
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
    // Determined when the ChaseStartedEvent is presented (see PresentChaseStarted),
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
    // are chosen -- see the switch in RevealActiveRequest), so PresentServe can just
    // await this already-started toss instead of kicking off a second one. Cleared once
    // PresentServe consumes it. Stays null for an AI-served rally (no request, no early
    // trigger), so PresentServe's fallback
    // -- starting the toss itself -- is unchanged for that path.
    private Coroutine _pendingServeToss;
    private HumanDecisionChannel _channel;
    private List<string> _narrative;
    // Typed, ordered record of the whole game -- Rally's own events plus the human's
    // decision prompts (DecisionRecordingStrategy) and each rally's score, all in the
    // order Core actually ran them. Written on the game's background thread; walked in
    // order by RunPresentation. The narrative text is only logged to the console now.
    private RallyEventLog _events;
    private Task<string> _gameTask;
    private bool _resultLogged;

    // Written only by the background game loop, read every frame by OnGUI for the
    // scoreboard. Plain ints are atomic to read/write in .NET, and a display that's
    // one frame stale is harmless, so no lock is needed just for this.
    private volatile int _scoreA;
    private volatile int _scoreB;

    // Currently-displayed decision (null = none) and its in-progress sub-state.
    private object _activeRequest;
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
        StartCoroutine(RunPresentation());
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
        LogNewNarrative();

        if (_gameTask != null && _gameTask.IsCompleted && !_resultLogged)
        {
            _resultLogged = true;
            LogNewNarrative();
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
    /// _activeRequest, applies highlights, cuts the phase camera, populates the hand
    /// strip, sets prompt text / shows buttons, and (for BlockCards with nothing in
    /// flight) starts the slow-motion hold. Called by PresentDecision when the
    /// presentation loop reaches this decision in the event stream -- everything that
    /// happened before it has already been presented.
    /// </summary>
    private void RevealActiveRequest(object req)
    {
        _activeRequest = req;
        ResetPerRequestState();
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
            case HitCardsRequest:
                SetPromptText("Drag cards onto your attackers:");
                ShowDoneButton("Done", () => (object)(_hitPlacements ?? new List<(int, Card, AttackPosition)>()));
                break;
            case BlockCardsRequest:
                // Blind commit, ahead of the attacker's final lane. Normally the AI's
                // own pass is still held in flight toward its setter (see
                // ShouldHoldFlight), and that held flight drives the slow-motion itself;
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

        // ServeRequest only ever exists for a human server (team A), and comes BEFORE
        // the ServeEvent (which only exists once this is answered) -- waiting for that
        // event meant the receiving team sat frozen in last rally's positions for
        // however long the human took to choose their serve -- exactly the "nobody has
        // moved" complaint this fixes. Neither the toss nor the receiving team's formation needs to know the
        // eventual card/target: the toss is just a vertical hop-and-catch at the
        // server's own spot, and Receive formation is a fixed team shape, not aimed at
        // whichever role ends up targeted.
        if (req is ServeRequest)
        {
            // Same early-trigger reasoning as the formation/toss lines above -- the
            // previous rally's floating labels (a dig's transferred number, lingering
            // "X" attackers, etc.) would otherwise sit on screen through this entire
            // decision, since PresentServe's own ClearFloatingNumbers doesn't run until
            // the ServeEvent exists, which requires this very choice to already be
            // answered. Confirmed live: without this, "Choose a card to serve" showed
            // straight through last rally's leftover numbers.
            ClearFloatingNumbers();
            // The RECEIVING team already eased into shape here -- but the SERVING team
            // itself only ever got an instant SnapTeamToFormation, and only once
            // the ServeEvent arrives, which (per the reasoning above) is well after this
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
        // Live, the instant the card lands -- not the real value (see
        // PresentAttackCommit's own "X" for why), and not waiting on Core's own
        // AttackCommitEvents, which only exist once every placement in this
        // HitCardsRequest is submitted together.
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
                // _chaseRole (set off the ChaseStartedEvent -- see PresentChaseStarted) is the
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
                // numbers disappear" wipe PresentAttackCommit does for the AI's side, just
                // triggered here instead since these placements happen live via drag,
                // well before any AttackCommitEvent exists for them.
                if (!_attackPhaseCleared)
                {
                    ClearFloatingNumbers();
                    _attackPhaseCleared = true;
                }
                HighlightHitOptions(hitReq);
                UpdateHitTrajectoryPreviews(hitReq);
                break;
            case AttackLaneRequest laneReq:
                // Same one-shot "all other numbers disappear" wipe PresentAttackCommit/
                // HitCardsRequest's own reveal already do -- needed as a fallback here
                // too for a fully forced-blind exchange (every lane blind-drawn, no
                // hand cards left on either side): neither of those two triggers ever
                // fires then (no HitCardsRequest goes out, and blind-drawn AttackCommits
                // commits carry no card to label -- see PresentAttackCommit), so
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
    // rally events that already drive ball movement, see PresentEvent. Cleared
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

    // -- Event presentation --------------------------------------------------------
    //
    // Presentation is driven by Core's typed event stream (_events), walked strictly
    // in order by ONE loop (RunPresentation): each event's handler runs to completion
    // before the next starts, and a DecisionRequestedEvent reveals the human's prompt
    // and waits for the answer right there in sequence. Core always runs ahead of this
    // loop up to the human's next decision, so the whole stretch of the rally up to
    // that decision is already sitting in the log -- which is what lets every flight
    // decide whether to hold by looking ahead (see ShouldHoldFlight) instead of via
    // per-decision flags.
    //
    // Ball flights never block the loop (they'd deadlock against their own hold);
    // they're queued (QueueFlight) so each starts only once the previous one has
    // finished, and handlers that need the ball to have actually arrived wait on the
    // specific flight's handle.

    /// <summary>One queued ball flight's progress.</summary>
    private sealed class FlightHandle
    {
        public bool Started;
        public bool Done;
    }

    // Index (in _events) of the event currently being presented -- the loop only
    // advances past an event once its handler has finished.
    private int _presentIndex;
    private FlightHandle _lastQueuedFlight;
    // The receive/dig/free-ball -> setter pass currently in progress (SetEvent waits
    // for it to have started before moving the setting team, see PresentSet).
    private FlightHandle _setterPass;
    // The flight carrying the attack into the defense (kill -> digger, deflection ->
    // the attacker's own Libero); DigEvent/DeflectDigEvent finish it off.
    private FlightHandle _attackFlight;
    // The chase recovery flight -- ChaseEndedEvent either lets it land or runs it on
    // through to the floor.
    private FlightHandle _chaseFlight;
    private int _narrativeLogIndex;

    private static bool IsBallEvent(RallyEvent e) =>
        e is ServeEvent or ReceiveEvent or ChaseStartedEvent or ChaseEndedEvent or FreeBallDiscardEvent
            or FreeBallEvent or SwingEvent or AttackOutcomeEvent or DigEvent or DeflectDigEvent or RallyEndedEvent;

    /// <summary>
    /// The one hold rule: a flight launched while presenting event launchIndex holds
    /// at its pause point when a human decision sits between that event and the next
    /// ball event -- until the loop actually reaches that next ball event (i.e. the
    /// whole decision chain is answered and presented). No decision ahead (an AI-only
    /// stretch) means no hold at all. The next ball event may not exist yet (Core is
    /// blocked on the very decision in question), which counts as not reached.
    /// </summary>
    private bool ShouldHoldFlight(int launchIndex)
    {
        bool decisionAhead = false;
        for (int i = launchIndex + 1; _events.TryGet(i, out RallyEvent e); i++)
        {
            if (e is DecisionRequestedEvent)
            {
                decisionAhead = true;
            }
            else if (IsBallEvent(e))
            {
                return decisionAhead && _presentIndex < i;
            }
        }
        return decisionAhead;
    }

    /// <summary>Whether a human decision comes before the next ball event after index.</summary>
    private bool HumanDecisionBeforeNextBallEvent(int index)
    {
        for (int i = index + 1; _events.TryGet(i, out RallyEvent e); i++)
        {
            if (e is DecisionRequestedEvent)
            {
                return true;
            }
            if (IsBallEvent(e))
            {
                return false;
            }
        }
        return false;
    }

    private Func<bool> HoldForDecisionsAfter(int launchIndex) => () => ShouldHoldFlight(launchIndex);

    private FlightHandle QueueFlight(Func<IEnumerator> flight)
    {
        var handle = new FlightHandle();
        FlightHandle previous = _lastQueuedFlight;
        _lastQueuedFlight = handle;
        StartCoroutine(RunQueuedFlight(handle, previous, flight));
        return handle;
    }

    private IEnumerator RunQueuedFlight(FlightHandle handle, FlightHandle previous, Func<IEnumerator> flight)
    {
        if (previous != null)
        {
            yield return new WaitUntil(() => previous.Done);
        }
        handle.Started = true;
        yield return flight();
        handle.Done = true;
    }

    private IEnumerator WaitForQueuedFlights()
    {
        FlightHandle last = _lastQueuedFlight;
        if (last != null)
        {
            yield return new WaitUntil(() => last.Done);
        }
    }

    /// <summary>
    /// A miss: the given flight carries on along its own arc through the player it was
    /// heading for, into the floor (BallFlight.RunThroughToFloor) -- or, if it already
    /// landed on them, drops to the floor beside them.
    /// </summary>
    private IEnumerator RunFlightThroughToFloor(FlightHandle flight, string team, PlayerRole role)
    {
        if (flight == null)
        {
            yield break;
        }
        yield return new WaitUntil(() => flight.Started);
        if (!flight.Done)
        {
            _ballFlight?.RunThroughToFloor(floorLandingHeight);
            yield return new WaitUntil(() => flight.Done);
        }
        else if (_teamPositions.TryGetValue(team, out var positions) && positions.TryGetValue(role, out Transform target))
        {
            yield return QueueFlightAndWait(() => MoveBallToFloorPosition(target, stuffedLandingPeakHeight));
        }
    }

    private IEnumerator QueueFlightAndWait(Func<IEnumerator> flight)
    {
        FlightHandle handle = QueueFlight(flight);
        yield return new WaitUntil(() => handle.Done);
    }

    private string Opponent(string team) => team == _teamAName ? _teamBName : _teamAName;

    private void LogNewNarrative()
    {
        if (_narrative == null)
        {
            return;
        }
        List<string> newLines;
        lock (_narrative)
        {
            if (_narrativeLogIndex >= _narrative.Count)
            {
                return;
            }
            newLines = _narrative.GetRange(_narrativeLogIndex, _narrative.Count - _narrativeLogIndex);
            _narrativeLogIndex = _narrative.Count;
        }
        foreach (string line in newLines)
        {
            Debug.Log(line);
        }
    }

    private IEnumerator RunPresentation()
    {
        while (true)
        {
            if (!_events.TryGet(_presentIndex, out RallyEvent e))
            {
                if (_gameTask != null && _gameTask.IsCompleted)
                {
                    yield break;
                }
                yield return null;
                continue;
            }
            yield return PresentEvent(e);
            _presentIndex++;
        }
    }

    private IEnumerator PresentEvent(RallyEvent rallyEvent)
    {
        switch (rallyEvent)
        {
            case DecisionRequestedEvent decision:
                yield return PresentDecision(decision);
                break;
            case ServeEvent serve:
                yield return PresentServe(serve);
                break;
            case ReceiveEvent receive:
                PresentReceive(receive);
                break;
            case ChaseStartedEvent chaseStarted:
                PresentChaseStarted(chaseStarted);
                break;
            case ChaseAttemptEvent attempt:
                if (_chaseRole.HasValue)
                {
                    SetFloatingLabel(attempt.Team, _chaseRole.Value, attempt.Card.Value.ToString(),
                        attempt.RunningTotal >= attempt.TargetValue ? floatingSuccessColor : floatingFailureColor);
                }
                break;
            case ChaseEndedEvent chaseEnded:
                yield return PresentChaseEnded(chaseEnded);
                break;
            case FreeBallEvent freeBall:
                yield return PresentFreeBall(freeBall);
                break;
            case SetEvent set:
                yield return PresentSet(set);
                break;
            case AttackCommitEvent commit:
                PresentAttackCommit(commit);
                break;
            case BlockCommitEvent block:
                PresentBlockCommit(block);
                break;
            case SwingEvent swing:
                PresentSwing(swing);
                break;
            case RevealEvent reveal:
                if (PlayerRoleExtensions.LaneToRole.TryGetValue(reveal.Lane, out PlayerRole revealRole))
                {
                    SetFloatingNumber(reveal.Team, revealRole, reveal.Card.Value);
                }
                break;
            case ResolveEvent resolve:
                PresentResolve(resolve);
                break;
            case ShotEvent shot:
                _lastShotWasTip = shot.Shot == ShotKind.Tip;
                break;
            case AttackOutcomeEvent outcome:
                yield return PresentAttackOutcome(outcome);
                break;
            case DigEvent dig:
                yield return PresentDig(dig);
                break;
            case DeflectDigEvent deflect:
                yield return PresentDeflectDig(deflect);
                break;
            case RallyEndedEvent:
                // The last ball movement (a kill, a stuff, an ace) plays out in full,
                // then one beat before the next rally starts.
                yield return WaitForQueuedFlights();
                yield return new WaitForSeconds(narrativeBeatDelay);
                break;
            // RallyStarted, FreeBallDiscard (the bounce already launched at
            // ChaseEnded), Combo/ComboCardStuffed, Passive/BrokenPlayAvoided and
            // GameScore (the scoreboard reads the live score) are text-only.
        }
    }

    /// <summary>
    /// Reveals the human's prompt for this decision and waits for the answer. Core
    /// posts the request the instant after emitting this event, so it's always there
    /// (or about to be).
    /// </summary>
    private IEnumerator PresentDecision(DecisionRequestedEvent decision)
    {
        object request;
        while (!_channel.TryTakePending(out request))
        {
            yield return null;
        }
        if (!DecisionMatchesRequest(decision.Kind, request))
        {
            Debug.LogWarning($"GameRunner: decision event {decision.Kind} doesn't match request {request.GetType().Name}");
        }
        RevealActiveRequest(request);
        yield return new WaitUntil(() => _activeRequest == null);
    }

    private static bool DecisionMatchesRequest(DecisionKind kind, object request) => kind switch
    {
        DecisionKind.Serve => request is ServeRequest,
        DecisionKind.Receive => request is ReceiveRequest,
        DecisionKind.Set => request is SetCardRequest,
        DecisionKind.Exchange => request is ExchangeCardRequest,
        DecisionKind.HitCards => request is HitCardsRequest,
        DecisionKind.Block => request is BlockCardsRequest,
        DecisionKind.AttackLane => request is AttackLaneRequest,
        DecisionKind.TipOrHit => request is TipOrHitRequest,
        DecisionKind.Cover => request is CoverAttemptRequest,
        DecisionKind.Dig => request is DigCardRequest,
        DecisionKind.Chase => request is ChaseCardRequest,
        DecisionKind.FreeBallDiscard => request is FreeBallDiscardRequest,
        _ => false,
    };

    private IEnumerator PresentServe(ServeEvent serve)
    {
        int launchIndex = _presentIndex;
        string serverTeam = serve.Team;
        string receiverTeam = Opponent(serverTeam);
        _lastServeCardValue = serve.Card.Value;
        _lastServeTargetRole = serve.Target;
        _pendingReceiveTeam = receiverTeam;
        _pendingReceiveRole = serve.Target;
        _chaseRole = null;
        ClearFloatingNumbers(); // the ball crosses the net on every serve
        SetFloatingLabelOnBall(serve.Card.Value.ToString(), Color.white);
        CutToPhaseCameraIfIdle("Serve");
        // Sweeps up anything left over from the previous rally regardless of how it
        // ended (e.g. a stuffed block ends the rally before Swing culls down to one).
        HideAllTrajectoryPreviewsExcept(null);

        if (_pendingServeToss == null)
        {
            // An AI serve: snap both teams straight into their pre-serve shape -- the
            // receivers are already standing in their reception stance long before the
            // toss, so there's nothing to visibly animate before contact. (A human
            // serve already did this while the serve card was being chosen -- see
            // PrepareServeFormationThenToss -- and its toss is already underway, so
            // snapping the server here would teleport them mid-approach.)
            SnapTeamToFormation(serverTeam, "Serve");
            SnapTeamToFormation(receiverTeam, "Receive");
            yield return WaitForQueuedFlights();
        }
        yield return AwaitServeToss(serverTeam);

        // The serve launches the instant the toss reaches contact. The receivers were
        // snapped into Receive shape before the toss, so only the Setter's peel-off
        // toward its Set spot is a real move here -- it isn't receiving, so its actual
        // job (setting) starts the moment the serve is struck.
        ApplyTeamFormation(receiverTeam, "Receive", null, receiveFormationLeadDuration);
        if (_teamPositions.TryGetValue(receiverTeam, out var receiverPositions)
            && receiverPositions.TryGetValue(PlayerRole.Setter, out Transform receiverSetterT))
        {
            MovePlayerTo(receiverSetterT, GetFormationPosition(receiverTeam, PlayerRole.Setter, "Set"), receiveFormationLeadDuration);
        }

        float serveHeight = serveArcHeightByCardValue.Evaluate(serve.Card.Value);
        float serveSpeed = serveArcSpeedByCardValue.Evaluate(serve.Card.Value);
        PlayerRole target = serve.Target;
        QueueFlight(() => MoveBallTo(receiverTeam, target, serveHeight, serveSpeed,
            pauseAtFraction: serveReceptionPauseFraction, holdWhile: HoldForDecisionsAfter(launchIndex),
            destinationOverride: GetReceiveContactPoint(receiverTeam, target), contactHeight: receiveContactHeight,
            allowBounce: false));
    }

    private void PresentReceive(ReceiveEvent receive)
    {
        SetFloatingLabel(receive.Team, receive.Passer, receive.Card.Value.ToString(),
            receive.Clean ? floatingSuccessColor : floatingFailureColor);
        if (receive.Clean)
        {
            // The pass rebounds straight off the passer toward the setter -- it never
            // sits on the passer's head. A failed pass waits for ChaseStarted instead.
            LaunchPassToSetter(receive.Team, _presentIndex);
        }
    }

    /// <summary>
    /// The pass from whoever just played the ball (passer, digger, free-ball receiver)
    /// to the setter, holding just short of the setter's hands for as long as the
    /// human's set/hit/lane (or, for the AI's pass, block) decisions are pending.
    /// Moves both teams into shape the moment the pass leaves: the setting team to Set,
    /// the other to Dig.
    /// </summary>
    private void LaunchPassToSetter(string team, int launchIndex)
    {
        string other = Opponent(team);
        Func<bool> hold = HoldForDecisionsAfter(launchIndex);
        _setterPass = QueueFlight(() => PassToSetter(team, other, hold));
    }

    private IEnumerator PassToSetter(string team, string other, Func<bool> hold)
    {
        ApplyTeamFormation(team, "Set", null, setterReturnDuration);
        ApplyTeamFormation(other, "Dig", null, setterReturnDuration);
        yield return MoveBallTo(team, PlayerRole.Setter, receiveToSetPeakHeight, receiveToSetLateralSpeed,
            pauseAtFraction: setPauseFraction, holdWhile: hold,
            destinationOverride: GetSetContactPoint(team), allowBounce: false, contactHeight: setContactHeight);
    }

    private void PresentChaseStarted(ChaseStartedEvent chase)
    {
        CutToPhaseCameraIfIdle("Dig");
        if (!_pendingReceiveRole.HasValue)
        {
            return;
        }
        _chaseRole = GetAdjacentChaseRole(_pendingReceiveRole.Value);
        PlayerRole chaser = _chaseRole.Value;
        string team = chase.Team;
        Func<bool> hold = HoldForDecisionsAfter(_presentIndex);
        // Holds through every chase attempt (the next ball event is ChaseEnded).
        _chaseFlight = QueueFlight(() => MoveBallTo(team, chaser, chaseLandingPeakHeight,
            pauseAtFraction: chaseRecoveryPauseFraction, holdWhile: hold,
            contactHeight: digContactHeight, allowBounce: false));
    }

    private IEnumerator PresentChaseEnded(ChaseEndedEvent chase)
    {
        if (!chase.Succeeded)
        {
            // An ace: the scramble doesn't get there -- the ball carries on through
            // the chaser into the floor.
            if (_chaseRole.HasValue)
            {
                yield return RunFlightThroughToFloor(_chaseFlight, chase.Team, _chaseRole.Value);
            }
            yield break;
        }
        // Recovered: the chaser bounces it to a teammate (Core names nobody -- any
        // teammate other than the chaser, picked at random), holding late in that
        // bounce while the human pays the free ball's discard.
        if (!_chaseRole.HasValue || !_teamPositions.TryGetValue(chase.Team, out var positions))
        {
            yield break;
        }
        PlayerRole chaser = _chaseRole.Value;
        var candidates = positions.Keys.Where(role => role != chaser).ToList();
        if (candidates.Count == 0)
        {
            yield break;
        }
        PlayerRole target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        string team = chase.Team;
        Func<bool> hold = HoldForDecisionsAfter(_presentIndex);
        QueueFlight(() => MoveBallTo(team, target, freeBallBouncePeakHeight,
            pauseAtFraction: freeBallDiscardPauseFraction, holdWhile: hold,
            contactHeight: digContactHeight, allowBounce: false));
    }

    private IEnumerator PresentFreeBall(FreeBallEvent freeBall)
    {
        ClearFloatingNumbers();
        SetFloatingLabelOnBall("Free Ball", Color.white);
        // Scrambled defense, not a serve reception -- Dig formation/camera. The
        // receiving team's Setter heads straight for its Set spot (the receiver is
        // always Ds/Libero, never the Setter).
        CutToPhaseCameraIfIdle("Dig");
        string team = freeBall.ToTeam;
        PlayerRole receiver = freeBall.Receiver;
        ApplyTeamFormation(team, "Dig", null, receiveFormationLeadDuration);
        if (_teamPositions.TryGetValue(team, out var positions) && positions.TryGetValue(PlayerRole.Setter, out Transform setterT))
        {
            MovePlayerTo(setterT, GetFormationPosition(team, PlayerRole.Setter, "Set"), receiveFormationLeadDuration);
        }
        yield return QueueFlightAndWait(() => MoveBallTo(team, receiver, allowBounce: false));
        LaunchPassToSetter(team, _presentIndex);
    }

    private IEnumerator PresentSet(SetEvent set)
    {
        string team = set.Team;
        _lastAttackingTeam = team;
        _lastSetCardValue = set.Card.Value;
        string tempoLabel = GetTempoLabel(set.Card.Value);
        if (team != _teamAName)
        {
            CutToPhaseCameraIfIdle("Set");
        }
        // The pass to this setter moves both teams into Set/Dig shape when it leaves;
        // this set's tempo-specific moves below have to come after that, not be undone
        // by it.
        FlightHandle pass = _setterPass;
        if (pass != null)
        {
            yield return new WaitUntil(() => pass.Started);
        }
        SetFloatingLabel(team, PlayerRole.Setter, tempoLabel, GetTempoColor(tempoLabel));
        // Cleared once, by the attack phase's first sign (the human's HitCards reveal
        // or the AI's first AttackCommit) -- see _attackPhaseCleared.
        _attackPhaseCleared = false;

        ApplyTeamFormation(Opponent(team), "Dig", null, setterReturnDuration);
        ApplyTeamFormation(team, "Set", null, setterReturnDuration);
        // This team's eligible attackers (everyone but Setter/Libero) start toward this
        // tempo's approach waypoint the instant the set is called, well before the lane.
        if (_teamPositions.TryGetValue(team, out var attackPrepPositions))
        {
            foreach (var kv in attackPrepPositions)
            {
                if (kv.Key == PlayerRole.Setter || kv.Key == PlayerRole.Libero)
                {
                    continue;
                }
                MovePlayerTo(kv.Value, GetFormationPosition(team, kv.Key, "AttackPrep", tempoLabel), setterReturnDuration);
            }
        }
        // When a human decision (hit cards, block) is next, let everyone settle before
        // it's revealed -- the ball is held in the air meanwhile, so this costs nothing
        // visually. Otherwise don't wait: the ball would just land and sit.
        if (HumanDecisionBeforeNextBallEvent(_presentIndex))
        {
            yield return new WaitForSeconds(setterReturnDuration);
        }
    }

    private void PresentAttackCommit(AttackCommitEvent commit)
    {
        _lastAttackingTeam = commit.Team;
        CutToPhaseCameraIfIdle("Attack"); // first sign this exchange has reached the hitting phase
        // A blind-drawn card shows nothing until its Reveal.
        if (!commit.Card.HasValue || !PlayerRoleExtensions.LaneToRole.TryGetValue(commit.Lane, out PlayerRole role))
        {
            return;
        }
        if (!_attackPhaseCleared)
        {
            ClearFloatingNumbers();
            _attackPhaseCleared = true;
        }
        // "X", not the value -- the attacker's strength is what Block is guessing at.
        SetFloatingLabel(commit.Team, role, "X", Color.white);
        // The AI's committed lanes -- exactly what the human needs to see to block.
        // (The human's own open options get UpdateHitTrajectoryPreviews instead.)
        if (commit.Team != _teamAName)
        {
            string tempoLabel = GetTempoLabel(_lastSetCardValue);
            ShowTrajectoryPreview(role,
                GetSetContactPoint(commit.Team) + Vector3.up * setContactHeight,
                GetAttackContactPoint(commit.Team, role, tempoLabel) + Vector3.up * attackContactHeight,
                trajectorySelectedColor, GetFormationPeakHeight(commit.Team, role, "Attack", tempoLabel));
        }
    }

    private void PresentBlockCommit(BlockCommitEvent block)
    {
        // Only a quick set's forced blind single blocker shows an exact value -- a
        // normal block only surfaces as a lane total at Resolve.
        foreach (int lane in block.QuickLanes)
        {
            if (block.CardsByLane.TryGetValue(lane, out var cards) && cards.Count > 0
                && PlayerRoleExtensions.LaneToDefendingRole.TryGetValue(lane, out PlayerRole blocker))
            {
                CutToPhaseCameraIfIdle("Block");
                SetFloatingNumber(block.Team, blocker, cards[0].Value);
            }
        }
    }

    private void PresentSwing(SwingEvent swing)
    {
        string team = swing.Team;
        PlayerRole role = swing.Hitter;
        CutToPhaseCameraIfIdle("Attack");
        // Fresh placement offset for this swing -- shared by the attack flight's
        // target and the digger's own run, so both aim at the identical point.
        float angle = UnityEngine.Random.value * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(UnityEngine.Random.value) * placementVarianceRadius; // sqrt for uniform area density
        _pendingAttackOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        string tempoLabel = GetTempoLabel(_lastSetCardValue);
        Vector3 setterPos = GetSetContactPoint(team);
        Vector3 hitterDestination = GetAttackContactPoint(team, role, tempoLabel);
        float hitterPeakHeight = GetFormationPeakHeight(team, role, "Attack", tempoLabel);

        // Whole team into its Attack formation for this tempo, timed off the set's own
        // flight so the hitter lands on it; the Setter peels straight off to defense.
        float hitterDuration = EstimateSetToHitterDuration(team, role, tempoLabel) * hitterMoveLeadFraction;
        ApplyTeamFormation(team, "Attack", tempoLabel, hitterDuration);
        if (_teamPositions.TryGetValue(team, out var positions) && positions.TryGetValue(PlayerRole.Setter, out Transform setterT))
        {
            MovePlayerTo(setterT, GetFormationPosition(team, PlayerRole.Setter, "Dig"), setterReturnDuration);
        }

        HideAllTrajectoryPreviewsExcept(role);
        ShowTrajectoryPreview(role, setterPos + Vector3.up * setContactHeight, hitterDestination + Vector3.up * attackContactHeight,
            trajectorySelectedColor, hitterPeakHeight);
        // Holds short of the hitter while a human tip-or-hit is pending.
        Func<bool> hold = HoldForDecisionsAfter(_presentIndex);
        QueueFlight(() => FlySwing(team, role, hitterDestination, hitterPeakHeight, hold));
    }

    private IEnumerator FlySwing(string team, PlayerRole role, Vector3 hitterDestination, float hitterPeakHeight, Func<bool> hold)
    {
        yield return MoveBallTo(team, role, peakHeight: hitterPeakHeight, pauseAtFraction: swingPauseFraction, holdWhile: hold,
            destinationOverride: hitterDestination, contactHeight: attackContactHeight, allowBounce: false);
        HideTrajectoryPreview(role);
    }

    private void PresentResolve(ResolveEvent resolve)
    {
        _lastLane = resolve.Lane;
        _lastAttackCardValue = resolve.AttackCard.Value;
        // The earliest point the digging role is knowable -- start the whole defending
        // team into Dig shape now, the digger aiming at this swing's placement offset.
        PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);
        float remainingFlight = _ballFlight != null ? _ballFlight.RemainingFlightTime : 0f;
        float estimatedDuration = (remainingFlight + narrativeBeatDelay) * digApproachLeadFraction;
        ApplyTeamFormation(Opponent(resolve.Team), "Dig", null, estimatedDuration,
            extraOffsets: new Dictionary<PlayerRole, Vector3> { [defenderRole] = _pendingAttackOffset });
    }

    private IEnumerator PresentAttackOutcome(AttackOutcomeEvent outcome)
    {
        Func<bool> hold = HoldForDecisionsAfter(_presentIndex);
        switch (outcome.Outcome)
        {
            case AttackOutcomeType.Kill when outcome.Digger.HasValue:
            {
                // Through to the defense: the attack flies at the digger right away,
                // holding partway across while a human cover/dig decision is pending.
                string defendingTeam = Opponent(outcome.Team);
                PlayerRole digger = outcome.Digger.Value;
                bool soft = outcome.Shot == ShotKind.Tip;
                int attackValue = _lastAttackCardValue;
                Vector3 offset = _pendingAttackOffset;
                _attackFlight = QueueFlight(() => FlyAttack(defendingTeam, digger, soft, attackValue, offset, hold));
                break;
            }
            case AttackOutcomeType.Deflect:
            {
                // Off the block and back onto the attacker's own side, toward their
                // Libero (Core names no digger for a deflection).
                string team = outcome.Team;
                _attackFlight = QueueFlight(() => MoveBallTo(team, DeflectDigRole, pauseAtFraction: digPauseFraction,
                    holdWhile: hold, contactHeight: digContactHeight, allowBounce: false));
                break;
            }
            case AttackOutcomeType.Stuffed:
            {
                // Blocked straight back down onto the attacker's side, near the lane.
                if (PlayerRoleExtensions.LaneToRole.TryGetValue(outcome.Lane, out PlayerRole attackerRole)
                    && _teamPositions.TryGetValue(outcome.Team, out var attackerPositions)
                    && attackerPositions.TryGetValue(attackerRole, out Transform stuffTarget))
                {
                    yield return QueueFlightAndWait(() => MoveBallToFloorPosition(stuffTarget, stuffedLandingPeakHeight));
                }
                break;
            }
        }
    }

    private IEnumerator FlyAttack(string defendingTeam, PlayerRole digger, bool soft, int attackValue, Vector3 offset, Func<bool> hold)
    {
        ClearFloatingNumbers(); // the ball crosses the net into this dig
        // The attack's own value transfers onto the ball -- it's what the dig has to beat.
        SetFloatingLabelOnBall(attackValue.ToString(), Color.white);
        // A tip loops in on BallFlight's softer default arc; everything else is the
        // card-value-driven flat, fast hit.
        if (soft)
        {
            yield return MoveBallTo(defendingTeam, digger, pauseAtFraction: digPauseFraction, holdWhile: hold,
                targetOffset: offset, contactHeight: digContactHeight, allowBounce: false);
        }
        else
        {
            yield return MoveBallTo(defendingTeam, digger, attackPeakHeightByCardValue.Evaluate(attackValue), attackLateralSpeed,
                pauseAtFraction: digPauseFraction, holdWhile: hold,
                targetOffset: offset, contactHeight: digContactHeight, allowBounce: false);
        }
    }

    private IEnumerator PresentDig(DigEvent dig)
    {
        SetFloatingLabel(dig.Team, dig.Digger, dig.Card.Value.ToString(), dig.Dug ? floatingSuccessColor : floatingFailureColor);
        CutToPhaseCameraIfIdle("Dig");
        if (!dig.Dug)
        {
            // The kill drives straight on through the digger into the floor.
            yield return RunFlightThroughToFloor(_attackFlight, dig.Team, dig.Digger);
            yield break;
        }
        // The digging team's Setter starts toward setting this ball right away --
        // unless the Setter IS the digger, whose spot the ball is still flying to.
        if (dig.Digger != PlayerRole.Setter
            && _teamPositions.TryGetValue(dig.Team, out var positions)
            && positions.TryGetValue(PlayerRole.Setter, out Transform setterT))
        {
            MovePlayerTo(setterT, GetFormationPosition(dig.Team, PlayerRole.Setter, "Set"), setterReturnDuration);
        }
        FlightHandle attack = _attackFlight;
        if (attack != null)
        {
            yield return new WaitUntil(() => attack.Done);
        }
        LaunchPassToSetter(dig.Team, _presentIndex);
    }

    private IEnumerator PresentDeflectDig(DeflectDigEvent deflect)
    {
        SetFloatingLabel(deflect.Team, DeflectDigRole, deflect.Card.Value.ToString(),
            deflect.Dug ? floatingSuccessColor : floatingFailureColor);
        CutToPhaseCameraIfIdle("Dig");
        if (!deflect.Dug)
        {
            yield return RunFlightThroughToFloor(_attackFlight, deflect.Team, DeflectDigRole);
            yield break;
        }
        FlightHandle attack = _attackFlight;
        if (attack != null)
        {
            yield return new WaitUntil(() => attack.Done);
        }
        LaunchPassToSetter(deflect.Team, _presentIndex);
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
        // PlayServeToss/PresentServe for the same decision). Whichever camera is already
        // showing (set by ApplyPhaseCameraForRequest or CutToPhaseCameraIfIdle) is
        // expected to already frame wherever this flight travels; that's the whole
        // point of auditing each camera's coverage rather than chasing the ball.
        Vector3 dest = basePos + Vector3.up * (contactHeight ?? ballHeight) + (targetOffset ?? Vector3.zero);
        yield return _ballFlight.FlyTo(dest, peakHeight: peakHeight, lateralSpeed: lateralSpeed, pauseAtFraction: pauseAtFraction,
            allowBounce: allowBounce, holdWhile: holdWhile, slowMoWhile: () => _activeRequest != null,
            throughToFloorY: throughToFloor ? floorLandingHeight : null);
    }

    /// <summary>
    /// Flies the ball to the floor near (not ON) nearTransform -- for a rally-ending
    /// outcome that lands on the court itself (a kill drilled past the dig, a stuffed
    /// attack falling back on the attacker's own side) rather than arriving at a player
    /// who caught it. Confirmed live that landing exactly at the player's own position
    /// clips straight into their capsule, which also reads as "in reach" rather than the
    /// dead, undiggable ball it's supposed to be -- offset laterally (randomized side,
    /// relative to the team's own facing) instead. Waits for any flight already in
    /// progress first, same reentrancy reasoning as QueueFlight.
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
    /// well before PresentServe (which only runs once that decision is already
    /// answered and the ServeEvent exists) ever gets a
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
    /// contact point so the serve arc that follows (PresentServe)
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
        // finishing when this rally's serve gets presented -- same reentrancy
        // concern as QueueFlight, since this repositions the ball directly
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
    /// take, used to size the attacking team's Attack-phase move (see
    /// PresentSwing), computed from the Setter's Set-phase anchor to the hitter's Attack-phase
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
    /// (a new rally starting; see PresentServe), or a role to narrow down to just that
    /// one (the final swinging lane; see PresentSwing).
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
