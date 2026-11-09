using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class GameSoundAssetTests
    {
        [Test]
        public void CatalogCoversEveryCueWithUniquePlayableAssets()
        {
            const string root = "Assets/AssetRaw/Audios/FreshGuardSfx/";
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(root + "FreshGuardSoundCatalog.json");
            Assert.That(json, Is.Not.Null);
            var catalog = JsonUtility.FromJson<GameSoundCatalog>(json.text);
            Assert.That(catalog.entries.Length, Is.EqualTo(Enum.GetValues(typeof(GameSound)).Length));
            var ids = new HashSet<GameSound>();
            var locations = new HashSet<string>();
            foreach (var entry in catalog.entries)
            {
                var id = (GameSound)Enum.Parse(typeof(GameSound), entry.id);
                Assert.That(ids.Add(id), Is.True, entry.id);
                Assert.That(locations.Add(entry.location), Is.True, entry.location);
                Assert.That(entry.volume, Is.InRange(0f, 1f), entry.id);
                Assert.That(entry.cooldown, Is.GreaterThanOrEqualTo(0f), entry.id);
                Assert.That(entry.loop, Is.EqualTo(id == GameSound.ReelSpin), entry.id);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(root + entry.location + ".wav");
                Assert.That(clip, Is.Not.Null, entry.location);
                Assert.That(clip.length, Is.InRange(0.025f, 3f), entry.location);
                Assert.That(clip.channels, Is.EqualTo(1), entry.location);
                var samples = new float[clip.samples];
                Assert.That(clip.GetData(samples, 0), Is.True, entry.location);
                var peak = 0f;
                foreach (var sample in samples)
                {
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False, entry.location);
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Assert.That(peak, Is.InRange(0.05f, 0.9f), entry.location);
                Assert.That(Mathf.Abs(samples[0]) + Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(.01f), entry.location);
            }
        }
    }
}
