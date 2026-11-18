#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameConfig.bullet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class LaserBeamTests
    {
        [Test]
        public void BeamHitsEveryAlignedEnemyOnlyOnceAfterCompression()
        {
            var world = new World();
            for (var i = 1; i <= 12; i++) world.Add(i, i);
            world.Add(100, 4, 2);
            world.Add(101, -1);
            world.Add(102, 21);
            var hits = new List<int>();
            var beam = Beam(world, (state, id) => hits.Add(id));
            Assert.IsFalse(beam.Advance(0.04f));
            Assert.IsEmpty(hits);
            Assert.IsFalse(beam.Advance(0.05f));
            CollectionAssert.AreEqual(Enumerable.Range(1, 12), hits);
            Assert.AreEqual(Vector3.zero, beam.Position, "光束起点不沿路径飞行。");
            Assert.AreEqual(new Vector3(20, 0), beam.BeamEnd);
            Assert.IsFalse(beam.Advance(0.3f));
            Assert.AreEqual(12, hits.Count);
            Assert.IsTrue(beam.Advance(1f));
            Assert.AreEqual(12, hits.Count);
        }

        [Test]
        public void BeamUsesThePositionsAtTheHitMomentAndDoesNotHitLateEntrants()
        {
            var world = new World(); world.Add(1, 3, 2); world.Add(2, 5, 2);
            var hits = new List<int>();
            var beam = Beam(world, (state, id) => hits.Add(id));
            beam.Advance(0.04f);
            world.Targets[0] = new BulletTarget(1, new Vector3(3, 0), 0.1f);
            beam.Advance(0.05f);
            CollectionAssert.AreEqual(new[] { 1 }, hits);
            world.Targets[1] = new BulletTarget(2, new Vector3(5, 0), 0.1f);
            beam.Advance(0.4f);
            CollectionAssert.AreEqual(new[] { 1 }, hits);
        }

        [Test]
        public void BeamPauseFreezesBothHitTimingAndVisualLifetime()
        {
            var world = new World(); world.Add(1, 3);
            var hits = new List<int>();
            var beam = Beam(world, (state, id) => hits.Add(id));
            world.Running = false;
            Assert.IsFalse(beam.Advance(10));
            Assert.AreEqual(0, beam.Elapsed); Assert.IsEmpty(hits);
            world.Running = true;
            beam.Advance(0.1f);
            Assert.AreEqual(1, hits.Count);
            world.Running = false;
            beam.Advance(10);
            Assert.That(beam.Elapsed, Is.EqualTo(0.1f).Within(0.00001f));
            Assert.IsFalse(beam.IsResolved);
        }

        [Test]
        public void BeamRevalidatesVictimsAndStopsImmediatelyWhenBattleEnds()
        {
            var world = new World(); world.Add(1, 3); world.Add(2, 5); world.Add(3, 7);
            var hits = new List<int>();
            var beam = Beam(world, (state, id) => { hits.Add(id); world.Alive.Remove(2); if (id == 3) world.Valid = false; });
            Assert.IsTrue(beam.Advance(1));
            CollectionAssert.AreEqual(new[] { 1, 3 }, hits);
            world = new World(); world.Add(1, 3); world.Add(2, 5); hits.Clear();
            beam = Beam(world, (state, id) => { hits.Add(id); world.Valid = false; });
            Assert.IsTrue(beam.Advance(1));
            CollectionAssert.AreEqual(new[] { 1 }, hits);
        }

        [Test]
        public void BeamCanResumeFrozenVictimsAfterAPauseInsideAHitCallback()
        {
            var world = new World(); world.Add(1, 3); world.Add(2, 5);
            var hits = new List<int>();
            var beam = Beam(world, (state, id) => { hits.Add(id); if (id == 1) world.Running = false; });
            Assert.IsFalse(beam.Advance(0.1f));
            CollectionAssert.AreEqual(new[] { 1 }, hits);
            world.Running = true; beam.Advance(0.1f);
            CollectionAssert.AreEqual(new[] { 1, 2 }, hits);
        }

        [TestCase(1f, 0f, 20f, 0f)]
        [TestCase(0f, 1f, 0f, 10f)]
        [TestCase(1f, 1f, 10f, 10f)]
        [TestCase(1f, -1f, 10f, -10f)]
        public void BeamLengthClipsToTheFiringSidesBoundary(float x, float y, float endX, float endY)
        {
            var world = new World();
            var beam = Beam(world, (state, id) => { }, new Vector3(x, y));
            Assert.That(Vector3.Distance(new Vector3(endX, endY), beam.BeamEnd), Is.LessThan(0.0001f));
        }

        [Test]
        public void ZeroLengthBeamCannotHitTargetsBehindTheBoundary()
        {
            var world = new World(); world.Add(1, 0);
            var hits = 0;
            var beam = Beam(world, (state, id) => hits++, Vector3.left);
            Assert.IsTrue(beam.Advance(1)); Assert.AreEqual(0, hits);
        }

        [Test]
        public void RealLaserPrefabUsesTheProvidedFramesAndPreservesTheMuzzleAndEndAnchors()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/ScatterLaserBeam.prefab");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animation = instance.GetComponentInChildren<BattleSpriteAnimation>();
                Assert.IsNotNull(animation); Assert.IsNotNull(instance.GetComponent<LaserBeamView>());
                var clip = animation.Clip;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip,
                    new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" });
                Assert.AreEqual(16, keys.Length);
                Assert.That(clip.length, Is.EqualTo(16f / 24f).Within(0.00001f));
                var beam = Beam(new World(), (state, id) => { });
                instance.GetComponent<RobotBulletView>().Bind(beam);
                var art = animation.transform;
                Assert.That(Vector3.Distance(Vector3.zero, art.position), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(beam.BeamEnd, art.TransformPoint(new Vector3(-3.76f, 0))), Is.LessThan(0.0001f));
                beam.Advance(0.1f); instance.GetComponent<RobotBulletView>().Present(beam);
                var sprite = art.GetComponent<SpriteRenderer>().sprite;
                Assert.AreEqual("ScatterLaser_02", sprite.name);
                Assert.That(sprite.pivot.x, Is.EqualTo(444f).Within(0.01f));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static BulletState Beam(World world, Action<BulletState, int> hit, Vector3? direction = null)
        {
            var model = new BulletModel(3501, "ScatterLaserBeam", BulletFlightType.Beam, false,
                BulletHitMode.PierceEnemies, 0, 16f / 24f, 0, 0.1f);
            var action = new CombatActionSnapshot(1, 1, BattleSide.Player, 1501, Array.Empty<int>(),
                CombatOutcomeTag.SingleDamage, 20, Vector3.zero, Vector3.right, 1);
            return new BulletState(new BulletLauncher(model, action, direction ?? Vector3.right,
                beamHitDelay: 2f / 24f), world, hit);
        }

        private sealed class World : IProjectileWorld
        {
            internal bool Running = true, Valid = true;
            internal readonly List<BulletTarget> Targets = new List<BulletTarget>();
            internal readonly HashSet<int> Alive = new HashSet<int>();
            internal void Add(int id, float x, float y = 0)
            { Targets.Add(new BulletTarget(id, new Vector3(x, y), 0.1f)); Alive.Add(id); }
            public bool IsValid(long battleId) => Valid && battleId == 1;
            public bool IsRunning => Running;
            public Bounds GetBattleBounds(BattleSide side) => new Bounds(new Vector3(10, 0), new Vector3(20, 20, 0));
            public IReadOnlyList<BulletTarget> GetTargets(BattleSide side) => Targets;
            public bool TryGetPosition(BattleSide side, int id, out Vector3 position)
            { position = Targets.Find(t => t.InstanceId == id).Position; return Alive.Contains(id); }
            public bool TryGetDeathPosition(BattleSide side, int id, out Vector3 position)
            { position = default; return false; }
        }
    }
}
#endif
