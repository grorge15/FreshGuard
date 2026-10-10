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
            var prefabs = new[] { "SkeletonEnemy", "EliteSkeletonEnemy", "SkeletonBossEnemy" };
            foreach (var name in prefabs)
            {
                var prefab = assets.Single(a => a.AssetInfo.AssetPath ==
                    "Assets/AssetRaw/Actor/Enemy/Prefabs/" + name + ".prefab");
                Assert.AreEqual(name, prefab.Address);
                var dependencies = AssetDatabase.GetDependencies(prefab.AssetInfo.AssetPath, true);
                Assert.IsTrue(dependencies.Any(p => p.EndsWith(".controller")));
                Assert.AreEqual(4, dependencies.Count(p => p.EndsWith(".anim")));
                Assert.IsTrue(dependencies.Where(p => p.StartsWith("Assets/AssetRaw/Actor/Enemy/"))
                    .All(p => assets.Any(a => a.AssetInfo.AssetPath == p)));
            }
            foreach (var asset in assets.Where(a => a.AssetInfo.AssetPath.StartsWith("Assets/AssetRaw/Actor/")))
            {
                var path = asset.AssetInfo.AssetPath;
                if (path.StartsWith("Assets/AssetRaw/Actor/Enemy/Frame/") ||
                    path.StartsWith("Assets/AssetRaw/Actor/Enemy/Animations/"))
                    Assert.AreEqual(path.Substring("Assets/AssetRaw/Actor/".Length), asset.Address);
                else
                    Assert.AreEqual(Path.GetFileNameWithoutExtension(path), asset.Address);
            }
        }
    }
}
