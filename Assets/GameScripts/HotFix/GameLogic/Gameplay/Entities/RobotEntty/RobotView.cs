using System;
using System.Collections.Generic;
using GameConfig;
using UnityEngine;

namespace GameLogic
{
    /// <summary>预制体的配置关联；实体仍由单局管理器统一管理。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotView : MonoBehaviour
    {
        [SerializeField] private int _configId;
        // 预留真实物理间隙，包含 PolygonCollider2D 的边缘厚度。
        private const float CollisionInsetRatio = 0.06f;
        [SerializeField] private SpriteRenderer _iconRenderer;
        [SerializeField] private SpriteRenderer _portraitRenderer;
        [SerializeField] private SpriteRenderer _levelFrameRenderer;
        [SerializeField] private Transform _starsRoot;
        [SerializeField] private SpriteRenderer[] _levelStars;
        [Header("碰撞反馈")]
        [SerializeField, Range(1f, 1.5f)] private float _collisionScale = 1.08f;
        [SerializeField, Min(0.05f)] private float _collisionDuration = 0.22f;

        public int ConfigId => _configId;
        public int InstanceId { get; private set; }
        private SpriteRenderer[] _cells;
        private SpriteRenderer[] _outline;
        private PolygonCollider2D _bodyCollider;
        private bool _collisionInsetApplied;
        private RobotEntity _robot;
        private BattleEntityRegistry _registry;
        private Vector3? _authoredScale;
        private BattleShopSceneController _host;
        private CollisionVisual[] _collisionVisuals;
        private float _collisionElapsed;
        private float _collisionStartScale = 1f;
        private float _collisionCurrentScale = 1f;
        private bool _collisionPlaying;
        private RobotImpactOutline _impactOutline;
        private const float MergeConvergeDuration = 0.18f;
        private const float MergePopDuration = 0.48f;
        private const float MergeTotalDuration = 0.62f;
        private bool _mergePlaying;
        private float _mergeElapsed;
        private int _mergePreviousLevel;
        private CollisionVisual[] _mergeVisuals;
        private Vector3 _mergeStartOffset;
        private SpriteRenderer _mergeStar;
        private Vector3 _mergeStarPosition;
        private Vector3 _mergeStarScale;
        private GameObject _mergeSourceGhost;
        private SpriteRenderer[] _mergeSourceRenderers;
        private Vector3 _mergeSourceStart;
        private GameObject _mergeRing;
        private LineRenderer _mergeRingRenderer;
        private Material _mergeRingMaterial;
        private Vector3[] _mergeRingPoints;
        private Vector3[] _mergeRingOutward;

        private struct CollisionVisual
        {
            public Transform Transform;
            public Vector3 Position;
            public Vector3 Scale;
            public Vector3 Pivot;
        }

        public void ApplyBoardScale(PlacementBoardView board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!_authoredScale.HasValue) _authoredScale = transform.localScale;
            var factor = board.CellSize / MapGridMath.REFERENCE_CELL_SIZE;
            var original = _authoredScale.Value;
            transform.localScale = new Vector3(original.x * factor, original.y * factor, original.z);
        }
        private bool _mergeCandidate;
        private bool _berserkTrigger;
        public int MergeFeedbackCount { get; private set; }

        public Vector3 GetAttackOrigin()
        {
            if (_robot?.Configuration?.ShapeId_Ref == null) return transform.position;
            var sum = Vector3.zero;
            var count = 0;
            foreach (var offset in _robot.Configuration.ShapeId_Ref.CellOffsets)
            {
                var cell = transform.Find("Cell_" + offset[0] + "_" + offset[1]);
                if (cell == null) continue;
                sum += cell.position; count++;
            }
            return count > 0 ? sum / count : transform.position;
        }

        public void BindInput(BattleShopSceneController host)
        {
            _host = host;
            if (_robot != null)
                BattleSideRenderGroup.BindRoot(gameObject,
                    host != null ? host.GetComponent<BattleSideRenderRegistry>() : null, _robot.Side);
            var input = GetComponent<RobotBoardPointerInput>();
            if (input != null) input.Bind(host, InstanceId);
        }

