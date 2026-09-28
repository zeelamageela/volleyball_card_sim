# Unity Prototype

A presentation layer (3D court, ball flight, cameras, and — as of this doc — a hook
for 2D sprite animation) over a C# port of the same rules engine `src/` implements in
Python. This is where the game is actually *played*; `src/`/`main.py`/`play.py` remain
the balance-testing and simulation side.

Project lives inside this repo (not split out) so both sides can stay in sync as the
ruleset evolves. The C# rules engine (`Assets/Scripts/Core/`) currently tracks
`src/`'s locked ruleset (see the top-level `CHANGELOG.md`) by hand — there's no shared
source of truth between the two languages yet, just a port kept in sync by eye.

## Opening it

Unity `6000.4.5f1`, URP. Open `unity/` as a project (Unity Hub, or `unity open` via
the CLI at `/Users/zill/.unity/bin/unity` — see the `unity-cli` skill for the full
command surface, including driving a live connected Editor headlessly).

## Layout

- `Assets/Scripts/Core/` — the rules engine (`VolleyballCore.asmdef`, `noEngineReferences:
  true` — zero UnityEngine dependency, so it's provably safe to run off the main thread).
  `Rally.cs` plays one rally; `Game.cs`/`GameRunner.cs`'s own `RunGame` play rallies
  back-to-back. `IStrategy` is the one seam between rules and decision-making —
  `SmartStrategy`/`RandomStrategy`/`DummyStrategy` are AI implementations, `HumanStrategy`
  (in `Assets/Scripts/`, *not* Core — it posts to a channel and blocks, which needs to
  stay possible without polluting Core with UI concerns) is the human's.
- `Assets/Scripts/Data/` (`VolleyballData.asmdef`) — CSV loading, shared with `src/`'s
  own `data/*.csv` files by relative path (the reason this project lives inside the repo).
- `Assets/Scripts/GameRunner.cs` — the presentation layer. Big file; see below.
- `Assets/Scripts/Editor/FormationSetupWindow.cs` — authoring tool for the
  `Formations/{phase}[/{tempo}]/{role}` anchor hierarchy every player eases between.
  `Window > Formation Setup`.
- `Assets/Scripts/Tests/EditMode/` — 157 NUnit tests (`unity test` headlessly, or
  `unity cmd run_tests --mode EditMode` against a live Editor — but always
  `editor_stop` first: running the suite mid-Play-mode hangs the Editor), covering
  Core's rules and, as of the event-stream work below, the presentation-facing
  event/decision plumbing too.

## Presentation architecture

Core runs a rally on a background thread (`Task.Run`) and reports what happened as a
strictly-ordered stream of typed events (`Assets/Scripts/Core/RallyEvents.cs`) —
`ServeEvent`, `ReceiveEvent`, `SetEvent`, `SwingEvent`, `DigEvent`, and so on, one per
real thing that happens, each carrying the facts presentation needs (the digging role,
the free ball's receiver, whether a set was drawn blind) rather than making
presentation re-derive them. `DecisionRecordingStrategy` wraps the human's strategy and
emits a `DecisionRequestedEvent` into the same stream right before each real decision
call, so a human prompt sits in the exact spot it happens relative to everything else —
one ordered timeline for the whole game, not two separate streams (narrative text +
decision requests) that used to have to be reconciled after the fact.

`GameRunner.RunPresentation` walks that stream on the main thread, one event at a time,
never skipping ahead. Ball flights don't block this loop (they'd deadlock against their
own pause) — they're chained through a small queue (`QueueFlight`/`FlightHandle`) so
each starts only once the previous one has genuinely finished, which is also the
mechanism that gates every "don't move/hold until the ball has really arrived" rule
below.

**The hold rule.** Because Core always runs ahead of presentation up to the human's
next decision, a flight launched while presenting event N can ask "is there a human
decision between N and the next ball-touching event, that hasn't been reached yet?" —
if yes, it holds in flight (slow motion, not a stop) until presentation catches up to
that event; if no, it plays straight through at full speed. One rule
(`ShouldHoldFlight`) replaces what used to be five separate per-decision flags, and is
why an AI-vs-AI rally never pauses while a human decision genuinely does, without
either case being special-cased.

**Nothing sits on someone's head.** Every decision holds the ball mid-flight, never on
a player — `serveReceptionPauseFraction`/`setPauseFraction`/`swingPauseFraction`/
`digPauseFraction`/`chaseRecoveryPauseFraction`/`freeBallDiscardPauseFraction` (all
default 0.55) say how far into that leg it holds; `BallFlight.absoluteMaxHoldFraction`
(0.9) is a hard ceiling on how far a long hold's own slow-motion creep can carry it,
independent of whatever default is chosen.

**A player doesn't move until they've genuinely touched the ball.** E.g. the Setter's
peel-off to defense is the first line of `FlySwing`, the coroutine `QueueFlight` only
starts once the pass into their hands has actually finished — not the moment the
attack lane is narratively decided, which can be well before that.

## Touch cues (2D sprite animation hook)

`PlayerTouchReceiver` (optional, no-op until you attach one) gets an `OnTouch` call the
instant a player genuinely touches the ball — `TouchKind` (Serve/Receive/Set/Attack/
Dig/Deflect/Chase/FreeBallCatch), a success flag, an optional `ShotKind`, and a
world-space point to face/reach toward. Fired by `GameRunner.FireTouchCue`, at the same
physical-touch granularity as the hold rule above (never at the narrative moment).

`PlayerSpriteAnimator` is a reference implementation: it does nothing but set four
Animator parameters from each cue — a Trigger named after the `TouchKind`, an Int
`Direction` (front/back/left/right, bucketed from the cue's facing point against
whichever camera is actually live — see `SpriteFacing`), a Bool `Success`, an Int
`Shot`. Every actual clip/state/transition lives in the Animator Controller you build
against those four; this component only ever calls `Animator.Set*`.

## Cameras

Convention-based, not hardcoded: a camera named `Player {Phase} Camera` (Serve/
Receive/Set/Attack/Block/Dig) is used automatically the moment it exists in the scene,
falling back to `defaultCamera` otherwise (`GameRunner.PhaseCameraNames`).
`GetActiveGameplayCamera` (whichever camera is actually enabled right now) is the one
piece of that any other script — `PlayerSpriteAnimator` included — should read if it
needs to know what the player is looking through; it's `internal`, not `private`, for
exactly that reason.

## Known gaps / open questions

- Presentation-only contact points (an anchor plus a flat scalar offset/height, same
  for every player at a phase) were considered for a dedicated per-anchor authoring
  system, then dropped: that problem (a 3D rig's hand drifting from an authored
  contact point) doesn't apply to billboard sprites, which have no literal hand in 3D
  space to misalign. Worth revisiting only if this ever moves to a skeletal character.
- No "started/stopped moving" signal exists yet for idle-vs-walking sprite poses —
  `PlayerSpriteAnimator` only reacts to ball touches; a player easing between
  formations via `MovePlayerTo`/`ApplyTeamFormation` has no cue of its own.
- Core (C#) and `src/` (Python) are two independent rule implementations kept in sync
  by hand, not by a shared source of truth — a rules change made in one won't
  propagate to the other automatically.
