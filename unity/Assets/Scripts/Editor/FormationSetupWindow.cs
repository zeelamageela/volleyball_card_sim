using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VolleyballCore;

/// <summary>
/// Authoring tool for GameRunner's per-phase (and, for Attack, per-tempo) team
/// formations. The actual position data lives as ordinary child Transforms under each
/// team root -- Players/Formations/{phase}[/{tempo}]/{role} -- so placing a player is
/// just dragging its anchor with Unity's stock Move tool in the Scene view. This window
/// only helps generate/navigate/preview that hierarchy; it holds no position data of
/// its own.
///
/// Phase and tempo names here (Receive/Set/Attack/Dig, Quick/Mid/High) must match
/// GameRunner.cs's own naming exactly (GetFormationPosition, GetTempoLabel) -- there's
/// no shared source of truth between them yet, just parallel string literals.
/// </summary>
public sealed class FormationSetupWindow : EditorWindow
{
    private static readonly string[] Teams = { "Players", "AIPlayers" };
    // AttackPrep sits between Set and Attack: an authored "approach" waypoint a hitter
    // eases toward the moment their own team sets (GameRunner's SetRegex), well before
    // the specific attack lane is even chosen, rather than sitting still at their Set
    // spot until the real Attack-phase anchor snaps them into their final swing
    // position. Per-tempo, same as Attack -- see IsPerTempo/PathFor.
    private static readonly string[] Phases = { "Serve", "Receive", "Set", "AttackPrep", "Attack", "Dig" };
    private static readonly string[] Tempos = { "Quick", "Mid", "High" };
    private static readonly PlayerRole[] Roles = (PlayerRole[])System.Enum.GetValues(typeof(PlayerRole));
    // Mirrors GridPlayer.CanAttack()'s Setter/Libero exclusion -- expressed locally
    // since this tool has no GridPlayer instance to call it on.
    private static readonly PlayerRole[] AttackerRoles = { PlayerRole.Opp, PlayerRole.Mb, PlayerRole.Oh, PlayerRole.Ds };

    private int _teamIndex;
    private int _phaseIndex;
    private int _tempoIndex;
    private int _copyPhaseIndex;
    private int _copyTempoIndex;

    private bool _previewing;
    private Dictionary<Transform, Vector3> _preservedLivePositions;

    [MenuItem("Window/Formation Setup")]
    private static void Open() => GetWindow<FormationSetupWindow>("Formation Setup");

