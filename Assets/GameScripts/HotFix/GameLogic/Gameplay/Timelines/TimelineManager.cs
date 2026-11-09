using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class TimelineManager : IDisposable
    {
        private readonly BattleRuntime _battle;
        private readonly Dictionary<string, TimelineModel> _models = new Dictionary<string, TimelineModel>();
        private readonly Dictionary<string, ITimelineAction> _actions = new Dictionary<string, ITimelineAction>();
        private readonly Dictionary<string, Func<SkillCastSnapshot, TimelineModel>> _factories = new Dictionary<string, Func<SkillCastSnapshot, TimelineModel>>();
        private readonly Dictionary<SkillOwnerRef, TimelineObj> _active = new Dictionary<SkillOwnerRef, TimelineObj>();
        public int Count => _active.Count;
        public bool IsDisposed { get; private set; }
        public TimelineManager(BattleRuntime battle) { _battle = battle; }
        public void Register(TimelineModel model) => _models.Add(model.Key, model);
        public void RegisterFactory(string key, Func<SkillCastSnapshot, TimelineModel> factory) => _factories.Add(key, factory);
        public void RegisterAction(string key, ITimelineAction action) => _actions.Add(key, action ?? throw new ArgumentNullException(nameof(action)));
        public bool HasChannel(SkillOwnerRef owner) => _active.ContainsKey(owner);
        public void NotifyAttackLaunched(SkillCastSnapshot cast)
        {
            if (!IsActive(cast) || !_active.TryGetValue(cast.Owner, out var timeline) || timeline.AttackLaunchNotified) return;
            // 先封口，通知中的嵌套调用不能再次增加次数。
            timeline.AttackLaunchNotified = true;
            if (_battle.TryResolveOwner(cast.Owner, out var owner))
                owner.Buffs.OnAttackLaunched(cast, () => !_battle.IsDisposed);
        }
        public bool IsActive(SkillCastSnapshot cast) => cast != null &&
            _active.TryGetValue(cast.Owner, out var timeline) && !timeline.IsFinished && ReferenceEquals(timeline.Cast, cast);
        public bool TryPrepare(SkillCastSnapshot cast, out TimelineObj timeline, out SkillCastFailure failure)
        {
            timeline = null; failure = SkillCastFailure.InvalidTimeline;
            if (IsDisposed) return false;
            TimelineModel model;
            if (_factories.TryGetValue(cast.TimelineKey, out var factory))
            {
                model = factory(cast);
                if (model == null) return false;
            }
            else if (!_models.TryGetValue(cast.TimelineKey, out model)) return false;
            var actions = new Action[model.Nodes.Count];
            var releases = new Action[actions.Length];
            var singleUse = new HashSet<ITimelineAction>();
            try
            {
                for (var i = 0; i < actions.Length; i++)
                {
                    if (!_actions.TryGetValue(model.Nodes[i].ActionKey, out var action)) return false;
                    if (action is ISingleUseTimelineAction && !singleUse.Add(action))
                    { failure = SkillCastFailure.NotPrepared; return false; }
                    var prepared = action is IReleasableTimelineAction owned
                        ? owned.TryPrepare(cast, model.Nodes[i], out actions[i], out releases[i])
                        : action.TryPrepare(cast, model.Nodes[i], out actions[i]);
                    if (!prepared || actions[i] == null)
                    { failure = SkillCastFailure.NotPrepared; return false; }
                }
                timeline = new TimelineObj(model, cast, actions, releases);
                failure = SkillCastFailure.None;
                return true;
            }
            finally
            {
                if (timeline == null) foreach (var release in releases) release?.Invoke();
            }
        }
        internal void Start(TimelineObj timeline)
        {
            _active.Add(timeline.Cast.Owner, timeline);
            ExecuteDue(timeline);
        }
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsDisposed) return;
            foreach (var timeline in new List<TimelineObj>(_active.Values))
            {
                if (IsDisposed) break;
                if (timeline.IsFinished) continue;
                timeline.Elapsed = Math.Min(timeline.Model.Duration, timeline.Elapsed + deltaTime);
                ExecuteDue(timeline);
            }
        }

        public float TimeUntilNextNode
        {
            get
            {
                var result = float.PositiveInfinity;
                foreach (var timeline in _active.Values)
                    if (!timeline.IsFinished)
                    {
                        var due = timeline.Cursor < timeline.Model.Nodes.Count
                            ? timeline.Model.Nodes[timeline.Cursor].Time : timeline.Model.Duration;
                        result = Math.Min(result, Math.Max(0f, due - timeline.Elapsed));
                    }
                return result;
            }
        }
        private void ExecuteDue(TimelineObj timeline)
        {
            while (!IsDisposed && !timeline.IsFinished && timeline.Cursor < timeline.Model.Nodes.Count &&
                   timeline.Model.Nodes[timeline.Cursor].Time <= timeline.Elapsed)
            {
                if (!_battle.TryResolveOwner(timeline.Cast.Owner, out var owner) || !owner.CanCast)
                { Cancel(timeline.Cast.Owner); return; }
                var index = timeline.Cursor++;
                try { timeline.Actions[index](); }
                catch { Cancel(timeline.Cast.Owner); throw; }
            }
            if (!timeline.IsFinished && !IsDisposed)
            {
                if (!_battle.TryResolveOwner(timeline.Cast.Owner, out var owner) || !owner.CanCast)
                    Cancel(timeline.Cast.Owner);
                else if (timeline.Elapsed >= timeline.Model.Duration) Finish(timeline, false);
            }
        }
        public void Cancel(SkillOwnerRef owner)
        { if (_active.TryGetValue(owner, out var timeline)) Finish(timeline, true); }
        private void Finish(TimelineObj timeline, bool cancelled)
        {
            timeline.IsFinished = true; timeline.IsCancelled = cancelled; _active.Remove(timeline.Cast.Owner);
            timeline.ReleasePreparation();
        }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            foreach (var timeline in new List<TimelineObj>(_active.Values)) Finish(timeline, true);
            _models.Clear(); _actions.Clear(); _factories.Clear();
        }
    }
}
