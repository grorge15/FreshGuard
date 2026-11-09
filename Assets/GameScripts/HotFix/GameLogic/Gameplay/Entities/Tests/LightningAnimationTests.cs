using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class LightningAnimationTests
    {
        private const string Folder = "Assets/AssetRaw/Actor/Prefabs/bullets/";

        [TestCase("LightningSpark", 10)]
        [TestCase("LightningArc", 5)]
        public void ClipsContainEverySourceFrameInOrder(string name, int frameCount)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Effects/" + name + ".anim");
            Assert.IsTrue(clip != null, "需要可编辑的序列帧 AnimationClip。");
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            Assert.AreEqual(frameCount, keys.Select(k => k.value).Distinct().Count());
            CollectionAssert.AreEqual(keys.Take(frameCount).Select(k => k.value.name).OrderBy(n => n),
                keys.Take(frameCount).Select(k => k.value.name));
            Assert.AreEqual(frameCount / 24f, clip.length, 0.0001f);
        }

        [Test]
        public void HitAnimationUsesSnapshotsPausesAndClearsForReuse()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "LightningChainEffect.prefab"));
            try
            {
                Assert.IsEmpty(root.GetComponentsInChildren<LineRenderer>(true), "旧程序线条必须被序列帧替换。");
                var view = root.GetComponent<LightningChainEffectView>();
                var positions = new List<Vector3> { new Vector3(1, 2, 0), new Vector3(3, 2, 0), new Vector3(3, 4, 0) };
                view.Play(positions);
                var segment = root.transform.Find("ChainSegments").GetComponentsInChildren<SpriteRenderer>().First(r => r.enabled).transform;
                Assert.AreEqual(positions[0], segment.position);
                Assert.That(Vector3.Distance(positions[1], segment.TransformPoint(Vector3.right * 4f)), Is.LessThan(0.001f));
                positions[0] = Vector3.zero;
                Assert.AreEqual(new Vector3(1, 2, 0), segment.position, "命中后不得追踪调用方的位置列表。");
                var renderer = segment.GetComponent<SpriteRenderer>();
                var initialSprite = renderer.sprite;
                Assert.IsFalse(view.Advance(0f));
                Assert.AreSame(initialSprite, renderer.sprite, "零战斗时间必须保持动画帧。");
                Assert.IsFalse(view.Advance(0.1f));
                Assert.AreNotSame(initialSprite, renderer.sprite, "战斗时间必须实际推进序列帧。");
                Assert.IsTrue(view.Advance(view.Lifetime));
                Assert.IsFalse(root.GetComponentsInChildren<SpriteRenderer>(true).Any(r => r.enabled));
                view.Play(new[] { Vector3.one });
                Assert.AreEqual(1, root.GetComponentsInChildren<SpriteRenderer>().Count(r => r.enabled), "单目标只显示火花。");
                view.Clear();
                Assert.IsTrue(view.Advance(0f));
                view.Play(System.Array.Empty<Vector3>());
                Assert.IsFalse(root.GetComponentsInChildren<SpriteRenderer>(true).Any(r => r.enabled));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FlyingBoltUsesSequenceAndNoLegacyMaterialOrLine()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "InductionBolt.prefab");
            Assert.IsNotNull(prefab.GetComponent<RobotBulletView>());
            Assert.IsEmpty(prefab.GetComponentsInChildren<LineRenderer>(true));
            Assert.IsTrue(prefab.GetComponent("BattleSpriteAnimation") != null);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Material>(Folder + "LightningChain.mat") == null);
        }
    }
}
