using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace GameLogic.Tests
{
    public sealed class StoreIsolationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        [TearDown] public void TearDown() => _f?.Dispose();

        [Test]
        public void ExportedContainerSkillsAreEnabledAndCaptureZeroDamageIsolation()
        {
            var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1607, out _), "保鲜盒普通攻击应启用");
            Assert.IsTrue(catalog.TryGet(1602, out _));
            Assert.IsTrue(catalog.TryGetLevel(1602, 1, out var level));
            Assert.AreEqual(0, level.Parameters["damage"]);
            Assert.AreEqual(2f, level.HitBuffs.Single(b => b.BuffId == 2600).Duration);
            Assert.IsTrue(catalog.TryGetBullet(3602, out var bullet));
            Assert.AreEqual("RobotBullet", bullet.Prefab);
        }

        [Test]
        public void IsolationStopsOnlySelfMovementAndLargeFrameResumesAtExpiry()
        {
            _f = new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            var view = _f.PlayerView;
            _f.Context.Registry.TryGet(view.InstanceId, out var entity);
            var enemy = (EnemyEntity)entity;
            var start = view.transform.position;
            _f.Context.Battle.AddBuff(enemy, new BuffModel(2600, "store_isolation"),
                _f.Context.Battle.Player.OwnerRef, 2f);
            Assert.AreEqual(enemy.BaseSpeed, enemy.MoveSpeed, "装盒不能改写减速属性");
            _f.Advance(3f);
            Assert.That(Vector3.Distance(start, view.transform.position), Is.EqualTo(enemy.BaseSpeed).Within(0.00001f));
            Assert.AreEqual(0, enemy.Buffs.Count);
        }

        [Test]
        public void SameCasterRefreshesAndDifferentCastersExpireIndependentlyWithoutRestartingAnimation()
        {
            Prepare(); var enemy = Enemy(); var changes = new List<bool>();
            _f.Context.Battle.EnemyIsolationChanged += (owner, active, prefab) => changes.Add(active);
            Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            _f.Advance(1);
            var closed = Box().GetComponentInChildren<SpriteRenderer>().sprite;
            Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            Assert.AreEqual(1, enemy.Buffs.Count);
            Assert.AreEqual(closed, Box().GetComponentInChildren<SpriteRenderer>().sprite);
            Isolate(enemy, _f.Context.Battle.Opponent.OwnerRef, 3);
            Assert.AreEqual(2, enemy.Buffs.Count);
            _f.Advance(2);
            Assert.IsTrue(enemy.IsIsolated); Assert.AreEqual(1, enemy.Buffs.Count);
            CollectionAssert.AreEqual(new[] { true }, changes);
            _f.Advance(1);
            Assert.IsFalse(enemy.IsIsolated);
            CollectionAssert.AreEqual(new[] { true, false }, changes);
            Assert.AreEqual(1, Controller.IsolationEffectCount, "开盒尚未结束");
            _f.Advance(0.34f); Assert.AreEqual(0, Controller.IsolationEffectCount);
        }

        [Test]
        public void ZeroDamageProjectileAppliesIsolationWhileCancelledHitDoesNot()
        {
            Prepare(); _f.Context.Battle.Configure(SkillConfigAdapter.FromTables(_f.Tables));
            var enemy = Enemy(); var hp = enemy.CurrentHp;
            var source = _f.Context.Registry.CreateRobotFromConfig(_f.Tables.TbRobot.Get(703), BattleSide.Player, 1);
            source.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var action = new CombatActionSnapshot(_f.Context.BattleId, source.InstanceId, source.Side, 1602,
                new[] { enemy.InstanceId }, CombatOutcomeTag.SingleDamage, 0, Vector3.zero, Vector3.zero, 1,
                hitBuffs: new[] { new CombatBuffApplication(2600, 2f) });
            _f.Context.Combat.ResolveProjectileHit(_f.Context.Combat.CaptureProjectileAttack(action), enemy.InstanceId);
            Assert.AreEqual(hp, enemy.CurrentHp); Assert.IsTrue(enemy.IsIsolated);
            Assert.AreEqual(source.OwnerRef, enemy.Buffs.Items.Single().Source);
            enemy.Buffs.Clear();
            enemy.Buffs.Add(new BuffModel(987, "test"), source.OwnerRef, 5, behavior: new CancelHit());
            _f.Context.Combat.ResolveProjectileHit(_f.Context.Combat.CaptureProjectileAttack(action), enemy.InstanceId);
            Assert.IsFalse(enemy.IsIsolated);
        }

        [Test]
        public void SlowCanExpireDuringIsolationAndForcedMovementKeepsBoxInForeground()
        {
            Prepare(); _f.Advance(2); var enemy = Enemy(); var view = _f.PlayerView;
            _f.Context.Battle.AddBuff(enemy, new BuffModel(2505, "slow"), _f.Context.Battle.Player.OwnerRef, 1,
                new Dictionary<string, float> { { "slow", 0.4f } });
            Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            var atIsolation = view.transform.position;
            _f.Advance(1);
            Assert.AreEqual(atIsolation, view.transform.position); Assert.AreEqual(enemy.BaseSpeed, enemy.MoveSpeed);
            Assert.IsTrue(Controller.TryKnockback(BattleSide.Player, enemy.InstanceId, 0.3f));
            Assert.That(Vector3.Distance(atIsolation, view.transform.position), Is.EqualTo(0.3f).Within(0.00001));
            Assert.AreEqual(view.GetVisualBounds().center, Box().transform.position);
            view.transform.position = new Vector3(-3, -1.8f, 0);
            _f.Advance(0);
            view.GetVisualSorting(out var layer, out var order);
            var renderer = Box().GetComponentInChildren<SpriteRenderer>();
            Assert.AreEqual(layer, renderer.sortingLayerID); Assert.AreEqual(order + 1, renderer.sortingOrder);
        }

        [Test]
        public void PoisonContinuesWhileIsolatedAndLethalTickRecyclesBox()
        {
            Prepare(); var enemy = Enemy(); Isolate(enemy, _f.Context.Battle.Player.OwnerRef, 5);
            var source = _f.Context.Registry.CreateRobotFromConfig(_f.Tables.TbRobot.Get(603), BattleSide.Player, 1);
            _f.Context.Battle.AddBuff(enemy, new BuffModel(2507, "poison"), source.OwnerRef, 5,
                new Dictionary<string, float> { { "poisonDamage", 50 }, { "poisonInterval", 1 },
                    { "poisonMaxLayers", 3 }, { "skillId", 1507 } });
            var start = _f.PlayerView.transform.position;
            _f.Advance(1); Assert.AreEqual(50, enemy.CurrentHp);
            Assert.AreEqual(start, _f.PlayerView.transform.position);
            var id = enemy.InstanceId; _f.Advance(1);
            Assert.IsFalse(_f.Context.Registry.TryGet(id, out _));
            Assert.AreEqual(0, Controller.IsolationEffectCount);
        }

        [Test]
        public void PauseStopsLifetimeAndReapplyingDuringOpeningStartsClosingAgain()
        {
            Prepare(); var enemy = Enemy(); Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            _f.Advance(0.2f); var sprite = Box().GetComponentInChildren<SpriteRenderer>().sprite;
            _f.Host.SetFrozen(true); _f.Advance(10);
            Assert.That(enemy.Buffs.Items[0].Remaining, Is.EqualTo(1.8f).Within(0.00001));
            Assert.AreEqual(sprite, Box().GetComponentInChildren<SpriteRenderer>().sprite);
            _f.Host.SetFrozen(false); _f.Advance(1.9f);
            Assert.IsFalse(enemy.IsIsolated); Assert.IsNotNull(Box());
            Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            Assert.IsTrue(enemy.IsIsolated); Assert.AreEqual(1, Controller.IsolationEffectCount);
            _f.Advance(2.34f); Assert.AreEqual(0, Controller.IsolationEffectCount);
            _f.Clear(); Assert.AreEqual(0, _f.Root.GetComponentsInChildren<StoreIsolationEffectView>(true).Length);
        }

        [Test]
        public void IsolationDelaysTowerArrivalByFullDuration()
        {
            _f = new EnemyBattleControllerTests.Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            _f.SetRoutes(new[] { new Vector3(-0.81f, 0, 0) }); _f.Prepare(); _f.Start();
            var travel = (double)typeof(EnemyBattleController).GetMethod("NextArrivalDelay",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Controller, null);
            var enemy = Enemy(); Isolate(enemy, _f.Context.Battle.Player.OwnerRef, 2, false);
            _f.Advance(2f + (float)travel - 0.01f);
            Assert.IsTrue(_f.Context.Registry.TryGet(enemy.InstanceId, out _));
            Assert.AreEqual(5, _f.Host.Towers.PlayerTower.CurrentHp);
            _f.Advance(0.02f); Assert.AreEqual(4, _f.Host.Towers.PlayerTower.CurrentHp);
        }

        [Test]
        public void LateLoadedEffectSamplesCurrentOpeningTimeAndDoesNotRestartControl()
        {
            var pending = new UniTaskCompletionSource<GameObject>();
            Prepare((name, parent, token) => pending.Task);
            var enemy = Enemy(); Isolate(enemy, _f.Context.Battle.Player.OwnerRef);
            Assert.IsTrue(enemy.IsIsolated); Assert.AreEqual(0, Controller.IsolationEffectCount);
            _f.Advance(2.2f); Assert.IsFalse(enemy.IsIsolated);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/ContainerBullet.prefab");
            pending.TrySetResult(UnityEngine.Object.Instantiate(prefab, _f.Root.transform));
            var expected = UnityEngine.Object.Instantiate(prefab);
            try
            {
                expected.GetComponent<StoreIsolationEffectView>().SampleState(false, 0.2f);
                Assert.AreEqual(expected.GetComponentInChildren<SpriteRenderer>().sprite,
                    Box().GetComponentInChildren<SpriteRenderer>().sprite);
            }
            finally { UnityEngine.Object.DestroyImmediate(expected); }
            _f.Advance(0.14f); Assert.AreEqual(0, Controller.IsolationEffectCount);
        }

        [Test]
        public void LateLoadedEffectAfterClearIsReleasedAndCannotAttachToNewBattle()
        {
            var pending = new UniTaskCompletionSource<GameObject>();
            Prepare((name, parent, token) => pending.Task);
            Isolate(Enemy(), _f.Context.Battle.Player.OwnerRef);
            _f.Clear();
            var late = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/bullets/ContainerBullet.prefab"), _f.Root.transform);
            pending.TrySetResult(late);
            Assert.IsTrue(late == null, "终局后完成的加载必须释放");
            Assert.AreEqual(0, Controller.IsolationEffectCount);
        }

        [TestCase(1f, 1f)]
        [TestCase(2f, 0.75f)]
        public void ClosedBoxCoversEnemySpriteWithTenPercentPaddingAndDoesNotAccumulateScale(float x, float y)
        {
            Prepare(); var target = _f.PlayerView;
            target.transform.localScale = Vector3.Scale(target.transform.localScale, new Vector3(x, y, 1));
            Isolate(Enemy(), _f.Context.Battle.Player.OwnerRef);
            _f.Advance(0.34f);
            var box = Box(); var renderer = box.GetComponentInChildren<SpriteRenderer>();
            var enemySize = target.GetVisualBounds().size;
            Assert.GreaterOrEqual(renderer.bounds.size.x + 0.00001f, enemySize.x * 1.1f);
            Assert.GreaterOrEqual(renderer.bounds.size.y + 0.00001f, enemySize.y * 1.1f);
            Assert.That(Mathf.Min(renderer.bounds.size.x / enemySize.x, renderer.bounds.size.y / enemySize.y),
                Is.EqualTo(1.1f).Within(0.0001f), "按较大比例统一缩放，避免无限放大");
            var scale = box.transform.localScale;
            for (var i = 0; i < 5; i++) _f.Advance(0);
            Assert.AreEqual(scale, box.transform.localScale);
            target.transform.localScale *= 0.5f; _f.Advance(0);
            Assert.That(box.transform.localScale.x, Is.EqualTo(scale.x * 0.5f).Within(0.00001f));
        }

        private EnemyBattleController Controller => (EnemyBattleController)_f.Controller;
        private EnemyEntity Enemy() { _f.Context.Registry.TryGet(_f.PlayerView.InstanceId, out var e); return (EnemyEntity)e; }
        private StoreIsolationEffectView Box() => _f.Root.GetComponentsInChildren<StoreIsolationEffectView>().Single();
        private void Isolate(EnemyEntity enemy, SkillOwnerRef source, float duration = 2, bool visual = true) =>
            _f.Context.Battle.AddBuff(enemy, new BuffModel(2600, "store_isolation", effectPrefab: visual ? "ContainerBullet" : null), source, duration);
        private void Prepare(Func<string, Transform, CancellationToken, UniTask<GameObject>> effectLoad = null)
        {
            _f = new EnemyBattleControllerTests.Fixture();
            _f.Prepare((name, parent, token) => name == "ContainerBullet"
                ? effectLoad != null ? effectLoad(name, parent, token)
                    : UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AssetRaw/Actor/Prefabs/bullets/ContainerBullet.prefab"), parent)) : _f.Load(name, parent, token));
            _f.Start();
        }
        private sealed class CancelHit : BuffBehavior
        {
            public override void OnBeHurt(BuffContainer container, BuffObj buff, CombatDamageContext context) => context.Outcome.Cancel();
        }
    }
}
