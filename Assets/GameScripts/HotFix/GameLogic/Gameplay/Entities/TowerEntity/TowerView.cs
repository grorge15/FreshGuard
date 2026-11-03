using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>共用塔预制体的血量与阵营表现，不处理伤害或对局胜负。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class TowerView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _healthFill;
        [SerializeField, Min(0.01f)] private float _hitShakeAmplitude = 0.07f;
        [SerializeField, Min(0.05f)] private float _hitShakeDuration = 0.22f;
        private TowerEntity _tower;
        private Vector3 _fullFillScale;
        private Vector3 _bodyBasePosition;
        private float _hitShakeElapsed;
        private bool _hitShakePlaying;
        private bool _cached;
        public int InstanceId => _tower != null ? _tower.InstanceId : 0;
        public float HealthFraction { get; private set; } = 1f;
        public bool IsDead { get; private set; }

        private void Awake() { EnsureCollisionBody(); }

        private void OnValidate() { EnsureCollisionBody(); }

        private void EnsureCollisionBody()
        {
            var collider = GetComponent<BoxCollider2D>();
            if (collider == null) collider = gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.offset = Vector2.zero;
            collider.size = Vector2.one;
        }

        public void Bind(TowerEntity tower)
        {
            if (tower == null || tower.IsRemoved) throw new ArgumentException("塔实体不可绑定。", nameof(tower));
            if (_tower == tower) return;
            if (_body == null || _healthFill == null) throw new InvalidOperationException("塔视图缺少身体或血条引用。");
            Unbind();
            if (!_cached)
            {
                _fullFillScale = _healthFill.transform.localScale;
                _bodyBasePosition = _body.transform.localPosition;
                _cached = true;
            }
            _tower = tower;
            _tower.HealthChanged += OnHealthChanged;
            Render(tower.CurrentHp, tower.MaxHp, tower.Side);
        }

        public void Unbind()
        {
            if (_tower != null)
            {
                _tower.HealthChanged -= OnHealthChanged;
            }
            _tower = null;
        }

        private void OnHealthChanged(TowerHealthSnapshot snapshot)
        {
            if (_tower == null || snapshot.InstanceId != _tower.InstanceId) return;
            Render(snapshot.CurrentHp, snapshot.MaxHp, snapshot.Side);
            if (snapshot.CurrentHp < snapshot.PreviousHp) PlayHitShake();
        }

        private void PlayHitShake()
        {
            if (_body == null) return;
            _body.transform.localPosition = _bodyBasePosition;
            _hitShakeElapsed = 0f;
            _hitShakePlaying = true;
        }

        private void LateUpdate() => AdvanceHitFeedback(Time.unscaledDeltaTime);

        internal void AdvanceHitFeedback(float deltaTime)
        {
            if (!_hitShakePlaying || _body == null) return;
            _hitShakeElapsed += Mathf.Max(0f, deltaTime);
            var duration = Mathf.Max(0.05f, _hitShakeDuration);
            var progress = Mathf.Clamp01(_hitShakeElapsed / duration);
            var offset = Mathf.Sin(progress * Mathf.PI * 6f) * (1f - progress) * _hitShakeAmplitude;
            _body.transform.localPosition = _bodyBasePosition + Vector3.right * offset;
            if (progress >= 1f) ResetHitShake();
        }

        private void ResetHitShake()
        {
            _hitShakePlaying = false;
            _hitShakeElapsed = 0f;
            if (_body != null && _cached) _body.transform.localPosition = _bodyBasePosition;
        }

        private void Render(int hp, int maxHp, BattleSide side)
        {
            HealthFraction = Mathf.Clamp01((float)hp / maxHp);
            IsDead = hp == 0;
            _healthFill.transform.localScale = new Vector3(_fullFillScale.x * HealthFraction, _fullFillScale.y, _fullFillScale.z);
            var color = side == BattleSide.Player ? new Color(0.3f, 0.75f, 1f) : new Color(1f, 0.4f, 0.3f);
            _body.color = IsDead ? new Color(0.35f, 0.35f, 0.35f) : color;
            _healthFill.color = color;
        }

        private void OnDisable() { ResetHitShake(); Unbind(); }
        private void OnDestroy() { ResetHitShake(); Unbind(); }
    }
}
