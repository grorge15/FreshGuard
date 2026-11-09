using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    public sealed class TimelineNode
    {
        public float Time { get; }
        public string ActionKey { get; }
        public IReadOnlyDictionary<string, float> Parameters { get; }
        public TimelineNode(float time, string actionKey, IReadOnlyDictionary<string, float> parameters = null)
        {
            EntityValidation.RequireFiniteNonNegative(time, nameof(time));
            if (string.IsNullOrWhiteSpace(actionKey)) throw new ArgumentException(nameof(actionKey));
            Time = time; ActionKey = actionKey; Parameters = BuffObj.Copy(parameters);
        }
    }
    public sealed class TimelineModel
    {
        public string Key { get; }
        public float Duration { get; }
        public IReadOnlyList<TimelineNode> Nodes { get; }
        public TimelineModel(string key, float duration, IReadOnlyList<TimelineNode> nodes)
        {
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            if (string.IsNullOrWhiteSpace(key) || nodes == null) throw new ArgumentException(nameof(key));
            var copy = nodes.OrderBy(n => n.Time).ToList(); // LINQ排序稳定，同时间沿注册顺序。
            if (copy.Any(n => n.Time > duration)) throw new ArgumentException("节点超出时间轴总时长。");
            Key = key; Duration = duration; Nodes = copy.AsReadOnly();
        }
    }
    public interface ITimelineAction
    {
        /// <summary>接受施法前检查准备状态；返回的动作只捕获值、ID与单局服务。</summary>
        bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute);
    }
    /// <summary>使用单个预备表现槽的动作，一次施法只允许一个节点，禁止接受后静默漏发。</summary>
    public interface ISingleUseTimelineAction : ITimelineAction { }
    /// <summary>准备时预留资源；未接受、取消或正常结束都由时间轴统一归还未用部分。</summary>
    public interface IReleasableTimelineAction : ITimelineAction
    {
        bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release);
    }
    public sealed class TimelineObj
    {
        public TimelineModel Model { get; }
        public SkillCastSnapshot Cast { get; }
        public float Elapsed { get; internal set; }
        public int Cursor { get; internal set; }
        public bool IsFinished { get; internal set; }
        public bool IsCancelled { get; internal set; }
        internal bool AttackLaunchNotified;
        internal readonly Action[] Actions;
        private Action[] _releases;
        internal TimelineObj(TimelineModel model, SkillCastSnapshot cast, Action[] actions, Action[] releases)
        { Model = model; Cast = cast; Actions = actions; _releases = releases; }
        internal void ReleasePreparation()
        {
            var releases = _releases; _releases = null;
            if (releases == null) return;
            Exception failure = null;
            foreach (var release in releases)
                try { release?.Invoke(); } catch (Exception e) { if (failure == null) failure = e; }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
