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

        public int ConfigId => _configId;
        public int InstanceId { get; private set; }
        private SpriteRenderer[] _cells;
        private SpriteRenderer[] _outline;
        private PolygonCollider2D _bodyCollider;
        private bool _collisionInsetApplied;
        private RobotEntity _robot;
        private BattleEntityRegistry _registry;
        private Vector3? _authoredScale;

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
        private float _feedbackUntil;
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
            var useIcon = _iconRenderer != null && _iconRenderer.sprite != null;
            var color = _mergeCandidate ? Color.Lerp(RobotLevelStyle.ColorForLevel(robot.Level), Color.cyan, 0.65f) :
                Time.unscaledTime < _feedbackUntil ? Color.Lerp(RobotLevelStyle.ColorForLevel(robot.Level), Color.white, 0.65f) :
                RobotLevelStyle.ColorForLevel(robot.Level);
            if (useIcon)
            {
                _iconRenderer.enabled = visible;
                _iconRenderer.color = color;
            }
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
        public void PlayMergeFeedback()
        {
            MergeFeedbackCount++;
            _feedbackUntil = Time.unscaledTime + 0.4f;
            if (_robot != null) Render(_robot);
        }
        private void Update()
        {
            if (_feedbackUntil <= 0f || _robot == null) return;
            Render(_robot);
            if (Time.unscaledTime >= _feedbackUntil) _feedbackUntil = 0f;
        }

        /// <summary>部署已购机器人时绑定原实体，不再次创建或注册。</summary>
        public void Bind(BattleEntityRegistry registry, int instanceId)
        {
            Entity entity;
            if (registry == null || !registry.TryGet(instanceId, out entity) ||
                !(entity is RobotEntity robot) || robot.ConfigId != _configId)
                throw new ArgumentException("视图与已购买机器人不匹配。", nameof(instanceId));
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
