using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class AddBuffInfoTests
    {
        private BattleRuntime _battle;
        private BuffModel _model;
        [SetUp] public void Prepare() { _battle = new BattleRuntime(701); _model = new BuffModel(900, "noop", 5, tickInterval: 1); }
        [TearDown] public void Clear() => _battle.Dispose();

        private void Send(float time = 0, bool set = false, int stacks = 1,
            IReadOnlyDictionary<string, float> param = null, bool permanent = false, long? instance = null)
        {
            _battle.Buffs.Submit(new AddBuffInfo(_model, _battle.Player.OwnerRef,
                _battle.Player.OwnerRef, time, set, stacks, param, permanent: permanent, instanceId: instance));
        }

        [Test] public void RuntimeModelWorksWithoutCatalogAndStacksCanIncreaseDecreaseOrStay()
        {
            Send(5, true, 2); var buff = _battle.Player.Buffs.Items.Single();
            Assert.AreSame(_model, buff.Model); Assert.AreEqual(2, buff.Stacks);
            Send(stacks: 2); Assert.AreEqual(4, buff.Stacks);
            Send(stacks: 0); Assert.AreEqual(4, buff.Stacks);
            Send(stacks: -3); Assert.AreEqual(1, buff.Stacks);
            Send(stacks: -1); Assert.AreEqual(0, _battle.Player.Buffs.Count);
            Send(5, true, -1); Assert.AreEqual(0, _battle.Player.Buffs.Count);
        }

        [Test] public void AppendingTimeKeepsTickPhaseWhileSettingTimeRestartsIt()
        {
            Send(5, true); var buff = _battle.Player.Buffs.Items.Single();
            _battle.Player.Buffs.Advance(0.4f);
            Send(2, stacks: 0); Assert.AreEqual(6.6f, buff.Remaining, 0.0001f);
            Assert.AreEqual(0.6, buff.NextTickRemaining, 0.0001);
            Send(3, true, 0); Assert.AreEqual(3, buff.Remaining); Assert.AreEqual(1, buff.NextTickRemaining);
            Send(-4, stacks: 0); Assert.AreEqual(0, _battle.Player.Buffs.Count);
        }

        [Test] public void PermanentBuffCanBeChangedBackToTimedBuff()
        {
            Send(permanent: true); var buff = _battle.Player.Buffs.Items.Single();
            _battle.Player.Buffs.Advance(10); Assert.AreEqual(1, _battle.Player.Buffs.Count);
            Assert.IsTrue(float.IsPositiveInfinity(buff.Remaining));
            Send(1, true, 0); _battle.Player.Buffs.Advance(1); Assert.AreEqual(0, _battle.Player.Buffs.Count);
        }

        [Test] public void NullParametersPreserveAndNewParametersReplace()
        {
            Send(5, true, param: new Dictionary<string, float> { { "value", 2 } });
            var buff = _battle.Player.Buffs.Items.Single();
            Send(stacks: 0); Assert.AreEqual(2, buff.Get("value"));
            Send(stacks: 0, param: new Dictionary<string, float> { { "other", 3 } });
            Assert.AreEqual(0, buff.Get("value")); Assert.AreEqual(3, buff.Get("other"));
        }

        [Test] public void CrossContainerRequestsWaitAndCopyParametersAtSubmission()
        {
            var values = new Dictionary<string, float> { { "value", 2 } };
            var calls = new List<string>();
            _battle.BuffBehaviors.Register("observe", () => new Hook { Occur = (c, b) => calls.Add("occur") });
            _battle.Player.Buffs.Add(_model, _battle.Player.OwnerRef, 5, behavior: new Hook { Tick = (c, b) =>
            {
                _battle.Buffs.Submit(new AddBuffInfo(new BuffModel(901, "observe"), c.Owner,
                    _battle.Opponent.OwnerRef, 3, true, param: values));
                Assert.AreEqual(0, _battle.Opponent.Buffs.Count);
                values["value"] = 100;
                calls.Add("tick-end");
            }});
            _battle.Player.Buffs.Advance(1);
            CollectionAssert.AreEqual(new[] { "tick-end", "occur" }, calls);
            Assert.AreEqual(2, _battle.Opponent.Buffs.Items.Single().Get("value"));
        }

        [Test] public void RemovedOrReusedCarrierCannotReceiveQueuedRequest()
        {
            var id = _battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
            _battle.Registry.TryGet(id, out var target);
            _battle.Player.Buffs.Add(_model, _battle.Player.OwnerRef, 5, behavior: new Hook { Tick = (c, b) =>
            {
                _battle.Buffs.Submit(new AddBuffInfo(_model, c.Owner, target.OwnerRef, 3, true));
                _battle.Registry.Remove(id, EntityRemovalReason.Manual, out _);
                _battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
            }});
            _battle.Player.Buffs.Advance(1);
            Assert.AreEqual(0, target.Buffs.Count);
        }

        [Test] public void RemovedCasterDoesNotCancelAlreadyAcceptedEffect()
        {
            var id = _battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
            _battle.Registry.TryGet(id, out var caster);
            var source = caster.OwnerRef;
            _battle.Registry.Remove(id, EntityRemovalReason.Manual, out _);
            _battle.Buffs.Submit(new AddBuffInfo(_model, source, _battle.Player.OwnerRef, 3, true));
            Assert.AreEqual(source, _battle.Player.Buffs.Items.Single().Source);
        }

        [Test] public void ExactInstanceModificationDoesNotAddOrReplacePoisonLayers()
        {
            var poison = new BuffModel(2507, "poison");
            var values = new Dictionary<string, float> { { "poisonDamage", 10 }, { "poisonInterval", 1 }, { "poisonMaxLayers", 3 } };
            for (var i = 0; i < 3; i++)
                _battle.Buffs.Submit(new AddBuffInfo(poison, _battle.Player.OwnerRef, _battle.Player.OwnerRef, 5, true, param: values));
            var layer = _battle.Player.Buffs.Items[1];
            _battle.Buffs.Submit(new AddBuffInfo(poison, layer.Source, _battle.Player.OwnerRef, 2, true, 0, instanceId: layer.InstanceId));
            Assert.AreEqual(3, _battle.Player.Buffs.Count); Assert.AreEqual(2, layer.Remaining);
            _battle.Buffs.Submit(AddBuffInfo.Remove(_battle.Player.OwnerRef, layer));
            Assert.AreEqual(2, _battle.Player.Buffs.Count); CollectionAssert.DoesNotContain(_battle.Player.Buffs.Items, layer);
            _battle.Buffs.Submit(AddBuffInfo.Clear(_battle.Player.OwnerRef));
            Assert.AreEqual(0, _battle.Player.Buffs.Count);
        }

        [Test] public void MergePreservesOtherKeysAndStacksClampWithoutOverflow()
        {
            Send(5, true, 2, new Dictionary<string, float> { { "value", 2 }, { "other", 3 } });
            _battle.Buffs.Submit(new AddBuffInfo(_model, _battle.Player.OwnerRef, _battle.Player.OwnerRef,
                addStack: int.MaxValue, param: new Dictionary<string, float> { { "value", 4 } }, mergeParameters: true));
            var buff = _battle.Player.Buffs.Items.Single();
            Assert.AreEqual(5, buff.Stacks); Assert.AreEqual(4, buff.Get("value")); Assert.AreEqual(3, buff.Get("other"));
        }

        [Test] public void WrongBattleAndMissingInstanceCannotChangeCurrentBuff()
        {
            Send(5, true);
            _battle.Buffs.Submit(new AddBuffInfo(_model, SkillOwnerRef.Battlefield(999, BattleSide.Player),
                _battle.Player.OwnerRef, 3, true));
            Send(3, true, 0, instance: 999);
            Assert.AreEqual(5, _battle.Player.Buffs.Items.Single().Remaining);
        }

        private sealed class Hook : BuffBehavior
        {
            public Action<BuffContainer, BuffObj> Occur;
            public Action<BuffContainer, BuffObj> Tick;
            public override void OnOccur(BuffContainer c, BuffObj b) => Occur?.Invoke(c, b);
            public override void OnTick(BuffContainer c, BuffObj b) => Tick?.Invoke(c, b);
        }
    }
}