        public void Render(RobotEntity robot)
        {
            if (robot == null || robot.InstanceId != InstanceId) return;
            _robot = robot;
            if (_cells == null)
            {
                var cells = new List<SpriteRenderer>();
                foreach (Transform child in transform)
                    if (child.name.StartsWith("Cell_", StringComparison.Ordinal) && child.TryGetComponent<SpriteRenderer>(out var cell))
                        cells.Add(cell);
                _cells = cells.ToArray();
                var outlineRoot = transform.Find("ShapeOutline");
                _outline = outlineRoot != null ? outlineRoot.GetComponentsInChildren<SpriteRenderer>(true) : Array.Empty<SpriteRenderer>();
            }
            var visible = !robot.IsRemoved && robot.Location == RobotLocation.Board && !robot.IsDragging;
            if (!visible) ResetCollisionFeedback();
            var useIcon = _iconRenderer != null && _iconRenderer.sprite != null;
            var visualLevel = _mergePreviousLevel > 0 && _mergeElapsed < MergeConvergeDuration
                ? _mergePreviousLevel : robot.Level;
            var color = _mergeCandidate && _mergePreviousLevel == 0
                ? Color.Lerp(RobotLevelStyle.ColorForLevel(visualLevel), Color.cyan, 0.65f)
                : RobotLevelStyle.ColorForLevel(visualLevel);
            if (useIcon)
            {
                _iconRenderer.enabled = visible;
                _iconRenderer.color = color;
            }
            if (_portraitRenderer != null)
            {
                _portraitRenderer.enabled = visible && useIcon && _portraitRenderer.sprite != null;
                _portraitRenderer.color = Color.white;
            }
            if (_levelFrameRenderer != null)
            {
                _levelFrameRenderer.enabled = visible && useIcon && _levelFrameRenderer.sprite != null;
                _levelFrameRenderer.color = RobotLevelStyle.FrameColorForLevel(visualLevel);
            }
            RenderStars(robot, visible && useIcon);
            foreach (var edge in _outline) edge.enabled = visible && !useIcon;
            var collider = GetPhysicalCollider();
            if (collider != null)
            {
                collider.isTrigger = _berserkTrigger;
                collider.enabled = visible;
            }
            foreach (var cell in _cells)
            {
                // 占格节点继续提供攻击原点和拖拽选格；正式外观使用整张 Icon。
                cell.enabled = visible && !useIcon;
                cell.color = color;
            }
        }

        private void RenderStars(RobotEntity robot, bool visible)
        {
            if (_levelStars == null) return;
            var count = Mathf.Clamp(robot.Level, 1, RobotLevelStyle.MaxStars);
            var cells = robot.Configuration?.ShapeId_Ref?.CellOffsets;
            var anchor = RobotLevelStyle.StarAnchor(cells);
            var fit = RobotLevelStyle.StarScaleForCells(cells);
            var spacing = RobotLevelStyle.StarSpacingForCells(cells);
            var cellSize = MapGridMath.REFERENCE_CELL_SIZE;
            for (var i = 0; i < _levelStars.Length; i++)
            {
                var star = _levelStars[i];
                if (star == null) continue;
                star.enabled = visible && i < count && star.sprite != null;
                star.color = Color.white;
                star.transform.localPosition = (anchor + Vector2.right * ((i - (count - 1) * 0.5f) * spacing)) * cellSize;
                if (star.sprite != null)
                    star.transform.localScale = Vector3.one * (RobotLevelStyle.StarSpriteSize * fit * cellSize / star.sprite.bounds.size.x);
            }
        }

        public void SetBerserkTrigger(bool enabled)
        {
            _berserkTrigger = enabled;
            var collider = GetPhysicalCollider();
            if (collider != null) collider.isTrigger = enabled;
        }

        /// <summary>整体轮廓同时负责实体碰撞、暴走触发和指针命中。</summary>
        public PolygonCollider2D GetPhysicalCollider()
        {
            if (_bodyCollider == null) _bodyCollider = GetComponent<PolygonCollider2D>();
            if (_bodyCollider != null && !_collisionInsetApplied)
            {
                _collisionInsetApplied = true;
                InsetCollisionOutline(_bodyCollider, MapGridMath.REFERENCE_CELL_SIZE * CollisionInsetRatio);
            }
            return _bodyCollider;
        }

