using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    public class PlayerRoleTests
    {
        [TestCase(PlayerRole.Oh, true)]
        [TestCase(PlayerRole.Mb, true)]
        [TestCase(PlayerRole.Opp, true)]
        [TestCase(PlayerRole.Setter, false)]
        [TestCase(PlayerRole.Ds, false)]
        [TestCase(PlayerRole.Libero, false)]
        public void IsFrontRowMatchesRole(PlayerRole role, bool expected)
        {
            Assert.AreEqual(expected, role.IsFrontRow());
            Assert.AreEqual(!expected, role.IsBackRow());
        }

        [Test]
        public void LaneToRoleMapsAllThreeLanes()
        {
            Assert.AreEqual(PlayerRole.Oh, PlayerRoleExtensions.LaneToRole[1]);
            Assert.AreEqual(PlayerRole.Mb, PlayerRoleExtensions.LaneToRole[2]);
            Assert.AreEqual(PlayerRole.Opp, PlayerRoleExtensions.LaneToRole[3]);
        }

        [Test]
        public void LaneToDefendingRoleIsMirroredAcrossTheNet()
        {
            // The physically mirrored court means an attacker's lane 1 (their OH, per
            // LaneToRole) faces the defending team's OPP, not their OH -- lane 3 mirrors
            // the other way, and lane 2 (MB) stays put since the middle isn't mirrored.
            Assert.AreEqual(PlayerRole.Opp, PlayerRoleExtensions.LaneToDefendingRole[1]);
            Assert.AreEqual(PlayerRole.Mb, PlayerRoleExtensions.LaneToDefendingRole[2]);
            Assert.AreEqual(PlayerRole.Oh, PlayerRoleExtensions.LaneToDefendingRole[3]);
        }

        [Test]
        public void DisplayNameMatchesPythonEnumValues()
        {
            Assert.AreEqual("Setter", PlayerRole.Setter.DisplayName());
            Assert.AreEqual("OPP", PlayerRole.Opp.DisplayName());
            Assert.AreEqual("MB", PlayerRole.Mb.DisplayName());
            Assert.AreEqual("OH", PlayerRole.Oh.DisplayName());
            Assert.AreEqual("DS", PlayerRole.Ds.DisplayName());
            Assert.AreEqual("Libero", PlayerRole.Libero.DisplayName());
        }
    }
}
