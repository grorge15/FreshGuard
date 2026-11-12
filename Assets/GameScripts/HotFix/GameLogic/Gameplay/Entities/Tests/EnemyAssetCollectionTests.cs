using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using YooAsset.Editor;

namespace GameLogic.Tests
{
    public sealed class EnemyAssetCollectionTests
    {
        [Test]
        public void EnemyAssetsHaveUniqueAddressesAndPrefabNamesRemainLoadable()
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset");
            var assets = setting.BeginCollect("DefaultPackage", true, false).CollectAssets;
            Assert.AreEqual(assets.Count, assets.Select(a => a.Address).Distinct().Count());
            var prefabs = new[] { "SpinachEnemy", "ExpiredCanEnemy", "GhostOnionEnemy", "AppleEnemy" };
            foreach (var name in prefabs)
            {
                var prefab = assets.Single(a => Path.GetFileName(a.AssetInfo.AssetPath) == name + ".prefab");
                Assert.AreEqual(name, prefab.Address);
                var dependencies = AssetDatabase.GetDependencies(prefab.AssetInfo.AssetPath, true);
                Assert.IsTrue(dependencies.Any(p => p.EndsWith(".controller")));
                Assert.AreEqual(4, dependencies.Count(p => p.EndsWith(".anim")));
                Assert.IsTrue(dependencies.Where(p => p.StartsWith("Assets/AssetRaw/Actor/"))
                    .All(p => assets.Any(a => a.AssetInfo.AssetPath == p)));
            }

            var frames = assets.Where(a => Path.GetFileName(a.AssetInfo.AssetPath) == "frame_016.png").ToArray();
            Assert.GreaterOrEqual(frames.Length, 2, "必须覆盖不同敌人的同名帧图片");
            Assert.AreEqual(frames.Length, frames.Select(a => a.Address).Distinct().Count());
        }

        [Test]
        public void PhysicsBallPrefabAndSpriteHaveDifferentAddresses()
        {
            var setting = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset");
            var assets = setting.BeginCollect("DefaultPackage", true, false).CollectAssets;
            var prefab = assets.Single(a => Path.GetFileName(a.AssetInfo.AssetPath) == "PhysicalCollisionBall.prefab");
            var sprite = assets.Single(a => Path.GetFileName(a.AssetInfo.AssetPath) == "PhysicalCollisionBall.png");
            Assert.AreEqual("PhysicalCollisionBall", prefab.Address);
            Assert.AreNotEqual(prefab.Address, sprite.Address);
        }
    }
}
