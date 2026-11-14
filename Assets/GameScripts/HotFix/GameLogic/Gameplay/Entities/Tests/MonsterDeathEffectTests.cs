using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace GameLogic.Tests
{
    public sealed class MonsterDeathEffectTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCase("Spinach", "SpinachEnemy", "SpinachEnemy", 55, 1.1f)]
        [TestCase("FrozenCan", "ExpiredCanEnemy", "FrozenCanEnemy", 65, 1.3f)]
        [TestCase("GhostOnion", "GhostOnionEnemy", "GhostOnionEnemy", 72, 1.44f)]
        public void DeathUsesPackedTransparentFramesAndOneCompletionEvent(
            string key, string prefabName, string animationName, int count, float completionTime)
        {
            var prefabPath = "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsTrue(new SerializedObject(prefab.GetComponent<EnemyView>())
                .FindProperty("_deathAnimationIncludesHitEffect").boolValue);
            var clip = DeathClip(animationName);
            Assert.IsFalse(clip.isLooping);
            Assert.IsEmpty(AnimationUtility.GetCurveBindings(clip));
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            Assert.AreEqual("Visual", binding.path);
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            var sprites = keys.Select(frame => (Sprite)frame.value).Distinct().ToArray();
            Assert.AreEqual(count, sprites.Length);
            var dependencies = AssetDatabase.GetDependencies(prefabPath, true);
            var packables = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                "Assets/AssetArt/Atlas/Frames_" + animationName + ".spriteatlasv2").GetPackables();
            foreach (var sprite in sprites)
            {
                var path = AssetDatabase.GetAssetPath(sprite);
                StringAssert.Contains("/" + key + "/DeathImpact/", path);
                CollectionAssert.Contains(dependencies, path);
                CollectionAssert.Contains(packables, sprite);
                Assert.AreEqual(sprites[0].pivot, sprite.pivot);
                Assert.AreEqual(sprites[0].pixelsPerUnit, sprite.pixelsPerUnit);
            }
            var completion = AnimationUtility.GetAnimationEvents(clip).Single();
            Assert.AreEqual("OnDeathAnimationComplete", completion.functionName);
            Assert.That(completion.time, Is.EqualTo(completionTime).Within(.001f));
            Assert.Greater(completion.time, keys[keys.Length - 2].time);
            Assert.Less(completion.time, clip.length);
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprites.Last()))));
                Assert.IsTrue(texture.GetPixels32().All(pixel => pixel.a == 0), "回收前不能残留叶片、罐身或灵体。");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase("SpinachEnemy", "SpinachEnemy")]
        [TestCase("ExpiredCanEnemy", "FrozenCanEnemy")]
        [TestCase("GhostOnionEnemy", "GhostOnionEnemy")]
        public void AllDeathFramesKeepTheAuthoredFootAnchorScaleAndFacing(string prefabName, string animationName)
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + prefabName + ".prefab"));
            _root.transform.position = new Vector3(2f, -3f, 0f);
            var visual = _root.transform.Find("Visual");
            var renderer = visual.GetComponent<SpriteRenderer>();
            var position = _root.transform.position;
            var scale = _root.transform.localScale;
            var rotation = _root.transform.rotation;
            var anchor = visual.localPosition;
            var facing = renderer.flipX;
            var clip = DeathClip(animationName);
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            foreach (var frame in AnimationUtility.GetObjectReferenceCurve(clip, binding))
            {
                clip.SampleAnimation(_root, frame.time);
                Assert.AreSame(frame.value, renderer.sprite);
                Assert.AreEqual(position, _root.transform.position);
                Assert.AreEqual(scale, _root.transform.localScale);
                Assert.AreEqual(rotation, _root.transform.rotation);
                Assert.AreEqual(anchor, visual.localPosition);
                Assert.AreEqual(facing, renderer.flipX);
            }
        }

        private static AnimationClip DeathClip(string animationName)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Enemy/" +
                animationName + "/" + animationName + "_Death.anim");
        }
    }
}
