using System;
using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>
    /// Executes one full rally between a serving and receiving team.
    /// Ported from src/game.py's Rally class.
    ///
    /// Card lifecycle for attack / block phases:
    ///   CommitCard()  -- removes from hand (card is 'in play', not yet discarded)
    ///   RefillHand()  -- draws back up to HandSize
    ///   DiscardMany() -- sends committed cards to the discard pile after resolution
    /// </summary>
    public sealed class Rally
    {
        private static readonly Dictionary<int, string> LaneNames = new()
        {
            { 1, "OH" }, { 2, "MB" }, { 3, "OPP" },
        };

        private readonly Team _srv;
        private readonly Team _rcv;
        private readonly IStrategy _srvStrat;
        private readonly IStrategy _rcvStrat;
        private readonly Random _rng;
        private readonly List<string> _narrative;
        private bool _brokenPlay; // Track if setter dug (broken play for next set)

        public Rally(
            Team servingTeam,
            Team receivingTeam,
            IStrategy servingStrategy,
            IStrategy receivingStrategy,
            Random rng,
            List<string> narrative = null)
        {
            _srv = servingTeam;
            _rcv = receivingTeam;
            _srvStrat = servingStrategy;
            _rcvStrat = receivingStrategy;
            _rng = rng;
            _narrative = narrative;
            _brokenPlay = false;
        }

        private void Narrate(string msg) => _narrative?.Add(msg);

        private static string PositionLabel(AttackPosition position) =>
            position == AttackPosition.Front ? "front" : "back";

        public RallyResult Play()
        {
            int exchange = 0;

            // Replenish hands before rally starts
            _srv.RefillHand();
            _rcv.RefillHand();

            // -- SERVE --
            var (serveCard, target) = PhaseServe();
            int serveValue = serveCard.Value + (_srv.AbilityEngine?.ServeValueBonus() ?? 0);
            Narrate(
                $"\n  Serve:   {_srv.Name} card {serveCard.Value}"
                + (serveValue != serveCard.Value ? $" (eff {serveValue})" : "")
                + $"  →  targeting {target.Role.DisplayName()}"
            );

            // -- RECEIVE --
            Card receiveCard = PhaseReceive(serveValue);
            bool rcvOk = receiveCard.Value >= serveValue;
            Narrate(
                $"  Receive: {_rcv.Name} card {receiveCard.Value}"
                + $"  vs serve {serveValue}"
                + $"  →  {(rcvOk ? "clean pass" : "FAILED — chase needed")}"
            );

            Team attacker, defender;
            IStrategy atkStrat, defStrat;
            if (receiveCard.Value < serveValue)
            {
                // Failed reception -> chase. This is the *only* place chase exists in the
                // whole game now -- a failed dig (of any kind: kill, tip, roll shot,
                // tied-deflection) is always an immediate point, never a chase.
                ChaseResult chase = PhaseChase(_rcv, _rcvStrat, receiveCard.Value, serveCard.Value);
                if (chase.Outcome == ChaseOutcome.Failed)
                {
                    return new RallyResult(
                        _srv.Name,
                        $"Serve ace (serve={serveValue} > receive={receiveCard.Value}), chase failed",
                        0);
                }
                // A successful chase skips the normal attack and crosses back as a
                // mandatory, guaranteed free ball instead -- but it isn't free for the
                // chasing team: they pay for it by discarding one more card on top of
                // whatever was spent chasing itself. Any card is valid (unlike every
                // other hand decision in the game, there's no value threshold here), so
                // this is purely a cost, not a check that can fail.
                _rcv.RefillHand();
                Card discardCard = _rcvStrat.ChooseFreeBallDiscard(_rcv.Hand);
                _rcv.PlayCard(discardCard);
                Narrate($"  Chase:   discard {discardCard.Value} (cost of the free ball)");
                GridPlayer freeBallTarget = _rcvStrat.ChooseFreeBallTarget(_srv.EligibleReceivers());
                Narrate($"  Chase:   SUCCEEDED — mandatory free ball to {_srv.Name} ({freeBallTarget.Role.DisplayName()})");
                attacker = _srv;
                defender = _rcv;
                atkStrat = _srvStrat;
                defStrat = _rcvStrat;
            }
            else
            {
                // Receiving team attacks first on a clean pass.
                attacker = _rcv;
                defender = _srv;
                atkStrat = _rcvStrat;
                defStrat = _srvStrat;
            }

            // -- ATTACK LOOP --
            while (exchange < GameConstants.MaxExchanges)
            {
                exchange++;
                var quickLanes = new List<int>(); // Track quick set lanes for this exchange

                // Consume any pending set delta from a previous dig success
                int setDelta = attacker.AbilityEngine?.ConsumeSetDelta() ?? 0;
                var (setCard, template) = PhaseSet(attacker, atkStrat, setDelta);
                string frontStr = string.Join(" + ", template.FrontLanes.Select(l => LaneNames.GetValueOrDefault(l, l.ToString())));
                string backStr = template.BackLanes.Count > 0
                    ? string.Join(" + ", template.BackLanes.Select(l => LaneNames.GetValueOrDefault(l, l.ToString())))
                    : "none";
                Narrate(
                    $"\n  Set:     {attacker.Name} card {setCard.Value}"
                    + $"  →  front [{frontStr}]  back [{backStr}]"
                    + $"  max {template.MaxAttackers}"
                );

                var attackCards = PhaseHit(attacker, atkStrat, template);
                foreach (var kv in attackCards.OrderBy(kv => kv.Key))
                {
                    foreach (var ac in kv.Value)
                    {
                        if (ac.Blind)
                        {
                            Narrate(
                                $"  Attack:  {attacker.Name} lane {kv.Key} {LaneNames.GetValueOrDefault(kv.Key, "")}"
                                + $"  BLIND DRAW ({PositionLabel(ac.Position)})");
                        }
                        else
                        {
                            Narrate(
                                $"  Attack:  {attacker.Name} lane {kv.Key} {LaneNames.GetValueOrDefault(kv.Key, "")}"
                                + $"  card {ac.Card.Value} ({PositionLabel(ac.Position)})");
                        }
                    }
                }

                // Determine if this is a quick set (1-3) and which lanes are quick.
                // Quickset lanes force a single blind blocker with no stacking --
                // enforced in PhaseBlockCommit.
                bool isQuickSet = setCard.Value <= 3;
                if (isQuickSet)
                {
                    foreach (int lane in template.FrontLanes)
                    {
                        if (attackCards.TryGetValue(lane, out var acs))
                        {
                            foreach (var ac in acs)
                            {
                                if (ac.Position == AttackPosition.Front)
                                {
                                    quickLanes.Add(lane);
                                    break;
                                }
                            }
                        }
                    }
                }

                // Fire on_quick_set if MB lane is among the front lanes
                if (template.FrontLanes.Contains(2) && attacker.AbilityEngine != null)
                {
                    attacker.AbilityEngine.ActivateQuickSet();
                }
                // Notify engine of set card value (for tip_threshold_delta)
                attacker.AbilityEngine?.ActivateTipThreshold(setCard.Value);

                // BLOCK COMMIT (defender places cards blind to lane choice)
                var (blockLayout, blockMax, blockCards) = PhaseBlockCommit(
                    defender, defStrat, attackCards.Keys.ToList(), quickLanes);

                // LANE CHOICE -- strategies pick from the committed attack lanes directly
                // (no card-matching elimination pass; that mechanic was retired).
                var simplifiedAttacks = new Dictionary<int, Card>();
                foreach (var kv in attackCards)
                {
                    int lane = kv.Key;
                    var cardsList = kv.Value;
                    var front = cardsList.Where(ac => ac.Position == AttackPosition.Front).ToList();
                    var back = cardsList.Where(ac => ac.Position == AttackPosition.Back).ToList();
                    var knownFront = front.Where(ac => !ac.Blind).ToList();
                    var knownBack = back.Where(ac => !ac.Blind).ToList();
                    if (knownFront.Count > 0)
                    {
                        simplifiedAttacks[lane] = knownFront[0].Card;
                    }
                    else if (knownBack.Count > 0)
                    {
                        simplifiedAttacks[lane] = knownBack[0].Card;
                    }
                    else if (front.Count > 0)
                    {
                        simplifiedAttacks[lane] = new Card(0, front[0].Card.Color);
                    }
                    else
                    {
                        simplifiedAttacks[lane] = new Card(0, back[0].Card.Color);
                    }
                }

                int attackLane = atkStrat.ChooseAttackLane(simplifiedAttacks, blockLayout);

                // Resolve role from chosen lane before any role-gated ability checks.
                PlayerRole? attackerRole = PlayerRoleExtensions.LaneToRole.TryGetValue(attackLane, out var roleForLane)
                    ? roleForLane : (PlayerRole?)null;

                // SLIDE_LANES ability - can shift to adjacent lane with lower block
                if (attacker.AbilityEngine != null && attackerRole.HasValue)
                {
                    var cardsOnCurrent = attackCards[attackLane];
                    var front = cardsOnCurrent.Where(ac => ac.Position == AttackPosition.Front).ToList();
                    var back = cardsOnCurrent.Where(ac => ac.Position == AttackPosition.Back).ToList();
                    Card currentCard = front.Count > 0 ? front[0].Card : back[0].Card;

                    if (attacker.AbilityEngine.SlideLanes(attackerRole.Value, currentCard.Value))
                    {
                        var adjacentLanes = new List<int>();
                        if (attackLane > 1 && attackCards.ContainsKey(attackLane - 1))
                        {
                            adjacentLanes.Add(attackLane - 1);
                        }
                        if (attackLane < 3 && attackCards.ContainsKey(attackLane + 1))
                        {
                            adjacentLanes.Add(attackLane + 1);
                        }

                        if (adjacentLanes.Count > 0)
                        {
                            int currentBlock = blockLayout.GetValueOrDefault(attackLane, 0);
                            int bestAdjacent = adjacentLanes.OrderBy(l => blockLayout.GetValueOrDefault(l, 0)).First();
                            if (blockLayout.GetValueOrDefault(bestAdjacent, 0) < currentBlock)
                            {
                                attackLane = bestAdjacent;
                                attackerRole = PlayerRoleExtensions.LaneToRole.TryGetValue(attackLane, out var r2)
                                    ? r2 : (PlayerRole?)null;
                            }
                        }
                    }
                }

                // Narrated as soon as the final (post-SlideLanes) lane/role is known --
                // well before Reveal:/Resolve: below, which only fire once tip-or-hit and
                // block/dig resolution are already done. Presentation (Unity's GameRunner)
                // uses this line to start moving the ball to the hitter right away instead
                // of waiting until Resolve:, for both teams (the AI has no decision request
                // to hook, so a narrative line is the only timing signal available for it).
                if (attackerRole.HasValue)
                {
                    Narrate($"  Swing:   {attacker.Name} lane {attackLane} {attackerRole.Value.DisplayName()}");
                }

                // Reveal any blind-drawn cards on the committed lane
                if (attackCards.TryGetValue(attackLane, out var revealCards))
                {
                    foreach (var bac in revealCards)
                    {
                        if (bac.Blind)
                        {
                            Narrate($"  Reveal:  lane {attackLane} blind draw  →  card {bac.Card.Value} ({PositionLabel(bac.Position)})");
                        }
                    }
                }

                // Declared resolve order for the chosen lane: front-row card first,
                // then back-row card (a "combo" when both are present).
                var cardsOnLane = attackCards[attackLane];
                var frontAttacks = cardsOnLane.Where(ac => ac.Position == AttackPosition.Front).ToList();
                var backAttacks = cardsOnLane.Where(ac => ac.Position == AttackPosition.Back).ToList();
                var resolveOrder = new List<AttackCard>();
                resolveOrder.AddRange(frontAttacks);
                resolveOrder.AddRange(backAttacks);
                if (resolveOrder.Count == 2)
                {
                    Narrate(
                        $"  Combo:   lane {attackLane} — resolve order "
                        + $"{resolveOrder[0].Card.Value} ({PositionLabel(resolveOrder[0].Position)}) then "
                        + $"{resolveOrder[1].Card.Value} ({PositionLabel(resolveOrder[1].Position)})");
                }

                // Apply attacker abilities that are decided once per exchange
                // (attack bonus, pierce block, quick-set MB bonus, hold card).
                int mbQsBonus = attacker.AbilityEngine?.ConsumeMbAttackBonus() ?? 0;
                attacker.AbilityEngine?.SetHandSize(attacker.Hand.Count);

                // Hold card check: retain the first card in resolve order if the
                // ability fires (mirrors the pre-combo single-card behavior).
                // NOTE: uses reference identity on the AttackCard wrapper (not value
                // equality on Card) so that a duplicate-valued card elsewhere in this
                // exchange's attack cards is not also excluded from discard.
                var allAttackCardWrappers = attackCards.Values.SelectMany(list => list).ToList();
                AttackCard holdCandidateWrapper = resolveOrder[0];
                var cardsToDiscardWrappers = allAttackCardWrappers;
                if (attacker.AbilityEngine != null && attackerRole.HasValue
                    && attacker.HeldCard == null
                    && attacker.AbilityEngine.HoldCardCheck(attackerRole.Value, holdCandidateWrapper.Card.Value))
                {
                    attacker.HeldCard = holdCandidateWrapper.Card;
                    cardsToDiscardWrappers = allAttackCardWrappers
                        .Where(ac => !ReferenceEquals(ac, holdCandidateWrapper))
                        .ToList();
                }

                // Committed attack cards discarded; attacker refills (ball went over net)
                attacker.DiscardMany(cardsToDiscardWrappers.Select(ac => ac.Card));
                attacker.RefillHand();

                // Resolve the lane's card(s) in declared order. A genuinely stuffed
                // non-final card removes the lane's single highest blocker card
                // before the next card resolves against what remains.
                var laneBlockCards = new List<Card>(blockCards.GetValueOrDefault(attackLane, new List<Card>()));
                for (int idx = 0; idx < resolveOrder.Count; idx++)
                {
                    var ac = resolveOrder[idx];
                    bool isLastCard = idx == resolveOrder.Count - 1;
                    CardOutcome outcome = ResolveCard(
                        attacker, defender, atkStrat, defStrat, exchange,
                        attackLane, attackerRole, ac, laneBlockCards, mbQsBonus, isLastCard);

                    if (outcome.Result != null)
                    {
                        return outcome.Result;
                    }
                    if (outcome.Stuffed)
                    {
                        if (laneBlockCards.Count > 0)
                        {
                            Card highest = laneBlockCards.OrderByDescending(c => c.Value).First();
                            laneBlockCards.Remove(highest);
                            Narrate(
                                $"  Combo:   lane {attackLane} card {idx + 1} stuffed"
                                + $"  →  highest blocker ({highest.Value}) removed,"
                                + " second card resolves");
                        }
                        continue;
                    }
                    attacker = outcome.Attacker;
                    defender = outcome.Defender;
                    atkStrat = outcome.AtkStrat;
                    defStrat = outcome.DefStrat;
                    break;
                }
            }

            // Safety: rally hit the exchange cap
            return new RallyResult(
                _rng.Choice(new[] { _srv.Name, _rcv.Name }),
                "Rally limit reached",
                exchange);
        }

        // -- Single-card resolution --

        private CardOutcome ResolveCard(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange,
            int attackLane, PlayerRole? attackerRole, AttackCard ac, List<Card> laneBlockCards,
            int mbQsBonus, bool isLastCard)
        {
            Card attackCard = ac.Card;
            bool isBackRowAttack = ac.IsBackRow();
            int laneBlockValue = laneBlockCards.Sum(c => c.Value);
            int laneBlockMax = laneBlockCards.Count > 0 ? laneBlockCards.Max(c => c.Value) : 0;
            bool isDoubleBlocked = laneBlockCards.Count >= 2;

            int effectiveAttack = attackCard.Value;
            int effectiveBlock = laneBlockValue;

            if (attacker.AbilityEngine != null && attackerRole.HasValue)
            {
                var role = attackerRole.Value;
                var engine = attacker.AbilityEngine;
                effectiveAttack += engine.AttackValueBonus(role, attackCard.Value);
                effectiveAttack += engine.OverBlockBonus(role, attackCard.Value, isDoubleBlocked);

                // FORCE_HIGH_BLOCK - filter out low-value blocks
                int forceThreshold = engine.ForceHighBlockThreshold(role, attackCard.Value);
                if (forceThreshold > 0)
                {
                    var highBlocks = laneBlockCards.Where(c => c.Value > forceThreshold).Select(c => c.Value).ToList();
                    effectiveBlock = highBlocks.Sum();
                    laneBlockMax = highBlocks.Count > 0 ? highBlocks.Max() : 0;
                }

                // Pierce block checks (front or back-row)
                if (engine.PierceBlock(role, attackCard.Value))
                {
                    effectiveBlock = 0;
                }
                else if (isBackRowAttack && engine.BackRowPierce(role, attackCard.Value))
                {
                    effectiveBlock = 0;
                }
                // Passive ability: Back Court Threat (Medium team)
                else if (isBackRowAttack && attacker.PassiveAbility == "Back Court Threat")
                {
                    if (laneBlockCards.Count > 1)
                    {
                        var sortedBlocks = laneBlockCards.Select(c => c.Value).OrderBy(v => v).ToList();
                        effectiveBlock = sortedBlocks.Skip(1).Sum();
                        Narrate(
                            $"  Passive: Back Court Threat ignores first blocker "
                            + $"({sortedBlocks[0]}), effective block = {effectiveBlock}");
                    }
                    else
                    {
                        effectiveBlock = 0;
                        if (laneBlockCards.Count > 0)
                        {
                            Narrate($"  Passive: Back Court Threat ignores single blocker ({laneBlockCards[0].Value})");
                        }
                    }
                }
                else if (engine.MinBlockerOnly(role, attackCard.Value))
                {
                    effectiveBlock = laneBlockCards.Count > 0 ? laneBlockCards.Min(c => c.Value) : 0;
                }
                else if (engine.SingleBlockOnly(role, attackCard.Value))
                {
                    effectiveBlock = laneBlockMax;
                }
            }

            // MB quick-set bonus only applies when the resolving card is the
            // front-row MB attacker in lane 2.
            if (attackLane == 2 && ac.Position == AttackPosition.Front)
            {
                effectiveAttack += mbQsBonus;
            }

            string atkEff = effectiveAttack != attackCard.Value ? $"{attackCard.Value}→{effectiveAttack}" : effectiveAttack.ToString();
            string blkEff = effectiveBlock != laneBlockValue ? $"{laneBlockValue}→{effectiveBlock}" : effectiveBlock.ToString();
            Narrate(
                $"  Resolve: {attacker.Name} lane {attackLane} {LaneNames.GetValueOrDefault(attackLane, "")}"
                + $"  atk {atkEff}  vs  blk {blkEff}");

            int tipThreshold = GameConstants.TipThreshold;
            if (attacker.AbilityEngine != null)
            {
                tipThreshold += attacker.AbilityEngine.ConsumeTipThresholdDelta();
            }

            // Wipe off the block: card=1 ability, block present -> instant point
            if (laneBlockValue > 0 && attacker.AbilityEngine != null && attackerRole.HasValue
                && attacker.AbilityEngine.WipeBlock(attackerRole.Value, attackCard.Value))
            {
                return CardOutcome.Ended(new RallyResult(
                    attacker.Name,
                    $"Wipe off the block (card={attackCard.Value}, block={laneBlockValue})",
                    exchange));
            }

            // Special shot types override tip/hit (checked in priority order)
            string shot = "hit";
            if (attacker.AbilityEngine != null && attackerRole.HasValue)
            {
                var role = attackerRole.Value;
                var engine = attacker.AbilityEngine;
                if (engine.RollShot(role, attackCard.Value))
                {
                    shot = "roll";
                    effectiveBlock = 0; // roll shot goes over block
                }
                else if (engine.HeavySpin(role, attackCard.Value))
                {
                    shot = "heavy_spin";
                    effectiveBlock = 0; // heavy spin bypasses the block
                }
                else if (engine.SeamShot(role, attackCard.Value))
                {
                    shot = "seam";
                }
            }

            // Back-row attacks cannot tip
            if (shot == "hit" && effectiveAttack <= tipThreshold && !isBackRowAttack)
            {
                shot = atkStrat.ChooseTipOrHit(effectiveAttack, effectiveBlock);
            }

            Narrate($"  Shot:    {shot.ToUpperInvariant()}");

            return shot switch
            {
                "tip" => ResolveTip(
                    attacker, defender, atkStrat, defStrat, exchange,
                    attackLane, attackerRole, attackCard, effectiveAttack, laneBlockCards, isLastCard),
                "roll" => ResolveRollShot(
                    attacker, defender, atkStrat, defStrat, exchange, attackLane, attackCard, effectiveAttack),
                "heavy_spin" => ResolveHeavySpin(
                    attacker, defender, atkStrat, defStrat, exchange, attackLane, attackerRole, attackCard, effectiveAttack),
                _ => ResolveHit(
                    attacker, defender, atkStrat, defStrat, exchange,
                    attackLane, attackerRole, attackCard, effectiveAttack, effectiveBlock,
                    shot, isLastCard),
            };
        }

        /// <summary>
        /// Tip (any card &lt;=5, front row only): checked against the single lowest
        /// blocker card in the lane (not the stack total). This is the inverse
        /// direction from a normal hit vs. block: a tip is a soft, disguised shot, so
        /// a blocker who's quick/low (same or lower card than the tip) reads it and
        /// stuffs it outright -- including an exact tie, unlike a hit's tie-goes-to-
        /// deflection rule -- while only a blocker committed to a bigger swing (a
        /// strictly higher card) misses it and lets it through. Digging a tip is the
        /// same inverted logic: the defender needs a card *same or lower* than the
        /// tip's own value to control the soft shot (too big a swing on a dig
        /// overplays it). No chase after a failed tip dig.
        /// </summary>
        private CardOutcome ResolveTip(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange,
            int attackLane, PlayerRole? attackerRole, Card attackCard, int effectiveAttack,
            List<Card> laneBlockCards, bool isLastCard)
        {
            int effectiveTip = effectiveAttack;
            if (attacker.AbilityEngine != null && attackerRole.HasValue)
            {
                // Subtracts, not adds -- a lower tip value is the advantage here (harder
                // to block, harder to dig), the opposite of a hit's effective-value bonus.
                effectiveTip -= attacker.AbilityEngine.TipValueBonus(attackerRole.Value);
            }

            int? lowestBlocker = laneBlockCards.Count > 0 ? laneBlockCards.Min(c => c.Value) : (int?)null;

            if (lowestBlocker.HasValue && lowestBlocker.Value <= effectiveTip)
            {
                Narrate($"  Tip:     {effectiveTip} vs lowest blocker {lowestBlocker.Value}  →  STUFFED");
                if (!isLastCard)
                {
                    return CardOutcome.StuffedContinue();
                }
                return CardOutcome.Ended(new RallyResult(
                    defender.Name,
                    $"Tip stuffed (tip={effectiveTip}, blocker={lowestBlocker.Value})",
                    exchange));
            }

            // Tip beats the block (or the lane is empty): defender digs, same-or-lower,
            // no chase on failure.
            Card digCard = PhaseDig(defender, defStrat, effectiveTip, DigType.Tip);
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(attackLane, attackCard.Value);
            int effectiveTipDig = digCard.Value;
            if (defender.AbilityEngine != null)
            {
                // Subtracts too, mirroring TipValueBonus above -- a lower effective dig
                // value is the defender's advantage under this inverted comparison.
                effectiveTipDig -= defender.AbilityEngine.TipDigThreshold(defenderRole);
            }
            if (effectiveTipDig <= effectiveTip)
            {
                Narrate(
                    $"  Dig:     {defender.Name} card {digCard.Value}"
                    + (effectiveTipDig != digCard.Value ? $" (eff {effectiveTipDig})" : "")
                    + $"  ≤ tip {effectiveTip}  →  DUG");
                // Tips are too fast for adjacent coverage -- setter dig always breaks play
                _brokenPlay = defenderRole == PlayerRole.Setter;
                return CardOutcome.Continue(defender, attacker, defStrat, atkStrat);
            }

            Narrate(
                $"  Dig:     {defender.Name} card {digCard.Value}"
                + (effectiveTipDig != digCard.Value ? $" (eff {effectiveTipDig})" : "")
                + $"  > tip {effectiveTip}  →  NOT DUG, no chase");
            return CardOutcome.Ended(new RallyResult(
                attacker.Name,
                $"Tip not dug, no chase (tip={effectiveTip}, dig={digCard.Value})",
                exchange));
        }

        /// <summary>
        /// Exact-tie deflection: the ball falls to the attacker's own side, and that
        /// team digs it -- equal-or-higher required. No chase on any dig failure,
        /// this one included -- a failed dig always ends the rally immediately, point
        /// to whichever side didn't have to dig. A successful direct dig keeps the
        /// same team attacking (the ball never crossed the net).
        /// </summary>
        private CardOutcome ResolveOwnSideDeflect(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange, int tiedValue)
        {
            int deflectPenalty = defender.AbilityEngine?.DeflectDigThreshold() ?? 0;
            int deflectTarget = Math.Max(1, tiedValue - deflectPenalty);

            Card digCard = PhaseDig(attacker, atkStrat, deflectTarget, DigType.Tip);
            if (digCard.Value >= deflectTarget)
            {
                Narrate($"  Deflect: attacker side, {attacker.Name} card {digCard.Value}  ≥ {deflectTarget}  →  DUG");
                return CardOutcome.Continue(attacker, defender, atkStrat, defStrat);
            }

            Narrate($"  Deflect: attacker side, {attacker.Name} card {digCard.Value}  < {deflectTarget}  →  NOT DUG, no chase");
            return CardOutcome.Ended(new RallyResult(
                defender.Name,
                $"Deflect not dug, no chase (target={deflectTarget}, dig={digCard.Value})",
                exchange));
        }

        private CardOutcome ResolveHit(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange,
            int attackLane, PlayerRole? attackerRole, Card attackCard, int effectiveAttack, int effectiveBlock,
            string shot, bool isLastCard)
        {
            AttackOutcomeType outcome = AttackResolution.ResolveAttack(effectiveAttack, effectiveBlock);
            Narrate($"  Outcome: {outcome.ToString().ToUpperInvariant()}  (atk {effectiveAttack} vs blk {effectiveBlock})");

            if (outcome == AttackOutcomeType.Stuffed)
            {
                if (!isLastCard)
                {
                    return CardOutcome.StuffedContinue();
                }
                return CardOutcome.Ended(new RallyResult(
                    defender.Name,
                    $"Stuffed (attack={effectiveAttack}, block={effectiveBlock})",
                    exchange));
            }

            if (outcome == AttackOutcomeType.Deflect)
            {
                if (shot == "seam")
                {
                    // Seam shot: deflect redirects onto defending team's side instead
                    return CardOutcome.Ended(new RallyResult(
                        attacker.Name,
                        $"Seam shot deflect (attack={effectiveAttack}, block={effectiveBlock})",
                        exchange));
                }
                return ResolveOwnSideDeflect(attacker, defender, atkStrat, defStrat, exchange, effectiveAttack);
            }

            // KILL
            int digTarget = effectiveAttack;
            if (attacker.AbilityEngine != null && attackerRole.HasValue)
            {
                digTarget += attacker.AbilityEngine.AttackDigThreshold(attackerRole.Value);
            }
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(attackLane, attackCard.Value);
            int coverThreshold = AttackResolution.CoverThreshold(defender);
            bool coverAttempted = false;
            Card digCard;
            if (defenderRole == PlayerRole.Setter && coverThreshold > 0)
            {
                if (defStrat.CoverDrawsFromDeck())
                {
                    if (defender.Deck.DrawPileSize > 0)
                    {
                        Card coverCard = defender.DrawForAction();
                        defender.Deck.Discard(coverCard);
                        digCard = coverCard;
                        coverAttempted = true;
                    }
                    else
                    {
                        digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                    }
                }
                else if (defender.Hand.Count > 0)
                {
                    Card? coverCard = defStrat.ChooseCoverAttempt(defender.Hand, coverThreshold);
                    if (coverCard.HasValue)
                    {
                        defender.PlayCard(coverCard.Value);
                        digCard = coverCard.Value;
                        coverAttempted = true;
                    }
                    else
                    {
                        digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                    }
                }
                else
                {
                    digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                }
            }
            else
            {
                digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
            }

            int effectiveDig = digCard.Value;
            if (defender.AbilityEngine != null)
            {
                effectiveDig += defender.AbilityEngine.DefenderDigThreshold(defenderRole);
            }

            if (effectiveDig >= digTarget)
            {
                Narrate(
                    $"  Dig:     {defender.Name} card {digCard.Value}"
                    + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                    + $"  ≥ {digTarget}  →  DUG");
                if (defender.AbilityEngine != null)
                {
                    defender.AbilityEngine.RecordDigSuccess(defenderRole, DigType.Normal);
                    if (defender.AbilityEngine.DeckSwapOpponentOnDig(DigType.Normal))
                    {
                        if (attacker.Hand.Count > 0)
                        {
                            Card highest = attacker.Hand.OrderByDescending(c => c.Value).First();
                            attacker.Hand.Remove(highest);
                            attacker.Deck.Discard(highest);
                            if (attacker.Deck.DrawPileSize > 0)
                            {
                                Card newCard = attacker.DrawForAction();
                                attacker.Hand.Add(newCard);
                            }
                        }
                    }
                }
                if (defenderRole == PlayerRole.Setter)
                {
                    if (coverAttempted && digCard.Value >= coverThreshold)
                    {
                        Narrate($"  Cover:   adjacent player reached (card {digCard.Value}) — no broken play");
                        _brokenPlay = false;
                    }
                    else if (defender.PassiveAbility == "Safe Setter")
                    {
                        Narrate("  Passive: Safe Setter prevents broken play");
                        _brokenPlay = false;
                    }
                    else
                    {
                        _brokenPlay = true;
                    }
                }
                else
                {
                    _brokenPlay = false;
                }
                return CardOutcome.Continue(defender, attacker, defStrat, atkStrat);
            }

            // No chase on any dig failure -- always an immediate point to the attacker,
            // blocked or not (the old unblocked-only "no chase" special case is now just
            // what every dig failure does).
            Narrate(
                $"  Dig:     {defender.Name} card {digCard.Value}"
                + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                + $"  < {digTarget}  →  NOT DUG, no chase");
            return CardOutcome.Ended(new RallyResult(
                attacker.Name,
                $"Kill, no chase (attack={effectiveAttack} > block={effectiveBlock}, dig={digCard.Value})",
                exchange));
        }

        /// <summary>Dormant ability-gated shot: block ignored, dug like a tip. No chase on
        /// a failed dig, same as every other dig in the game.</summary>
        private CardOutcome ResolveRollShot(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange,
            int attackLane, Card attackCard, int effectiveAttack)
        {
            Card digCard = PhaseDig(defender, defStrat, effectiveAttack, DigType.Tip);
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(attackLane, attackCard.Value);
            int effectiveDig = digCard.Value;
            if (defender.AbilityEngine != null)
            {
                effectiveDig += defender.AbilityEngine.TipDigThreshold(defenderRole);
            }
            if (effectiveDig <= effectiveAttack)
            {
                Narrate(
                    $"  Dig:     {defender.Name} card {digCard.Value}"
                    + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                    + $"  ≤ roll {effectiveAttack}  →  DUG");
                _brokenPlay = defenderRole == PlayerRole.Setter;
                return CardOutcome.Continue(defender, attacker, defStrat, atkStrat);
            }

            Narrate(
                $"  Dig:     {defender.Name} card {digCard.Value}"
                + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                + $"  > roll {effectiveAttack}  →  NOT DUG, no chase");
            return CardOutcome.Ended(new RallyResult(
                attacker.Name,
                $"Roll shot kill, no chase (roll={effectiveAttack}, dig={digCard.Value})",
                exchange));
        }

        /// <summary>Dormant ability-gated shot: block ignored, failed dig is an instant point, no chase.</summary>
        private CardOutcome ResolveHeavySpin(
            Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat, int exchange,
            int attackLane, PlayerRole? attackerRole, Card attackCard, int effectiveAttack)
        {
            int digTarget = effectiveAttack;
            if (attacker.AbilityEngine != null && attackerRole.HasValue)
            {
                digTarget += attacker.AbilityEngine.AttackDigThreshold(attackerRole.Value);
            }
            PlayerRole defenderRole = AttackResolution.GetDigDefenderRole(attackLane, attackCard.Value);
            int coverThreshold = AttackResolution.CoverThreshold(defender);
            bool coverAttempted = false;
            Card digCard;
            if (defenderRole == PlayerRole.Setter && coverThreshold > 0)
            {
                if (defStrat.CoverDrawsFromDeck())
                {
                    if (defender.Deck.DrawPileSize > 0)
                    {
                        Card coverCard = defender.Deck.Draw();
                        defender.Deck.Discard(coverCard);
                        digCard = coverCard;
                        coverAttempted = true;
                    }
                    else
                    {
                        digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                    }
                }
                else if (defender.Hand.Count > 0)
                {
                    Card? coverCard = defStrat.ChooseCoverAttempt(defender.Hand, coverThreshold);
                    if (coverCard.HasValue)
                    {
                        defender.PlayCard(coverCard.Value);
                        digCard = coverCard.Value;
                        coverAttempted = true;
                    }
                    else
                    {
                        digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                    }
                }
                else
                {
                    digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
                }
            }
            else
            {
                digCard = PhaseDig(defender, defStrat, digTarget, DigType.Normal);
            }

            int effectiveDig = digCard.Value;
            if (defender.AbilityEngine != null)
            {
                effectiveDig += defender.AbilityEngine.DefenderDigThreshold(defenderRole);
            }

            if (effectiveDig >= digTarget)
            {
                Narrate(
                    $"  Dig:     {defender.Name} card {digCard.Value}"
                    + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                    + $"  ≥ {digTarget}  →  HEAVY SPIN DUG");
                if (defender.AbilityEngine != null)
                {
                    defender.AbilityEngine.RecordDigSuccess(defenderRole, DigType.Normal);
                    if (defender.AbilityEngine.DeckSwapOpponentOnDig(DigType.Normal))
                    {
                        if (attacker.Hand.Count > 0)
                        {
                            Card highest = attacker.Hand.OrderByDescending(c => c.Value).First();
                            attacker.Hand.Remove(highest);
                            attacker.Deck.Discard(highest);
                            if (attacker.Deck.DrawPileSize > 0)
                            {
                                Card newCard = attacker.Deck.Draw();
                                attacker.Hand.Add(newCard);
                            }
                        }
                    }
                }
                if (defenderRole == PlayerRole.Setter)
                {
                    if (coverAttempted && digCard.Value >= coverThreshold)
                    {
                        Narrate($"  Cover:   adjacent player reached (card {digCard.Value}) — no broken play");
                        _brokenPlay = false;
                    }
                    else
                    {
                        _brokenPlay = true;
                    }
                }
                else
                {
                    _brokenPlay = false;
                }
                return CardOutcome.Continue(defender, attacker, defStrat, atkStrat);
            }

            Narrate(
                $"  Dig:     {defender.Name} card {digCard.Value}"
                + (effectiveDig != digCard.Value ? $" (eff {effectiveDig})" : "")
                + $"  < {digTarget}  →  NOT DUG (no chase)");
            return CardOutcome.Ended(new RallyResult(
                attacker.Name,
                $"Heavy spin not dug, no chase (attack={effectiveAttack}, dig={digCard.Value})",
                exchange));
        }

        // -- Phase helpers --

        /// <summary>
        /// Chase: keep adding cards (up to 2 attempts) until the running total reaches
        /// the target, or fail. Only ever called for a failed serve reception -- a
        /// failed dig, of any kind, never chases (see ResolveHit/ResolveTip/
        /// ResolveOwnSideDeflect/ResolveRollShot, all of which end the rally
        /// immediately on a dig failure instead). A SUCCESS just reports that the
        /// target was reached -- the caller turns it into a mandatory, guaranteed free
        /// ball back across the net. FAILED means the opposing team wins the point.
        /// </summary>
        private ChaseResult PhaseChase(Team team, IStrategy strat, int runningTotal, int targetValue, int extraBonus = 0)
        {
            runningTotal += extraBonus + (team.AbilityEngine?.ChaseBonus() ?? 0);
            Narrate($"\n  Chase:   {team.Name}  need {targetValue}  starting at {runningTotal}");

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Card card;
                if (team.Hand.Count > 0)
                {
                    card = strat.ChooseChaseCard(team.Hand, runningTotal, targetValue);
                    team.PlayCard(card);
                }
                else
                {
                    card = team.BlindDraw();
                }
                runningTotal += card.Value;
                Narrate($"  Chase {attempt}: card {card.Value}  →  total {runningTotal} / {targetValue}");

                if (runningTotal >= targetValue)
                {
                    Narrate("  Chase:   SUCCEEDED");
                    return new ChaseResult(ChaseOutcome.Success);
                }
            }

            Narrate($"  Chase:   FAILED  (total {runningTotal} < {targetValue})");
            return new ChaseResult(ChaseOutcome.Failed);
        }

        private (Card, GridPlayer) PhaseServe()
        {
            var eligible = _rcv.EligibleServeReceivers();
            Card card;
            GridPlayer target;
            if (_srv.Hand.Count > 0)
            {
                (card, target) = _srvStrat.ChooseServe(_srv.Hand, eligible);
                _srv.PlayCard(card);
            }
            else
            {
                card = _srv.DrawForAction();
                _srv.Deck.Discard(card);
                target = _rng.Choice(eligible);
            }
            _srv.RefillHand();
            return (card, target);
        }

        private Card PhaseReceive(int serveValue)
        {
            Card card;
            if (_rcv.Hand.Count > 0)
            {
                card = _rcvStrat.ChooseReceiveCard(_rcv.Hand, serveValue);
                _rcv.PlayCard(card);
            }
            else
            {
                card = _rcv.DrawForAction();
                _rcv.Deck.Discard(card);
            }
            // No refill here: hand replenishes only when the ball crosses the net.
            return card;
        }

        private (Card, SetTemplate) PhaseSet(Team team, IStrategy strat, int setValueDelta = 0)
        {
            Card card;
            if (team.Hand.Count > 0)
            {
                card = strat.ChooseSetCard(team.Hand, _brokenPlay);
                team.PlayCard(card);
            }
            else
            {
                card = team.DrawForAction();
                team.Deck.Discard(card);
            }
            // No refill here: hand replenishes only when the ball crosses the net.

            int effectiveValue = card.Value + setValueDelta;

            // Apply Setter's passive on_set bonus ONLY if not broken play
            if (!_brokenPlay && team.AbilityEngine != null)
            {
                int setBonus = team.AbilityEngine.OnSetBonus(card.Value);
                effectiveValue += setBonus;
            }

            effectiveValue = Math.Min(10, Math.Max(1, effectiveValue));

            // Select template based on broken play status.
            // Team-specific template maps are injected from CSV runtime config.
            var templateMap = _brokenPlay ? team.BrokenPlayTemplates : team.SetterTemplates;
            SetTemplate template = templateMap[effectiveValue];

            // Reset broken play flag after use
            _brokenPlay = false;

            return (card, template);
        }

        /// <summary>
        /// Place attack cards according to template. Hand cards are placed first
        /// (known values). If the hand does not fill all available slots up to
        /// template.MaxAttackers, the remaining slots are filled with blind draws
        /// straight from the deck -- face-down cards whose values are hidden from
        /// both strategies during block commit and lane commitment. Values are
        /// revealed in the narrative after the attacker locks in a lane.
        /// </summary>
        private Dictionary<int, List<AttackCard>> PhaseHit(Team team, IStrategy strat, SetTemplate template)
        {
            var attackCards = new Dictionary<int, List<AttackCard>>();
            int placed;

            if (team.Hand.Count > 0)
            {
                // Exchange card ability: before committing, optionally swap worst hand
                // card for the deck top (fires when any front-row player has the ability)
                if (team.AbilityEngine != null && team.AbilityEngine.ExchangeCardEligible())
                {
                    Card? deckTop = team.Deck.Peek();
                    if (deckTop.HasValue)
                    {
                        Card? swapOut = strat.ChooseExchangeCard(team.Hand, deckTop.Value);
                        if (swapOut.HasValue && team.Hand.Contains(swapOut.Value))
                        {
                            team.PlayCard(swapOut.Value); // removes from hand, discards
                            Card newCard = team.DrawForAction();
                            team.Hand.Add(newCard);
                        }
                    }
                }
                var placements = strat.ChooseHitCards(team.Hand, template);
                var cardsToCommit = placements.Select(p => p.Card).ToList();
                team.CommitCards(cardsToCommit);
                foreach (var (lane, card, position) in placements)
                {
                    if (!attackCards.TryGetValue(lane, out var list))
                    {
                        list = new List<AttackCard>();
                        attackCards[lane] = list;
                    }
                    list.Add(new AttackCard(card, position, false));
                }
                placed = placements.Count;
            }
            else
            {
                placed = 0; // no hand -- all slots filled with blind draws below
            }

            // -- BLIND DRAWS --
            // If hand placements didn't reach max_attackers, fill remaining slots
            // with cards drawn directly from the deck (face-down threats).
            // Priority: front lanes first, then back lanes not already covered,
            // then back lanes that share a lane with a front placement.
            if (placed < template.MaxAttackers)
            {
                var usedSlots = new HashSet<(int, AttackPosition)>();
                foreach (var kv in attackCards)
                {
                    foreach (var ac in kv.Value)
                    {
                        usedSlots.Add((kv.Key, ac.Position));
                    }
                }
                var frontSet = new HashSet<int>(template.FrontLanes);
                var candidates = new List<(int Lane, AttackPosition Position)>();

                foreach (int lane in template.FrontLanes.OrderBy(l => l))
                {
                    if (!usedSlots.Contains((lane, AttackPosition.Front)))
                    {
                        candidates.Add((lane, AttackPosition.Front));
                    }
                }
                foreach (int lane in template.BackLanes.Where(l => !frontSet.Contains(l)).OrderBy(l => l))
                {
                    if (!usedSlots.Contains((lane, AttackPosition.Back)))
                    {
                        candidates.Add((lane, AttackPosition.Back));
                    }
                }
                foreach (int lane in template.BackLanes.Where(l => frontSet.Contains(l)).OrderBy(l => l))
                {
                    if (!usedSlots.Contains((lane, AttackPosition.Back)))
                    {
                        candidates.Add((lane, AttackPosition.Back));
                    }
                }

                int remaining = template.MaxAttackers - placed;
                foreach (var (lane, position) in candidates.Take(remaining))
                {
                    Card blindCard = team.DrawForAction();
                    if (!attackCards.TryGetValue(lane, out var list))
                    {
                        list = new List<AttackCard>();
                        attackCards[lane] = list;
                    }
                    list.Add(new AttackCard(blindCard, position, true));
                }
            }

            return attackCards;
        }

        /// <summary>
        /// Returns (blockLayout, blockMax, blockCards) where:
        ///   blockLayout : {lane: total block value} -- unblocked lanes absent (value=0)
        ///   blockMax    : {lane: highest single card} -- max individual card per lane
        ///   blockCards  : {lane: [Card]} -- actual cards placed
        ///
        /// Quick set rules (when quickLanes is non-empty):
        ///   - Only 1 card allowed per quick lane
        ///   - Blocker MUST draw from deck (blind) for quick lanes
        /// </summary>
        private (Dictionary<int, int>, Dictionary<int, int>, Dictionary<int, List<Card>>) PhaseBlockCommit(
            Team team, IStrategy strat, List<int> attackLanes, List<int> quickLanes = null)
        {
            quickLanes ??= new List<int>();
            Dictionary<int, List<Card>> placement;

            if (team.Hand.Count > 0)
            {
                // Compute wild_block threshold (max across all blocking roles)
                int wildThreshold = 0;
                if (team.AbilityEngine != null)
                {
                    foreach (var role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
                    {
                        wildThreshold = Math.Max(wildThreshold, team.AbilityEngine.WildBlockThreshold(role));
                    }
                }
                int wideSpreadThreshold = 0;
                if (team.AbilityEngine != null)
                {
                    foreach (var role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
                    {
                        wideSpreadThreshold = Math.Max(wideSpreadThreshold, team.AbilityEngine.WideSpreadBonus(role));
                    }
                }

                placement = new Dictionary<int, List<Card>>();
                var nonQuickLanes = attackLanes.Where(ln => !quickLanes.Contains(ln)).ToList();

                if (quickLanes.Count > 0)
                {
                    // Handle quick lanes first (blind draws)
                    foreach (int lane in quickLanes)
                    {
                        if (team.Deck.DrawPileSize > 0)
                        {
                            Card card = team.DrawForAction();
                            placement[lane] = new List<Card> { card }; // Only 1 card allowed
                            Narrate($"  Block:   Quick set lane {lane} → blind draw card {card.Value}");
                        }
                        // If no deck, skip this lane (no block)
                    }
                }

                // Handle non-quick lanes normally (hand selection) -- one card per
                // blocker (Oh/Mb/Opp), each restricted to a lane within their reach
                // (see PlayerRoleExtensions.BlockableLanes), grouped by lane afterward
                // since everything downstream still works in lane-total terms.
                if (nonQuickLanes.Count > 0)
                {
                    var blockerChoices = strat.ChooseBlockCards(team.Hand, nonQuickLanes, wildThreshold, wideSpreadThreshold);
                    var handCards = new List<Card>();
                    foreach (var kv in blockerChoices)
                    {
                        PlayerRole role = kv.Key;
                        (int lane, Card card) = kv.Value;
                        // Defensive: don't trust the strategy blindly -- ignore a lane
                        // this blocker can't actually reach, or one nothing is attacking.
                        if (!PlayerRoleExtensions.BlockableLanes[role].Contains(lane) || !nonQuickLanes.Contains(lane))
                        {
                            continue;
                        }
                        if (!placement.TryGetValue(lane, out var list))
                        {
                            list = new List<Card>();
                            placement[lane] = list;
                        }
                        list.Add(card);
                        handCards.Add(card);
                    }
                    team.CommitCards(handCards);
                }
            }
            else
            {
                // No-hand team: flip one card per attacked lane, lowest lane to highest.
                placement = new Dictionary<int, List<Card>>();
                foreach (int lane in attackLanes.OrderBy(l => l))
                {
                    Card card = team.DrawForAction();
                    placement[lane] = new List<Card> { card };
                }
            }

            // Discard block cards; no refill (blocking does not send ball over net).
            var allBlockCards = placement.Values.SelectMany(cards => cards).ToList();
            team.DiscardMany(allBlockCards);

            var blockLayout = placement.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(c => c.Value));
            var blockMax = placement.ToDictionary(kv => kv.Key, kv => kv.Value.Max(c => c.Value));

            // Apply defender block abilities
            if (team.AbilityEngine != null)
            {
                var engine = team.AbilityEngine;
                // Per-player block value bonus (e.g. OH, MB, OPP individual bonuses)
                foreach (int lane in blockLayout.Keys.ToList())
                {
                    if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out var role))
                    {
                        int bonus = engine.BlockValueBonus(role);
                        if (bonus != 0)
                        {
                            blockLayout[lane] += bonus;
                        }
                    }
                }
                // Draw and add block: draw N cards from deck, add to block, discard
                foreach (int lane in blockLayout.Keys.ToList())
                {
                    if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out var role))
                    {
                        int drawCount = engine.DrawAndAddBlock(role);
                        if (drawCount > 0)
                        {
                            var drawnCards = new List<Card>();
                            for (int i = 0; i < drawCount; i++)
                            {
                                if (team.Deck.TotalSize > 0)
                                {
                                    drawnCards.Add(team.DrawForAction());
                                }
                            }

                            int drawnValue = 0;
                            if (drawnCards.Count > 0)
                            {
                                if (drawnCards.Count == 2)
                                {
                                    // Special rule: keep both if either card is <=5, otherwise keep highest
                                    if (drawnCards[0].Value <= 5 || drawnCards[1].Value <= 5)
                                    {
                                        drawnValue = drawnCards[0].Value + drawnCards[1].Value;
                                    }
                                    else
                                    {
                                        drawnValue = Math.Max(drawnCards[0].Value, drawnCards[1].Value);
                                    }
                                }
                                else
                                {
                                    // For 1 card or other counts, add all
                                    drawnValue = drawnCards.Sum(c => c.Value);
                                }
                            }

                            if (drawnValue > 0)
                            {
                                blockLayout[lane] += drawnValue;
                            }
                            team.DiscardMany(drawnCards);
                        }
                    }
                }
                // MB adjacent block bonus: adds to lanes 1 and 3 when MB is blocking
                int adj = engine.AdjacentBlockBonus(blockLayout.ContainsKey(2));
                if (adj != 0)
                {
                    foreach (int adjLane in new[] { 1, 3 })
                    {
                        blockLayout[adjLane] = blockLayout.GetValueOrDefault(adjLane, 0) + adj;
                    }
                }
                // Wide spread bonus: fires when |card1-card2| >= N, adds +N to block sum
                foreach (int lane in blockLayout.Keys.ToList())
                {
                    if (PlayerRoleExtensions.LaneToRole.TryGetValue(lane, out var role))
                    {
                        int wsp = engine.WideSpreadBonus(role);
                        if (wsp != 0)
                        {
                            var laneCards = placement.GetValueOrDefault(lane, new List<Card>());
                            if (laneCards.Count == 2 && Math.Abs(laneCards[0].Value - laneCards[1].Value) >= wsp)
                            {
                                blockLayout[lane] += wsp;
                            }
                        }
                    }
                }
            }

            return (blockLayout, blockMax, placement);
        }

        private Card PhaseDig(Team team, IStrategy strat, int targetValue, DigType digType)
        {
            Card card;
            if (team.Hand.Count > 0)
            {
                card = strat.ChooseDigCard(team.Hand, targetValue, digType);
                team.PlayCard(card);
            }
            else
            {
                card = team.DrawForAction();
                team.Deck.Discard(card);
            }
            // No refill here: hand replenishes only when the ball crosses the net.
            return card;
        }

        /// <summary>
        /// Result of resolving one attack card within a chosen lane. Ported from
        /// src/game.py's _CardOutcome.
        ///
        /// Result   -- set when the rally is over (win/loss decided).
        /// Stuffed  -- set when this card was genuinely stuffed and another combo
        ///             card remains to try (never set together with Result, and
        ///             never set for the last card in the lane -- that case resolves
        ///             straight to a Stuffed Result instead).
        /// Attacker/Defender/AtkStrat/DefStrat -- the roles to carry into the next
        ///             exchange when the rally continues (Result is null, Stuffed is
        ///             false).
        /// </summary>
        private sealed class CardOutcome
        {
            public RallyResult Result { get; private set; }
            public bool Stuffed { get; private set; }
            public Team Attacker { get; private set; }
            public Team Defender { get; private set; }
            public IStrategy AtkStrat { get; private set; }
            public IStrategy DefStrat { get; private set; }

            public static CardOutcome Ended(RallyResult result) => new() { Result = result };

            public static CardOutcome StuffedContinue() => new() { Stuffed = true };

            public static CardOutcome Continue(Team attacker, Team defender, IStrategy atkStrat, IStrategy defStrat) =>
                new() { Attacker = attacker, Defender = defender, AtkStrat = atkStrat, DefStrat = defStrat };
        }
    }
}