    private void OnEnable()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        SceneView.duringSceneGui -= OnSceneGUI;
        StopPreview(); // don't leave the court mid-preview if the window closes
    }

    /// <summary>
    /// Confirmed live the hard way: a preview left on during a scene save bakes the
    /// previewed positions in as the live players' new "permanent" spot -- which then
    /// becomes their base/fallback position (GameRunner snapshots it at play-mode
    /// start) and silently corrupts every un-authored phase/role until someone notices
    /// something's off. Force the restore before any save can commit, regardless of
    /// whether this window even has focus at the time.
    /// </summary>
    private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => StopPreview();

    private bool IsAttack => Phases[_phaseIndex] == "Attack";
    // AttackPrep is per-tempo too (a Quick-set approach looks different from a High-set
    // one) but, unlike Attack, is never itself a ball-flight destination -- no
    // FormationAnchorHeight override belongs on it (see GenerateAnchors) and no
    // trajectory-preview arc is drawn for it (see OnSceneGUI's own IsAttack-only gate).
    private bool IsPerTempo => Phases[_phaseIndex] is "Attack" or "AttackPrep";

    private string PathFor(int phaseIndex, int tempoIndex) =>
        Phases[phaseIndex] is "Attack" or "AttackPrep" ? $"Formations/{Phases[phaseIndex]}/{Tempos[tempoIndex]}" : $"Formations/{Phases[phaseIndex]}";

    private string CurrentPath => PathFor(_phaseIndex, _tempoIndex);

    private Transform TeamRoot => GameObject.Find(Teams[_teamIndex])?.transform;

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        int newTeam = EditorGUILayout.Popup("Team", _teamIndex, Teams);
        int newPhase = EditorGUILayout.Popup("Phase", _phaseIndex, Phases);
        using (new EditorGUI.DisabledScope(!IsPerTempo))
        {
            _tempoIndex = EditorGUILayout.Popup("Tempo", _tempoIndex, Tempos);
        }
        if (newTeam != _teamIndex || newPhase != _phaseIndex)
        {
            _teamIndex = newTeam;
            _phaseIndex = newPhase;
            if (_previewing) StopPreview(); // switching target mid-preview would silently leave the old target's live capsules stuck
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Missing Anchors")) GenerateAnchors();
            if (GUILayout.Button("Select && Frame")) SelectAndFrame();
        }

        EditorGUILayout.Space();
        bool newPreviewing = EditorGUILayout.ToggleLeft("Preview on court (moves the real players)", _previewing);
        if (newPreviewing != _previewing)
        {
            if (newPreviewing) StartPreview(); else StopPreview();
        }
        if (_previewing)
        {
            EditorGUILayout.HelpBox(
                "Previewing -- the real player capsules are temporarily parked at this formation's anchors. " +
                "Saving the scene now auto-restores them first, but if you're dragging things around, turn " +
                "preview off before you're done so you don't lose track of which is which.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Copy positions from another set", EditorStyles.boldLabel);
        _copyPhaseIndex = EditorGUILayout.Popup("Source phase", _copyPhaseIndex, Phases);
        using (new EditorGUI.DisabledScope(Phases[_copyPhaseIndex] is not ("Attack" or "AttackPrep")))
        {
            _copyTempoIndex = EditorGUILayout.Popup("Source tempo", _copyTempoIndex, Tempos);
        }
        if (GUILayout.Button("Copy Into Current Selection"))
        {
            CopyFormation(PathFor(_copyPhaseIndex, _copyTempoIndex), CurrentPath);
        }

        EditorGUILayout.Space();
        string otherTeam = Teams[_teamIndex == 0 ? 1 : 0];
        EditorGUILayout.LabelField("Mirror from the other team", EditorStyles.boldLabel);
        if (GUILayout.Button($"Mirror ALL Formations from {otherTeam} → {Teams[_teamIndex]}"))
        {
            MirrorAllFromOtherTeam();
        }
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing;
        }
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Generate Formation Group");
        go.transform.SetParent(parent, worldPositionStays: false);
        return go.transform;
    }

    private static Transform FindOrCreateGroup(Transform root, string path)
    {
        Transform current = root;
        foreach (string segment in path.Split('/'))
        {
            current = FindOrCreateChild(current, segment);
        }
        return current;
    }

    /// <summary>
    /// Maps each live player capsule (direct child of the team root, named after its
    /// role) to its PlayerRole -- mirrors GameRunner.FindTeamPositions' own
    /// case-insensitive Enum.TryParse exactly, since the actual scene names ("OPP",
    /// "MB", "OH", "DS", "Setter", "Libero") don't all match PlayerRole.ToString()'s
    /// casing ("Opp", "Mb", "Oh", "Ds"...). A plain root.Find(role.ToString()) silently
    /// returns null for 4 of 6 roles and was the source of a real bug here (everything
    /// but Setter/Libero collapsing to the team root's own position).
    /// </summary>
    private static Dictionary<PlayerRole, Transform> GetLiveRoleTransforms(Transform root)
    {
        var map = new Dictionary<PlayerRole, Transform>();
        foreach (Transform child in root)
        {
            if (System.Enum.TryParse(child.name, ignoreCase: true, out PlayerRole role))
            {
                map[role] = child;
            }
        }
        return map;
    }

    private void GenerateAnchors()
    {
        Transform root = TeamRoot;
        if (root == null)
        {
            Debug.LogWarning($"Formation Setup: no '{Teams[_teamIndex]}' GameObject in the open scene.");
            return;
        }

        Transform group = FindOrCreateGroup(root, CurrentPath);
        var liveRoles = GetLiveRoleTransforms(root);
        foreach (PlayerRole role in Roles)
        {
            if (group.Find(role.ToString()) != null)
            {
                continue; // already authored -- never overwrite an existing anchor
            }
            Vector3 seedPos = liveRoles.TryGetValue(role, out Transform liveRole) ? liveRole.position : root.position;
            var anchor = new GameObject(role.ToString());
            Undo.RegisterCreatedObjectUndo(anchor, "Generate Formation Anchor");
            anchor.transform.SetParent(group, worldPositionStays: false);
            anchor.transform.position = seedPos;
            if (IsAttack)
            {
                // -1 = not overridden yet -- GameRunner falls back to BallFlight's own
                // default until this is explicitly raised/lowered on the anchor itself.
                anchor.AddComponent<FormationAnchorHeight>();
            }
        }
    }

    private void SelectAndFrame()
    {
        Transform root = TeamRoot;
        Transform group = root != null ? root.Find(CurrentPath) : null;
        if (group == null)
        {
            Debug.LogWarning("Formation Setup: nothing generated yet for this selection -- click Generate Missing Anchors first.");
            return;
        }
        Selection.objects = group.Cast<Transform>().Select(t => (Object)t.gameObject).ToArray();
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private void StartPreview()
    {
        Transform root = TeamRoot;
        Transform group = root != null ? root.Find(CurrentPath) : null;
        if (group == null)
        {
            Debug.LogWarning("Formation Setup: nothing generated yet for this selection -- click Generate Missing Anchors first.");
            return;
        }

        var liveRoles = GetLiveRoleTransforms(root);
        _preservedLivePositions = new Dictionary<Transform, Vector3>();
        foreach (PlayerRole role in Roles)
        {
            Transform anchor = group.Find(role.ToString());
            if (!liveRoles.TryGetValue(role, out Transform liveRole) || anchor == null)
            {
                continue;
            }
            _preservedLivePositions[liveRole] = liveRole.position;
            liveRole.position = anchor.position;
        }
        _previewing = true;
    }

    private void StopPreview()
    {
        if (_preservedLivePositions != null)
        {
            foreach (var kv in _preservedLivePositions)
            {
                if (kv.Key != null)
                {
                    kv.Key.position = kv.Value;
                }
            }
        }
        _preservedLivePositions = null;
        _previewing = false;
    }

    private void CopyFormation(string sourcePath, string destPath)
    {
        Transform root = TeamRoot;
        if (root == null)
        {
            Debug.LogWarning($"Formation Setup: no '{Teams[_teamIndex]}' GameObject in the open scene.");
            return;
        }
        Transform source = root.Find(sourcePath);
        if (source == null)
        {
            Debug.LogWarning($"Formation Setup: source set '{sourcePath}' has no anchors yet.");
            return;
        }
        Transform dest = FindOrCreateGroup(root, destPath);
        foreach (PlayerRole role in Roles)
        {
            Transform sourceAnchor = source.Find(role.ToString());
            if (sourceAnchor == null)
            {
                continue;
            }
            Transform destAnchor = FindOrCreateChild(dest, role.ToString());
            Undo.RecordObject(destAnchor, "Copy Formation");
            destAnchor.position = sourceAnchor.position;
        }
    }

    /// <summary>
    /// Mirrors every authored phase/tempo/role anchor from the other team onto the
    /// currently selected team. A plain position copy would be wrong here -- AIPlayers
    /// sits rotated 180 degrees on Y from Players (confirmed live), so this round-trips
    /// each anchor through the SOURCE team's local space and back out through the DEST
    /// team's, the same TransformPoint/InverseTransformPoint pattern GameRunner's own
    /// GetFormationPosition and PlayServeToss already rely on for exactly this reason.
    /// </summary>
    private void MirrorAllFromOtherTeam()
    {
        string sourceTeamName = Teams[_teamIndex == 0 ? 1 : 0];
        Transform sourceRoot = GameObject.Find(sourceTeamName)?.transform;
        Transform destRoot = TeamRoot;
        if (sourceRoot == null || destRoot == null)
        {
            Debug.LogWarning("Formation Setup: couldn't find both teams in the open scene.");
            return;
        }

        foreach (string phase in Phases)
        {
            if (phase is "Attack" or "AttackPrep")
            {
                foreach (string tempo in Tempos)
                {
                    MirrorSet(sourceRoot, destRoot, $"Formations/{phase}/{tempo}");
                }
            }
            else
            {
                MirrorSet(sourceRoot, destRoot, $"Formations/{phase}");
            }
        }
        Debug.Log($"Formation Setup: mirrored all formations from {sourceTeamName} onto {Teams[_teamIndex]}.");
    }

    private static void MirrorSet(Transform sourceRoot, Transform destRoot, string path)
    {
        Transform sourceGroup = sourceRoot.Find(path);
        if (sourceGroup == null)
        {
            return; // nothing authored for this set on the source team -- leave the dest set alone
        }
        Transform destGroup = FindOrCreateGroup(destRoot, path);
        foreach (PlayerRole role in Roles)
        {
            Transform sourceAnchor = sourceGroup.Find(role.ToString());
            if (sourceAnchor == null)
            {
                continue;
            }
            Vector3 localOffset = sourceRoot.InverseTransformPoint(sourceAnchor.position);
            Vector3 mirroredWorldPos = destRoot.TransformPoint(localOffset);
            Transform destAnchor = FindOrCreateChild(destGroup, role.ToString());
            Undo.RecordObject(destAnchor, "Mirror Formation");
            destAnchor.position = mirroredWorldPos;
        }
    }

    /// <summary>
    /// Draws the planned Set-Setter -> Attack-hitter arc for the current team/tempo
    /// selection over the actual court, so it's obvious while placing anchors whether a
    /// hitter's spot is a sensible landing point for a real set from here -- not just a
    /// number. Reuses fts.ComputeArcPreviewPoints (the same analytic solve
    /// BallFlight.FlyTo itself uses) and the scene's real BallFlight peak
    /// height/lateral speed, so this always matches what the runtime trajectory preview
    /// (GameRunner's ShowTrajectoryPreview) would actually show for the same anchors.
    /// </summary>
    private void OnSceneGUI(SceneView view)
    {
        if (Phases[_phaseIndex] != "Attack")
        {
            return;
        }
        Transform root = TeamRoot;
        var ballFlight = Object.FindFirstObjectByType<BallFlight>();
        var runner = Object.FindFirstObjectByType<GameRunner>();
        if (root == null || ballFlight == null || runner == null)
        {
            return;
        }
        Transform setAnchor = root.Find($"Formations/Set/{PlayerRole.Setter}");
        Transform attackGroup = root.Find(CurrentPath);
        if (setAnchor == null || attackGroup == null)
        {
            return;
        }
        // Same backward-offset + contact-height treatment as GameRunner's own
        // GetSetContactPoint, read straight from the runtime component so this preview
        // can never drift from what the real flight actually does.
        Vector3 setContactPoint = setAnchor.position + root.TransformDirection(new Vector3(0f, 0f, runner.SetContactBackOffset))
            + Vector3.up * runner.SetContactHeight;

        Handles.color = new Color(1f, 0.85f, 0f, 0.85f);
        foreach (PlayerRole role in AttackerRoles)
        {
            Transform hitterAnchor = attackGroup.Find(role.ToString());
            if (hitterAnchor == null)
            {
                continue;
            }
            // Same fallback shape as GameRunner.GetFormationPeakHeight -- an authored
            // override on this specific anchor wins, else the scene's global default.
            float peakHeight = ballFlight.DefaultPeakHeight;
            if (hitterAnchor.TryGetComponent(out FormationAnchorHeight heightOverride) && heightOverride.peakHeight >= 0f)
            {
                peakHeight = heightOverride.peakHeight;
            }
            // Same backward-offset + contact-height treatment as GameRunner's own
            // GetAttackContactPoint, read straight from the runtime component so this
            // preview can never drift from what the real flight actually does.
            Vector3 contactPoint = hitterAnchor.position + root.TransformDirection(new Vector3(0f, 0f, runner.AttackContactBackOffset))
                + Vector3.up * runner.AttackContactHeight;
            Vector3[] points = fts.ComputeArcPreviewPoints(setContactPoint, contactPoint, peakHeight, ballFlight.DefaultLateralSpeed);
            Handles.DrawAAPolyLine(3f, points);
        }
    }
}
