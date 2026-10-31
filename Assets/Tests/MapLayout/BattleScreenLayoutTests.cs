using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BattleScreenLayoutTests
    {
        [TestCase(1080, 1920)]
        [TestCase(1080, 2380)]
        [TestCase(540, 1190)]
        public void EntireWorldBoundsProjectInsideAvailableArea(int width, int height)
        {
            var screen = new Vector2(width, height);
            var unit = width / 1080f;
            var area = Rect.MinMaxRect(16 * unit, 274 * unit, width - 16 * unit, height - 16 * unit);
            var bounds = new Bounds(new Vector3(-0.03f, 0.7f, 0), new Vector3(5.52f, 8.61f, 0));
            Vector2 center;
            float size;
            Assert.IsTrue(Calculate(screen, area, bounds, out center, out size));
            var pixelsPerUnit = height / (2f * size);
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
                foreach (var y in new[] { bounds.min.y, bounds.max.y })
                {
                    var point = (new Vector2(x, y) - center) * pixelsPerUnit + screen * 0.5f;
                    Assert.That(point.x, Is.InRange(area.xMin - 0.001f, area.xMax + 0.001f));
                    Assert.That(point.y, Is.InRange(area.yMin - 0.001f, area.yMax + 0.001f));
                }
        }

        [Test]
        public void SameAspectAndScaledSafeAreaProduceSameCamera()
        {
            var bounds = new Bounds(Vector3.one, new Vector3(5.5f, 8.6f, 0));
            Vector2 first, second;
            float firstSize, secondSize;
            Assert.IsTrue(Calculate(new Vector2(1080, 2380), new Rect(40, 350, 1000, 1900), bounds, out first, out firstSize));
            Assert.IsTrue(Calculate(new Vector2(540, 1190), new Rect(20, 175, 500, 950), bounds, out second, out secondSize));
            Assert.That(Vector2.Distance(first, second), Is.LessThan(0.00001f));
            Assert.That(firstSize, Is.EqualTo(secondSize).Within(0.00001f));
        }

        [Test]
        public void InvalidOrEmptyInputDoesNotProduceCameraValues()
        {
            Vector2 center;
            float size;
            Assert.IsFalse(Calculate(Vector2.zero, new Rect(0, 0, 100, 100), new Bounds(Vector3.zero, Vector3.one), out center, out size));
            Assert.IsFalse(Calculate(new Vector2(1080, 1920), new Rect(0, 0, 0, 100), new Bounds(Vector3.zero, Vector3.one), out center, out size));
        }

        private static bool Calculate(Vector2 screen, Rect area, Bounds bounds, out Vector2 center, out float size)
        {
            return BattleScreenLayout.TryCalculate(screen, area, bounds, out center, out size);
        }
    }
}