        private void Awake() { GetPhysicalCollider(); }

        // 偏移整个多边形的边，不能缩放整体包围盒，也不能分别缩小占格造成内部接缝。
        private static void InsetCollisionOutline(PolygonCollider2D collider, float inset)
        {
            if (inset <= 0f) return;
            for (var path = 0; path < collider.pathCount; path++)
            {
                var points = collider.GetPath(path);
                var result = new Vector2[points.Length];
                var area = 0f;
                for (var i = 0; i < points.Length; i++)
                {
                    var next = points[(i + 1) % points.Length];
                    area += points[i].x * next.y - next.x * points[i].y;
                }
                var winding = area >= 0f ? 1f : -1f;
                for (var i = 0; i < points.Length; i++)
                {
                    var before = (points[i] - points[(i + points.Length - 1) % points.Length]).normalized;
                    var after = (points[(i + 1) % points.Length] - points[i]).normalized;
                    var first = new Vector2(-before.y, before.x) * winding;
                    var second = new Vector2(-after.y, after.x) * winding;
                    result[i] = points[i] + (first + second) * (inset / Mathf.Max(0.001f, 1f + Vector2.Dot(first, second)));
                }
                collider.SetPath(path, result);
            }
        }

        /// <summary>按触点选择最近的真实占格，返回配置中的相对坐标。</summary>
        public bool TryGetNearestOccupiedCell(Vector3 worldPosition, out BoardCoordinate cell)
        {
            cell = default(BoardCoordinate);
            if (_robot == null || _robot.Definition == null || !IsFinite(worldPosition)) return false;
            var found = false;
            var bestDistance = float.PositiveInfinity;
            foreach (var candidate in _robot.Definition.Cells)
            {
                var child = transform.Find("Cell_" + candidate.Column + "_" + candidate.Row);
                if (child == null) continue;
                var distance = (GetCellWorldCenter(child) - worldPosition).sqrMagnitude;
                if (!found || distance < bestDistance - 0.000001f ||
                    (Mathf.Abs(distance - bestDistance) <= 0.000001f && IsEarlier(candidate, cell)))
                {
                    found = true;
                    bestDistance = distance;
                    cell = candidate;
                }
            }
            return found;
        }

        private Vector3 GetCellWorldCenter(Transform cell)
        {
            var sprite = cell.GetComponent<SpriteRenderer>();
            return sprite != null && sprite.sprite != null ? cell.TransformPoint(sprite.sprite.bounds.center) : cell.position;
        }

