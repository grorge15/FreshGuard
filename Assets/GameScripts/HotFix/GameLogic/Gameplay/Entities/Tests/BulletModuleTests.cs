using System;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BulletModuleTests
    {
        [Test]
        public void TrackingArcUsesLatestTargetAndReportsHitAtMostOnce()
        {
            var world=new World(); var hits=0;
            var state=State(world,s=>{hits++; Assert.IsTrue(s.IsResolved); Assert.IsTrue(s.Advance(10f));});
            Assert.IsFalse(state.Advance(0.5f)); Assert.AreEqual(new Vector3(1f,1f,0f),state.Position);
            world.Position=new Vector3(4f,0f,0f);
            Assert.IsTrue(state.Advance(0.5f)); Assert.AreEqual(world.Position,state.Position);
            Assert.IsTrue(state.Advance(1f)); Assert.AreEqual(1,hits);
        }
        [Test]
        public void MissingTargetEndsWithoutRetargetingAndFreezeDoesNotAdvance()
        {
            var world=new World(); var hits=0; var state=State(world,s=>hits++);
            world.Running=false; Assert.IsFalse(state.Advance(5f)); Assert.AreEqual(0f,state.Elapsed);
            world.Running=true; world.TargetValid=false;
            Assert.IsTrue(state.Advance(1f)); Assert.AreEqual(0,hits);
            world.TargetValid=true; Assert.IsTrue(state.Advance(1f)); Assert.AreEqual(0,hits);
        }
        [Test]
        public void ManagerReleasesExactlyOnceWhenHitSynchronouslyEndsBattle()
        {
            var world=new World(); var manager=new BulletManager(); var released=0;
            manager.Add(State(world,s=>manager.Dispose()),null,()=>released++);
            manager.Add(State(world,s=>Assert.Fail("终局后不得命中")),null,()=>released++);
            manager.Advance(1f); Assert.AreEqual(2,released); Assert.AreEqual(0,manager.Count);
            manager.Dispose(); Assert.AreEqual(2,released);
        }
        private static BulletState State(World world,Action<BulletState> hit)
        {
            var action=new CombatActionSnapshot(1,1,BattleSide.Player,1500,new[]{2},CombatOutcomeTag.SingleDamage,
                20,Vector3.zero,world.Position,1);
            return new BulletState(new BulletLauncher(new BulletModel(1f,1f),action),world,hit);
        }
        private sealed class World : IBulletWorld
        {
            internal Vector3 Position=new Vector3(2f,0f,0f);
            internal bool Valid=true,Running=true,TargetValid=true;
            public bool IsValid(long battleId)=>Valid&&battleId==1;
            public bool IsRunning=>Running;
            public bool TryGetPosition(BattleSide side,int id,out Vector3 position){position=Position;return TargetValid&&id==2;}
        }
    }
}
