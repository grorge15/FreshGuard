using System;
using GameConfig.glass;
using UnityEngine;

namespace GameLogic
{
    /// <summary>图片由Editor按配置绑定为Prefab依赖，受击过程不加载资源。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class GlassView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Sprite[] _stageSprites;
        private GlassBoardController _owner;
        private float[] _upperRatios;
        private int _maxDurability;
        private Vector3 _visualScale;
        private float _flashRemaining;
        private float _breakElapsed;
        private bool _broken;
        public int InstanceId { get; private set; }
        public BoardCoordinate Coordinate { get; private set; }
        public bool IsBroken => _broken;
        public int StageIndex { get; private set; }
        public Sprite CurrentSprite => _renderer != null ? _renderer.sprite : null;
        public GlassBoardController Owner => _owner;

        public void Bind(GlassBoardController owner, GlassEntity entity, Glass config, float cellSize)
        {
            if (owner == null || entity == null || config == null || _renderer == null ||
                _stageSprites == null || _stageSprites.Length != config.StageSprites.Length ||
                config.StageUpperRatios.Length != _stageSprites.Length)
                throw new InvalidOperationException("玻璃视图、配置或阶段图片缺失。");
            _upperRatios = (float[])config.StageUpperRatios.Clone();
            for (var i = 0; i < _stageSprites.Length; i++)
            {
                if (_stageSprites[i] == null || _stageSprites[i].name != config.StageSprites[i] ||
                    float.IsNaN(_upperRatios[i]) || float.IsInfinity(_upperRatios[i]) ||
                    _upperRatios[i] <= 0f || _upperRatios[i] > 1f || (i > 0 && _upperRatios[i] <= _upperRatios[i - 1]))
                    throw new InvalidOperationException("玻璃阶段配置与Prefab引用不匹配：" + config.Id);
            }
            if (_upperRatios.Length == 0 || _upperRatios[_upperRatios.Length - 1] != 1f)
                throw new InvalidOperationException("玻璃最后一阶段上限必须为1。");
            _owner = owner;
            InstanceId = entity.InstanceId;
            Coordinate = entity.Coordinate;
            _maxDurability = entity.MaxDurability;
            _broken = false;
            _breakElapsed = _flashRemaining = 0f;
            var collider = GetComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.offset = Vector2.zero;
            collider.size = Vector2.one * cellSize;
            collider.enabled = true;
            var bounds = _stageSprites[_stageSprites.Length - 1].bounds.size;
            _visualScale = new Vector3(cellSize * 0.94f / bounds.x, cellSize * 0.94f / bounds.y, 1f);
            _renderer.transform.localScale = _visualScale;
            _renderer.color = Color.white;
            ShowDurability(entity.CurrentDurability, false);
        }

        public GlassCollisionResult Collide(PhysicalCollisionBall ball) =>
            _owner == null || _broken ? GlassCollisionResult.Ignored : _owner.ApplyCollision(this, ball);

        public void ShowDurability(int currentDurability, bool flash = true)
        {
            if (_broken) return;
            var ratio = (float)currentDurability / _maxDurability;
            for (var i = 0; i < _upperRatios.Length; i++)
                if (ratio <= _upperRatios[i]) { StageIndex = i; _renderer.sprite = _stageSprites[i]; break; }
            if (flash) _flashRemaining = 0.06f;
        }

        public void BeginBreak()
        {
            if (_broken) return;
            _broken = true;
            _breakElapsed = 0f;
            _flashRemaining = 0f;
            GetComponent<BoxCollider2D>().enabled = false;
        }

        /// <returns>表现结束，可销毁视图。</returns>
        public bool Advance(float deltaTime)
        {
            if (_broken)
            {
                _breakElapsed += deltaTime;
                var progress = Mathf.Clamp01(_breakElapsed / 0.15f);
                _renderer.transform.localScale = _visualScale * Mathf.Lerp(1f, 1.1f, progress);
                _renderer.color = new Color(1f, 1f, 1f, 1f - progress);
                return progress >= 1f;
            }
            _flashRemaining = Mathf.Max(0f, _flashRemaining - deltaTime);
            _renderer.color = _flashRemaining > 0f ? new Color(1.6f, 1.6f, 1.6f, 1f) : Color.white;
            return false;
        }
    }
}