        private static bool IsEarlier(BoardCoordinate candidate, BoardCoordinate current)
        {
            return candidate.Column < current.Column ||
                (candidate.Column == current.Column && candidate.Row < current.Row);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public void SetMergeCandidate(bool candidate) { _mergeCandidate = candidate; if (_robot != null) Render(_robot); }

        public void PlayPlacementFeedback()
        {
            ResetMergeFeedback();
            ResetCollisionFeedback();
            if (!isActiveAndEnabled || _iconRenderer == null || !_iconRenderer.enabled) return;
            _mergeVisuals = CaptureMergeVisuals();
            _mergeStartOffset = Vector3.zero;
            _mergeElapsed = 0f;
            _mergePlaying = true;
            ApplyMergeVisuals(0.75f, Vector3.zero);
        }

        public void PlayMergeFeedback() => PlayMergeFeedback(null, Mathf.Max(1, (_robot?.Level ?? 1) - 1), _robot?.Level ?? 1);

        public void PlayMergeFeedback(Vector3? sourcePosition, int previousLevel, int level)
        {
            ResetMergeFeedback();
            ResetCollisionFeedback();
            MergeFeedbackCount++;
            _mergeCandidate = false;
            _mergePreviousLevel = Mathf.Clamp(previousLevel, 1, Mathf.Max(1, level));
            if (_robot != null) Render(_robot);
            if (!isActiveAndEnabled || _iconRenderer == null || !_iconRenderer.enabled)
            {
                ResetMergeFeedback();
                return;
            }
            _mergeVisuals = CaptureMergeVisuals();
            _mergeStartOffset = sourcePosition.HasValue
                ? transform.InverseTransformVector(sourcePosition.Value - transform.position) * 0.08f
                : Vector3.zero;
            _mergeStar = level > previousLevel && _levelStars != null && level > 0 && level <= _levelStars.Length
                ? _levelStars[level - 1] : null;
            if (_mergeStar != null)
            {
                _mergeStarPosition = _mergeStar.transform.localPosition;
                _mergeStarScale = _mergeStar.transform.localScale;
            }
            CreateMergeRing();
            if (sourcePosition.HasValue) CreateSourceGhost(sourcePosition.Value);
            _mergeElapsed = 0f;
            _mergePlaying = true;
            ApplyMergeVisuals(0.75f, _mergeStartOffset);
            ApplyMergeStar(0f);
        }

        private CollisionVisual[] CaptureMergeVisuals()
        {
            var result = new List<CollisionVisual>();
            foreach (var node in new[] { _iconRenderer != null ? _iconRenderer.transform : null,
                _portraitRenderer != null ? _portraitRenderer.transform : null,
                _levelFrameRenderer != null ? _levelFrameRenderer.transform : null, _starsRoot })
            {
                if (node == null) continue;
                result.Add(new CollisionVisual { Transform = node, Position = node.localPosition,
                    Scale = node.localScale, Pivot = Vector3.zero });
            }
            return result.ToArray();
        }

        private void CreateSourceGhost(Vector3 sourcePosition)
        {
            _mergeSourceStart = sourcePosition;
            _mergeSourceGhost = new GameObject("MergeSourceGhost");
            _mergeSourceGhost.transform.SetParent(transform.parent, true);
            _mergeSourceGhost.transform.position = sourcePosition;
            _mergeSourceGhost.transform.rotation = transform.rotation;
            _mergeSourceGhost.transform.localScale = transform.localScale * 0.9f;
            foreach (var source in new[] { _iconRenderer, _levelFrameRenderer, _portraitRenderer })
            {
                if (source == null || source.sprite == null) continue;
                var child = new GameObject(source.gameObject.name).AddComponent<SpriteRenderer>();
                child.transform.SetParent(_mergeSourceGhost.transform, false);
                child.transform.localPosition = source.transform.localPosition;
                child.transform.localRotation = source.transform.localRotation;
                child.transform.localScale = source.transform.localScale;
                child.sprite = source.sprite;
                child.color = source.color;
                child.sharedMaterial = source.sharedMaterial;
                child.sortingLayerID = source.sortingLayerID;
                child.sortingOrder = source.sortingOrder + 20;
                child.maskInteraction = source.maskInteraction;
            }
            _mergeSourceRenderers = _mergeSourceGhost.GetComponentsInChildren<SpriteRenderer>();
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null) BattleSideRenderGroup.BindRoot(_mergeSourceGhost, group.Registry, group.Side);
        }

