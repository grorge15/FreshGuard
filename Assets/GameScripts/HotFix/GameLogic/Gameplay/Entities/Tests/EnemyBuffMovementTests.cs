using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyBuffMovementTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        [TearDown] public void TearDown()=>_f?.Dispose();
        [TestCase(2001,1f)] [TestCase(2002,0.6f)] [TestCase(2003,0.25f)]
        public void ActualEnemyConfigurationMultipliesTheAmountOfSlowItReceives(int configId,float received)
        {
            _f=new EnemyBattleControllerTests.Fixture();
            var id=_f.Context.Registry.CreateEnemyFromConfig(_f.Tables.TbEnemy.Get(configId),BattleSide.Player);
            _f.Context.Registry.TryGet(id,out var entity); var enemy=(EnemyEntity)entity;
            Slow(enemy,0.3f,3f,_f.Context.Battle.Player.OwnerRef);
            Assert.AreEqual(received,enemy.Configuration.ControlResist);
            Assert.That(enemy.EffectiveSpeed,Is.EqualTo(enemy.BaseSpeed*(1f-0.3f*received)).Within(0.00001f));
        }
        [Test]
        public void SlowSourcesHaveIndependentExpiryUseStrongestAndRespectHalfSpeedFloor()
        {
            _f=new EnemyBattleControllerTests.Fixture();
            var id=_f.Context.Registry.CreateEnemy(1,BattleSide.Player,100,10f);
            _f.Context.Registry.TryGet(id,out var entity); var enemy=(EnemyEntity)entity;
            Slow(enemy,0.3f,1f,_f.Context.Battle.Player.OwnerRef);
            Slow(enemy,0.9f,2f,_f.Context.Battle.Opponent.OwnerRef);
            Assert.AreEqual(5f,enemy.EffectiveSpeed);
            enemy.Buffs.Advance(1f); Assert.AreEqual(1,enemy.Buffs.Count); Assert.AreEqual(5f,enemy.EffectiveSpeed);
            enemy.Buffs.Advance(1f); Assert.AreEqual(0,enemy.Buffs.Count); Assert.AreEqual(10f,enemy.EffectiveSpeed);
        }
        [Test]
        public void LargeMovementFrameSplitsAtSlowExpiryAndRestoresBaseSpeedForRemainder()
        {
            _f=new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            var view=_f.PlayerView; _f.Context.Registry.TryGet(view.InstanceId,out var entity); var enemy=(EnemyEntity)entity;
            var start=view.transform.position;
            Slow(enemy,0.3f,3f,_f.Context.Battle.Player.OwnerRef);
            _f.Advance(4f);
            Assert.That(Vector3.Distance(start,view.transform.position),Is.EqualTo(0.3f*0.7f*3f+0.3f).Within(0.00001f));
            Assert.AreEqual(0,enemy.Buffs.Count); Assert.AreEqual(enemy.BaseSpeed,enemy.EffectiveSpeed);
        }
        [Test]
        public void ArrivalAndMovementUseTheSameSlowedSpeedAcrossExpiry()
        {
            var tables=WaveTestData.Tables(new[]{WaveTestData.Plan(1,1)},new[]{WaveTestData.Spawn(1,0,1,2001,1)});
            _f=new EnemyBattleControllerTests.Fixture(tables); _f.SetRoutes(new[]{new Vector3(-0.81f,0f,0f)}); _f.Prepare(); _f.Start();
            var id=_f.PlayerView.InstanceId; _f.Context.Registry.TryGet(id,out var entity);
            Slow((EnemyEntity)entity,0.3f,1f,_f.Context.Battle.Player.OwnerRef);
            _f.Advance(1.2f); Assert.IsTrue(_f.Context.Registry.TryGet(id,out _));
            Assert.AreEqual(5,_f.Host.Towers.PlayerTower.CurrentHp);
            _f.Advance(0.3f); Assert.IsFalse(_f.Context.Registry.TryGet(id,out _));
            Assert.AreEqual(4,_f.Host.Towers.PlayerTower.CurrentHp);
        }
        [Test]
        public void ZeroReceivedControlFactorIsImmuneAndUnconfiguredEnemyReceivesFullSlow()
        {
            _f=new EnemyBattleControllerTests.Fixture();
            var config=_f.Tables.TbEnemy.Get(2001);
            typeof(GameConfig.enemy.Enemy).GetField("ControlResist").SetValue(config,0f);
            var id=_f.Context.Registry.CreateEnemyFromConfig(config,BattleSide.Player);
            _f.Context.Registry.TryGet(id,out var entity); var enemy=(EnemyEntity)entity;
            Slow(enemy,0.3f,3f,_f.Context.Battle.Player.OwnerRef); Assert.AreEqual(enemy.BaseSpeed,enemy.EffectiveSpeed);
            var rawId=_f.Context.Registry.CreateEnemy(1,BattleSide.Player,100,10f);
            _f.Context.Registry.TryGet(rawId,out var raw); Slow((EnemyEntity)raw,0.3f,3f,_f.Context.Battle.Player.OwnerRef);
            Assert.AreEqual(7f,((EnemyEntity)raw).EffectiveSpeed);
        }
        [Test]
        public void SceneRuntimePhaseDoesNotDoubleAdvanceEnemyBuffBeforeMovement()
        {
            _f=new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            var view=_f.PlayerView; _f.Context.Registry.TryGet(view.InstanceId,out var entity); var enemy=(EnemyEntity)entity;
            Slow(enemy,0.3f,3f,_f.Context.Battle.Player.OwnerRef);
            _f.Context.Battle.AdvanceBeforeEnemyMovement(0.5f);
            Assert.AreEqual(3f,enemy.Buffs.Items[0].Remaining);
            _f.Advance(0.5f); Assert.That(enemy.Buffs.Items[0].Remaining,Is.EqualTo(2.5f).Within(0.00001f));
            _f.Host.SetFrozen(true);
            typeof(BattleShopSceneController).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(_f.Host,null);
            _f.Advance(10f); Assert.That(enemy.Buffs.Items[0].Remaining,Is.EqualTo(2.5f).Within(0.00001f));
        }
        private void Slow(EnemyEntity enemy,float amount,float duration,SkillOwnerRef source)=>
            _f.Context.Battle.AddBuff(enemy,new BuffModel(2505,"slow"),source,duration,new Dictionary<string,float>{{"slow",amount}});
    }
}
