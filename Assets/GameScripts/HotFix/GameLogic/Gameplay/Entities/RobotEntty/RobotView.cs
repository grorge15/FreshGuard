using System;
using GameConfig;
using UnityEngine;

namespace GameLogic
{
    /// <summary>预制体的配置关联；实体仍由单局管理器统一管理。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotView : MonoBehaviour
    {
        [SerializeField] private int _configId;

        public int ConfigId => _configId;
        public int InstanceId { get; private set; }
        private SpriteRenderer[] _cells;
        private Collider2D[] _colliders;
        private TextMesh _levelLabel;
        private RobotEntity _robot;
        private BattleEntityRegistry _registry;
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
                _cells = GetComponentsInChildren<SpriteRenderer>(true);
                _colliders = GetComponentsInChildren<Collider2D>(true);
                var label = transform.Find("LevelLabel");
                if (label != null) _levelLabel = label.GetComponent<TextMesh>();
            }
            var visible = !robot.IsRemoved && robot.Location == RobotLocation.Board && !robot.IsDragging;
            foreach (var collider in _colliders)
            {
                collider.isTrigger = _berserkTrigger;
                collider.enabled = visible;
            }
            foreach (var cell in _cells)
            {
                cell.enabled = visible;
                cell.color = _mergeCandidate ? Color.Lerp(RobotLevelStyle.ColorForLevel(robot.Level), Color.cyan, 0.65f) :
                    Time.unscaledTime < _feedbackUntil ? Color.Lerp(RobotLevelStyle.ColorForLevel(robot.Level), Color.white, 0.65f) :
                    RobotLevelStyle.ColorForLevel(robot.Level);
            }
            if (_levelLabel != null)
            {
                _levelLabel.text = "Lv" + robot.Level;
                _levelLabel.gameObject.SetActive(visible);
            }
        }

        public void SetBerserkTrigger(bool enabled)
        {
            _berserkTrigger = enabled;
            if (_colliders == null) _colliders = GetComponentsInChildren<Collider2D>(true);
            foreach (var collider in _colliders) if (collider != null) collider.isTrigger = enabled;
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