        private void CreateMergeRing()
        {
            _mergeRing = new GameObject("MergeEnergyRing");
            // 跟随视觉节点回弹，沿实际外形向外偏移，避免光圈缩进底板内部。
            _mergeRing.transform.SetParent(_iconRenderer.transform, false);
            _mergeRing.layer = gameObject.layer;
            _mergeRingRenderer = _mergeRing.AddComponent<LineRenderer>();
            var ringShader = Shader.Find("Sprites/Default");
            if (ringShader == null)
            {
                DestroyMergeObject(ref _mergeRing);
                _mergeRingRenderer = null;
                return;
            }
            _mergeRingMaterial = new Material(ringShader);
            _mergeRingRenderer.sharedMaterial = _mergeRingMaterial;
            _mergeRingRenderer.loop = true;
            _mergeRingRenderer.useWorldSpace = false;
            _mergeRingRenderer.widthMultiplier = 0.01f;
            _mergeRingRenderer.numCornerVertices = 4;
            _mergeRingRenderer.sortingLayerID = _iconRenderer.sortingLayerID;
            _mergeRingRenderer.sortingOrder = _iconRenderer.sortingOrder + 30;
            _mergeRingRenderer.startColor = _mergeRingRenderer.endColor = new Color(1f, 1f, 1f, 0f);
            var collider = GetPhysicalCollider();
            if (collider == null || collider.pathCount == 0)
            {
                DestroyMergeObject(ref _mergeRing);
                _mergeRingRenderer = null;
                if (Application.isPlaying) Destroy(_mergeRingMaterial); else DestroyImmediate(_mergeRingMaterial);
                _mergeRingMaterial = null;
                return;
            }
            var path = collider.GetPath(0);
            _mergeRingPoints = new Vector3[path.Length];
            _mergeRingOutward = new Vector3[path.Length];
            var area = 0f;
            for (var i = 0; i < path.Length; i++)
            {
                var next = path[(i + 1) % path.Length];
                area += path[i].x * next.y - next.x * path[i].y;
            }
            var winding = area >= 0f ? 1f : -1f;
            for (var i = 0; i < path.Length; i++)
            {
                var before = (path[i] - path[(i + path.Length - 1) % path.Length]).normalized;
                var after = (path[(i + 1) % path.Length] - path[i]).normalized;
                var first = new Vector2(before.y, -before.x) * winding;
                var second = new Vector2(after.y, -after.x) * winding;
                var outward = (first + second) / Mathf.Max(0.001f, 1f + Vector2.Dot(first, second));
                var point = path[i] + collider.offset + outward * (MapGridMath.REFERENCE_CELL_SIZE * CollisionInsetRatio);
                _mergeRingPoints[i] = _iconRenderer.transform.InverseTransformPoint(collider.transform.TransformPoint(point));
                _mergeRingOutward[i] = _iconRenderer.transform.InverseTransformVector(collider.transform.TransformVector(outward));
            }
            _mergeRingRenderer.positionCount = _mergeRingPoints.Length;
            UpdateMergeRing(0f);
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null) BattleSideRenderGroup.BindRoot(_mergeRing, group.Registry, group.Side);
        }

        /// <summary>同步缩放底板、立绘和星星，保持碰撞、占格节点和攻击原点不变。</summary>
        public void PlayCollisionFeedback()
        {
            if (!isActiveAndEnabled || _robot == null || !_robot.CanParticipate || _mergePlaying) return;
            if (_impactOutline == null)
            {
                _impactOutline = GetComponent<RobotImpactOutline>();
                if (_impactOutline == null) _impactOutline = gameObject.AddComponent<RobotImpactOutline>();
            }
            _impactOutline.Play(_iconRenderer, GetPhysicalCollider());
            if (_collisionVisuals == null)
            {
                if (_iconRenderer == null || _iconRenderer.sprite == null) return;
                var pivot = _iconRenderer.transform.TransformPoint(_iconRenderer.sprite.bounds.center);
                var visuals = new List<CollisionVisual>();
                foreach (var node in new[] { _iconRenderer.transform, _portraitRenderer != null ? _portraitRenderer.transform : null,
                    _levelFrameRenderer != null ? _levelFrameRenderer.transform : null, _starsRoot })
                {
                    if (node == null) continue;
                    visuals.Add(new CollisionVisual
                    {
                        Transform = node, Position = node.localPosition, Scale = node.localScale,
                        Pivot = node.parent.InverseTransformPoint(pivot)
                    });
                }
                _collisionVisuals = visuals.ToArray();
            }
            // 从当前大小重新回弹，不叠加倍率，密集碰撞也不会越变越大。
            _collisionStartScale = _collisionCurrentScale;
            _collisionElapsed = 0f;
            _collisionPlaying = true;
        }

        private void LateUpdate()
        {
            if (_host != null && _host.IsFrozen) return;
            AdvanceCollisionFeedback(Time.deltaTime);
            _impactOutline?.Advance(Time.deltaTime);
            AdvanceMergeFeedback(Time.unscaledDeltaTime);
        }

        internal void AdvanceMergeFeedback(float deltaTime)
        {
            if (!_mergePlaying) return;
            var previousElapsed = _mergeElapsed;
            _mergeElapsed += Mathf.Max(0f, deltaTime);
            // 只在收拢完成时切换等级固有色，不插值染色或闪白。
            if (_mergePreviousLevel > 0 && previousElapsed < MergeConvergeDuration &&
                _mergeElapsed >= MergeConvergeDuration && _robot != null) Render(_robot);
            var converge = Mathf.Clamp01(_mergeElapsed / MergeConvergeDuration);
            if (_mergeSourceGhost != null)
            {
                _mergeSourceGhost.transform.position = Vector3.Lerp(_mergeSourceStart, transform.position,
                    Mathf.SmoothStep(0f, 1f, converge));
                var opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_mergeElapsed - 0.12f) / 0.06f));
                foreach (var renderer in _mergeSourceRenderers)
                {
                    var color = renderer.color;
                    color.a = opacity;
                    renderer.color = color;
                }
                if (converge >= 1f) DestroyMergeObject(ref _mergeSourceGhost);
            }
            float scale;
            if (_mergeElapsed <= MergeConvergeDuration)
                scale = Mathf.Lerp(0.75f, 1.15f, Mathf.SmoothStep(0f, 1f, converge));
            else
                scale = Mathf.Lerp(1.15f, 1f, Mathf.SmoothStep(0f, 1f,
                    (_mergeElapsed - MergeConvergeDuration) / (MergePopDuration - MergeConvergeDuration)));
            ApplyMergeVisuals(scale, Vector3.Lerp(_mergeStartOffset, Vector3.zero, converge));
            ApplyMergeStar(Mathf.Clamp01((_mergeElapsed - MergeConvergeDuration) / 0.4f));
            UpdateMergeRing(Mathf.Clamp01((_mergeElapsed - 0.12f) / 0.5f));
            if (_mergeElapsed >= MergeTotalDuration) ResetMergeFeedback();
        }

        private void ApplyMergeVisuals(float scale, Vector3 offset)
        {
            if (_mergeVisuals == null) return;
            foreach (var visual in _mergeVisuals)
            {
                if (visual.Transform == null) continue;
                visual.Transform.localPosition = visual.Position + offset;
                visual.Transform.localScale = new Vector3(visual.Scale.x * scale, visual.Scale.y * scale, visual.Scale.z);
            }
        }

        private void ApplyMergeStar(float progress)
        {
            if (_mergeStar == null) return;
            var rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.65f));
            var bounce = progress < 0.65f ? Mathf.Lerp(0.5f, 1.2f, rise) :
                Mathf.Lerp(1.2f, 1f, Mathf.SmoothStep(0f, 1f, (progress - 0.65f) / 0.35f));
            _mergeStar.transform.localPosition = _mergeStarPosition + Vector3.down * (0.07f * (1f - rise));
            _mergeStar.transform.localScale = _mergeStarScale * bounce;
            _mergeStar.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.22f)));
        }

        private void UpdateMergeRing(float progress)
        {
            if (_mergeRingRenderer == null || _mergeRingPoints == null) return;
            var gap = MapGridMath.REFERENCE_CELL_SIZE * Mathf.Lerp(0.035f, 0.105f, Mathf.SmoothStep(0f, 1f, progress));
            for (var i = 0; i < _mergeRingPoints.Length; i++)
                _mergeRingRenderer.SetPosition(i, _mergeRingPoints[i] + _mergeRingOutward[i] * gap);
            var opacity = 0.82f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.1f)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.25f) / 0.75f)));
            var color = new Color(1f, 1f, 1f, opacity);
            _mergeRingRenderer.startColor = _mergeRingRenderer.endColor = color;
            _mergeRingRenderer.enabled = opacity > 0f;
        }

        private void ResetMergeFeedback()
        {
            var restoreLevel = _mergePreviousLevel > 0;
            if (_mergeVisuals != null) ApplyMergeVisuals(1f, Vector3.zero);
            if (_mergeStar != null)
            {
                _mergeStar.transform.localPosition = _mergeStarPosition;
                _mergeStar.transform.localScale = _mergeStarScale;
                _mergeStar.color = Color.white;
            }
            _mergePlaying = false;
            _mergeElapsed = 0f;
            _mergePreviousLevel = 0;
            _mergeVisuals = null;
            _mergeStar = null;
            DestroyMergeObject(ref _mergeSourceGhost);
            _mergeSourceRenderers = null;
            DestroyMergeObject(ref _mergeRing);
            _mergeRingRenderer = null;
            _mergeRingPoints = null;
            _mergeRingOutward = null;
            if (_mergeRingMaterial != null)
            {
                if (Application.isPlaying) Destroy(_mergeRingMaterial); else DestroyImmediate(_mergeRingMaterial);
                _mergeRingMaterial = null;
            }
            if (restoreLevel && _robot != null) Render(_robot);
        }

        private static void DestroyMergeObject(ref GameObject instance)
        {
            if (instance == null) return;
            if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            instance = null;
        }

        private void AdvanceCollisionFeedback(float deltaTime)
        {
            if (!_collisionPlaying) return;
            if (_robot == null || !_robot.CanParticipate) { ResetCollisionFeedback(); return; }
            _collisionElapsed += Mathf.Max(0f, deltaTime);
            var duration = Mathf.Max(0.05f, _collisionDuration);
            if (_collisionElapsed >= duration) { ResetCollisionFeedback(); return; }
            var progress = _collisionElapsed / duration;
            var peak = Mathf.Max(1f, _collisionScale);
            var scale = progress < 0.25f
                ? Mathf.Lerp(_collisionStartScale, peak, Mathf.SmoothStep(0f, 1f, progress / 0.25f))
                : Mathf.Lerp(peak, 1f, Mathf.SmoothStep(0f, 1f, (progress - 0.25f) / 0.75f));
            ApplyCollisionScale(scale);
        }

        private void ApplyCollisionScale(float scale)
        {
            _collisionCurrentScale = scale;
            if (_collisionVisuals == null) return;
            foreach (var visual in _collisionVisuals)
            {
                if (visual.Transform == null) continue;
                visual.Transform.localPosition = visual.Pivot + (visual.Position - visual.Pivot) * scale;
                visual.Transform.localScale = new Vector3(visual.Scale.x * scale, visual.Scale.y * scale, visual.Scale.z);
            }
        }

        private void ResetCollisionFeedback()
        {
            _impactOutline?.Clear();
            if (!_collisionPlaying) return;
            _collisionPlaying = false;
            ApplyCollisionScale(1f);
        }

        private void OnDisable() { ResetCollisionFeedback(); ResetMergeFeedback(); }

        /// <summary>部署已购机器人时绑定原实体，不再次创建或注册。</summary>
        public void Bind(BattleEntityRegistry registry, int instanceId)
        {
            Entity entity;
            if (registry == null || !registry.TryGet(instanceId, out entity) ||
                !(entity is RobotEntity robot) || robot.ConfigId != _configId)
                throw new ArgumentException("视图与已购买机器人不匹配。", nameof(instanceId));
            ResetCollisionFeedback();
            InstanceId = instanceId;
            _registry = registry;
            Render(robot);
        }

        public bool TryGetComboRobot(BattleEntityRegistry registry, BattleSide side, out RobotEntity robot)
        {
            robot = null;
            if (!isActiveAndEnabled || registry == null || !ReferenceEquals(_registry, registry) ||
                !registry.TryGet(InstanceId, out var entity) || !ReferenceEquals(entity, _robot) ||
                _robot == null || _robot.Side != side || !_robot.CanParticipate) return false;
            robot = _robot;
            return true;
        }

        /// <summary>业务方加载预制体后显式创建实体，不在 Awake 中自动注册。</summary>
        public RobotEntity CreateEntity(BattleEntityRegistry registry, BattleSide side, int level = 1, int maxLevel = 5)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return registry.CreateRobotFromConfig(ConfigSystem.Instance.Tables.TbRobot.Get(_configId), side, level, maxLevel);
        }
    }
}
