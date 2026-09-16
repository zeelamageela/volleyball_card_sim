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
