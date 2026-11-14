using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ParabolicTrajectoryTests
    {
        [Test]
        public void ReferenceShotUsesApprovedOffsetPeakAndVerticalLanding()
        {
            var target = new Vector3(6f, 0f);
            var path = new ParabolicTrajectory(Vector3.zero, target, 1.9f);
            var peak = path.Evaluate(target, path.ApexProgress, out var peakDirection);
            Assert.That(Vector3.Distance(new Vector3(4.98f, 1.9f), peak), Is.LessThan(0.00001f));
            Assert.That(path.ApexProgress, Is.EqualTo(0.44f / 0.567041434f).Within(0.0002f),
                "与已确认GIF的B档上升/下坠时间比例一致。");
            Assert.That(Vector3.Dot(peakDirection, Vector3.right), Is.GreaterThan(0.9999f));
            Assert.AreEqual(target, path.Evaluate(target, 1f, out var endDirection));
            Assert.AreEqual(Vector3.down, endDirection);
        }

        [TestCase(6f)]
        [TestCase(-6f)]
        [TestCase(0f)]
        public void PeakRetainsMotionAndTangentOnBothSides(float x)
        {
            var target = new Vector3(x, x == 0f ? 4f : 0f);
            var path = new ParabolicTrajectory(Vector3.zero, target, 1.9f);
            const float step = 0.001f;
            var before = path.Evaluate(target, path.ApexProgress - step, out var incoming);
            var peak = path.Evaluate(target, path.ApexProgress, out var tangent);
            var after = path.Evaluate(target, path.ApexProgress + step, out var outgoing);
            var incomingSpeed = Vector3.Distance(before, peak) / step;
            var outgoingSpeed = Vector3.Distance(peak, after) / step;
            Assert.That(incomingSpeed, Is.GreaterThan(0.1f));
            Assert.That(outgoingSpeed / incomingSpeed, Is.InRange(0.92f, 1.08f),
                "不能在最高点降到零速后重新起步。");
            // 竖直目标的顶点曲率很大，在更小邻域内检查速度与切线的极限连续性。
            const float tangentStep = 0.00001f;
            before = path.Evaluate(target, path.ApexProgress - tangentStep, out incoming);
            after = path.Evaluate(target, path.ApexProgress + tangentStep, out outgoing);
            Assert.That(Vector3.Distance(peak, after) / Vector3.Distance(before, peak), Is.InRange(0.92f, 1.08f));
            Assert.That(Vector3.Dot(incoming, tangent), Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(outgoing, tangent), Is.GreaterThan(0.99f));
        }

        [Test]
        public void DescentAcceleratesToRecommendedLandingSpeed()
        {
            var target = new Vector3(6f, 0f);
            var path = new ParabolicTrajectory(Vector3.zero, target, 1.9f);
            var fall = 1f - path.ApexProgress;
            var ascentSpeed = SampleSpeed(path, target, 0.3f);
            var first = SampleSpeed(path, target, path.ApexProgress + fall * 0.1f);
            var middle = SampleSpeed(path, target, path.ApexProgress + fall * 0.5f);
            var last = SampleSpeed(path, target, path.ApexProgress + fall * 0.9f);
            var landing = SampleSpeed(path, target, 0.999f);
            Assert.That(middle, Is.GreaterThan(first));
            Assert.That(last, Is.GreaterThan(middle));
            Assert.That(landing / ascentSpeed, Is.EqualTo(2.3f).Within(0.07f));
        }

        [TestCase(6f, 0f, 1.9f)]
        [TestCase(-6f, 0f, 1.9f)]
        [TestCase(0f, 4f, 1f)]
        [TestCase(0f, -4f, 1f)]
        [TestCase(6f, 3f, 1f)]
        [TestCase(-6f, -3f, 1f)]
        [TestCase(6f, 0f, 0f)]
        [TestCase(0f, 0f, 0f)]
        public void MirroredElevatedAndDegenerateShotsRemainFiniteAndArrive(float x, float y, float height)
        {
            var target = new Vector3(x, y);
            var path = new ParabolicTrajectory(Vector3.zero, target, height);
            Assert.AreEqual(Vector3.zero, path.Evaluate(target, 0f, out _));
            for (var i = 0; i <= 100; i++)
            {
                var point = path.Evaluate(target, i / 100f, out var direction);
                Assert.IsFalse(float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude));
                Assert.IsFalse(float.IsNaN(direction.sqrMagnitude) || float.IsInfinity(direction.sqrMagnitude));
            }
            Assert.AreEqual(target, path.Evaluate(target, 1f, out _));
        }

        [Test]
        public void TrackingLandingCanMoveWithoutRestartingTheLaunchClock()
        {
            var initial = new Vector3(6f, 0f);
            var path = new ParabolicTrajectory(Vector3.zero, initial, 1.9f);
            var moved = new Vector3(7f, 1f);
            var oldPoint = path.Evaluate(initial, 0.8f, out _);
            var newPoint = path.Evaluate(moved, 0.8f, out _);
            Assert.That(newPoint.x, Is.GreaterThan(oldPoint.x));
            Assert.That(newPoint.y, Is.GreaterThan(oldPoint.y));
            Assert.AreEqual(moved, path.Evaluate(moved, 1f, out _));
        }

        private static float SampleSpeed(ParabolicTrajectory path, Vector3 target, float progress)
        {
            const float step = 0.0001f;
            var before = path.Evaluate(target, progress - step, out _);
            var after = path.Evaluate(target, progress + step, out _);
            return Vector3.Distance(before, after) / (2f * step);
        }
    }
}
