using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BattleSplitLayoutTests
    {
        private static readonly Bounds Player = new Bounds(new Vector3(0, -2, 0), new Vector3(5.5f, 4.4f, 0));
        private static readonly Bounds Opponent = new Bounds(new Vector3(0, 3, 0), new Vector3(4.1f, 3.3f, 0));

        [Test]
        public void ReferenceKeepsInfoAtPrefabSizeWithoutShrinkingPlayerOnNormalScreen()
        {
            var player = new Bounds(new Vector3(-0.03f, -1.354f, 0), new Vector3(5.512f, 4.512f, 0));
            var opponent = new Bounds(new Vector3(-0.07f, 3.35f, 0), new Vector3(4.134f, 3.306f, 0));
            float ppu, infoScale;
            Assert.IsTrue(BattleScreenLayout.TryCalculateReference(player, opponent, new Vector2(1056, 246), out ppu, out infoScale));
            Assert.AreEqual(1f, infoScale, "信息栏不应继承V1战场空隙产生的约0.7倍缩放。");
            foreach (var height in new[] { 1920, 2160, 2380 })
            {
                BattleScreenLayoutResult result;
                Assert.IsTrue(BattleScreenLayout.TryCalculateSplit(new Vector2(1080, height), new Rect(0, 0, 1080, height),
                    new Rect(12, 12, 1056, 246), 1, player, opponent, ppu, infoScale, 1, out result));
                Assert.AreEqual(new Vector2(1040, 156), result.InfoTarget.size);
                Assert.That(result.PlayerPixelsPerUnit, Is.EqualTo(ppu).Within(0.00001f), "普通屏幕应由敌方让出信息栏空间，我方保留基准。");
            }
        }

        [Test]
        public void SameWidthKeepsPlayerAndLongScreenEnlargesOpponentUntilCapped()
        {
            var normal = Calculate(1080, 1920);
            var longer = Calculate(1080, 2380);
            var tallest = Calculate(1080, 4000);
            Assert.AreEqual(Read<Rect>(normal, "PlayerTarget").size, Read<Rect>(longer, "PlayerTarget").size);
            Assert.That(Read<Rect>(longer, "OpponentTarget").width, Is.GreaterThan(Read<Rect>(normal, "OpponentTarget").width));
            var player = Read<Rect>(tallest, "PlayerTarget");
            var opponent = Read<Rect>(tallest, "OpponentTarget");
            Assert.That(opponent.width, Is.LessThanOrEqualTo(player.width + 0.001f));
            Assert.That(opponent.height, Is.LessThanOrEqualTo(player.height + 0.001f));
            Assert.That(Mathf.Min(player.width / opponent.width, player.height / opponent.height), Is.EqualTo(1f).Within(0.00001f));
            Assert.That(opponent.yMin, Is.EqualTo(Read<Rect>(tallest, "InfoTarget").yMax + 16).Within(0.001f));
            Assert.That(opponent.yMax, Is.LessThan(3984));
        }

        [Test]
        public void HalfResolutionScalesEveryTargetAndKeepsSquareCells()
        {
            var full = Calculate(1080, 2380);
            var half = Calculate(540, 1190);
            foreach (var name in new[] { "PlayerTarget", "OpponentTarget", "InfoTarget", "PlayerArea", "OpponentArea" })
            {
                var a = Read<Rect>(full, name);
                var b = Read<Rect>(half, name);
                Assert.That(Vector2.Distance(a.position * 0.5f, b.position), Is.LessThan(0.001f));
                Assert.That(Vector2.Distance(a.size * 0.5f, b.size), Is.LessThan(0.001f));
            }
            var rect = Read<Rect>(full, "PlayerTarget");
            Assert.That(rect.width / Player.size.x, Is.EqualTo(rect.height / Player.size.y).Within(0.001f));
        }

        [Test]
        public void NarrowSafeAreaAndShortScreenFitAllRegions()
        {
            var result = Calculate(1080, 1450, new Rect(100, 50, 880, 1300));
            var p = Read<Rect>(result, "PlayerTarget");
            var e = Read<Rect>(result, "OpponentTarget");
            Assert.That(p.xMin, Is.GreaterThanOrEqualTo(116));
            Assert.That(e.yMax, Is.LessThanOrEqualTo(1334.001f));
            Assert.That(p.yMin, Is.EqualTo(274).Within(0.001f));
            Assert.That(e.yMin, Is.GreaterThan(Read<Rect>(result, "InfoTarget").yMax));
            Assert.That(p.width, Is.LessThan(5.5f * 190));
        }

        [TestCase(1080, 1920)]
        [TestCase(1080, 2160)]
        [TestCase(540, 1190)]
        public void PlayerViewportIncludesFullInformationBarWithoutMovingTheBoard(int width, int height)
        {
            var result = Calculate(width, height);
            var viewport = Read<Rect>(result, "PlayerViewport");
            var info = Read<Rect>(result, "InfoTarget");
            var target = Read<Rect>(result, "PlayerTarget");
            var ppu = Read<float>(result, "PlayerPixelsPerUnit");
            Assert.That(viewport.yMax, Is.GreaterThanOrEqualTo(info.yMax));
            Vector2 center;
            float size;
            Assert.IsTrue(BattleScreenLayout.TryCalculateCamera(viewport, target, Player, ppu, out center, out size));
            var projected = ((Vector2)Player.center - center) * ppu + viewport.center;
            Assert.That(Vector2.Distance(projected, target.center), Is.LessThan(0.001f));
            Assert.That(viewport.height / (2 * size), Is.EqualTo(ppu).Within(0.001f));
        }

        private static object Calculate(int width, int height, Rect? safe = null)
        {
            var method = typeof(BattleScreenLayout).GetMethod("TryCalculateSplit", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "V2需要独立分区计算入口。");
            var q = width / 1080f;
            var args = new object[] { new Vector2(width, height), safe ?? new Rect(0, 0, width, height),
                new Rect(12*q, 12*q, 1056*q, 246*q), q, Player, Opponent, 190f, 1f, 1f, null };
            Assert.IsTrue((bool)method.Invoke(null, args));
            return args[9];
        }

        private static T Read<T>(object result, string name) => (T)result.GetType().GetProperty(name).GetValue(result);
    }
}
