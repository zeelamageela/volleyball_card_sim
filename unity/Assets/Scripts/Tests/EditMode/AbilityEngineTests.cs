using System.Collections.Generic;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported behavior checks for src/abilities.py's AbilityEngine, mirroring
    /// the intent of tests/test_rally_endings.py's ability unit tests (constructed
    /// directly here since the CSV-loading layer isn't ported yet).</summary>
    public class AbilityEngineTests
    {
        private static AbilityEngine Engine(params (PlayerRole Role, PlayerCard Card)[] roster)
        {
            var dict = new Dictionary<PlayerRole, PlayerCard>();
            foreach (var (role, card) in roster)
            {
                dict[role] = card;
            }
            return new AbilityEngine(dict);
        }

        private static PlayerCard Card(string name, string roleName, params Ability[] abilities) =>
            new(name, roleName, new List<Ability>(abilities));

        [Test]
        public void PierceBlockFiresAtThresholdAndNotBelow()
        {
            var card = Card("Titan", "OPP",
                new Ability("Titan Pierce", Trigger.OnAttack, "attack_card_value", ">=9", EffectType.PierceBlock, 1));
            var engine = Engine((PlayerRole.Opp, card));

            Assert.IsTrue(engine.PierceBlock(PlayerRole.Opp, 9));
            Assert.IsTrue(engine.PierceBlock(PlayerRole.Opp, 10));
            Assert.IsFalse(engine.PierceBlock(PlayerRole.Opp, 8));
            Assert.IsFalse(engine.PierceBlock(PlayerRole.Oh, 9)); // wrong role
        }

        [Test]
        public void WipeBlockFiresOnlyOnCardValueOne()
        {
            var card = Card("Trickster", "OH",
                new Ability("Wipe", Trigger.OnAttack, "attack_card_value", "1", EffectType.WipeBlock, 1));
            var engine = Engine((PlayerRole.Oh, card));

            Assert.IsTrue(engine.WipeBlock(PlayerRole.Oh, 1));
            Assert.IsFalse(engine.WipeBlock(PlayerRole.Oh, 2));
        }

        [Test]
        public void RollShotFiresAtThreshold()
        {
            var card = Card("Roller", "OH",
                new Ability("Roller", Trigger.OnAttack, "attack_card_value", ">=6", EffectType.RollShot, 1));
            var engine = Engine((PlayerRole.Oh, card));

            Assert.IsTrue(engine.RollShot(PlayerRole.Oh, 6));
            Assert.IsFalse(engine.RollShot(PlayerRole.Oh, 5));
        }

        [Test]
        public void AttackValueBonusSumsMultipleMatchingAbilities()
        {
            var card = Card("Stacker", "OH",
                new Ability("Bonus A", Trigger.OnAttack, "", "", EffectType.AttackValueBonus, 2),
                new Ability("Bonus B", Trigger.OnAttack, "", "", EffectType.AttackValueBonus, 3));
            var engine = Engine((PlayerRole.Oh, card));

            Assert.AreEqual(5, engine.AttackValueBonus(PlayerRole.Oh, 7));
        }

        [Test]
        public void IsActiveTrueAbilitiesAreIgnored()
        {
            // is_active: true means "requires manual activation, not wired anywhere" --
            // effectively disabled for now, per the is_active semantics discovery.
            var card = Card("Dormant", "OH",
                new Ability("Should not fire", Trigger.OnAttack, "", "", EffectType.PierceBlock, 1, isActive: true));
            var engine = Engine((PlayerRole.Oh, card));

            Assert.IsFalse(engine.PierceBlock(PlayerRole.Oh, 5));
        }

        [Test]
        public void WideSpreadBonusReturnsEffectValue()
        {
            var card = Card("Shield", "MB",
                new Ability("Shield", Trigger.OnBlock, "", "", EffectType.WideSpreadBonus, 5));
            var engine = Engine((PlayerRole.Mb, card));

            Assert.AreEqual(5, engine.WideSpreadBonus(PlayerRole.Mb));
            Assert.AreEqual(0, engine.WideSpreadBonus(PlayerRole.Oh)); // no card for this role
        }

        [Test]
        public void ForceHighBlockThresholdRespectsCondition()
        {
            var card = Card("Titan", "OPP",
                new Ability("Force High", Trigger.OnAttack, "attack_card_value", ">=5", EffectType.ForceHighBlock, 5));
            var engine = Engine((PlayerRole.Opp, card));

            Assert.AreEqual(5, engine.ForceHighBlockThreshold(PlayerRole.Opp, 5));
            Assert.AreEqual(0, engine.ForceHighBlockThreshold(PlayerRole.Opp, 4));
        }

        [Test]
        public void SetterCoverThresholdReturnsLowestAcrossLiberoAndDs()
        {
            var libero = Card("Wall", "Libero",
                new Ability("Cover", Trigger.OnDig, "", "", EffectType.SetterCover, 6));
            var ds = Card("Reach", "DS",
                new Ability("Cover", Trigger.OnDig, "", "", EffectType.SetterCover, 4));
            var engine = Engine((PlayerRole.Libero, libero), (PlayerRole.Ds, ds));

            Assert.AreEqual(4, engine.SetterCoverThreshold());
        }

        [Test]
        public void SetterCoverThresholdIsZeroWithNoAbility()
        {
            var engine = Engine();
            Assert.AreEqual(0, engine.SetterCoverThreshold());
        }

        [Test]
        public void DeckSwapOpponentOnDigScansEveryPlayerOnTheTeam()
        {
            var thief = Card("Thief", "DS",
                new Ability("Swap", Trigger.OnDigSuccess, "dig_type", "normal", EffectType.DeckSwapOpponent, 1));
            var engine = Engine((PlayerRole.Ds, thief));

            Assert.IsTrue(engine.DeckSwapOpponentOnDig(DigType.Normal));
            Assert.IsFalse(engine.DeckSwapOpponentOnDig(DigType.Tip));
        }

        [Test]
        public void HandSizeModifierScansAllRosterAbilities()
        {
            var deepBenchPlayer = Card("Bench", "Setter",
                new Ability("Deep Bench", Trigger.OnRoster, "", "", EffectType.HandSizeMod, 1));
            var engine = Engine((PlayerRole.Setter, deepBenchPlayer));

            Assert.AreEqual(1, engine.HandSizeModifier());
        }

        [Test]
        public void ExchangeCardEligibleChecksOnlyFrontRow()
        {
            var oh = Card("Swapper", "OH",
                new Ability("Exchange", Trigger.OnAttack, "", "", EffectType.ExchangeCard, 1));
            var engineWithFrontRow = Engine((PlayerRole.Oh, oh));
            Assert.IsTrue(engineWithFrontRow.ExchangeCardEligible());

            var setter = Card("NotEligible", "Setter",
                new Ability("Exchange", Trigger.OnAttack, "", "", EffectType.ExchangeCard, 1));
            var engineWithBackRowOnly = Engine((PlayerRole.Setter, setter));
            Assert.IsFalse(engineWithBackRowOnly.ExchangeCardEligible());
        }

        [Test]
        public void ChaseBonusSumsLiberoAndDs()
        {
            var libero = Card("L", "Libero", new Ability("A", Trigger.OnChase, "", "", EffectType.ChaseCardBonus, 2));
            var ds = Card("D", "DS", new Ability("B", Trigger.OnChase, "", "", EffectType.ChaseCardBonus, 3));
            var engine = Engine((PlayerRole.Libero, libero), (PlayerRole.Ds, ds));

            Assert.AreEqual(5, engine.ChaseBonus());
        }

        [Test]
        public void ConsumeSetDeltaAccumulatesAndClears()
        {
            var libero = Card("L", "Libero",
                new Ability("Reset", Trigger.OnDigSuccess, "", "", EffectType.SetValueDelta, 2));
            var engine = Engine((PlayerRole.Libero, libero));

            engine.RecordDigSuccess(PlayerRole.Libero, DigType.Normal);
            Assert.AreEqual(2, engine.ConsumeSetDelta());
            Assert.AreEqual(0, engine.ConsumeSetDelta()); // cleared after consuming
        }
    }
}
