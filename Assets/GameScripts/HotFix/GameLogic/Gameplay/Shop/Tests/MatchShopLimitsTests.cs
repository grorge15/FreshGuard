using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace GameLogic.Tests
{
    public sealed class MatchShopLimitsTests
    {
        [Test] public void MixedCapsRemainPerRobotAndOpponentCanMergeUpToCapWhilePlayerKeepsFive()
        {
            var profile=ScriptableObject.CreateInstance<DemoBattleProfile>();
            try
            {
                profile.Robots.Add(new DemoRobotReference{RobotId=501,ReferenceLevel=2});
                profile.Robots.Add(new DemoRobotReference{RobotId=502,ReferenceLevel=4});
                profile.Robots.Add(new DemoRobotReference{RobotId=505,ReferenceLevel=1});
                var rules=ShopTestData.Rules(1,0,0);
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
                var snapshot=new MatchEntrySnapshot(profile,state,rules,new ShopSequenceRandom());
                using(var context=new BattleShopContext(rules,ShopTestData.Candidates,ShopTestData.Candidates,id=>ShopTestData.Robot(id),new ShopSequenceRandom(),snapshot))
                {
                    Assert.That(context.GetRobotLevelCap(BattleSide.Opponent,501),Is.EqualTo(2));
                    Assert.That(context.GetRobotLevelCap(BattleSide.Opponent,502),Is.EqualTo(4));
                    Assert.That(context.GetRobotLevelCap(BattleSide.Opponent,505),Is.EqualTo(1));
                    Assert.That(context.GetRobotLevelCap(BattleSide.Player,505),Is.EqualTo(5));
                    var target=ShopTestData.Purchase(context,0,BattleSide.Opponent);
                    var source=ShopTestData.Purchase(context,1,BattleSide.Opponent);
                    var board=new BoardModel(6,2);
                    Assert.That(context.TryDeploy(BattleSide.Opponent,target.InstanceId,BattleSide.Opponent,board,new BoardCoordinate(0,0)),Is.EqualTo(ShopOperationResult.Success));
                    Assert.That(context.TryMerge(BattleSide.Opponent,source.InstanceId,target.InstanceId),Is.EqualTo(ShopOperationResult.Success));
                    Assert.That(target.Level,Is.EqualTo(2));Assert.That(target.MaxLevel,Is.EqualTo(2));
                    var player=ShopTestData.Purchase(context,0,BattleSide.Player);
                    Assert.That(player.MaxLevel,Is.EqualTo(5));
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(profile);}
        }
        [Test] public void OpponentOneLevelCapClampsRerollPriceAndCreatedEntityAndRejectsMerge()
        {
            var constructor=typeof(BattleShopContext).GetConstructors().FirstOrDefault(c=>c.GetParameters().Any(p=>p.Name=="entry"));
            Assert.That(constructor,Is.Not.Null,"Context必须消费同一个入场快照");
            var profile=ScriptableObject.CreateInstance<DemoBattleProfile>();
            try
            {
                foreach(var id in ShopTestData.Candidates)profile.Robots.Add(new DemoRobotReference{RobotId=id,ReferenceLevel=1});
                var rules=new ShopRules(new IReadOnlyDictionary<int,double>[] {
                    new Dictionary<int,double>{{1,1}},new Dictionary<int,double>{{1,1}},new Dictionary<int,double>{{1,1}}},
                    new Dictionary<int,int>{{2,6},{3,8},{4,12},{5,16},{6,20}},initialCoins:100,secondStarProbability:1);
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
                var snapshot=new MatchEntrySnapshot(profile,state,rules,new ShopSequenceRandom());
                using(var context=(BattleShopContext)constructor.Invoke(new object[]{rules,ShopTestData.Candidates,ShopTestData.Candidates,(Func<int,GameConfig.robot.Robot>)(id=>ShopTestData.Robot(id)),new ShopSequenceRandom(),snapshot}))
                {
                    Assert.That(context.TryRefresh(BattleSide.Opponent),Is.EqualTo(ShopOperationResult.Success));
                    var offer=context.GetState(BattleSide.Opponent).Slots[0].Offer;
                    Assert.That(offer.Level,Is.EqualTo(1)); Assert.That(offer.Price,Is.EqualTo(6));
                    var target=ShopTestData.Purchase(context,context.GetState(BattleSide.Opponent).Slots[0].SlotId,BattleSide.Opponent);
                    var source=ShopTestData.Purchase(context,context.GetState(BattleSide.Opponent).Slots[1].SlotId,BattleSide.Opponent);
                    Assert.That(target.MaxLevel,Is.EqualTo(1));
                    var board=new BoardModel(4,2);
                    context.TryDeploy(BattleSide.Opponent,target.InstanceId,BattleSide.Opponent,board,new BoardCoordinate(0,0));
                    Assert.That(context.TryMerge(BattleSide.Opponent,source.InstanceId,target.InstanceId),Is.EqualTo(ShopOperationResult.MaxLevelReached));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
