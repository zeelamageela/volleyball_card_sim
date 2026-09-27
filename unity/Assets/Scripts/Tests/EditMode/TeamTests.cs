using System;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestTeamHand, plus coverage for the
    /// rest of Team's public surface.</summary>
    public class TeamTests
    {
        private static Team MakeTeam(int seed = 42) => new Team("Test", new Random(seed));

        [Test]
        public void DrawStartingHandGives5Cards()
        {
            var team = MakeTeam();
            team.DrawStartingHand();
            Assert.AreEqual(GameConstants.HandSize, team.Hand.Count);
        }

        [Test]
        public void RefillHandRestoresTo5()
        {
            var team = MakeTeam();
            team.DrawStartingHand();
            team.PlayCard(team.Hand[0]);
            team.PlayCard(team.Hand[0]);
            Assert.AreEqual(3, team.Hand.Count);

            team.RefillHand();
            Assert.AreEqual(GameConstants.HandSize, team.Hand.Count);
        }

        [Test]
        public void RefillHandReturnsHeldCardFirst()
        {
            var team = MakeTeam();
            team.DrawStartingHand();
            var held = team.Hand[0];
            team.CommitCard(held); // remove without discarding, as game.py's hold_card_check does
            team.HeldCard = held;
            Assert.AreEqual(4, team.Hand.Count);

            team.RefillHand();
            Assert.AreEqual(GameConstants.HandSize, team.Hand.Count);
            Assert.IsNull(team.HeldCard);
            Assert.Contains(held, team.Hand.ToList());
        }

        [Test]
        public void EligibleReceiversExcludesSetter()
        {
            var team = MakeTeam();
            var roles = team.EligibleReceivers().Select(p => p.Role).ToList();
            Assert.IsFalse(roles.Contains(PlayerRole.Setter));
        }

        [Test]
        public void EligibleReceiversAreBackRow()
        {
            var team = MakeTeam();
            foreach (var p in team.EligibleReceivers())
            {
                Assert.IsTrue(p.IsBackRow());
            }
        }

        [Test]
        public void EligibleServeReceiversIncludesOhAndOpp()
        {
            var team = MakeTeam();
            var roles = team.EligibleServeReceivers().Select(p => p.Role).ToList();
            Assert.IsTrue(roles.Contains(PlayerRole.Oh));
            Assert.IsTrue(roles.Contains(PlayerRole.Opp));
        }

        [Test]
        public void EligibleServeReceiversExcludesMbAndSetter()
        {
            var team = MakeTeam();
            var roles = team.EligibleServeReceivers().Select(p => p.Role).ToList();
            Assert.IsFalse(roles.Contains(PlayerRole.Mb));
            Assert.IsFalse(roles.Contains(PlayerRole.Setter));
        }

        [Test]
        public void PlayCardRemovesFromHandAndDiscards()
        {
            var team = MakeTeam();
            team.DrawStartingHand();
            int before = team.Hand.Count;
            var card = team.Hand[0];

            team.PlayCard(card);

            Assert.AreEqual(before - 1, team.Hand.Count);
            Assert.AreEqual(1, team.Deck.DiscardPileSize);
        }

        [Test]
        public void CommitCardRemovesFromHandWithoutDiscarding()
        {
            var team = MakeTeam();
            team.DrawStartingHand();
            int before = team.Hand.Count;
            var card = team.Hand[0];

            team.CommitCard(card);

            Assert.AreEqual(before - 1, team.Hand.Count);
            Assert.AreEqual(0, team.Deck.DiscardPileSize);
        }

        [Test]
        public void DrawForActionWithEliteDrawKeepsHighestOfTwo()
        {
            var team = new Team("Elite", new Random(1), passiveAbility: "Elite Draw");
            var card = team.DrawForAction();
            // Elite Draw discards exactly one card (the lower of the two drawn).
            Assert.AreEqual(1, team.Deck.DiscardPileSize);
            Assert.AreEqual(38, team.Deck.DrawPileSize); // 40 - 2 drawn
        }

        [Test]
        public void BlindDrawImmediatelyDiscards()
        {
            var team = MakeTeam();
            team.BlindDraw();
            Assert.AreEqual(0, team.Hand.Count);
            Assert.AreEqual(1, team.Deck.DiscardPileSize);
        }

        [Test]
        public void GetPlayerReturnsMatchingRole()
        {
            var team = MakeTeam();
            Assert.AreEqual(4, team.GetPlayer(PlayerRole.Oh).Position);
        }

        [Test]
        public void UseHandFalseSkipsHandLogic()
        {
            var team = new Team("Dummy", new Random(3), useHand: false);
            team.DrawStartingHand();
            team.RefillHand();
            Assert.IsEmpty(team.Hand);
        }
    }
}
