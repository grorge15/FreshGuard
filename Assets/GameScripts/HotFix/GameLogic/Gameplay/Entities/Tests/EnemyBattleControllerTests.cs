using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyBattleControllerTests
    {
        private Fixture _fixture;

        [TearDown] public void TearDown() { _fixture?.Dispose(); _fixture = null; }

        [Test]
        public void BothSidesChooseIndependentP1AndP2PositionsForInitialAndLaterEnemies()
        {
            var f = Create(false);
            SetField(f.Controller, "_playerP1Radius", 0.2f);
            SetField(f.Controller, "_playerP2Radius", 0.4f);
            SetField(f.Controller, "_opponentP1Radius", 0.3f);
            SetField(f.Controller, "_opponentP2Radius", 0.6f);
            f.Prepare(); f.Start();
            var samples = new System.Collections.Generic.List<Vector3>();
            for (var batch = 0; batch < 2; batch++)
            {
                if (batch > 0) f.Advance(0.5f);
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var view = f.Views.Where(v => f.Side(v) == side).OrderBy(v => v.InstanceId).Last();
                    var points = GetEnemyRoute(f, view);
                    for (var p = 1; p <= 2; p++)
                    {
                        var center = new Vector3((p == 1 ? -3f : -1f) + (side == BattleSide.Player ? 0f : 10f), 0f, 0f);
                        var offset = points[p] - center;
                        var radius = (side == BattleSide.Player ? 0.2f : 0.3f) * p;
                        Assert.That(offset.magnitude, Is.InRange(0.000001f, radius + 0.00001f));
                        Assert.AreEqual(0f, offset.z);
                        samples.Add(offset);
                    }
                    Assert.AreEqual(new Vector3(side == BattleSide.Player ? -3f : 7f, 1f, 0f), points[0]);
                }
            }
            Assert.AreEqual(8, samples.Distinct().Count(), "每只怪物、每个节点应独立采样");
        }

        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void MovementAndKnockbackFollowTheSameSampledWaypointRoute(BattleSide side)
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            var f = _fixture;
            SetField(f.Controller, side == BattleSide.Player ? "_playerP1Radius" : "_opponentP1Radius", 0.15f);
            SetField(f.Controller, side == BattleSide.Player ? "_playerP2Radius" : "_opponentP2Radius", 0.25f);
            f.Prepare(); f.Start();
            var view = f.Views.Single(v => f.Side(v) == side);
            var points = GetEnemyRoute(f, view);
            var firstLength = Vector3.Distance(points[0], points[1]);
            var secondLength = Vector3.Distance(points[1], points[2]);
            f.Context.Registry.TryGet(view.InstanceId, out var entity);
            var enemy = (EnemyEntity)entity;
            f.Advance(firstLength / enemy.MoveSpeed);
            Assert.That(Vector3.Distance(view.transform.position, points[1]), Is.LessThan(0.0001f));
            f.Advance(secondLength * 0.4f / enemy.MoveSpeed);
            Assert.That(Vector3.Distance(view.transform.position, Vector3.Lerp(points[1], points[2], 0.4f)), Is.LessThan(0.0001f));
            Assert.That(enemy.RouteProgress, Is.EqualTo(firstLength + secondLength * 0.4f).Within(0.0001f));
            var knockback = secondLength * 0.4f + firstLength * 0.5f;
            Assert.IsTrue(((EnemyBattleController)f.Controller).TryKnockback(side, view.InstanceId,
                knockback / enemy.Configuration.KnockbackResist));
            Assert.That(Vector3.Distance(view.transform.position, Vector3.Lerp(points[0], points[1], 0.5f)), Is.LessThan(0.0001f));
            f.Advance(firstLength * 0.5f / enemy.MoveSpeed);
            Assert.That(Vector3.Distance(view.transform.position, points[1]), Is.LessThan(0.0001f), "击退后继续使用原采样点，不能重新随机");
        }

        private static Vector3[] GetEnemyRoute(Fixture f, EnemyView view)
        {
            var entries = (System.Collections.IDictionary)typeof(EnemyBattleController)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            var entry = entries[view.InstanceId];
            return (Vector3[])entry.GetType().GetField("Points", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
        }

        [Test]
        public void BothSidesSpawnInitialAndLaterEnemiesInsideTheirOwnCircle()
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 3, 2001, 6) }));
            var f = _fixture;
            SetField(f.Controller, "_playerSpawnRadius", 0.4f);
            SetField(f.Controller, "_opponentSpawnRadius", 0.8f);
            f.SetRoutes(new[] { new Vector3(-3f, 1f, 2f), new Vector3(-3f, 0f, 2f) });
            f.Prepare(); f.Start();
            var positions = new System.Collections.Generic.List<Vector3>();
            for (var i = 0; i < 6; i++)
            {
                if (i > 0) f.Advance(0.5f);
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var view = f.Views.Where(v => f.Side(v) == side).OrderBy(v => v.InstanceId).Last();
                    var center = new Vector3(side == BattleSide.Player ? -3f : 7f, 1f, 2f);
                    var offset = view.transform.position - center;
                    Assert.That(offset.magnitude, Is.LessThanOrEqualTo(side == BattleSide.Player ? 0.40001f : 0.80001f));
                    Assert.AreEqual(0f, offset.z, "出生圆位于 XY 平面，保留原节点深度");
                    Assert.That(offset.sqrMagnitude, Is.GreaterThan(0f), "不能仍在固定点出生");
                    positions.Add(offset);
                }
            }
            Assert.AreEqual(12, positions.Distinct().Count(), "后续出生也应独立采样");
        }

        [Test]
        public void CircleSpawnMovementAndKnockbackUseEachEnemiesActualOrigin()
        {
            var f = Create(false);
            SetField(f.Controller, "_playerSpawnRadius", 0.5f);
            SetField(f.Controller, "_opponentSpawnRadius", 0.5f);
            f.Prepare(); f.Start();
            var views = f.Views;
            var starts = views.Select(v => v.transform.position).ToArray();
            f.Advance(0.25f);
            for (var i = 0; i < views.Length; i++)
            {
                var view = views[i];
                var side = f.Side(view);
                var nextPoint = new Vector3(side == BattleSide.Player ? -3f : 7f, 0f, 0f);
                f.Context.Registry.TryGet(view.InstanceId, out var entity);
                Assert.That(Vector3.Distance(view.transform.position, Vector3.MoveTowards(starts[i], nextPoint, 0.075f)),
                    Is.LessThan(0.0001f));
                Assert.That(((EnemyEntity)entity).RouteProgress, Is.EqualTo(0.075f).Within(0.0001f));
                Assert.IsTrue(((EnemyBattleController)f.Controller).TryKnockback(side, view.InstanceId, 100f));
                Assert.That(Vector3.Distance(starts[i], view.transform.position), Is.LessThan(0.0001f));
            }
            f.Advance(6f);
            for (var i = 0; i < views.Length; i++)
            {
                var view = views[i];
                f.Context.Registry.TryGet(view.InstanceId, out var entity);
                Assert.That(((EnemyEntity)entity).RouteProgress, Is.EqualTo(1.8f).Within(0.0001f), "跨过第一段后进度仍从实际出生点累计");
                Assert.IsTrue(((EnemyBattleController)f.Controller).TryKnockback(f.Side(view), view.InstanceId, 100f));
                Assert.That(Vector3.Distance(starts[i], view.transform.position), Is.LessThan(0.0001f), "跨节点击退也不能回到圆心");
            }
        }

        [TestCase(2001)]
        [TestCase(2002)]
        [TestCase(2003)]
        public void InitialAndLaterEnemiesInheritTheirOwnBeltScale(int enemyId)
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 2, enemyId, 2) }));
            var f = _fixture;
            var playerBelt = new GameObject("Battle_playerBelt").transform;
            playerBelt.SetParent(f.Root.transform, false);
            playerBelt.localScale = new Vector3(0.8f, 0.8f, 1f);
            var opponentBelt = new GameObject("Battle_enemyBelt").transform;
            opponentBelt.SetParent(f.Root.transform, false);
            opponentBelt.localScale = new Vector3(0.6f, 0.6f, 1f);
            SetField(f.Controller, "_playerEnemyRoot", playerBelt);
            SetField(f.Controller, "_opponentEnemyRoot", opponentBelt);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/Prefabs/" +
                f.Tables.TbEnemy.Get(enemyId).EnemyPrefab + ".prefab");
            var authoredScale = prefab.transform.localScale;
            f.Prepare(); f.Start();
            Assert.AreEqual(2, f.Views.Length);
            Assert.That(Vector3.Distance(f.PlayerView.transform.position, new Vector3(-3f, 1f, 0f)), Is.LessThan(0.0001f));
            f.Advance(1f);
            Assert.AreEqual(4, f.Views.Length, "后续出兵也必须使用同侧 Belt");
            foreach (var view in f.Views)
            {
                var belt = f.Side(view) == BattleSide.Player ? playerBelt : opponentBelt;
                Assert.AreSame(belt, view.transform.parent);
                Assert.That(Vector3.Distance(authoredScale, view.transform.localScale), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(Vector3.Scale(authoredScale, belt.localScale), view.transform.lossyScale),
                    Is.LessThan(0.0001f));
            }
            var latest = f.Views.Where(v => f.Side(v) == BattleSide.Player).OrderBy(v => v.InstanceId).Last();
            Assert.That(Vector3.Distance(latest.transform.position, new Vector3(-3f, 1f, 0f)), Is.LessThan(0.0001f));
            f.Clear();
            Assert.AreEqual(0, playerBelt.childCount);
            Assert.AreEqual(0, opponentBelt.childCount);
        }

        [Test]
        public void NewWaveStartsWhilePreviousEnemiesKeepTheirIdentityAndPosition()
        {
            _fixture = new Fixture(WaveTestData.Tables(
                new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(2, 2) },
                new[] { WaveTestData.Spawn(1, 0, 8, 2001, 4), WaveTestData.Spawn(2, 3, 5, 2001, 1) }));
            var f = _fixture; f.Prepare(); f.Start();
            var oldId = f.PlayerView.InstanceId;
            f.Context.Registry.ApplyDamage(oldId, 7, out _);
            f.Advance(3f);
            var controller = (EnemyBattleController)f.Controller;
            Assert.AreEqual(2, controller.CurrentWave);
            Assert.AreEqual(3, f.Spawned(BattleSide.Player));
            Assert.AreEqual(3, f.Spawned(BattleSide.Opponent));
            Assert.IsFalse(controller.IsAllSpawnsCompleted, "前波仍有后续出兵，不能在切波时丢弃");
            Assert.IsTrue(f.Context.Registry.TryGet(oldId, out var old));
            Assert.AreEqual(93, ((EnemyEntity)old).CurrentHp);
            Assert.That(((EnemyEntity)old).RouteProgress, Is.EqualTo(0.9f).Within(0.0001f));
            f.Advance(3f);
            Assert.AreEqual(5, f.Spawned(BattleSide.Player));
            Assert.IsTrue(controller.IsAllSpawnsCompleted);
        }

        [Test]
        public void CycleTwoUsesNewHealthWithoutResettingEnemiesFromCycleOne()
        {
            var plans = Enumerable.Range(1, 11).Select(w => WaveTestData.Plan(w, w)).ToArray();
            var groups = Enumerable.Range(1, 11).Select(w => WaveTestData.Spawn(w, w - 1, w, 2001, 1)).ToArray();
            _fixture = new Fixture(WaveTestData.Tables(plans, groups));
            var f = _fixture; f.Prepare(); f.Start();
            var oldId = f.PlayerView.InstanceId;
            f.Context.Registry.ApplyDamage(oldId, 7, out _);
            f.Advance(10f);
            var controller = (EnemyBattleController)f.Controller;
            Assert.AreEqual(11, controller.CurrentWave);
            Assert.IsTrue(f.Context.Registry.TryGet(oldId, out var old));
            Assert.AreEqual(93, ((EnemyEntity)old).CurrentHp);
            Assert.AreEqual(1, ((EnemyEntity)old).Cycle);
            var latest = f.Views.Where(v => f.Side(v) == BattleSide.Player).OrderBy(v => v.InstanceId).Last();
            f.Context.Registry.TryGet(latest.InstanceId, out var entity);
            Assert.AreEqual(220, ((EnemyEntity)entity).CurrentHp);
            Assert.AreEqual(2, ((EnemyEntity)entity).Cycle);
            foreach (var view in f.Views) f.Context.Registry.Remove(view.InstanceId, EntityRemovalReason.Manual, out _);
            f.Advance(10f);
            Assert.IsTrue(controller.IsAllEnemiesCleared);
            Assert.IsTrue(f.Host.IsCombatRunning, "全部出完且清空不能自行触发终局");
            Assert.AreEqual(11, controller.CurrentWave);
            Assert.That(controller.ElapsedSeconds, Is.EqualTo(20d).Within(0.0001d));
        }

        [TestCase(2002, 2)]
        [TestCase(2003, 0)]
        public void EliteAndBossLeaksReadConfiguredDamageBeforeEntityRecycling(int enemyId, int remainingHp)
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, enemyId, 1) }));
            var f = _fixture; f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) });
            f.Prepare(); f.Start(); f.Advance(1f);
            Assert.AreEqual(remainingHp, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(remainingHp, f.Host.Towers.OpponentTower.CurrentHp);
        }

        [Test]
        public void ContinuousClockKeepsAdvancingAfterTheLastEnemyClears()
        {
            var f = Create(); f.Start(); f.Advance(4.5f);
            foreach (var view in f.Views)
                f.Context.Registry.Remove(view.InstanceId, EntityRemovalReason.Manual, out _);
            f.Advance(1f);
            Assert.IsTrue(f.Completed);
            var elapsed = f.Controller.GetType().GetProperty("ElapsedSeconds");
            Assert.IsNotNull(elapsed, "波次系统需要持续的整局战斗时钟");
            Assert.That((double)elapsed.GetValue(f.Controller), Is.EqualTo(5.5d).Within(0.0001d));
            f.Advance(10f);
            Assert.That((double)elapsed.GetValue(f.Controller), Is.EqualTo(15.5d).Within(0.0001d));
        }

        [Test]
        public void FirstWaveTablesSupplyTowerHealthAndReferencedEnemy()
        {
            var tables = LoadTables();
            var towerTable = typeof(Tables).GetProperty("TbTower");
            var waveTable = typeof(Tables).GetProperty("TbWaveSpawn");
            Assert.IsNotNull(towerTable, "缺少 Tower 配置表");
            Assert.IsNotNull(waveTable, "缺少波次出兵表");
            var tower = GetRow(towerTable.GetValue(tables), 3001);
            var wave = GetRow(waveTable.GetValue(tables), 2300);
            Assert.AreEqual(5, Field<int>(tower, "MaxHp"));
            Assert.AreEqual("Tower", Field<string>(tower, "Prefab"));
            Assert.AreEqual(2001, Field<int>(wave, "EnemyId"));
            Assert.AreEqual(11, Field<int>(wave, "EnemyCount"));
            CollectionAssert.AreEqual(new[] { 0, 20 }, Field<int[]>(wave, "SpawnSecRange"));
            Assert.AreEqual(1, tables.TbEnemy.Get(Field<int>(wave, "EnemyId")).LeakDamage);
        }

        [Test]
        public void EnemyDisappearsAtTowerColliderEdgeAndDealsExactlyOneDamage()
        {
            var f = Create(false);
            var tower = f.Root.GetComponentsInChildren<TowerView>().Single(v => v.transform.position.x == 0f);
            tower.transform.localScale = Vector3.one;
            f.SetRoutes(new[] { new Vector3(-0.8f, 0f, 0f) });
            Physics2D.SyncTransforms();
            Assert.That(tower.GetComponent<Collider2D>().ClosestPoint(new Vector2(-0.8f, 0f)).x,
                Is.EqualTo(-0.5f).Within(Physics2D.defaultContactOffset + 0.0001f), "测试塔边缘包含 Unity 的接触容差");
            f.Prepare(); f.Start();
            var id = f.PlayerView.InstanceId;
            f.Advance(0.9f);
            Assert.IsTrue(f.Context.Registry.TryGet(id, out _));
            Assert.AreEqual(5, f.Host.Towers.PlayerTower.CurrentHp);
            f.Advance(0.11f);
            Assert.IsFalse(f.Context.Registry.TryGet(id, out _), "到达碰撞体边缘即移除，不应继续走到塔中心");
            Assert.AreEqual(4, f.Host.Towers.PlayerTower.CurrentHp);
            f.Advance(0f);
            Assert.AreEqual(4, f.Host.Towers.PlayerTower.CurrentHp);
        }

        [Test]
        public void PreparationCreatesNoEntitiesAndStartIsIdempotent()
        {
            var f = Create();
            Assert.AreEqual(0, f.LiveCount);
            Assert.AreEqual(0, f.Spawned(BattleSide.Player));
            Assert.IsFalse(f.Completed);
            f.Start(); f.Start();
            Assert.AreEqual(2, f.LiveCount);
            Assert.AreEqual(1, f.Spawned(BattleSide.Player));
            Assert.AreEqual(1, f.Spawned(BattleSide.Opponent));
            Assert.That(f.Views.All(v => v.InstanceId > 0 && v.AnimationState == "Move"));
            Assert.AreEqual(new Vector3(-3f, 1f, 0f), f.PlayerView.transform.position);
        }

        [Test]
        public void SpawnBoundaryGivesNewEnemyOnlyItsOwnMovementTime()
        {
            var f = Create(); f.Start(); f.Advance(0.5f);
            Assert.AreEqual(2, f.Spawned(BattleSide.Player));
            var player = f.Views.Where(v => f.Side(v) == BattleSide.Player).OrderBy(v => v.InstanceId).ToArray();
            Assert.That(player[0].transform.position.y, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(player[1].transform.position.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void LargeStepCrossesNodesAndCompletesBothSidesWithOneLeakPerEnemy()
        {
            var f = Create(); f.Start(); f.Advance(20f); f.Advance(20f);
            Assert.AreEqual(10, f.Spawned(BattleSide.Player));
            Assert.AreEqual(10, f.Spawned(BattleSide.Opponent));
            Assert.LessOrEqual(f.Host.Towers.PlayerTower.CurrentHp, 1);
            Assert.LessOrEqual(f.Host.Towers.OpponentTower.CurrentHp, 1);
            Assert.IsTrue(f.Host.Towers.IsEnded);
            f.Host.EndBattle();
            Assert.AreEqual(0, f.LiveCount);
            Assert.IsTrue(f.Context.IsEnded);
        }

        [Test]
        public void SmallStepsAndLargeStepHaveSamePositionsAtLastSpawn()
        {
            var f = Create(); f.Start(); f.Advance(4.5f);
            var expected = f.Views.OrderBy(v => v.InstanceId).Select(v => v.transform.position).ToArray();
            f.Dispose(); _fixture = null;
            f = Create(); f.Start();
            for (var i = 0; i < 45; i++) f.Advance(0.1f);
            var actual = f.Views.OrderBy(v => v.InstanceId).Select(v => v.transform.position).ToArray();
            Assert.AreEqual(20, actual.Length);
            for (var i = 0; i < actual.Length; i++) Assert.That(Vector3.Distance(expected[i], actual[i]), Is.LessThan(0.0001f));
        }

        [Test]
        public void MovementPublishesDistanceAlongTheRouteInsteadOfLeavingProgressAtZero()
        {
            var f = Create(); f.Start();
            var id = f.PlayerView.InstanceId;
            f.Advance(2.5f);
            Assert.IsTrue(f.Context.Registry.TryGet(id, out var entity));
            Assert.That(((EnemyEntity)entity).RouteProgress, Is.EqualTo(0.75f).Within(0.0001f),
                "敌人速度减半后，走完1单位竖段再走0.25单位横段，应发布0.75单位路线进度");
        }

        [Test]
        public void FreezeStopsSpawnMovementAndAnimatorWithoutCatchup()
        {
            var f = Create(); f.Start();
            var view = f.PlayerView; var position = view.transform.position;
            f.Host.SetFrozen(true); f.Advance(10f);
            Assert.AreEqual(position, view.transform.position);
            Assert.AreEqual(1, f.Spawned(BattleSide.Player));
            Assert.AreEqual(0f, view.GetComponent<Animator>().speed);
            f.Host.SetFrozen(false); f.Advance(0.5f);
            Assert.AreEqual(2, f.Spawned(BattleSide.Player));
            Assert.AreEqual(1f, view.GetComponent<Animator>().speed);
        }

        [Test]
        public void ExternalRemovalAndPoolReuseDoNotLeakOldEnemy()
        {
            var f = Create(); f.Start();
            var oldId = f.PlayerView.InstanceId;
            Assert.AreEqual(DamageResult.Killed, f.Context.Registry.ApplyDamage(oldId, 100, out _));
            var unrelatedId = f.Context.Registry.CreateEnemyFromConfig(f.Tables.TbEnemy.Get(2001), BattleSide.Player);
            f.Advance(20f);
            Assert.LessOrEqual(f.Host.Towers.PlayerTower.CurrentHp, 4);
            Assert.LessOrEqual(f.Host.Towers.OpponentTower.CurrentHp, 4);
            Assert.IsTrue(f.Context.Registry.TryGet(unrelatedId, out var entity));
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
            Assert.IsTrue(f.Host.Towers.IsEnded);
        }

        [Test]
        public void TowerDeathStopsFurtherDamageAndGenerationInSameStep()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) }); f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, 4);
            f.Start(); f.Advance(20f);
            Assert.AreEqual(0, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.IsTrue(f.Host.Towers.IsEnded);
            Assert.IsFalse(f.Host.IsCombatRunning);
            Assert.That(f.Spawned(BattleSide.Player), Is.LessThan(10));
            Assert.AreEqual(4, f.Host.Towers.OpponentTower.CurrentHp, "同一抵塔时刻敌方也受到已提交的1点伤害，后续时刻停止");
        }

        [Test]
        public void CoincidentNodesAreSkippedAndZeroSpeedDoesNotMove()
        {
            var f = Create(false); f.SetRoutes(new[] { Vector3.zero, Vector3.zero, Vector3.zero });
            f.Prepare(); f.Start(); f.Advance(0f);
            Assert.AreEqual(4, f.Host.Towers.PlayerTower.CurrentHp);
            f.Dispose(); _fixture = null;
            f = Create(); f.Start();
            var view = f.PlayerView;
            f.Context.Registry.TryGet(view.InstanceId, out var entity);
            ((EnemyEntity)entity).SetMoveSpeed(0f);
            f.Advance(4f);
            Assert.AreEqual(new Vector3(-3f, 1f, 0f), view.transform.position);
            Assert.AreEqual(5, f.Host.Towers.PlayerTower.CurrentHp);
        }

        [Test]
        public void MissingNodesAndFailedLoadingCanRetryWithoutResidualEntities()
        {
            var f = Create(false);
            SetField(f.Controller, "_playerRoutePoints", new Transform[0]);
            Assert.Throws<InvalidOperationException>(() => f.Prepare());
            f.SetRoutes(new[] { new Vector3(-3f, 1f, 0f), new Vector3(-3f, 0f, 0f), new Vector3(-1f, 0f, 0f) });
            var loaded = 0;
            Assert.Throws<InvalidOperationException>(() => f.Prepare((name, parent, token) =>
            {
                if (++loaded == 5) throw new InvalidOperationException("Expected loader failure");
                return f.Load(name, parent, token);
            }));
            Assert.AreEqual(0, f.LiveCount);
            f.Prepare(); f.Start(); Assert.AreEqual(2, f.LiveCount);
            f.Clear(); f.Clear();
            Assert.AreEqual(0, f.LiveCount);
            Assert.AreEqual(0, f.Spawned(BattleSide.Player));
            f.Prepare(); f.Start(); Assert.AreEqual(2, f.LiveCount);
        }

        [Test]
        public void SimultaneousLethalArrivalsCommitBothTowersAndPreferPlayerVictory()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.63f, 0f, 0f) });
            var points = (Transform[])f.Controller.GetType().GetField("_opponentRoutePoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            points[0].position = new Vector3(9.37f, 0f, 0f);
            f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, 4);
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Opponent, 4);
            var requests = 0;
            f.Host.Towers.SettlementRequested += snapshot =>
            {
                requests++;
                Assert.AreEqual(BattleSide.Opponent, snapshot.DefeatedSide);
            };
            f.Start(); f.Advance(0.5f);
            Assert.AreEqual(0, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(0, f.Host.Towers.OpponentTower.CurrentHp);
            Assert.AreEqual(1, requests);
        }

        [Test]
        public void EarlierArrivalWinsEvenWhenItsEnemyWasCreatedSecond()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.63f, 0f, 0f) });
            var points = (Transform[])f.Controller.GetType().GetField("_opponentRoutePoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            points[0].position = new Vector3(9.46f, 0f, 0f);
            f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, 4);
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Opponent, 4);
            f.Start(); f.Advance(0.5f);
            Assert.AreEqual(1, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(0, f.Host.Towers.OpponentTower.CurrentHp);
        }

        [Test]
        public void RemovalNotificationFreezingBattleCannotSwallowCommittedLeak()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) });
            f.Prepare(); f.Start();
            f.PlayerView.RemovalAnimationCompleted += _ => f.Host.SetFrozen(true);
            f.Advance(0.5f);
            Assert.AreEqual(4, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.IsTrue(f.Host.IsFrozen);
        }

        [Test]
        public void ArrivalCallbackFreezeKeepsAllEnemiesAtTheSameBattleTime()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) });
            var points = (Transform[])f.Controller.GetType().GetField("_opponentRoutePoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            points[0].position = new Vector3(9f, 0f, 0f);
            f.Prepare(); f.Start();
            var opponent = f.Views.Single(v => f.Side(v) == BattleSide.Opponent);
            f.PlayerView.RemovalAnimationCompleted += _ => f.Host.SetFrozen(true);
            f.Advance(0.5f);
            Assert.IsTrue(f.Host.IsFrozen);
            Assert.That(opponent.transform.position.x, Is.EqualTo(9.1f).Within(0.00001f));
            f.Host.SetFrozen(false); f.Advance(0.1f);
            Assert.That(opponent.transform.position.x, Is.EqualTo(9.13f).Within(0.00001f));
        }

        [Test]
        public void ThrowingRemovalNotificationStillReleasesTheArrivedView()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) });
            f.Prepare(); f.Start();
            var view = f.PlayerView;
            view.RemovalAnimationCompleted += _ => throw new InvalidOperationException("Expected removal listener failure");
            Assert.Throws<InvalidOperationException>(() => f.Advance(0.5f));
            Assert.IsTrue(view == null, "已摘出活动集合的表现也必须被释放");
            Assert.AreEqual(0, f.LiveCount);
            Assert.IsFalse(f.Host.IsCombatRunning);
        }

        [Test]
        public void LateOldPreparationCannotClearNewPreparation()
        {
            var f = Create(false);
            var pending = new UniTaskCompletionSource<GameObject>();
            var old = f.PrepareTask((name, parent, token) => pending.Task);
            f.Clear(); f.Prepare();
            pending.TrySetResult(f.Load("SkeletonEnemy", f.Root.transform, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => old.GetAwaiter().GetResult());
            f.Start(); Assert.AreEqual(2, f.LiveCount);
        }

        [TestCase(5, 10)]
        [TestCase(20, 10)]
        [TestCase(21, 15)]
        [TestCase(30, 15)]
        public void ConfiguredFinalWaveOpensOneVictoryAfterBothSidesAndDeathViewsFinish(int waveCount, int wavesPerCycle)
        {
            _fixture = new Fixture(WaveTestData.Tables(
                Enumerable.Range(1, waveCount).Select(w => WaveTestData.Plan(w, w)).ToArray(),
                Enumerable.Range(1, waveCount).Select(w => WaveTestData.Spawn(w, w - 1, w, 2001, 1)).ToArray(), wavesPerCycle));
            var f = _fixture;
            f.SetRoutes(new[] { new Vector3(-100f, 0f, 0f) });
            f.Prepare();
            var enemies = (EnemyBattleController)f.Controller;
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            f.Start(); f.Advance(waveCount - 1);
            Assert.AreEqual(waveCount, enemies.CurrentWave);
            Assert.IsTrue(enemies.IsAllSpawnsCompleted);
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            foreach (var view in f.Views)
            {
                Assert.AreEqual(DamageResult.Killed, f.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal));
                enemies.NotifyKilled(removal);
            }
            Assert.IsTrue(enemies.IsAllEnemiesCleared);
            Assert.IsFalse(enemies.IsWaveVictoryReady, "逻辑清空后仍须等待最后的死亡表现");
            CheckWaveClear(f, f.Context);
            Assert.IsTrue(f.Host.IsCombatRunning);
            f.Host.SetFrozen(true); f.Advance(4f);
            Assert.Greater(enemies.DeathViewCount, 0);
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            f.Host.SetFrozen(false);
            f.Advance(3f);
            Assert.IsTrue(enemies.IsWaveVictoryReady, "无动画事件时现有3秒兜底也必须允许完成");
            var notices = 0;
            Action<BattleSettlementSnapshot> listener = snapshot =>
            {
                notices++;
                Assert.AreEqual(f.Context.BattleId, snapshot.BattleId);
                Assert.AreEqual(BattleSettlementReason.FinalWaveCleared, snapshot.Reason);
                Assert.AreEqual(BattleSettlementResult.Victory, snapshot.Result);
                Assert.AreEqual(5, snapshot.PlayerTowerHp);
                Assert.AreEqual(5, snapshot.OpponentTowerHp);
                Assert.IsTrue(f.Context.IsEnded);
            };
            TEngine.GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener);
            try
            {
                CheckWaveClear(f, f.Context);
                Assert.IsFalse(f.Host.IsCombatRunning);
                Assert.IsFalse(enemies.IsWaveVictoryReady);
                f.Host.EndBattle(); f.Host.EndBattle();
                Assert.AreEqual(1, notices);
            }
            finally { TEngine.GameEvent.RemoveEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener); }
        }

        [Test]
        public void EmptyGapBeforeFutureSpawnCannotWinAndDeathCallbackCompletesOnlyTheLastWave()
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(2, 2) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1), WaveTestData.Spawn(2, 10, 11, 2001, 1) }));
            var f = _fixture; f.Prepare(); f.Start();
            var enemies = (EnemyBattleController)f.Controller;
            KillAndComplete(f);
            Assert.AreEqual(0, enemies.LiveCount);
            Assert.IsFalse(enemies.IsAllSpawnsCompleted);
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            CheckWaveClear(f, f.Context);
            Assert.IsTrue(f.Host.IsCombatRunning);
            f.Advance(10f);
            KillAndComplete(f);
            Assert.IsTrue(enemies.IsWaveVictoryReady);
            CheckWaveClear(f, f.Context);
            Assert.IsFalse(f.Host.IsCombatRunning);
        }

        [TestCase(0, BattleSettlementReason.FinalWaveCleared, BattleSettlementResult.Victory)]
        [TestCase(4, BattleSettlementReason.TowerDestroyed, BattleSettlementResult.Defeat)]
        public void LastLeakCommitsDamageBeforeChoosingVictoryOrDefeat(int previousDamage,
            BattleSettlementReason reason, BattleSettlementResult result)
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            var f = _fixture;
            f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) });
            f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, previousDamage);
            var notifications = 0;
            f.Host.Towers.SettlementRequested += snapshot =>
            {
                notifications++;
                Assert.AreEqual(reason, snapshot.Reason);
                Assert.AreEqual(result, snapshot.Result);
                Assert.AreEqual(4 - previousDamage, snapshot.PlayerTowerHp);
            };
            f.Host.Towers.PlayerTower.HealthChanged += _ =>
                Assert.IsFalse(((EnemyBattleController)f.Controller).IsWaveVictoryReady, "抵塔伤害提交过程中不能提前就绪");
            f.Start(); f.Advance(1f);
            CheckWaveClear(f, f.Context);
            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void ManualRemovalAndOldContextCannotProduceClearVictory()
        {
            _fixture = new Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            var f = _fixture; f.Prepare(); f.Start();
            foreach (var view in f.Views) f.Context.Registry.Remove(view.InstanceId, EntityRemovalReason.Manual, out _);
            f.Advance(0f);
            var enemies = (EnemyBattleController)f.Controller;
            Assert.IsTrue(enemies.IsAllEnemiesCleared);
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            CheckWaveClear(f, f.Context);
            Assert.IsTrue(f.Host.IsCombatRunning);
            f.Clear();
            Assert.IsFalse(enemies.IsWaveVictoryReady);
            f.Prepare(); f.Start(); KillAndComplete(f);
            Assert.IsTrue(enemies.IsWaveVictoryReady, "新准备必须重置异常移除状态");
            using (var old = new BattleShopContext(ShopRules.FromTables(f.Tables), new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, f.Tables.TbRobot.Get))
                CheckWaveClear(f, old);
            Assert.IsTrue(f.Host.IsCombatRunning, "旧局上下文不能结算当前局");
            CheckWaveClear(f, f.Context);
            Assert.IsFalse(f.Host.IsCombatRunning);
        }

        private static void KillAndComplete(Fixture f)
        {
            foreach (var view in f.Views)
            {
                f.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal);
                ((EnemyBattleController)f.Controller).NotifyKilled(removal);
                view.OnDeathAnimationComplete();
            }
        }

        private static void CheckWaveClear(Fixture f, BattleShopContext context) => typeof(BattleShopSceneController)
            .GetMethod("TryCompleteWaveClear", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f.Host, new object[] { context });

        private Fixture Create(bool prepare = true) { _fixture = new Fixture(); if (prepare) _fixture.Prepare(); return _fixture; }
        private static Tables LoadTables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        private static object GetRow(object table, int id) => table.GetType().GetMethod("Get").Invoke(table, new object[] { id });
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) continue;
                field.SetValue(target, value); return;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        internal sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("EnemyBattleFixture");
            public readonly Tables Tables;
            public readonly BattleShopContext Context;
            public readonly BattleShopSceneController Host;
            public readonly Component Controller;
            private bool _disposed;
            public Fixture(Tables tables = null)
            {
                Tables = tables ?? WaveTestData.SingleWaveTables();
                var type = typeof(EnemyEntity).Assembly.GetType("GameLogic.EnemyBattleController");
                Assert.IsNotNull(type, "缺少 EnemyBattleController");
                Host = Root.AddComponent<BattleShopSceneController>();
                SetField(Host, "_bootstrap", Root.AddComponent<PlacementBoardsBootstrap>());
                Controller = Root.AddComponent(type);
                // 既有时序/战斗用例使用固定路线；圆形出生用例显式设置各侧半径。
                SetField(Controller, "_playerSpawnRadius", 0f);
                SetField(Controller, "_opponentSpawnRadius", 0f);
                SetField(Controller, "_playerP1Radius", 0f);
                SetField(Controller, "_playerP2Radius", 0f);
                SetField(Controller, "_opponentP1Radius", 0f);
                SetField(Controller, "_opponentP2Radius", 0f);
                Context = new BattleShopContext(ShopRules.FromTables(Tables), new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, Tables.TbRobot.Get);
                SetField(Host, "<Context>k__BackingField", Context);
                SetField(Host, "_ready", true); SetField(Host, "_running", true);
                SetField(Host, "_enemies", Controller);
                var session = new TowerBattleSession(Context.BattleId, Context.Registry, 5);
                SetField(Host, "_towers", session);
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var tower = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AssetRaw/Actor/Prefabs/Tower/Tower.prefab"), Root.transform).GetComponent<TowerView>();
                    tower.transform.position = side == BattleSide.Player ? Vector3.zero : Vector3.right * 10f;
                    tower.Bind(side == BattleSide.Player ? session.PlayerTower : session.OpponentTower);
                    SetField(Host, side == BattleSide.Player ? "_playerTowerView" : "_opponentTowerView", tower);
                }
                var deathMethod = Host.GetType().GetMethod("OnTowerSettlementRequested", BindingFlags.Instance | BindingFlags.NonPublic);
                session.SettlementRequested += (Action<BattleSettlementSnapshot>)Delegate.CreateDelegate(typeof(Action<BattleSettlementSnapshot>), Host, deathMethod);
                SetRoutes(new[] { new Vector3(-3f, 1f, 0f), new Vector3(-3f, 0f, 0f), new Vector3(-1f, 0f, 0f) });
            }
            public void SetRoutes(Vector3[] points)
            {
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var route = points.Select((p, i) =>
                    {
                        var node = new GameObject(side + "P" + i).transform; node.SetParent(Root.transform);
                        node.position = p + (side == BattleSide.Player ? Vector3.zero : Vector3.right * 10f); return node;
                    }).ToArray();
                    SetField(Controller, side == BattleSide.Player ? "_playerRoutePoints" : "_opponentRoutePoints", route);
                }
            }
            public UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/Prefabs/" + name + ".prefab");
                return UniTask.FromResult(UnityEngine.Object.Instantiate(prefab, parent));
            }
            public UniTask PrepareTask(Func<string, Transform, CancellationToken, UniTask<GameObject>> load = null) =>
                (UniTask)Controller.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(m => m.Name == "PrepareAsync" && m.GetParameters().Length == 5)
                    .Invoke(Controller, new object[] { Host, Context, Tables, CancellationToken.None, load ?? Load });
            public void Prepare(Func<string, Transform, CancellationToken, UniTask<GameObject>> load = null) => PrepareTask(load).GetAwaiter().GetResult();
            public void Start() => Invoke("StartBattle");
            public void Advance(float dt) => Invoke("Advance", dt);
            public void Clear() => Invoke("Clear");
            private void Invoke(string name, params object[] args)
            {
                try { Controller.GetType().GetMethod(name).Invoke(Controller, args); }
                catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            }
            public int LiveCount => (int)Controller.GetType().GetProperty("LiveCount").GetValue(Controller);
            public bool Completed => (bool)Controller.GetType().GetProperty("IsAllEnemiesCleared").GetValue(Controller);
            public int Spawned(BattleSide side) => (int)Controller.GetType().GetMethod("GetSpawnedCount").Invoke(Controller, new object[] { side });
            public EnemyView[] Views => Root.GetComponentsInChildren<EnemyView>().Where(v => v.InstanceId > 0).ToArray();
            public EnemyView PlayerView => Views.First(v => Side(v) == BattleSide.Player);
            public BattleSide Side(EnemyView view) { Context.Registry.TryGet(view.InstanceId, out var entity); return entity.Side; }
            public void Dispose()
            {
                if (_disposed) return; _disposed = true;
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
                Context?.Dispose();
            }
        }
    }
}
