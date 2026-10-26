using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class TimelineManager : IDisposable
    {
        private readonly BattleRuntime _battle;
        private readonly Dictionary<string, TimelineModel> _models = new Dictionary<string, TimelineModel>();
        private readonly Dictionary<string, ITimelineAction> _actions = new Dictionary<string, ITimelineAction>();
        private readonly Dictionary<SkillOwnerRef, TimelineObj> _active = new Dictionary<SkillOwnerRef, TimelineObj>();
        public int Count => _active.Count;
        public bool IsDisposed { get; private set; }
        public TimelineManager(BattleRuntime battle) { _battle = battle; }
        public void Register(TimelineModel model) => _models.Add(model.Key, model);
        public void RegisterAction(string key, ITimelineAction action) => _actions.Add(key, action ?? throw new ArgumentNullException(nameof(action)));
        public bool HasChannel(SkillOwnerRef owner) => _active.ContainsKey(owner);
        public bool TryPrepare(SkillCastSnapshot cast, out TimelineObj timeline, out SkillCastFailure failure)
        {
            timeline = null; failure = SkillCastFailure.InvalidTimeline;
            if (IsDisposed || !_models.TryGetValue(cast.TimelineKey, out var model)) return false;
            var actions = new Action[model.Nodes.Count];
            var singleUse = new HashSet<ITimelineAction>();
            for (var i = 0; i < actions.Length; i++)
            {
                if (!_actions.TryGetValue(model.Nodes[i].ActionKey, out var action)) return false;
                if (action is ISingleUseTimelineAction && !singleUse.Add(action))
                { failure = SkillCastFailure.NotPrepared; return false; }
                if (!action.TryPrepare(cast, model.Nodes[i], out actions[i]) || actions[i] == null)
                { failure = SkillCastFailure.NotPrepared; return false; }
            }
            timeline = new TimelineObj(model, cast, actions); failure = SkillCastFailure.None; return true;
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
        { timeline.IsFinished = true; timeline.IsCancelled = cancelled; _active.Remove(timeline.Cast.Owner); }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            foreach (var timeline in new List<TimelineObj>(_active.Values)) Finish(timeline, true);
            _models.Clear(); _actions.Clear();
        }
    }
}
