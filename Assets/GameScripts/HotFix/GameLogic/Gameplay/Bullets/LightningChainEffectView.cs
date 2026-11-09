using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>命中快照上的火花与逐跳闪电序列帧；不选敌、不结算伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class LightningChainEffectView : MonoBehaviour
    {
        [SerializeField] private BattleSpriteAnimation _chainTemplate;
        [SerializeField] private BattleSpriteAnimation _sparkTemplate;
        [SerializeField, Min(0.01f)] private float _chainReferenceLength = 4f;
        [SerializeField, Min(0.01f)] private float _chainThickness = 0.22f;
        [Min(0.01f)] public float Lifetime = 10f / 24f;
        private readonly List<BattleSpriteAnimation> _chains = new List<BattleSpriteAnimation>();
        private readonly List<BattleSpriteAnimation> _sparks = new List<BattleSpriteAnimation>();
        private float _elapsed;
        private float _duration;
        private bool _playing;

        public void Play(IReadOnlyList<Vector3> positions)
        {
            Clear();
            if (positions == null || positions.Count == 0) return;
            if (_chainTemplate == null || _sparkTemplate == null ||
                !IsPositive(Lifetime) || !IsPositive(_chainReferenceLength) || !IsPositive(_chainThickness))
                throw new InvalidOperationException("闪电表现需要两种序列帧模板及有效尺寸、寿命。");
            _duration = Mathf.Max(Lifetime, Mathf.Max(_chainTemplate.Duration, _sparkTemplate.Duration));
            for (var i = 0; i < positions.Count; i++)
            {
                var spark = GetOrCreate(_sparks, _sparkTemplate, i);
                spark.transform.position = positions[i];
                spark.transform.rotation = Quaternion.identity;
                spark.Sample(0f);
                if (i == 0) continue;
                var direction = positions[i] - positions[i - 1];
                if (direction.sqrMagnitude < 0.000001f) continue;
                var chain = GetOrCreate(_chains, _chainTemplate, i - 1);
                chain.transform.position = positions[i - 1];
                chain.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                chain.transform.localScale = new Vector3(direction.magnitude / _chainReferenceLength, _chainThickness, 1f);
                chain.Sample(0f);
            }
            _playing = true;
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null && group.Registry != null) group.Bind(group.Registry, group.Side);
        }

        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!_playing) return true;
            _elapsed += deltaTime;
            if (_elapsed >= _duration) { Clear(); return true; }
            foreach (var chain in _chains) if (chain.gameObject.activeSelf) chain.Sample(_elapsed);
            foreach (var spark in _sparks) if (spark.gameObject.activeSelf) spark.Sample(_elapsed);
            return false;
        }

        public void Clear()
        {
            _playing = false;
            _elapsed = _duration = 0f;
            if (_chainTemplate != null) { _chainTemplate.Clear(); _chainTemplate.gameObject.SetActive(false); }
            if (_sparkTemplate != null) { _sparkTemplate.Clear(); _sparkTemplate.gameObject.SetActive(false); }
            foreach (var chain in _chains) { chain.Clear(); chain.gameObject.SetActive(false); }
            foreach (var spark in _sparks) { spark.Clear(); spark.gameObject.SetActive(false); }
        }

        private static BattleSpriteAnimation GetOrCreate(List<BattleSpriteAnimation> items,
            BattleSpriteAnimation template, int index)
        {
            while (items.Count <= index)
            {
                var item = Instantiate(template, template.transform.parent, false);
                item.name = template.name + "_" + items.Count;
                items.Add(item);
            }
            items[index].gameObject.SetActive(true);
            return items[index];
        }

        private static bool IsPositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        private void OnDisable() { Clear(); }
    }
}
