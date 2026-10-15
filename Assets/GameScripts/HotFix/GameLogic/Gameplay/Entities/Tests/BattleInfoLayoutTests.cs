using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BattleInfoLayoutTests
    {
        [Test]
        public void PanelFitsBetweenBothBattleEdgesWithSixteenUnitMargin()
        {
            Assert.IsTrue(BattleInfoLayout.TryCalculate(new Rect(-300, 150, 600, 400),
                new Rect(-300, -450, 600, 400), new Rect(-540, -960, 1080, 1920), out var center, out var scale));
            Assert.AreEqual(new Vector2(0, 50), center);
            Assert.AreEqual(1f, scale);
            Assert.GreaterOrEqual(center.y - 78 * scale, -34);
            Assert.LessOrEqual(center.y + 78 * scale, 134);
        }

        [Test]
        public void NarrowGapAndOffsetCenterLimitScaleWithoutCoveringEitherBoard()
        {
            Assert.IsTrue(BattleInfoLayout.TryCalculate(new Rect(-200, 70, 600, 400),
                new Rect(-200, -450, 600, 400), new Rect(-270, -960, 540, 1920), out var center, out var scale));
            Assert.AreEqual(new Vector2(100, 10), center);
            Assert.That(scale, Is.EqualTo(308f / 1040f).Within(1e-6));
            Assert.LessOrEqual(center.x + 520 * scale, 254);
            Assert.GreaterOrEqual(center.y - 78 * scale, -34);
            Assert.LessOrEqual(center.y + 78 * scale, 54);
        }

        [Test]
        public void OverlappingBattlesHaveNoUsableInformationGap()
        {
            Assert.IsFalse(BattleInfoLayout.TryCalculate(new Rect(-300, 0, 600, 400),
                new Rect(-300, -300, 600, 400), new Rect(-540, -960, 1080, 1920), out _, out var scale));
            Assert.AreEqual(0, scale);
        }
    }
}
