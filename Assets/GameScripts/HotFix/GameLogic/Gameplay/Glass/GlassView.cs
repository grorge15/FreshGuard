using System;
using System.Collections.Generic;
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
        private float _cellSize;
        private Vector3 _visualPosition;
        private Quaternion _visualRotation;
        private Vector2 _impactDirection;
        private Material _shardMaterial;
        private readonly List<GlassShard> _shards = new List<GlassShard>();
        private bool _broken;
        private const float ShakeDuration = 0.08f;
        private const float BurstTime = 0.12f;
        private const float BreakDuration = 0.4f;

        private sealed class GlassShard
        {
            public GameObject GameObject;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Vector3 StartPosition;
            public Vector3 Travel;
            public float Rotation;
        }
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
            _cellSize = cellSize;
            _broken = false;
            _breakElapsed = _flashRemaining = 0f;
            CleanupShards();
            var collider = GetComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.offset = Vector2.zero;
            collider.size = Vector2.one * cellSize;
            collider.enabled = true;
            var bounds = _stageSprites[_stageSprites.Length - 1].bounds.size;
            _visualScale = new Vector3(cellSize * 0.94f / bounds.x, cellSize * 0.94f / bounds.y, 1f);
            _visualPosition = _renderer.transform.localPosition;
            _visualRotation = _renderer.transform.localRotation;
            _renderer.enabled = true;
            _renderer.transform.localScale = _visualScale;
            _renderer.transform.localPosition = _visualPosition;
            _renderer.transform.localRotation = _visualRotation;
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

        public void BeginBreak() => BeginBreak(Vector2.zero);

        public void BeginBreak(Vector2 impactDirection)
        {
            if (_broken) return;
            _broken = true;
            _breakElapsed = 0f;
            _flashRemaining = 0f;
            _impactDirection = impactDirection.sqrMagnitude > 0.0001f ? impactDirection.normalized : Vector2.zero;
            GetComponent<BoxCollider2D>().enabled = false;
        }

        /// <returns>表现结束，可销毁视图。</returns>
        public bool Advance(float deltaTime)
        {
            if (_broken)
            {
                _breakElapsed += Mathf.Max(0f, deltaTime);
                if (_breakElapsed < BurstTime)
                {
                    var shake = Mathf.Clamp01(_breakElapsed / ShakeDuration);
                    var offset = Mathf.Sin(shake * Mathf.PI * 5f) * (1f - shake) * _cellSize * 0.055f;
                    _renderer.transform.localPosition = _visualPosition + Vector3.right * offset;
                    var scale = _breakElapsed < ShakeDuration
                        ? Mathf.Lerp(1f, 0.94f, Mathf.SmoothStep(0f, 1f, shake))
                        : Mathf.Lerp(0.94f, 1.06f, Mathf.SmoothStep(0f, 1f,
                            (_breakElapsed - ShakeDuration) / (BurstTime - ShakeDuration)));
                    _renderer.transform.localScale = _visualScale * scale;
                    _renderer.color = _breakElapsed >= ShakeDuration ? new Color(1.7f, 1.7f, 1.7f, 1f) : Color.white;
                    return false;
                }
                if (_shards.Count == 0) CreateShards();
                AdvanceShards(Mathf.Clamp01((_breakElapsed - BurstTime) / (BreakDuration - BurstTime)));
                if (_breakElapsed < BreakDuration) return false;
                CleanupShards();
                return true;
            }
            _flashRemaining = Mathf.Max(0f, _flashRemaining - deltaTime);
            _renderer.color = _flashRemaining > 0f ? new Color(1.6f, 1.6f, 1.6f, 1f) : Color.white;
            return false;
        }

        private void CreateShards()
        {
            var sprite = _renderer.sprite;
            var shader = Shader.Find("Sprites/Default");
            if (sprite == null || shader == null) { _renderer.enabled = false; return; }
            _renderer.enabled = false;
            _renderer.transform.localPosition = _visualPosition;
            _renderer.transform.localScale = _visualScale;
            _renderer.transform.localRotation = _visualRotation;
            _shardMaterial = new Material(shader) { mainTexture = sprite.texture };
            var bounds = sprite.bounds;
            var center = bounds.center;
            var min = bounds.min;
            var max = bounds.max;
            var perimeter = new[]
            {
                new Vector2(min.x, min.y), new Vector2(center.x, min.y), new Vector2(max.x, min.y),
                new Vector2(max.x, max.y), new Vector2(center.x, max.y), new Vector2(min.x, max.y)
            };
            var textureRect = sprite.textureRect;
            for (var i = 0; i < perimeter.Length; i++)
            {
                var first = perimeter[i];
                var second = perimeter[(i + 1) % perimeter.Length];
                var mesh = new Mesh { name = "GlassShardMesh" };
                mesh.vertices = new[] { center, (Vector3)first, (Vector3)second };
                mesh.uv = new[] { SpriteUv(sprite, center, textureRect), SpriteUv(sprite, first, textureRect),
                    SpriteUv(sprite, second, textureRect) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                var shardObject = new GameObject("GlassShard_" + i, typeof(MeshFilter), typeof(MeshRenderer));
                shardObject.layer = gameObject.layer;
                shardObject.transform.SetParent(transform, false);
                shardObject.transform.localPosition = _visualPosition;
                shardObject.transform.localRotation = _visualRotation;
                shardObject.transform.localScale = _visualScale;
                shardObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = shardObject.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _shardMaterial;
                meshRenderer.sortingLayerID = _renderer.sortingLayerID;
                meshRenderer.sortingOrder = _renderer.sortingOrder + 1;
                var radial = ((first + second) * 0.5f - (Vector2)center).normalized;
                var direction = (radial + _impactDirection * 0.35f).normalized;
                _shards.Add(new GlassShard
                {
                    GameObject = shardObject, Mesh = mesh, Renderer = meshRenderer,
                    StartPosition = _visualPosition,
                    Travel = (Vector3)(direction * (_cellSize * Mathf.Lerp(0.45f, 0.85f, (i % 3) / 2f))),
                    Rotation = (i % 2 == 0 ? 1f : -1f) * Mathf.Lerp(55f, 150f, (i + 1f) / perimeter.Length)
                });
            }
        }

        private static Vector2 SpriteUv(Sprite sprite, Vector2 point, Rect textureRect)
        {
            var bounds = sprite.bounds;
            var normalized = new Vector2((point.x - bounds.min.x) / bounds.size.x,
                (point.y - bounds.min.y) / bounds.size.y);
            return new Vector2((textureRect.x + normalized.x * textureRect.width) / sprite.texture.width,
                (textureRect.y + normalized.y * textureRect.height) / sprite.texture.height);
        }

        private void AdvanceShards(float progress)
        {
            var move = 1f - Mathf.Pow(1f - progress, 3f);
            var alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.4f) / 0.6f));
            var scale = Mathf.Lerp(1f, 0.75f, progress);
            var block = new MaterialPropertyBlock();
            foreach (var shard in _shards)
            {
                if (shard.GameObject == null) continue;
                shard.GameObject.transform.localPosition = shard.StartPosition + shard.Travel * move;
                shard.GameObject.transform.localRotation = _visualRotation * Quaternion.Euler(0f, 0f, shard.Rotation * move);
                shard.GameObject.transform.localScale = _visualScale * scale;
                block.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                shard.Renderer.SetPropertyBlock(block);
            }
        }

        private void CleanupShards()
        {
            foreach (var shard in _shards)
            {
                if (shard.GameObject != null)
                {
                    if (Application.isPlaying) Destroy(shard.GameObject); else DestroyImmediate(shard.GameObject);
                }
                if (shard.Mesh != null)
                {
                    if (Application.isPlaying) Destroy(shard.Mesh); else DestroyImmediate(shard.Mesh);
                }
            }
            _shards.Clear();
            if (_shardMaterial != null)
            {
                if (Application.isPlaying) Destroy(_shardMaterial); else DestroyImmediate(_shardMaterial);
                _shardMaterial = null;
            }
        }

        private void OnDestroy() => CleanupShards();
    }
}
