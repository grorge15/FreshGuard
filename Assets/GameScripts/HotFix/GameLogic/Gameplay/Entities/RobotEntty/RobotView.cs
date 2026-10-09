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
        private bool _mergeCandidate;
        private float _feedbackUntil;
        public int MergeFeedbackCount { get; private set; }

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
            foreach (var collider in _colliders) collider.enabled = visible;
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
            Render(robot);
        }

        /// <summary>业务方加载预制体后显式创建实体，不在 Awake 中自动注册。</summary>
        public RobotEntity CreateEntity(BattleEntityRegistry registry, BattleSide side, int level = 1, int maxLevel = 5)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return registry.CreateRobotFromConfig(ConfigSystem.Instance.Tables.TbRobot.Get(_configId), side, level, maxLevel);
        }
    }
}
