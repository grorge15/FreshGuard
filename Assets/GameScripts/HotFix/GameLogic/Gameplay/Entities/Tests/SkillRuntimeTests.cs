using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class SkillRuntimeTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private SkillCatalog _catalog;
        private readonly List<string> _events = new List<string>();
        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry(); _battle = new BattleRuntime(41, _registry, new CombatRuntime(41));
            _catalog = new SkillCatalog(); _battle.Configure(_catalog); _events.Clear();
        }
        [TearDown] public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        [Test]
        public void BattlefieldDirectActionNeedsNoEntityAndRejectsCrossSideTargets()
        {
            var buff = new BuffModel(9, "noop"); _catalog.Add(buff);
            _battle.Timelines.Register(new TimelineModel("direct", 0f, new[]
            { new TimelineNode(0f, "buff.add", new Dictionary<string, float> { { "buffId", 9 }, { "duration", 3 } }) }));
            var model = AddSkill(1, "direct", SkillTargetRule.SameSideBattlefield, cooldown: 2f);
            _battle.Grant(_battle.Player, model); _battle.Grant(_battle.Opponent, model);
            Assert.IsNull(_battle.Player.OwnerRef.EntityId);
            Assert.AreEqual(0, _registry.CopyIds().Length);
            Assert.AreEqual(SkillCastFailure.InvalidTarget, Cast(_battle.Player, 1, SkillTarget.Battlefield(BattleSide.Opponent)).Failure);
            Assert.IsTrue(Cast(_battle.Player, 1, SkillTarget.Battlefield(BattleSide.Player)).Accepted);
            Assert.AreEqual(1, _battle.Player.Buffs.Count); Assert.AreEqual(0, _battle.Opponent.Buffs.Count);
            Assert.AreEqual(0, _battle.Bullets.Count);
            Assert.AreEqual(SkillCastFailure.Cooldown, Cast(_battle.Player, 1, SkillTarget.Battlefield(BattleSide.Player)).Failure);
            Assert.IsTrue(Cast(_battle.Opponent, 1, SkillTarget.Battlefield(BattleSide.Opponent)).Accepted);
            _battle.Player.Skills.TryGet(1, out var first); _battle.Opponent.Skills.TryGet(1, out var second);
            first.SetLevel(2); Assert.AreEqual(1, second.Level);
            _battle.Advance(2f); Assert.AreEqual(0f, first.CooldownRemaining);
            Assert.AreEqual(1f, _battle.Player.Buffs.Items[0].Remaining);
        }

        [TestCase(SkillTargetRule.SameSideRobot)] [TestCase(SkillTargetRule.SameSideEntity)]
        public void BattlefieldCanTargetOwnRobotAndRejectsOtherSideAndStaleIdentity(SkillTargetRule rule)
        {
            RegisterLog("direct", 0f, new TimelineNode(0f, "log"));
            var model = AddSkill(1, "direct", rule); _battle.Grant(_battle.Player, model);
            var own = _registry.CreateRobot(1, BattleSide.Player, 1, 7);
            var other = _registry.CreateRobot(1, BattleSide.Opponent, 1, 7);
            Assert.IsTrue(Cast(_battle.Player, 1, SkillTarget.Entity(own.Side, own.InstanceId)).Accepted);
            Assert.AreEqual(SkillCastFailure.InvalidTarget, Cast(_battle.Player, 1, SkillTarget.Entity(other.Side, other.InstanceId)).Failure);
            _registry.Remove(own.InstanceId, EntityRemovalReason.Manual, out _);
            Assert.AreEqual(SkillCastFailure.InvalidTarget, Cast(_battle.Player, 1, SkillTarget.Entity(BattleSide.Player, own.InstanceId)).Failure);
            Assert.AreEqual(SkillCastFailure.InvalidOwner, _battle.Skills.TryCast(new SkillCastRequest(
                SkillOwnerRef.Battlefield(99, BattleSide.Player), 1, SkillTriggerSource.Button, SkillTarget.None)).Failure);
        }

        [Test]
        public void TimelineExecutesZeroStableTiesCrossFrameAndEndAndReleasesChannel()
        {
            _battle.Timelines.RegisterAction("zero", new CallbackAction((cast, node) => _events.Add("zero")));
            _battle.Timelines.RegisterAction("a", new CallbackAction((cast, node) => _events.Add("a")));
            _battle.Timelines.RegisterAction("b", new CallbackAction((cast, node) => _events.Add("b")));
            _battle.Timelines.RegisterAction("end", new CallbackAction((cast, node) => _events.Add("end")));
            _battle.Timelines.Register(new TimelineModel("ordered", 1f, new[]
            { new TimelineNode(0f,"zero"), new TimelineNode(0.2f,"a"), new TimelineNode(0.2f,"b"), new TimelineNode(1f,"end") }));
            var model = AddSkill(1, "ordered"); _battle.Grant(_battle.Player, model);
            Assert.IsTrue(Cast(_battle.Player, 1).Accepted); CollectionAssert.AreEqual(new[] { "zero" }, _events);
            Assert.AreEqual(SkillCastFailure.Busy, Cast(_battle.Player, 1).Failure);
            _battle.Advance(1.5f); CollectionAssert.AreEqual(new[] { "zero", "a", "b", "end" }, _events);
            Assert.AreEqual(0, _battle.Timelines.Count);
            Assert.IsTrue(Cast(_battle.Player, 1).Accepted);
            _battle.Timelines.Cancel(_battle.Player.OwnerRef); _battle.Advance(3f);
            Assert.AreEqual(5, _events.Count, "取消只保留已执行零时刻节点");
        }

        [Test]
        public void OwnerRemovalCancelsPendingNodesWhilePooledIdentityCannotReviveThem()
        {
            RegisterLog("later", 1f, new TimelineNode(1f, "log"));
            var model = AddSkill(1, "later");
            var id = _registry.CreateEnemy(1, BattleSide.Player, 100, 1f);
            _registry.TryGet(id, out var entity); var reference = entity.OwnerRef; _battle.Grant(entity, model);
            Assert.IsTrue(Cast(entity, 1).Accepted);
            _registry.Remove(id, EntityRemovalReason.Manual, out _);
            _registry.CreateEnemy(1, BattleSide.Player, 100, 1f);
            _battle.Advance(2f); Assert.IsEmpty(_events); Assert.AreEqual(0, _battle.Timelines.Count);
            Assert.IsFalse(_battle.TryResolveOwner(reference, out _));
        }

        [Test]
        public void OnCastReplacesTimelineWithCopiedParametersAndCancellationShortCircuits()
        {
            RegisterLog("original", 0f, new TimelineNode(0f,"log"));
            _battle.Timelines.RegisterAction("read", new CallbackAction((cast,node) => _events.Add(cast.Get("value").ToString())));
            _battle.Timelines.Register(new TimelineModel("replacement", 0f, new[] { new TimelineNode(0f,"read") }));
            var model = AddSkill(1,"original", cooldown: 2f); _battle.Grant(_battle.Player,model); _battle.Grant(_battle.Opponent,model);
            _battle.Player.Buffs.Add(new BuffModel(10,"custom",priority:10),_battle.Player.OwnerRef,10f,behavior:new CallbackBuff
            { Cast = (container,buff,context) => { context.TimelineKey="replacement"; context.Parameters["value"]=17f; } });
            Assert.IsTrue(Cast(_battle.Player,1).Accepted); CollectionAssert.AreEqual(new[] { "17" },_events);
            Assert.IsTrue(Cast(_battle.Opponent,1).Accepted); Assert.AreEqual("original",_events.Last());
            _battle.Advance(2f); _battle.Player.Buffs.Clear(); _events.Clear();
            _battle.Player.Buffs.Add(new BuffModel(11,"custom",priority:20),_battle.Player.OwnerRef,10f,behavior:new CallbackBuff
            { Cast = (container,buff,context) => { _events.Add("cancel"); context.Cancel(); } });
            _battle.Player.Buffs.Add(new BuffModel(12,"custom",priority:10),_battle.Player.OwnerRef,10f,behavior:new CallbackBuff
            { Cast = (container,buff,context) => _events.Add("must not run") });
            Assert.AreEqual(SkillCastFailure.Cancelled,Cast(_battle.Player,1).Failure);
            CollectionAssert.AreEqual(new[] { "cancel" },_events);
            _battle.Player.Skills.TryGet(1,out var skill); Assert.AreEqual(0f,skill.CooldownRemaining);
        }

        [Test]
        public void InvalidReplacementAndUnpreparedActionsDoNotCommitCooldown()
        {
            RegisterLog("valid",0f,new TimelineNode(0f,"log"));
            var model=AddSkill(1,"valid",cooldown:4f); _battle.Grant(_battle.Player,model);
            _battle.Player.Buffs.Add(new BuffModel(10,"custom"),_battle.Player.OwnerRef,10f,behavior:new CallbackBuff
            { Cast=(container,buff,context)=>context.TimelineKey="missing" });
            Assert.AreEqual(SkillCastFailure.InvalidTimeline,Cast(_battle.Player,1).Failure);
            _battle.Player.Buffs.Clear();
            _battle.Timelines.RegisterAction("not-ready",new CallbackAction(null, false));
            _battle.Timelines.Register(new TimelineModel("not-ready",0f,new[]{new TimelineNode(0f,"not-ready")}));
            _battle.Player.Buffs.Add(new BuffModel(10,"custom"),_battle.Player.OwnerRef,10f,behavior:new CallbackBuff
            { Cast=(container,buff,context)=>context.TimelineKey="not-ready" });
            Assert.AreEqual(SkillCastFailure.NotPrepared,Cast(_battle.Player,1).Failure);
            _battle.Player.Skills.TryGet(1,out var skill); Assert.AreEqual(0f,skill.CooldownRemaining); Assert.IsEmpty(_events);
        }

        [Test]
        public void BuffStacksRefreshesSourcesAndTicksAcrossPeriodsIncludingExpiry()
        {
            var container=_battle.Player.Buffs; var model=new BuffModel(10,"custom",3,tickInterval:0.25f);
            var occurred=0; var ticks=0; var removed=0;
            var behavior=new CallbackBuff { Occur=(c,b)=>occurred++, Tick=(c,b)=>ticks++, Removed=(c,b)=>removed++ };
            var parameters=new Dictionary<string,float>{{"slow",0.2f}};
            container.Add(model,_battle.Player.OwnerRef,1f,parameters,behavior); parameters["slow"]=0.9f;
            container.Advance(0.3f); Assert.AreEqual(1,ticks);
            container.Add(model,_battle.Player.OwnerRef,1f,new Dictionary<string,float>{{"slow",0.4f}},behavior);
            container.Add(model,_battle.Opponent.OwnerRef,2f,behavior:behavior);
            Assert.AreEqual(2,container.Count); Assert.AreEqual(2,container.Find(10,_battle.Player.OwnerRef).Stacks);
            Assert.AreEqual(3,occurred); Assert.AreEqual(0.4f,container.Find(10,_battle.Player.OwnerRef).Get("slow"));
            container.Advance(1f); Assert.AreEqual(9,ticks); Assert.AreEqual(1,removed);
            Assert.IsNull(container.Find(10,_battle.Player.OwnerRef));
            Assert.AreEqual(1f,container.Find(10,_battle.Opponent.OwnerRef).Remaining);
            container.Clear(); Assert.AreEqual(2,removed);
        }

        [Test]
        public void BuffCallbackMutationsUseStableSnapshotAndApplyAfterRound()
        {
            RegisterLog("zero",0f,new TimelineNode(0f,"log"));
            var skill=AddSkill(1,"zero"); _battle.Grant(_battle.Player,skill);
            var container=_battle.Player.Buffs;
            var a=new BuffModel(1,"custom",priority:20); var b=new BuffModel(2,"custom",priority:10); var c=new BuffModel(3,"custom",priority:5);
            var once=true;
            container.Add(a,_battle.Player.OwnerRef,10f,behavior:new CallbackBuff { Cast=(co,bu,ct)=>
            {
                _events.Add("a"); if(!once)return; once=false;
                co.Remove(co.Find(2,_battle.Player.OwnerRef));
                co.Add(c,_battle.Player.OwnerRef,10f,behavior:new CallbackBuff { Cast=(x,y,z)=>_events.Add("c") });
            }});
            container.Add(b,_battle.Player.OwnerRef,10f,behavior:new CallbackBuff { Cast=(co,bu,ct)=>_events.Add("b") });
            Assert.IsTrue(Cast(_battle.Player,1).Accepted); CollectionAssert.AreEqual(new[]{"a","b","zero"},_events);
            _events.Clear(); Assert.IsTrue(Cast(_battle.Player,1).Accepted); CollectionAssert.AreEqual(new[]{"a","c","zero"},_events);
        }

        [Test]
        public void RegistryBuffCallbackCanRecycleAndCreateEntitiesWithoutInvalidatingIteration()
        {
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var enemy);
            var second=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(second,out var next);
            var oldBuffs=enemy.Buffs; var tick=0;
            oldBuffs.Add(new BuffModel(10,"custom",tickInterval:0.25f),enemy.OwnerRef,2f,behavior:new CallbackBuff
            { Tick=(container,buff)=>
            {
                tick++; _registry.Remove(id,EntityRemovalReason.Manual,out _);
                _registry.CreateEnemy(1,BattleSide.Player,100,1f);
            }});
            next.Buffs.Add(new BuffModel(11,"custom",tickInterval:0.25f),next.OwnerRef,2f,behavior:new CallbackBuff { Tick=(c,b)=>tick++ });
            Assert.DoesNotThrow(()=>_registry.AdvanceTime(0.5f)); Assert.AreEqual(3,tick);
            Assert.IsTrue(oldBuffs.IsDisposed); Assert.AreEqual(0,oldBuffs.Count);
        }

        [Test]
        public void ExpiryTickCanRefreshItsOwnBuffWithoutLosingTheRefresh()
        {
            var container=_battle.Player.Buffs; var model=new BuffModel(10,"custom",tickInterval:1f);
            container.Add(model,_battle.Player.OwnerRef,1f,behavior:new CallbackBuff
            { Tick=(c,b)=>c.Add(model,_battle.Player.OwnerRef,2f) });
            container.Advance(1f);
            Assert.AreEqual(1,container.Count,"到期回调刷新必须覆盖本次过期移除");
            Assert.AreEqual(2f,container.Items[0].Remaining);
        }

        [Test]
        public void EntityBuffCanEndBattleWithoutCallingDisposedCombatRuntime()
        {
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var entity);
            entity.Buffs.Add(new BuffModel(10,"custom",tickInterval:0.25f),entity.OwnerRef,2f,
                behavior:new CallbackBuff { Tick=(c,b)=>_battle.Dispose() });
            Assert.DoesNotThrow(()=>_battle.Advance(1f)); Assert.IsTrue(_battle.IsDisposed);
        }

        [Test]
        public void PassiveBuffUsesConfiguredBehaviorAndBattleDisposeClearsAllOwners()
        {
            var occurred=0;
            _battle.BuffBehaviors.Register("passive",()=>new CallbackBuff{Occur=(c,b)=>occurred++});
            _catalog.Add(new BuffModel(5,"passive"));
            var model=new SkillModel(1,SkillType.Battlefield,"unused",1,0f,SkillTargetRule.None,new[]{5});
            _catalog.Add(model); _catalog.Add(new SkillLevelModel(1,1,null));
            _battle.Grant(_battle.Player,model); _battle.Grant(_battle.Player,model); _battle.Grant(_battle.Opponent,model);
            Assert.AreEqual(2,occurred);
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var entity); _battle.Grant(entity,model);
            _battle.Dispose(); Assert.AreEqual(0,entity.Skills.Count); Assert.AreEqual(0,entity.Buffs.Count);
            Assert.AreEqual(0,_battle.Player.Buffs.Count); Assert.AreEqual(0,_battle.Opponent.Buffs.Count);
            Assert.AreEqual(SkillCastFailure.BattleEnded,Cast(_battle.Player,1).Failure);
        }

        [Test]
        public void PassiveOccurCanCreateAnotherEntityWithADifferentReservedIdentity()
        {
            var childId=0;
            AddRobotPassive(()=>childId=_registry.CreateEnemy(1,BattleSide.Player,100,1f));
            RobotEntity robot=null;
            Assert.DoesNotThrow(()=>robot=_registry.CreateRobot(1,BattleSide.Player,1,7));
            Assert.AreNotEqual(robot.InstanceId,childId); Assert.AreEqual(2,_registry.CopyIds().Length);
            Assert.AreEqual(3,_registry.CreateEnemy(1,BattleSide.Player,100,1f));
        }

        [Test]
        public void ThrowingPassiveOccurRollsBackOnlyItsOwnEntityAndNeverReusesItsId()
        {
            var childId=0;
            AddRobotPassive(()=>
            {
                childId=_registry.CreateEnemy(1,BattleSide.Player,100,1f);
                throw new InvalidOperationException("expected passive failure");
            });
            Assert.Throws<InvalidOperationException>(()=>_registry.CreateRobot(1,BattleSide.Player,1,7));
            CollectionAssert.AreEqual(new[]{childId},_registry.CopyIds());
            Assert.AreEqual(3,_registry.CreateEnemy(1,BattleSide.Player,100,1f));
        }

        [Test]
        public void ValidationFailureDoesNotConsumeAnEntityIdentity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>_registry.CreateEnemy(-1,BattleSide.Player,100,1f));
            Assert.AreEqual(1,_registry.CreateEnemy(1,BattleSide.Player,100,1f));
        }

        [Test]
        public void RegistryDisposeRejectsCreationAndReentryDuringRemovedCallbacks()
        {
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var entity);
            var rejected=false;
            entity.Buffs.Add(new BuffModel(10,"custom"),entity.OwnerRef,10f,behavior:new CallbackBuff { Removed=(c,b)=>
            {
                try { _registry.CreateEnemy(1,BattleSide.Player,100,1f); }
                catch(ObjectDisposedException) { rejected=true; }
                _registry.Dispose();
            }});
            Assert.DoesNotThrow(()=>_registry.Dispose()); Assert.IsTrue(rejected); Assert.AreEqual(0,_registry.CopyIds().Length);
        }

        [Test]
        public void ThrowingRemovedCallbackStillReturnsPooledEnemyAndDisposesEveryRegisteredEntity()
        {
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var first);
            var id2=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id2,out var second);
            first.Buffs.Add(new BuffModel(10,"custom"),first.OwnerRef,10f,behavior:new CallbackBuff
            { Removed=(c,b)=>throw new InvalidOperationException("expected removal failure") });
            Assert.Throws<InvalidOperationException>(()=>_registry.Dispose());
            Assert.AreEqual(0,_registry.CopyIds().Length); Assert.IsFalse(first.IsInitialized); Assert.IsFalse(second.IsInitialized);
            Assert.Throws<ObjectDisposedException>(()=>_registry.CreateEnemy(1,BattleSide.Player,100,1f));
        }

        private void AddRobotPassive(Action occur)
        {
            _battle.BuffBehaviors.Register("spawn-on-occur",()=>new CallbackBuff { Occur=(c,b)=>occur() });
            _catalog.Add(new BuffModel(20,"spawn-on-occur"));
            _catalog.Add(new SkillModel(7,SkillType.NormalAttack,"unused",1,0f,SkillTargetRule.None,new[]{20}));
            _catalog.Add(new SkillLevelModel(7,1,null));
        }

        [Test]
        public void BattleDisposeContinuesAfterOneBattlefieldBuffRemovalThrows()
        {
            _battle.Player.Buffs.Add(new BuffModel(10,"custom"),_battle.Player.OwnerRef,10f,
                behavior:new CallbackBuff { Removed=(c,b)=>throw new InvalidOperationException("expected battlefield removal failure") });
            _battle.Opponent.Buffs.Add(new BuffModel(11,"noop"),_battle.Opponent.OwnerRef,10f);
            var id=_registry.CreateEnemy(1,BattleSide.Player,100,1f); _registry.TryGet(id,out var entity);
            entity.Buffs.Add(new BuffModel(12,"noop"),entity.OwnerRef,10f);
            Assert.Throws<InvalidOperationException>(()=>_battle.Dispose());
            Assert.IsTrue(_battle.Combat.IsDisposed); Assert.AreEqual(0,_battle.Opponent.Buffs.Count);
            Assert.AreEqual(0,entity.Buffs.Count); Assert.IsTrue(entity.Buffs.IsDisposed);
        }

        [Test]
        public void LubanSkillLevelAndBuffDataAreLoadedAndConvertedWithoutSharingMutableParameters()
        {
            var tables=new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"AssetRaw/Configs/bytes",name+".bytes"))));
            var catalog=SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGetLevel(1505,1,out var level)); Assert.AreEqual(20,level.Parameters["damage"]);
            Assert.AreEqual(0.3f,level.Parameters["slow"]); Assert.AreEqual(3f,level.Parameters["duration"]);
            Assert.IsTrue(catalog.TryGetBuff(2505,out var buff)); Assert.AreEqual("slow",buff.BehaviorKey);
            Assert.AreEqual(8,tables.TbSkill.DataList.Count);
        }

        private SkillModel AddSkill(int id,string timeline,SkillTargetRule rule=SkillTargetRule.None,float cooldown=0f)
        {
            var model=new SkillModel(id,SkillType.Battlefield,timeline,2,cooldown,rule); _catalog.Add(model);
            _catalog.Add(new SkillLevelModel(id,1,new Dictionary<string,float>{{"value",1f}}));
            _catalog.Add(new SkillLevelModel(id,2,new Dictionary<string,float>{{"value",2f}})); return model;
        }
        private void RegisterLog(string key,float duration,params TimelineNode[] nodes)
        {
            // 每个测试中log动作只有一个，使用调用时的时间轴名确认选择。
            _battle.Timelines.RegisterAction("log",new CallbackAction((cast,node)=>_events.Add(cast.TimelineKey)));
            _battle.Timelines.Register(new TimelineModel(key,duration,nodes));
        }
        private SkillCastResult Cast(ISkillOwner owner,int skill,SkillTarget? target=null) =>
            _battle.Skills.TryCast(new SkillCastRequest(owner.OwnerRef,skill,SkillTriggerSource.Button,target??SkillTarget.None));
        private sealed class CallbackAction : ITimelineAction
        {
            private readonly Action<SkillCastSnapshot,TimelineNode> _callback; private readonly bool _ready;
            internal CallbackAction(Action<SkillCastSnapshot,TimelineNode> callback,bool ready=true){_callback=callback;_ready=ready;}
            public bool TryPrepare(SkillCastSnapshot cast,TimelineNode node,out Action execute)
            { execute=()=>_callback?.Invoke(cast,node); return _ready; }
        }
        private sealed class CallbackBuff : BuffBehavior
        {
            internal Action<BuffContainer,BuffObj> Occur,Tick,Removed;
            internal Action<BuffContainer,BuffObj,SkillCastContext> Cast;
            public override void OnOccur(BuffContainer c,BuffObj b)=>Occur?.Invoke(c,b);
            public override void OnTick(BuffContainer c,BuffObj b)=>Tick?.Invoke(c,b);
            public override void OnRemoved(BuffContainer c,BuffObj b)=>Removed?.Invoke(c,b);
            public override void OnCast(BuffContainer c,BuffObj b,SkillCastContext x)=>Cast?.Invoke(c,b,x);
        }
    }
}
