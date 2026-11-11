using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 敌人预制体的实体绑定与序列帧表现。仅保存管理器与实例 ID，不持有池对象引用。
    /// 业务方在移除实体后传入快照；死亡动画结束时通知业务方回收表现对象。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class EnemyView : MonoBehaviour
    {
        [SerializeField] private int _configId;
        [SerializeField] private bool _faceMovement;
        [SerializeField] private bool _spriteFacesRight = true;
        private Animator _animator;
        private BattleEntityRegistry _registry;
        private SpriteRenderer[] _spriteRenderers;
        private Color[] _baseSpriteColors;
        private bool[] _baseSpriteFlipX;
        private Transform[] _shakeRoots;
        private Vector3[] _shakeLocalOffsets;
        private bool _moving;
        private bool _attacking;
        private bool _removing;
        private float _hitFlashRemaining;
        private float _hitFlashDuration;
        private float _hitShakeAmplitude;
        private const float HIT_SHAKE_MAX_DISTANCE = 0.04f;

        public int ConfigId => _configId;
        public int InstanceId { get; private set; }
        public string AnimationState { get; private set; } = "Idle";
        public bool IsHitFlashing => _hitFlashRemaining > 0f;
        public event Action<int> RemovalAnimationCompleted;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            CacheVisuals();
        }

        private void OnEnable()
        {
            ResetBinding();
            PlayState("Idle", true);
        }

        private void OnDisable() { ResetBinding(); }

        public void Bind(BattleEntityRegistry registry, int instanceId)
        {
            Entity entity;
            if (registry == null || !registry.TryGet(instanceId, out entity) ||
                !(entity is EnemyEntity enemy) || !enemy.IsAlive || enemy.ConfigId != _configId)
                throw new ArgumentException("敌人视图与实体不匹配。", nameof(instanceId));
            if (_registry == registry && InstanceId == instanceId) return;
            ResetBinding();
            _registry = registry;
            InstanceId = instanceId;
            PlayState("Idle", true);
        }

        public void Unbind()
        {
            ResetBinding();
            PlayState("Idle", true);
        }

        public bool SetMoving(bool moving)
        {
            if (!HasAliveEntity()) return false;
            _moving = moving;
            if (!_attacking) PlayState(_moving ? "Move" : "Idle");
            return true;
        }

        public bool FaceMovement(Vector2 direction)
        {
            if (!_faceMovement || !HasAliveEntity() ||
                float.IsNaN(direction.x) || float.IsInfinity(direction.x) ||
                float.IsNaN(direction.y) || float.IsInfinity(direction.y)) return false;
            // 单视角角色保持直立；纵向路段的小幅横向偏移不触发左右反复切换。
            if (Mathf.Abs(direction.x) <= Mathf.Max(0.0001f, Mathf.Abs(direction.y) * 0.2f)) return false;
            CacheVisuals();
            var mirror = (direction.x > 0f) != _spriteFacesRight;
            for (var i = 0; i < _spriteRenderers.Length; i++)
                if (_spriteRenderers[i] != null) _spriteRenderers[i].flipX = _baseSpriteFlipX[i] ^ mirror;
            return true;
        }

        public bool PlayAttack()
        {
            if (_attacking || !HasAliveEntity()) return false;
            _attacking = true;
            PlayState("Attack");
            return true;
        }

        // 由 Attack 动画的末帧事件调用。
        public void OnAttackAnimationComplete()
        {
            if (!_attacking || _removing) return;
            _attacking = false;
            if (HasAliveEntity()) PlayState(_moving ? "Move" : "Idle");
        }

        /// <summary>调用方须先通过同一个管理器移除实体，再传入返回的快照。</summary>
        public bool PlayRemoval(EntityRemovalSnapshot removal)
        {
            if (_removing || _registry == null || InstanceId <= 0 ||
                removal.InstanceId != InstanceId || removal.ConfigId != _configId ||
                removal.Kind != EntityKind.Enemy || removal.RemovalReason == EntityRemovalReason.None)
                return false;
            Entity existing;
            if (_registry.TryGet(InstanceId, out existing)) return false;
            ClearHitFeedback();
            _registry = null;
            _removing = true;
            _attacking = false;
            _moving = false;
            if (removal.RemovalReason == EntityRemovalReason.Killed) PlayState("Death");
            else CompleteRemoval();
            return true;
        }

        // 由 Death 动画的末帧事件调用；重复事件不会重复通知。
        public void OnDeathAnimationComplete()
        {
            if (_removing && AnimationState == "Death") CompleteRemoval();
        }

        private void Update()
        {
            // 在 Animator 写入本帧姿态前撤销上帧偏移，LateUpdate 再叠加本帧抖动。
            RemoveHitShake();
            AdvanceHitFeedback(Time.unscaledDeltaTime);
            if (_registry == null || InstanceId <= 0) return;
            if (!HasAliveEntity())
            {
                // 未转交移除快照时停止查询并隐藏，防止已回收实体留下幽灵表现。
                ResetBinding();
                gameObject.SetActive(false);
            }
        }

        private void LateUpdate() { ApplyHitShake(); RefreshSortingOrder(); }

        public bool PlayHitFeedback(float duration = 0.25f)
        {
            if (!HasAliveEntity() || duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration)) return false;
            CacheVisuals();
            RemoveHitShake();
            _hitShakeAmplitude = Mathf.Min(HIT_SHAKE_MAX_DISTANCE, GetVisualBounds().size.y * 0.056f);
            _hitFlashDuration = duration;
            _hitFlashRemaining = duration;
            ApplyHitFlash(true);
            return true;
        }

        private void AdvanceHitFeedback(float deltaTime)
        {
            if (_hitFlashRemaining <= 0f || (_animator != null && _animator.speed <= 0f)) return;
            _hitFlashRemaining = Mathf.Max(0f, _hitFlashRemaining - Mathf.Max(0f, deltaTime));
            ApplyHitFlash(_hitFlashRemaining > 0f);
            if (_hitFlashRemaining <= 0f) RemoveHitShake();
        }

        private void ApplyHitShake()
        {
            RemoveHitShake();
            if (_hitFlashRemaining <= 0f || _shakeRoots == null) return;
            var progress = 1f - _hitFlashRemaining / _hitFlashDuration;
            var strength = _hitShakeAmplitude * (1f - progress);
            var offset = new Vector3(Mathf.Cos(progress * Mathf.PI * 4.5f),
                Mathf.Sin(progress * Mathf.PI * 3f) * 0.2f, 0f) * strength;
            for (var i = 0; i < _shakeRoots.Length; i++)
            {
                var visual = _shakeRoots[i];
                if (visual == null) continue;
                _shakeLocalOffsets[i] = visual.parent.InverseTransformVector(offset);
                visual.localPosition += _shakeLocalOffsets[i];
            }
        }

        private void RemoveHitShake()
        {
            if (_shakeRoots == null) return;
            for (var i = 0; i < _shakeRoots.Length; i++)
            {
                if (_shakeRoots[i] != null) _shakeRoots[i].localPosition -= _shakeLocalOffsets[i];
                _shakeLocalOffsets[i] = Vector3.zero;
            }
        }

        private void ClearHitFeedback()
        {
            RemoveHitShake();
            _hitFlashRemaining = 0f;
            _hitFlashDuration = 0f;
            _hitShakeAmplitude = 0f;
            ApplyHitFlash(false);
        }

        private void CacheVisuals()
        {
            if (_spriteRenderers != null) return;
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            _baseSpriteColors = new Color[_spriteRenderers.Length];
            _baseSpriteFlipX = new bool[_spriteRenderers.Length];
            var roots = new List<Transform>();
            for (var i = 0; i < _spriteRenderers.Length; i++)
            {
                _baseSpriteColors[i] = _spriteRenderers[i].color;
                _baseSpriteFlipX[i] = _spriteRenderers[i].flipX;
                var visual = _spriteRenderers[i].transform;
                if (visual == transform) continue;
                while (visual.parent != transform) visual = visual.parent;
                if (!roots.Contains(visual)) roots.Add(visual);
            }
            _shakeRoots = roots.ToArray();
            _shakeLocalOffsets = new Vector3[_shakeRoots.Length];
        }

        internal Bounds GetVisualBounds()
        {
            CacheVisuals();
            var bounds = new Bounds(transform.position + Vector3.up * 0.25f, Vector3.one * 0.5f);
            var found = false;
            foreach (var renderer in _spriteRenderers)
            {
                if (renderer == null || !renderer.enabled || renderer.sprite == null || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private void ApplyHitFlash(bool active)
        {
            CacheVisuals();
            for (var i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] == null) continue;
                _spriteRenderers[i].color = active ? Color.Lerp(_baseSpriteColors[i], Color.red, 0.85f) : _baseSpriteColors[i];
            }
        }

        private void RefreshSortingOrder()
        {
            CacheVisuals();
            // 越靠下越靠前；同一高度再用实例 ID 保证稳定顺序，避免排序相同导致的闪烁。
            GetVisualSorting(out _, out var order);
            foreach (var renderer in _spriteRenderers)
                if (renderer != null)
                {
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                    renderer.sortingOrder = order;
                }
        }

        internal void GetVisualSorting(out int layer, out int order)
        {
            CacheVisuals();
            layer = _spriteRenderers.Length > 0 && _spriteRenderers[0] != null ? _spriteRenderers[0].sortingLayerID : 0;
            order = Mathf.RoundToInt(-transform.position.y * 100f) * 10 + Mathf.Abs(InstanceId % 10);
        }

        private bool HasAliveEntity()
        {
            Entity entity;
            return !_removing && _registry != null && _registry.TryGet(InstanceId, out entity) &&
                entity is EnemyEntity enemy && enemy.IsAlive && enemy.ConfigId == _configId;
        }

        private void CompleteRemoval()
        {
            var removedId = InstanceId;
            // 先清理再通知，允许监听方立即复用并重新绑定这个视图。
            ResetBinding();
            RemovalAnimationCompleted?.Invoke(removedId);
        }

        private void ResetBinding()
        {
            _registry = null;
            InstanceId = 0;
            _moving = false;
            _attacking = false;
            _removing = false;
            ClearHitFeedback();
            for (var i = 0; i < _spriteRenderers.Length; i++)
                if (_spriteRenderers[i] != null) _spriteRenderers[i].flipX = _baseSpriteFlipX[i];
        }

        private void PlayState(string state, bool restart = false)
        {
            var changed = AnimationState != state;
            AnimationState = state;
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null || !isActiveAndEnabled) return;
            // 同一循环状态不反复重置时间；重新绑定、启用和单次动画从第 0 帧开始。
            if (changed || restart) _animator.Play(state, 0, 0f);
        }
    }
}
