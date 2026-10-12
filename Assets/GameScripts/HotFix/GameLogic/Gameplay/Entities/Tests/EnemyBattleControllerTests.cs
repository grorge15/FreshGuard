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
        public void FirstWaveTablesSupplyTowerHealthAndReferencedEnemy()
        {
            var tables = LoadTables();
            var towerTable = typeof(Tables).GetProperty("TbTower");
            var waveTable = typeof(Tables).GetProperty("TbWaveSpawn");
            Assert.IsNotNull(towerTable, "缺少 Tower 配置表");
            Assert.IsNotNull(waveTable, "缺少波次出兵表");
            var tower = GetRow(towerTable.GetValue(tables), 3001);
            var wave = GetRow(waveTable.GetValue(tables), 1);
            Assert.AreEqual(100, Field<int>(tower, "MaxHp"));
            Assert.AreEqual("Tower", Field<string>(tower, "Prefab"));
            Assert.AreEqual(1, Field<int>(wave, "WaveId"));
            Assert.AreEqual(2001, Field<int>(wave, "EnemyId"));
            Assert.AreEqual(10, Field<int>(wave, "Count"));
            Assert.AreEqual(0.5f, Field<float>(wave, "SpawnInterval"));
            Assert.AreEqual(1, tables.TbEnemy.Get(Field<int>(wave, "EnemyId")).LeakDamage);
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
            Assert.That(player[0].transform.position.y, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(player[1].transform.position.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void LargeStepCrossesNodesAndCompletesBothSidesWithOneLeakPerEnemy()
        {
            var f = Create(); f.Start(); f.Advance(20f); f.Advance(20f);
            Assert.AreEqual(10, f.Spawned(BattleSide.Player));
            Assert.AreEqual(10, f.Spawned(BattleSide.Opponent));
            Assert.AreEqual(90, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(90, f.Host.Towers.OpponentTower.CurrentHp);
            Assert.AreEqual(0, f.LiveCount);
            Assert.IsTrue(f.Completed);
            Assert.IsFalse(f.Context.IsEnded);
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
            Assert.That(((EnemyEntity)entity).RouteProgress, Is.EqualTo(1.5f).Within(0.0001f),
                "走完1单位竖段再走0.5单位横段，应发布1.5单位路线进度");
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
            Assert.AreEqual(91, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(90, f.Host.Towers.OpponentTower.CurrentHp);
            Assert.IsTrue(f.Context.Registry.TryGet(unrelatedId, out var entity));
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
            Assert.IsTrue(f.Completed);
        }

        [Test]
        public void TowerDeathStopsFurtherDamageAndGenerationInSameStep()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.1f, 0f, 0f) }); f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, 99);
            f.Start(); f.Advance(20f);
            Assert.AreEqual(0, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.IsTrue(f.Host.Towers.IsEnded);
            Assert.IsFalse(f.Host.IsCombatRunning);
            Assert.That(f.Spawned(BattleSide.Player), Is.LessThan(10));
            Assert.AreEqual(100, f.Host.Towers.OpponentTower.CurrentHp);
        }

        [Test]
        public void CoincidentNodesAreSkippedAndZeroSpeedDoesNotMove()
        {
            var f = Create(false); f.SetRoutes(new[] { Vector3.zero, Vector3.zero, Vector3.zero });
            f.Prepare(); f.Start(); f.Advance(0f);
            Assert.AreEqual(99, f.Host.Towers.PlayerTower.CurrentHp);
            f.Dispose(); _fixture = null;
            f = Create(); f.Start();
            var view = f.PlayerView;
            f.Context.Registry.TryGet(view.InstanceId, out var entity);
            SetField(entity, "<BaseSpeed>k__BackingField", 0f);
            f.Advance(4f);
            Assert.AreEqual(new Vector3(-3f, 1f, 0f), view.transform.position);
            Assert.AreEqual(100, f.Host.Towers.PlayerTower.CurrentHp);
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
        public void EarlierArrivalWinsEvenWhenItsEnemyWasCreatedSecond()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.24f, 0f, 0f) });
            var points = (Transform[])f.Controller.GetType().GetField("_opponentRoutePoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            points[0].position = new Vector3(9.94f, 0f, 0f);
            f.Prepare();
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Player, 99);
            f.Host.Towers.ApplyDamage(f.Context.BattleId, BattleSide.Opponent, 99);
            f.Start(); f.Advance(0.5f);
            Assert.AreEqual(1, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(0, f.Host.Towers.OpponentTower.CurrentHp);
        }

        [Test]
        public void RemovalNotificationFreezingBattleCannotSwallowCommittedLeak()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.1f, 0f, 0f) });
            f.Prepare(); f.Start();
            f.PlayerView.RemovalAnimationCompleted += _ => f.Host.SetFrozen(true);
            f.Advance(0.5f);
            Assert.AreEqual(99, f.Host.Towers.PlayerTower.CurrentHp);
            Assert.IsTrue(f.Host.IsFrozen);
        }

        [Test]
        public void ArrivalCallbackFreezeKeepsAllEnemiesAtTheSameBattleTime()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.1f, 0f, 0f) });
            var points = (Transform[])f.Controller.GetType().GetField("_opponentRoutePoints", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Controller);
            points[0].position = new Vector3(9f, 0f, 0f);
            f.Prepare(); f.Start();
            var opponent = f.Views.Single(v => f.Side(v) == BattleSide.Opponent);
            f.PlayerView.RemovalAnimationCompleted += _ => f.Host.SetFrozen(true);
            f.Advance(0.5f);
            Assert.IsTrue(f.Host.IsFrozen);
            Assert.That(opponent.transform.position.x, Is.EqualTo(9.1f).Within(0.00001f));
            f.Host.SetFrozen(false); f.Advance(0.1f);
            Assert.That(opponent.transform.position.x, Is.EqualTo(9.16f).Within(0.00001f));
        }

        [Test]
        public void ThrowingRemovalNotificationStillReleasesTheArrivedView()
        {
            var f = Create(false); f.SetRoutes(new[] { new Vector3(-0.1f, 0f, 0f) });
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
            public readonly Tables Tables = LoadTables();
            public readonly BattleShopContext Context;
            public readonly BattleShopSceneController Host;
            public readonly Component Controller;
            private bool _disposed;
            public Fixture()
            {
                var type = typeof(EnemyEntity).Assembly.GetType("GameLogic.EnemyBattleController");
                Assert.IsNotNull(type, "缺少 EnemyBattleController");
                Host = Root.AddComponent<BattleShopSceneController>();
                SetField(Host, "_bootstrap", Root.AddComponent<PlacementBoardsBootstrap>());
                Controller = Root.AddComponent(type);
                Context = new BattleShopContext(ShopRules.FromTables(Tables), new[] { 1001, 1002, 1003 }, new[] { 1001, 1002, 1003 }, Tables.TbRobot.Get);
                SetField(Host, "<Context>k__BackingField", Context);
                SetField(Host, "_ready", true); SetField(Host, "_running", true);
                SetField(Host, "_enemies", Controller);
                var session = new TowerBattleSession(Context.BattleId, Context.Registry, 100);
                SetField(Host, "_towers", session);
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var tower = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AssetRaw/Actor/Tower/Prefabs/Tower.prefab"), Root.transform).GetComponent<TowerView>();
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
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Enemy/Prefabs/" + name + ".prefab");
                return UniTask.FromResult(UnityEngine.Object.Instantiate(prefab, parent));
            }
            public UniTask PrepareTask(Func<string, Transform, CancellationToken, UniTask<GameObject>> load = null) =>
                (UniTask)Controller.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(m => m.Name == "PrepareAsync" && m.GetParameters().Length == 5)
                    .Invoke(Controller, new object[] { Host, Context, Tables, CancellationToken.None, load ?? Load });
            public void Prepare(Func<string, Transform, CancellationToken, UniTask<GameObject>> load = null) => PrepareTask(load).GetAwaiter().GetResult();
            public void Start() => Invoke("StartFirstWave");
            public void Advance(float dt) => Invoke("Advance", dt);
            public void Clear() => Invoke("Clear");
            private void Invoke(string name, params object[] args)
            {
                try { Controller.GetType().GetMethod(name).Invoke(Controller, args); }
                catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
            }
            public int LiveCount => (int)Controller.GetType().GetProperty("LiveCount").GetValue(Controller);
            public bool Completed => (bool)Controller.GetType().GetProperty("IsWaveCompleted").GetValue(Controller);
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
