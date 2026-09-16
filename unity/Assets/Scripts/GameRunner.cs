using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
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
    [SerializeField] private float ballHeight = 1.5f; // destination height above a player's position for the ball to arrive at

    [Header("Serve toss")]
    [SerializeField] private float serviceAreaDepth = 3f; // local-Z behind the server's own back row, where the toss happens
    [SerializeField] private float serveTossPeakOffset = 1.75f; // how high above ballHeight the toss goes before falling back
    [SerializeField] private float serveJumpHeight = 0.5f; // how high the server's own capsule hops, timed to the catch

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
    [SerializeField] private Vector2 standardPanelSize = new(320, 400); // card-list prompts (serve, receive, set, dig, chase, exchange, cover, attack lane)
    [SerializeField] private Vector2 compactPanelSize = new(320, 160);  // 2-button prompts (tip or hit)
    [SerializeField] private Vector2 widePanelSize = new(360, 500);     // multi-slot prompts (hit cards, block cards)

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

    // Permanent, never cleared -- applied once at startup so the two teams are always
    // visually distinguishable regardless of whose turn it is. Uses the same
    // MaterialPropertyBlock mechanism as the temporary highlights above, but on the
    // "AIPlayers" group, which the temporary highlight/clear logic never touches.
    [SerializeField] private Color aiTeamColor = new(0.6f, 0.2f, 0.8f);

    private static readonly Regex ServeRegex = new(@"Serve:\s+(\S+) card \d+.*targeting (\w+)");
    private static readonly Regex ReceiveRegex = new(@"Receive:\s+(\S+) card (\d+)");
    private static readonly Regex SetRegex = new(@"Set:\s+(\S+) card (\d+)");
    private static readonly Regex AttackRegex = new(@"Attack:\s+(\S+) lane (\d+) \S+\s+card (\d+)");
    private static readonly Regex SwingRegex = new(@"Swing:\s+(\S+) lane (\d+) (\w+)");
    private static readonly Regex RevealRegex = new(@"Reveal:\s+lane (\d+) blind draw.*card (\d+)");
    private static readonly Regex BlockQuicksetRegex = new(@"Block:\s+Quick set lane (\d+).*card (\d+)");
    private static readonly Regex FreeBallRegex = new(@"mandatory free ball to (\S+)");
    private static readonly Regex ResolveRegex = new(@"Resolve:\s+(\S+) lane (\d+) (\w+)\s+atk (\d+)");
    private static readonly Regex DigRegex = new(@"Dig:\s+(\S+) card (\d+)");

    private Transform _ball;
    private BallFlight _ballFlight;
    private Dictionary<string, Dictionary<PlayerRole, Transform>> _teamPositions;
    private Dictionary<Transform, (string Team, PlayerRole Role)> _transformOwner;
    private string _teamAName;
    private string _teamBName;
    private int _lastLane = -1;
    private int _lastAttackCardValue = -1;
    private PlayerRole? _lastServeTargetRole;
    private int? _lastChosenAttackLane;
    private string _pendingReceiveTeam;
    private PlayerRole? _pendingReceiveRole;
    private string _lastAttackingTeam;

    private readonly Dictionary<Transform, MaterialPropertyBlock> _highlightBlocks = new();

    // Card values floating above whoever just played them, cleared every time the ball
    // crosses the net (a fresh serve, or an attack coming over for a dig) so the display
    // always reflects only the current exchange in flight.
    private readonly Dictionary<Transform, int> _floatingNumbers = new();

    // -- Per-phase cameras: convention-based, not a fixed list -- a camera named
    // "Player {Phase} Camera" is used automatically for that decision type the moment
    // it exists in the scene, falling back to defaultCamera otherwise. Several request
    // types deliberately share one camera name rather than each getting its own: Hit/
    // AttackLane/TipOrHit all need the same "every hitter in frame" shot (Attack), Dig/
    // Chase are the same defensive framing (Dig), and FreeBall is just another way the
    // ball arrives at a back-row receiver (Receive) -- one distinct camera per *kind of
    // moment*, not one per request type. Exchange/Cover have no player tied to them at
    // all (see ApplyHighlightsForRequest's comment on those two) and intentionally have
    // no entry here, always falling back to defaultCamera.
    private static readonly Dictionary<Type, string> PhaseCameraNames = new()
    {
        { typeof(ServeRequest), "Serve" },
        { typeof(FreeBallTargetRequest), "Receive" },
        { typeof(ReceiveRequest), "Receive" },
        { typeof(SetCardRequest), "Set" },
        { typeof(HitCardsRequest), "Attack" },
        { typeof(BlockCardsRequest), "Block" },
        { typeof(AttackLaneRequest), "Attack" },
        { typeof(TipOrHitRequest), "Attack" },
        { typeof(DigCardRequest), "Dig" },
        { typeof(ChaseCardRequest), "Dig" },
    };

    // Guards PlayServeToss against running twice concurrently for the same team -- if a
    // second Serve line for the same server got processed before the first toss finished
    // (a genuine, confirmed possibility: multiple PlayLines batches can be in flight at
    // once), both coroutines would fight over the same setter Transform, and neither's
    // final restore would reliably win -- leaving the setter stuck away from its normal
    // position. See PlayServeToss.
    private readonly HashSet<string> _tossingTeams = new();

    private HumanDecisionChannel _channel;
    private List<string> _narrative;
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
    private Card? _servePendingCard;
    private List<(int Lane, Card Card, AttackPosition Position)> _hitPlacements;
    private (int Lane, AttackPosition Position)? _hitPendingSlot;
    private Dictionary<PlayerRole, (int Lane, Card Card)> _blockPlacements;
    private PlayerRole? _blockPendingRole;
    private int? _blockPendingLane;

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
        var humanStrategy = new HumanStrategy(_channel);
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

            var rally = new Rally(serving, receiving, srvStrat, rcvStrat, rng, _narrative);
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
        }

        return _scoreA > _scoreB ? teamA.Name : teamB.Name;
    }

    private void OnDestroy() => _channel?.Cancel();

    private void OnApplicationQuit() => _channel?.Cancel();

    private void Update()
    {
        if (_activeRequest == null && _channel != null && _channel.TryTakePending(out object req))
        {
            _activeRequest = req;
            ResetPerRequestState();
            FlushNarrative(); // safe: background thread is now confirmed blocked in Post()
            ApplyHighlightsForRequest(req);
            // Must run before PopulateHandStrip -- it switches to this decision's phase
            // camera, and PopulateHandStrip reads whichever camera is active right now
            // to assign each spawned card's DropCamera (used for the drop raycast). In
            // the old order, cards were handed a stale camera from the previous
            // decision (or none at all), silently breaking every drag-and-drop drop.
            ApplyPhaseCameraForRequest(req);
            PopulateHandStrip(req);

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
            if (req is SetCardRequest)
            {
                StartCoroutine(MoveBallToSetterWhenReady());
            }
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
            case ChaseCardRequest when team == _teamAName && role == GetCurrentDefenderRole():
                ResolveActiveRequest(card);
                break;
            case BlockCardsRequest blockReq when team == _teamAName:
                if (TryAddBlockCard(blockReq, role, card))
                {
                    Destroy(view.gameObject); // committed to a lane -- remove from the draggable strip
                }
                break;
            case HitCardsRequest hitReq when team == _teamAName && _hitPendingSlot.HasValue
                && role == HitSlotRole(_hitPendingSlot.Value.Lane, _hitPendingSlot.Value.Position):
                // Gated on a slot already being selected (via a hit-option button) rather
                // than resolving straight from the drop target -- DS has no single home
                // lane the way front-row hitters do, so a bare drop onto DS alone can't
                // tell us which back lane was meant. The button click is what disambiguates.
                _hitPlacements ??= new List<(int, Card, AttackPosition)>();
                _hitPlacements.Add((_hitPendingSlot.Value.Lane, card, _hitPendingSlot.Value.Position));
                _hitPendingSlot = null;
                HighlightHitOptions(hitReq);
                Destroy(view.gameObject);
                break;
        }
    }

    /// <summary>
    /// Drops a card directly onto a blocker's own capsule -- unlike the OnGUI panel,
    /// there's only one physical spot to drop on, so this can't distinguish "block my
    /// own lane" from "block the adjacent lane" when both are legal. Defaults to the
    /// blocker's own lane when it's attacked, otherwise whichever adjacent lane is.
    /// Anyone who wants the other option can still use the OnGUI panel, which lets you
    /// pick blocker and lane independently.
    /// </summary>
    private bool TryAddBlockCard(BlockCardsRequest request, PlayerRole role, Card card)
    {
        if (!PlayerRoleExtensions.BlockableLanes.TryGetValue(role, out var reachableLanes))
        {
            return false; // not a front-row blocking role at all
        }
        if (_blockPlacements != null && _blockPlacements.ContainsKey(role))
        {
            return false; // this blocker already committed via an earlier drag or button click
        }
        if (_blockPlacements != null && _blockPlacements.Values.Any(v => v.Card.Equals(card)))
        {
            return false; // this exact card already committed to another blocker
        }
        var legalLanes = reachableLanes.Where(request.AttackLanes.Contains).ToList();
        if (legalLanes.Count == 0)
        {
            return false;
        }
        int homeLane = PlayerRoleExtensions.LaneToRole.First(kv => kv.Value == role).Key;
        int lane = legalLanes.Contains(homeLane) ? homeLane : legalLanes[0];
        _blockPlacements ??= new Dictionary<PlayerRole, (int, Card)>();
        _blockPlacements[role] = (lane, card);
        return true;
    }

    // -- Player highlighting: orange = the single player actively making this decision
    // (digger/setter/hitter), green = the set of hitter options being chosen among
    // (e.g. every eligible attack lane while the setter places hit cards), blue = the
    // blockers currently being selected. Only the human's own players ("Players" group)
    // are ever highlighted -- every request that reaches this UI is the human's own
    // decision, so there's nothing to highlight on the AI's side.

    private void ApplyHighlightsForRequest(object request)
    {
        ClearAllHighlights();
        switch (request)
        {
            case SetCardRequest:
                HighlightRole(PlayerRole.Setter, activeSelectionColor);
                break;
            case DigCardRequest:
            case ChaseCardRequest:
                HighlightCurrentDefender(activeSelectionColor);
                break;
            case ReceiveRequest:
                if (_lastServeTargetRole.HasValue)
                {
                    HighlightRole(_lastServeTargetRole.Value, activeSelectionColor);
                }
                break;
            case HitCardsRequest hitReq:
                HighlightHitOptions(hitReq);
                break;
            case AttackLaneRequest laneReq:
                foreach (int lane in laneReq.AttackCards.Keys)
                {
                    HighlightLane(lane, hitterOptionColor);
                }
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
    /// Green on every still-open hitting option, orange on whichever one is currently
    /// selected (_hitPendingSlot) -- called both when a HitCardsRequest first goes active
    /// and again every time _hitPendingSlot changes, so the highlight always reflects the
    /// live selection state. Multiple open back lanes all resolve to the same player
    /// (DS) -- if any of them is the pending slot, that wins over green for DS, since a
    /// slot being pending logically applies to the one physical player waiting on a card,
    /// not to a specific lane number they don't stand in anyway.
    /// </summary>
    private void HighlightHitOptions(HitCardsRequest request)
    {
        // Without this, a hitter's green highlight would stay stuck forever once their
        // slot gets filled (or once a different slot becomes the pending one) -- this
        // method only ever adds colors for currently-relevant roles below, it never
        // removes one for a role that's no longer under consideration.
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
            bool isPending = _hitPendingSlot.HasValue && _hitPendingSlot.Value == (lane, position);
            PlayerRole role = HitSlotRole(lane, position);
            if (isPending || !colors.ContainsKey(role))
            {
                colors[role] = isPending ? activeSelectionColor : hitterOptionColor;
            }
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
    }

    // -- Floating card numbers: shows the value of whichever card a player (on either
    // team) just played, hovering above them, driven off the same narrative lines that
    // already drive ball movement -- see HandleLine below. Cleared every time the ball
    // crosses the net (fresh serve, or an attack coming over for a dig), so it always
    // reflects only the exchange currently in flight, per the user's own spec.

    private void SetFloatingNumber(string team, PlayerRole role, int value)
    {
        if (_teamPositions.TryGetValue(team, out var positions) && positions.TryGetValue(role, out Transform t))
        {
            _floatingNumbers[t] = value;
        }
    }

    private void ClearFloatingNumbers() => _floatingNumbers.Clear();

    private void ResetPerRequestState()
    {
        _servePendingCard = null;
        _hitPlacements = null;
        _hitPendingSlot = null;
        _blockPlacements = null;
        _blockPendingRole = null;
        _blockPendingLane = null;
    }

    /// <summary>Answers the currently-active request and clears it. Call exactly once per request.</summary>
    private void ResolveActiveRequest(object response)
    {
        // Captured here (not in DrawAttackLaneRequest's button handler) so it's correct
        // no matter how the response was produced -- TipOrHitRequest's highlight depends on it.
        if (_activeRequest is AttackLaneRequest && response is int lane)
        {
            _lastChosenAttackLane = lane;
        }
        // The human's own block placement is the one card-value we can't reliably read
        // back out of the narrative (only a blind quickset block gets a per-card line),
        // so show it immediately from the response we already have in hand -- each
        // blocker's own card above their own capsule, not aggregated by lane, since two
        // different blockers can now legally converge on the same lane.
        if (_activeRequest is BlockCardsRequest && response is Dictionary<PlayerRole, (int Lane, Card Card)> blockPlacements)
        {
            foreach (var kv in blockPlacements)
            {
                SetFloatingNumber(_teamAName, kv.Key, kv.Value.Card.Value);
            }
        }
        // The serve->receiver and receiver->setter legs pause in slow-motion partway
        // through, waiting specifically for these two decisions -- let the ball ease
        // back up to full speed and finish now that the answer is in.
        if (_activeRequest is ReceiveRequest or SetCardRequest)
        {
            _ballFlight?.Resume();
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
        if (_activeRequest is HitCardsRequest hitOptionsReq)
        {
            // Same reasoning as DrawFloatingNumbers -- positions come from
            // Camera.WorldToScreenPoint (already real screen coordinates), so this stays
            // outside the scale transform below too, to avoid double-transforming them.
            DrawHitOptionButtons(hitOptionsReq, guiScale);
        }

        Matrix4x4 previousMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(guiScale, guiScale), Vector2.zero);

        DrawScoreboard(guiScale);

        switch (_activeRequest)
        {
            case ServeRequest serveReq:
                DrawServeRequest(serveReq);
                break;
            case FreeBallTargetRequest freeBallReq:
                DrawFreeBallTargetRequest(freeBallReq);
                break;
            case ReceiveRequest receiveReq:
                DrawCardChoice(receiveReq.Hand, $"Choose a card to receive (serve = {receiveReq.ServeValue}):",
                    allowDecline: false, card => ResolveActiveRequest(card.Value));
                break;
            case SetCardRequest setReq:
                DrawCardChoice(setReq.Hand, setReq.BrokenPlay ? "Choose a set card (BROKEN PLAY):" : "Choose a set card:",
                    allowDecline: false, card => ResolveActiveRequest(card.Value));
                break;
            case HitCardsRequest hitReq:
                DrawHitCardsRequest(hitReq);
                break;
            case BlockCardsRequest blockReq:
                DrawBlockCardsRequest(blockReq);
                break;
            case AttackLaneRequest laneReq:
                DrawAttackLaneRequest(laneReq);
                break;
            case TipOrHitRequest tipReq:
                DrawTipOrHitRequest(tipReq);
                break;
            case DigCardRequest digReq:
                DrawCardChoice(digReq.Hand, $"Choose a dig card (target {digReq.TargetValue}, {digReq.DigType}):",
                    allowDecline: false, card => ResolveActiveRequest(card.Value));
                break;
            case ChaseCardRequest chaseReq:
                int needed = chaseReq.TargetValue - chaseReq.RunningTotal;
                DrawCardChoice(chaseReq.Hand, $"Choose a chase card (need {needed} more, total {chaseReq.RunningTotal}):",
                    allowDecline: false, card => ResolveActiveRequest(card.Value));
                break;
            case ExchangeCardRequest exchangeReq:
                DrawCardChoice(exchangeReq.Hand, $"Exchange a card for deck top ({exchangeReq.DeckTop})? (or Decline)",
                    allowDecline: true, card => ResolveActiveRequest(card));
                break;
            case CoverAttemptRequest coverReq:
                DrawCardChoice(coverReq.Hand, $"Attempt cover (need >= {coverReq.Threshold})? (or Decline)",
                    allowDecline: true, card => ResolveActiveRequest(card));
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
        float halfWidth = 20 * guiScale;
        float halfHeight = 15 * guiScale;
        foreach (var kv in _floatingNumbers)
        {
            if (kv.Key == null)
            {
                continue;
            }
            Vector3 worldPos = kv.Key.position + Vector3.up * (ballHeight + 1f);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0)
            {
                continue;
            }
            float guiY = Screen.height - screenPos.y;
            GUI.Label(new Rect(screenPos.x - halfWidth, guiY - halfHeight, halfWidth * 2, halfHeight * 2), kv.Value.ToString(), _floatingNumberStyle);
        }
    }

    /// <summary>
    /// A real button positioned above each still-open hitting option (world-positioned
    /// via Camera.WorldToScreenPoint, same technique as DrawFloatingNumbers), the primary
    /// way to place attack cards now -- click one to select/highlight that hitter (see
    /// HighlightHitOptions), then drag a card onto them (HandleCardDropped's
    /// HitCardsRequest case). The OnGUI text-list panel (DrawHitCardsRequest) still works
    /// unchanged alongside this, same "additional path, not a replacement" precedent as
    /// Block's drag support.
    /// </summary>
    private void DrawHitOptionButtons(HitCardsRequest request, float guiScale)
    {
        Camera cam = GetActiveGameplayCamera();
        if (cam == null || !_teamPositions.TryGetValue(_teamAName, out var positions))
        {
            return;
        }

        var usedSlots = _hitPlacements != null
            ? new HashSet<(int Lane, AttackPosition Position)>(_hitPlacements.Select(p => (p.Lane, p.Position)))
            : new HashSet<(int, AttackPosition)>();
        var usedCards = _hitPlacements != null
            ? new HashSet<Card>(_hitPlacements.Select(p => p.Card))
            : new HashSet<Card>();
        bool canPlaceMore = (_hitPlacements?.Count ?? 0) < request.Template.MaxAttackers
            && request.Hand.Any(c => !usedCards.Contains(c));
        if (!canPlaceMore)
        {
            return;
        }

        var slots = new List<(int Lane, AttackPosition Position)>();
        foreach (int lane in request.Template.FrontLanes)
        {
            if (!usedSlots.Contains((lane, AttackPosition.Front)))
            {
                slots.Add((lane, AttackPosition.Front));
            }
        }
        foreach (int lane in request.Template.BackLanes)
        {
            if (!usedSlots.Contains((lane, AttackPosition.Back)))
            {
                slots.Add((lane, AttackPosition.Back));
            }
        }

        float width = 100 * guiScale;
        float height = 26 * guiScale;
        // More than one open back lane resolves to the same physical player (DS) --
        // stack their buttons vertically instead of overlapping.
        var stackIndex = new Dictionary<PlayerRole, int>();
        foreach (var slot in slots)
        {
            PlayerRole role = HitSlotRole(slot.Lane, slot.Position);
            if (!positions.TryGetValue(role, out Transform t))
            {
                continue;
            }
            int stack = stackIndex.GetValueOrDefault(role, 0);
            stackIndex[role] = stack + 1;

            // Matches DrawFloatingNumbers' proven-safe height (ballHeight + 1f) as the
            // base -- an earlier, higher offset (ballHeight + 2f) pushed most buttons
            // outside some phase cameras' frustum, confirmed live via a screenshot that
            // showed only 1 of 3 front-lane buttons actually visible.
            Vector3 worldPos = t.position + Vector3.up * (ballHeight + 1f + stack * 0.4f);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0)
            {
                continue;
            }
            float guiY = Screen.height - screenPos.y;
            string label = slot.Position == AttackPosition.Front ? $"Lane {slot.Lane}" : $"Lane {slot.Lane} (Back)";
            if (GUI.Button(new Rect(screenPos.x - width / 2f, guiY - height / 2f, width, height), label))
            {
                _hitPendingSlot = slot;
                HighlightHitOptions(request);
            }
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

    private void DrawFreeBallTargetRequest(FreeBallTargetRequest request)
    {
        GUILayout.BeginArea(PanelRect(standardPanelSize), GUI.skin.box);
        GUILayout.Label("Mandatory free ball -- choose who it's played to:");
        foreach (GridPlayer receiver in request.EligibleReceivers)
        {
            if (GUILayout.Button(receiver.Role.DisplayName()))
            {
                ResolveActiveRequest(receiver);
            }
        }
        GUILayout.EndArea();
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

    private void DrawAttackLaneRequest(AttackLaneRequest request)
    {
        GUILayout.BeginArea(PanelRect(standardPanelSize), GUI.skin.box);
        GUILayout.Label("Choose an attack lane:");
        foreach (var kv in request.AttackCards)
        {
            int lane = kv.Key;
            string cardLabel = kv.Value.Value > 0 ? kv.Value.ToString() : "?? (blind)";
            int blockValue = request.BlockLayout.GetValueOrDefault(lane, 0);
            if (GUILayout.Button($"Lane {lane}: card {cardLabel}, block {blockValue}"))
            {
                ResolveActiveRequest(lane);
            }
        }
        GUILayout.EndArea();
    }

    private void DrawTipOrHitRequest(TipOrHitRequest request)
    {
        GUILayout.BeginArea(PanelRect(compactPanelSize), GUI.skin.box);
        GUILayout.Label($"Attack {request.AttackValue} vs block {request.BlockValue} — tip or hit?");
        if (GUILayout.Button("Tip"))
        {
            ResolveActiveRequest("tip");
        }
        if (GUILayout.Button("Hit"))
        {
            ResolveActiveRequest("hit");
        }
        GUILayout.EndArea();
    }

    private void DrawHitCardsRequest(HitCardsRequest request)
    {
        _hitPlacements ??= new List<(int, Card, AttackPosition)>();
        var usedSlots = new HashSet<(int, AttackPosition)>(_hitPlacements.Select(p => (p.Lane, p.Position)));
        var usedCards = new HashSet<Card>(_hitPlacements.Select(p => p.Card));
        var availableHand = request.Hand.Where(c => !usedCards.Contains(c)).ToList();

        GUILayout.BeginArea(PanelRect(widePanelSize), GUI.skin.box);
        GUILayout.Label($"Placing attackers ({_hitPlacements.Count}/{request.Template.MaxAttackers}):");
        foreach (var p in _hitPlacements)
        {
            GUILayout.Label($"  Lane {p.Lane} {p.Position}: {p.Card}");
        }

        if (_hitPendingSlot == null)
        {
            if (_hitPlacements.Count < request.Template.MaxAttackers && availableHand.Count > 0)
            {
                GUILayout.Label("Choose a slot:");
                foreach (int lane in request.Template.FrontLanes)
                {
                    if (!usedSlots.Contains((lane, AttackPosition.Front)) && GUILayout.Button($"Lane {lane} Front"))
                    {
                        _hitPendingSlot = (lane, AttackPosition.Front);
                        HighlightHitOptions(request);
                    }
                }
                foreach (int lane in request.Template.BackLanes)
                {
                    if (!usedSlots.Contains((lane, AttackPosition.Back)) && GUILayout.Button($"Lane {lane} Back"))
                    {
                        _hitPendingSlot = (lane, AttackPosition.Back);
                        HighlightHitOptions(request);
                    }
                }
            }
            if (GUILayout.Button("Done"))
            {
                var result = _hitPlacements;
                ResolveActiveRequest(result);
            }
        }
        else
        {
            var slot = _hitPendingSlot.Value;
            GUILayout.Label($"Choose a card for lane {slot.Lane} {slot.Position}:");
            foreach (Card card in availableHand)
            {
                if (GUILayout.Button($"{card}"))
                {
                    _hitPlacements.Add((slot.Lane, card, slot.Position));
                    _hitPendingSlot = null;
                    HighlightHitOptions(request);
                }
            }
            if (GUILayout.Button("Cancel"))
            {
                _hitPendingSlot = null;
                HighlightHitOptions(request);
            }
        }
        GUILayout.EndArea();
    }

    private void DrawBlockCardsRequest(BlockCardsRequest request)
    {
        _blockPlacements ??= new Dictionary<PlayerRole, (int, Card)>();
        var usedCards = new HashSet<Card>(_blockPlacements.Values.Select(v => v.Item2));
        var availableHand = request.Hand.Where(c => !usedCards.Contains(c)).ToList();

        GUILayout.BeginArea(PanelRect(widePanelSize), GUI.skin.box);
        GUILayout.Label("Block placement so far:");
        foreach (var kv in _blockPlacements)
        {
            GUILayout.Label($"  {kv.Key.DisplayName()}: lane {kv.Value.Item1}, card {kv.Value.Item2}");
        }

        if (_blockPendingRole == null)
        {
            GUILayout.Label("Choose a blocker + lane (or Done):");
            foreach (PlayerRole blockerRole in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
            {
                if (_blockPlacements.ContainsKey(blockerRole) || availableHand.Count == 0)
                {
                    continue;
                }
                foreach (int lane in PlayerRoleExtensions.BlockableLanes[blockerRole])
                {
                    if (!request.AttackLanes.Contains(lane))
                    {
                        continue;
                    }
                    if (GUILayout.Button($"{blockerRole.DisplayName()}: Lane {lane}"))
                    {
                        _blockPendingRole = blockerRole;
                        _blockPendingLane = lane;
                    }
                }
            }
            if (GUILayout.Button("Done"))
            {
                var result = _blockPlacements;
                ResolveActiveRequest(result);
            }
        }
        else
        {
            GUILayout.Label($"Choose a card for {_blockPendingRole.Value.DisplayName()} (lane {_blockPendingLane}):");
            foreach (Card card in availableHand)
            {
                if (GUILayout.Button($"{card}"))
                {
                    _blockPlacements[_blockPendingRole.Value] = (_blockPendingLane.Value, card);
                    _blockPendingRole = null;
                    _blockPendingLane = null;
                }
            }
            if (GUILayout.Button("Cancel"))
            {
                _blockPendingRole = null;
                _blockPendingLane = null;
            }
        }
        GUILayout.EndArea();
    }

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

    private void FlushNarrative()
    {
        if (_narrativeReadIndex >= _narrative.Count)
        {
            return;
        }
        var newLines = _narrative.Skip(_narrativeReadIndex).ToList();
        _narrativeReadIndex = _narrative.Count;
        foreach (string line in newLines)
        {
            Debug.Log(line);
        }
        ScanForStateUpdates(newLines); // synchronous, so highlighting can use the result immediately -- ball movement below is animated instead
        StartCoroutine(PlayLines(newLines));
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
                if (serverTeam != _teamAName && Enum.TryParse(m.Groups[2].Value, ignoreCase: true, out PlayerRole role))
                {
                    _lastServeTargetRole = role;
                }
            }
            else if ((m = ResolveRegex.Match(line)).Success)
            {
                _lastLane = int.Parse(m.Groups[2].Value);
                _lastAttackCardValue = int.Parse(m.Groups[4].Value);
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
                yield return HandleLine(line);
                if (!_lineHadRealWait)
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
            ClearFloatingNumbers(); // the ball crosses the net on every serve
            CutToPhaseCameraIfIdle("Serve");
            _lineHadRealWait = true;
            yield return PlayServeToss(serverTeam);
            if (Enum.TryParse(m.Groups[2].Value, ignoreCase: true, out PlayerRole role))
            {
                _pendingReceiveTeam = receiverTeam;
                _pendingReceiveRole = role;
                // Pause 75% of the way there and hold (slow-motion, not a stop) until
                // the receive card is actually chosen -- see ResolveActiveRequest. Only
                // when the human is the one receiving -- the AI decides instantly and
                // never resolves through that path, so pausing for it would hang forever.
                float servePause = receiverTeam == _teamAName ? 0.75f : -1f;
                yield return MoveBallToWhenReady(receiverTeam, role, pauseAtFraction: servePause,
                    stillPending: () => _activeRequest is ReceiveRequest);
            }
        }
        else if ((m = ReceiveRegex.Match(line)).Success)
        {
            // The Receive line itself doesn't name a role -- it's whoever the
            // preceding Serve line targeted, captured above.
            if (_pendingReceiveRole.HasValue)
            {
                SetFloatingNumber(_pendingReceiveTeam, _pendingReceiveRole.Value, int.Parse(m.Groups[2].Value));
            }
        }
        else if ((m = SetRegex.Match(line)).Success)
        {
            string team = m.Groups[1].Value;
            _lastAttackingTeam = team;
            SetFloatingNumber(team, PlayerRole.Setter, int.Parse(m.Groups[2].Value));
            if (team != _teamAName)
            {
                // Team A's flight to the setter was already kicked off the moment the
                // SetCardRequest went live (see Update()) -- by the time this line
                // exists, the human has already answered. The AI team has no such
                // request, so this narrative line remains its only trigger, and it
                // never pauses (the AI decides instantly).
                CutToPhaseCameraIfIdle("Set");
                _lineHadRealWait = true;
                yield return MoveBallToWhenReady(team, PlayerRole.Setter);
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
                SetFloatingNumber(team, role, int.Parse(m.Groups[3].Value));
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
            if (Enum.TryParse(m.Groups[3].Value, ignoreCase: true, out PlayerRole role))
            {
                _lineHadRealWait = true;
                yield return MoveBallToWhenReady(team, role);
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
            if (_lastAttackingTeam != null && PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out PlayerRole role))
            {
                string defendingTeam = _lastAttackingTeam == _teamAName ? _teamBName : _teamAName;
                SetFloatingNumber(defendingTeam, role, int.Parse(m.Groups[2].Value));
            }
        }
        else if (FreeBallRegex.Match(line).Success)
        {
            // A broken-dig recovery sends a mandatory free ball across the net without
            // ever narrating a "Dig:" line -- still a real net crossing, so it clears
            // the same way. No specific card value is tied to the crossing itself; the
            // recovering team's own Set/Attack lines will add their numbers next.
            ClearFloatingNumbers();
            CutToPhaseCameraIfIdle("Receive"); // a free ball is just another way the ball arrives at a back-row receiver
        }
        else if ((m = ResolveRegex.Match(line)).Success)
        {
            // Ball movement to the hitter is now handled by Swing: above, which fires
            // much earlier -- this just keeps the lane/attack-value bookkeeping
            // GetCurrentDefenderRole() (dig/chase highlighting) still needs.
            _lastLane = int.Parse(m.Groups[2].Value);
            _lastAttackCardValue = int.Parse(m.Groups[4].Value);
        }
        else if ((m = DigRegex.Match(line)).Success && _lastLane >= 0)
        {
            // The narrative's "Dig:" line doesn't name a role directly -- Core's
            // GetDigDefenderRole is the same public lookup Rally itself uses, so
            // we recompute it here from the last resolved lane/attack value
            // rather than re-parsing something the engine never printed.
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(_lastLane, _lastAttackCardValue);
            ClearFloatingNumbers(); // the ball crosses the net into this dig
            SetFloatingNumber(m.Groups[1].Value, defenderRole, int.Parse(m.Groups[2].Value));
            CutToPhaseCameraIfIdle("Dig");
            _lineHadRealWait = true;
            yield return MoveBallToWhenReady(m.Groups[1].Value, defenderRole);
        }
    }

    /// <summary>
    /// Starts the receiver->setter flight for team A's SetCardRequest, but only once any
    /// flight already in progress (the serve->receiver leg's resumed tail end, which may
    /// still be physically finishing when this request goes live) has genuinely
    /// completed -- FlyTo isn't reentrant-safe, since a second call while one is still
    /// running would overwrite its in-progress state on the same Rigidbody.
    /// </summary>
    private IEnumerator MoveBallToSetterWhenReady()
    {
        yield return MoveBallToWhenReady(_teamAName, PlayerRole.Setter, pauseAtFraction: 0.5f,
            stillPending: () => _activeRequest is SetCardRequest);
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
    /// stillPending guards a second, subtler race this wait itself can introduce for a
    /// PAUSING flight: ResolveActiveRequest's Resume() call is meant for whichever flight
    /// is *currently* held -- but if the human answers fast enough that this flight
    /// hasn't actually started yet (still blocked on the wait above), Resume() fires on
    /// the OLD flight instance instead, and this one starts moments later with a freshly
    /// reset (false) _resumeRequested that nothing will ever set again, holding forever.
    /// Confirmed live. If the decision this pause exists to wait for has already been
    /// answered by the time the wait clears, there's nothing left to wait for -- skip the
    /// pause entirely rather than hold on a signal that already came and went.
    /// </summary>
    private IEnumerator MoveBallToWhenReady(string teamName, PlayerRole role, float pauseAtFraction = -1f, Func<bool> stillPending = null)
    {
        if (_ballFlight != null)
        {
            yield return new WaitUntil(() => !_ballFlight.IsInFlight);
        }
        if (pauseAtFraction > 0f && stillPending != null && !stillPending())
        {
            pauseAtFraction = -1f;
        }
        yield return MoveBallTo(teamName, role, pauseAtFraction);
    }

    private IEnumerator MoveBallTo(string teamName, PlayerRole role, float pauseAtFraction = -1f)
    {
        if (_ball == null
            || _ballFlight == null
            || !_teamPositions.TryGetValue(teamName, out var positions)
            || !positions.TryGetValue(role, out var target))
        {
            yield break;
        }

        // No camera switch here -- fixed cameras only now (no more Follow Camera, see
        // PlayServeToss/HandleLine for the same decision). Whichever camera is already
        // showing (set by ApplyPhaseCameraForRequest or CutToPhaseCameraIfIdle) is
        // expected to already frame wherever this flight travels; that's the whole
        // point of auditing each camera's coverage rather than chasing the ball.
        Vector3 dest = target.position + Vector3.up * ballHeight;
        yield return _ballFlight.FlyTo(dest, pauseAtFraction: pauseAtFraction);
    }

    /// <summary>
    /// The pre-serve toss: relocates the server to a service area behind their own back
    /// row, tosses the ball up from near their head and lets it fall back to the normal
    /// contact height while they hop to meet it, then restores the server's position --
    /// the ball is deliberately left at the service position, not restored, so the
    /// unchanged serve arc that follows this (in HandleLine's Serve branch) realistically
    /// originates from behind the backline. Applies to every serve, both teams.
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

        Vector3 normalPos = setterT.position;
        Vector3 servicePos = setterT.parent.TransformPoint(new Vector3(0f, 0f, serviceAreaDepth));
        setterT.position = servicePos;
        _ball.position = servicePos + Vector3.up * ballHeight;

        Coroutine hop = StartCoroutine(HopTransform(setterT, serveJumpHeight, _ballFlight.TossDuration(serveTossPeakOffset)));
        yield return _ballFlight.TossVertical(serveTossPeakOffset);
        // HopTransform is sized to roughly the same duration as the toss, but its own
        // real-time timer can finish a frame or two after TossVertical's physics-based
        // termination returns here -- if this restore ran first, HopTransform's own final
        // "snap back to where it started" line (the *service* position, not normalPos)
        // would fire right after and silently clobber it, leaving the setter stuck at the
        // service spot forever. Waiting for the hop to fully finish first guarantees
        // normalPos is the last write, no matter which one's timer actually runs longer.
        yield return hop;

        // Nothing reads the server's position during this window -- the next thing that
        // could is the following rally's Set leg, well after -- so it's safe to snap
        // straight back rather than ease out.
        setterT.position = normalPos;
        _tossingTeams.Remove(serverTeam);
    }

    /// <summary>Eases a transform's Y position up and back down over duration, ending
    /// exactly where it started. No animator/rig needed -- a simple procedural hop.</summary>
    private IEnumerator HopTransform(Transform t, float height, float duration)
    {
        if (duration <= 0f)
        {
            yield break;
        }
        Vector3 basePos = t.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float phase = Mathf.Clamp01(elapsed / duration);
            float y = Mathf.Sin(phase * Mathf.PI) * height;
            t.position = basePos + Vector3.up * y;
            yield return null;
        }
        t.position = basePos;
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
