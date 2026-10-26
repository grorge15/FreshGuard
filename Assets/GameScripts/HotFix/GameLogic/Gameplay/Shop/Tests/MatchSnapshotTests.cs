using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace GameLogic.Tests
{
    public sealed class MatchSnapshotTests
    {
        [Test] public void FilteringPreservesOriginalDropOrdinalsOnBothSidesAndSnapshotCopiesInput()
        {
            var profile=ScriptableObject.CreateInstance<DemoBattleProfile>();
            try
            {
                profile.Robots.Add(new DemoRobotReference{RobotId=501,Unlocked=false});
                foreach(var id in new[]{502,505,601}) profile.Robots.Add(new DemoRobotReference{RobotId=id,ReferenceLevel=2});
                var slots=Enumerable.Range(0,3).Select(i=>(IReadOnlyDictionary<int,double>)new Dictionary<int,double>{{1,1000},{2,0},{3,1},{4,0}}).ToArray();
                var rules=new ShopRules(slots,new Dictionary<int,int>{{2,6},{3,8},{4,12},{5,16},{6,20}});
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,1234,DateTimeOffset.UtcNow){PlayerName="点击时的名字"};
                var entry=new MatchEntrySnapshot(profile,state,rules);
                profile.Robots[2].ReferenceLevel=5;profile.Robots.Clear();state.PlayerName="已修改";
                Assert.That(entry.PlayerName,Is.EqualTo("点击时的名字"));Assert.That(entry.Trophies,Is.EqualTo(1234));
                Assert.That(entry.Candidates,Is.EqualTo(new[]{502,505,601})); Assert.That(entry.OpponentLevelCaps[505],Is.EqualTo(2));
                using(var context=new BattleShopContext(rules,ShopTestData.Candidates,ShopTestData.Candidates,id=>ShopTestData.Robot(id),entry:entry))
                    foreach(var side in new[]{BattleSide.Player,BattleSide.Opponent})
                        Assert.That(context.GetState(side).Slots.All(s=>s.Offer.RobotId==505),Is.True,"不能把原第一候选的权重错移给502");
            }
            finally{UnityEngine.Object.DestroyImmediate(profile);}
        }
        [Test] public void PlayerBuffLayersDoNotIncreaseOpponentKindCountAndDefinitionsAreCopied()
        {
            var profile=ScriptableObject.CreateInstance<DemoBattleProfile>();
            try
            {
                foreach(var id in ShopTestData.Candidates)profile.Robots.Add(new DemoRobotReference{RobotId=id});
                profile.Buffs.Add(new DemoBuffDefinition{BuffId=1,Name="Buff",IconLocation="icon",Unlocked=true});
                profile.PlayerBuffs.Add(new DemoBuffCarry{BuffId=1,Layers=9});
                var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
                var entry=new MatchEntrySnapshot(profile,state,ShopTestData.Rules(),new ShopSequenceRandom());
                profile.Buffs[0].Name="changed";profile.PlayerBuffs[0].Layers=100;
                Assert.That(entry.PlayerBuffs[0].Layers,Is.EqualTo(9));Assert.That(entry.PlayerBuffs[0].Name,Is.EqualTo("Buff"));
                Assert.That(entry.OpponentBuffs.Count,Is.EqualTo(1));Assert.That(entry.OpponentBuffs[0].Layers,Is.EqualTo(1));
            }
            finally{UnityEngine.Object.DestroyImmediate(profile);}
        }
    }
}
