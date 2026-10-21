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
        private TowerEntity _tower;
        private Vector3 _fullFillScale;
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
            if (!_cached) { _fullFillScale = _healthFill.transform.localScale; _cached = true; }
            _tower = tower;
            _tower.HealthChanged += OnHealthChanged;
            _tower.Died += OnHealthChanged;
            Render(tower.CurrentHp, tower.MaxHp, tower.Side);
        }

        public void Unbind()
        {
            if (_tower != null)
            {
                _tower.HealthChanged -= OnHealthChanged;
                _tower.Died -= OnHealthChanged;
            }
            _tower = null;
        }

        private void OnHealthChanged(TowerHealthSnapshot snapshot)
        {
            if (_tower == null || snapshot.InstanceId != _tower.InstanceId) return;
            Render(snapshot.CurrentHp, snapshot.MaxHp, snapshot.Side);
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

        private void OnDisable() { Unbind(); }
        private void OnDestroy() { Unbind(); }
    }
}
