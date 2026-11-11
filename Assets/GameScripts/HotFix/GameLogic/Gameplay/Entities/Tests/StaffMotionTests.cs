using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class StaffMotionTests
    {
        private static StaffConfig Config(float grow = 0, float settle = 0) => new StaffConfig("StaffBullet", .15f, .8f, 1.2f, 1.2f, .2f, grow, settle);
        private static StaffState State(float grow = 0, float settle = 0) => new StaffState(1, 1, default, Config(grow, settle), 3);

        [Test]
        public void AppearanceOvershootsThenSettlesBeforeMovement()
        {
            var s = State(.2f, .1f);
            Assert.AreEqual(0, s.AppearScale);
            s.Advance(.2f); Assert.That(s.AppearScale, Is.EqualTo(1.1f).Within(.0001f)); Assert.AreEqual(3, s.Progress);
            s.Advance(.1f); Assert.That(s.AppearScale, Is.EqualTo(1).Within(.0001f)); Assert.That(s.Progress, Is.EqualTo(3).Within(.0001f));
            s.Advance(.5f); Assert.That(s.Progress, Is.EqualTo(2.6f).Within(.0001f));
        }
        [Test]
        public void SweptContactPreventsCrossingAndKeepsClearance()
        {
            var s = State(); s.Advance(1);
            Assert.That(s.Constrain(1, 4, 0, 0, .3f), Is.EqualTo(1.8f).Within(.0001f));
        }
        [Test]
        public void FiniteWidthAllowsOutsideEnemyToPass()
        {
            var s = State(); s.Advance(1);
            Assert.AreEqual(4, s.Constrain(1, 4, 1, 1, .3f));
        }
        [Test]
        public void WidthBoundaryIncludesEnemyRadius()
        {
            var s = State(); s.Advance(1);
            Assert.That(s.Constrain(1, 4, .9f, .9f, .3f), Is.EqualTo(1.8f).Within(.0001f));
        }
        [Test]
        public void IsolatedEnemyCanBePushedWithoutAutonomousMovement()
        {
            var s = State(); s.Advance(1);
            Assert.That(s.Constrain(2, 2, 0, 0, .3f), Is.EqualTo(1.8f).Within(.0001f));
        }
        [Test]
        public void EnemyAlreadyAheadIsNotAttractedBack()
        {
            var s = State(); s.Advance(1);
            Assert.AreEqual(4, s.Constrain(3.5f, 4, 0, 0, .3f));
        }
        [Test]
        public void StaffStopsAtDistanceOrSpawnAndNeverMovesBelowZero()
        {
            var s = State(); s.Advance(10);
            Assert.AreEqual(StaffPhase.Finished, s.Phase); Assert.That(s.Progress, Is.EqualTo(1.8f).Within(.0001f));
            var nearSpawn = new StaffState(1, 2, default, Config(), .2f);
            nearSpawn.Advance(10); Assert.AreEqual(0, nearSpawn.Progress); Assert.AreEqual(StaffPhase.Finished, nearSpawn.Phase);
        }
        [Test]
        public void AppearanceBlocksWithFullLogicalSize()
        {
            var s = State(.2f, .1f); s.Advance(.05f);
            Assert.That(s.Constrain(2, 4, 0, 0, .3f), Is.EqualTo(2.6f).Within(.0001f));
        }
        [Test]
        public void RouteMappingPreservesCorrespondingSegmentAtCorner()
        {
            var center = new BattleRouteGeometry(new[] { Vector3.zero, Vector3.right, new Vector3(1, 2) });
            var individual = new BattleRouteGeometry(new[] { new Vector3(0, .2f), new Vector3(2, .2f), new Vector3(2, 4.2f) });
            Assert.AreEqual(2, individual.ConvertTo(center, 4));
            Assert.AreEqual(4, center.ConvertTo(individual, 2));
            Assert.AreEqual(new Vector3(1, 1), center.Position(2));
        }
        [Test]
        public void ZeroLengthCorrespondingSegmentIsFinite()
        {
            var route = new BattleRouteGeometry(new[] { Vector3.zero, Vector3.zero, Vector3.right });
            Assert.AreEqual(Vector3.zero, route.Position(0)); Assert.AreEqual(.5f, route.ConvertTo(route, .5f));
        }
    }
}
