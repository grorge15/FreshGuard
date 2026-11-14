using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace GameLogic.Tests
{
    public sealed class AppleDeathEffectTests
    {
        private const string PREFAB_PATH = "Assets/AssetRaw/Actor/Prefabs/Prefabs/AppleEnemy.prefab";
        private const string CLIP_PATH = "Assets/AssetRaw/Actor/Animations/Enemy/AppleEnemy/AppleEnemy_Death.anim";
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void BurstFramesArePackedAndDeathCompletesAfterTheLastVisibleFrame()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CLIP_PATH);
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            var sprites = keys.Select(key => (Sprite)key.value).Distinct().ToArray();
            Assert.AreEqual(65, sprites.Length);
            Assert.IsFalse(clip.isLooping);
            Assert.IsEmpty(AnimationUtility.GetCurveBindings(clip), "死亡不能移动实体或改变预制体缩放。");
            var dependencies = AssetDatabase.GetDependencies(PREFAB_PATH, true);
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>("Assets/AssetArt/Atlas/Frames_AppleEnemy.spriteatlasv2");
            var packables = atlas.GetPackables();
            foreach (var sprite in sprites)
            {
                var path = AssetDatabase.GetAssetPath(sprite);
                StringAssert.Contains("/Apple/DeathBurst/", path, "不能再引用旧倒地帧。");
                CollectionAssert.Contains(dependencies, path);
                CollectionAssert.Contains(packables, sprite);
                Assert.AreEqual(sprites[0].pivot, sprite.pivot);
                Assert.AreEqual(sprites[0].pixelsPerUnit, sprite.pixelsPerUnit);
            }
            var completion = AnimationUtility.GetAnimationEvents(clip).Single();
            Assert.AreEqual("OnDeathAnimationComplete", completion.functionName);
            Assert.That(completion.time, Is.EqualTo(1.3f).Within(.001f));
            Assert.Greater(completion.time, keys[keys.Length - 2].time);
            Assert.Less(completion.time, clip.length);
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprites.Last()))));
                Assert.IsTrue(texture.GetPixels32().All(pixel => pixel.a == 0), "回收前应完全消失，不能留下尸体或碎片。");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SamplingEveryFrameKeepsTheFootAnchorRootTransformAndFacing()
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
            _root.transform.position = new Vector3(2f, -3f, 0f);
            var visual = _root.transform.Find("Visual");
            var renderer = visual.GetComponent<SpriteRenderer>();
            var position = _root.transform.position;
            var scale = _root.transform.localScale;
            var rotation = _root.transform.rotation;
            var foot = visual.localPosition;
            var facing = renderer.flipX;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CLIP_PATH);
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
            {
                clip.SampleAnimation(_root, key.time);
                Assert.AreSame(key.value, renderer.sprite);
                Assert.AreEqual(position, _root.transform.position);
                Assert.AreEqual(scale, _root.transform.localScale);
                Assert.AreEqual(rotation, _root.transform.rotation);
                Assert.AreEqual(foot, visual.localPosition);
                Assert.AreEqual(facing, renderer.flipX);
            }
        }

    }
}
