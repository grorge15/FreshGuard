using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum BuffChangeKind { Apply, Remove, Clear }

    /// <summary>一次不可变的Buff修改请求。只保存身份和数值，不持有池化实体。</summary>
    public sealed class AddBuffInfo
    {
        public BuffModel Model { get; }
        public SkillOwnerRef Caster { get; }
        public SkillOwnerRef Carrier { get; }
        public float Time { get; }
        public bool DurationSetTo { get; }
        public int AddStack { get; }
        // null保留已有参数；非null默认整体覆盖，MergeParameters为true时按键合并。
        public IReadOnlyDictionary<string, float> Param { get; }
        public bool MergeParameters { get; }
        public bool Permanent { get; }
        // 独立层精确修改时指定；不指定时由Behavior决定新增/刷新/替换。
        public long? InstanceId { get; }
        public BuffChangeKind Kind { get; }

        public AddBuffInfo(BuffModel model, SkillOwnerRef caster, SkillOwnerRef carrier,
            float time = 0f, bool durationSetTo = false, int addStack = 1,
            IReadOnlyDictionary<string, float> param = null, bool mergeParameters = false,
            bool permanent = false, long? instanceId = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            if (float.IsNaN(time) || float.IsInfinity(time) || (durationSetTo && time < 0f))
                throw new ArgumentOutOfRangeException(nameof(time));
            if (instanceId.HasValue && instanceId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(instanceId));
            Caster = caster; Carrier = carrier; Time = time; DurationSetTo = durationSetTo;
            AddStack = addStack; Param = param == null ? null : BuffObj.Copy(param);
            MergeParameters = mergeParameters; Permanent = permanent; InstanceId = instanceId;
            Kind = BuffChangeKind.Apply;
        }

        private AddBuffInfo(SkillOwnerRef carrier, BuffObj buff)
        {
            Carrier = carrier; Model = buff?.Model; Caster = buff?.Source ?? default(SkillOwnerRef);
            InstanceId = buff?.InstanceId; Kind = buff == null ? BuffChangeKind.Clear : BuffChangeKind.Remove;
        }
        public static AddBuffInfo Remove(SkillOwnerRef carrier, BuffObj buff)
            => new AddBuffInfo(carrier, buff ?? throw new ArgumentNullException(nameof(buff)));
        public static AddBuffInfo Clear(SkillOwnerRef carrier) => new AddBuffInfo(carrier, null);
    }
}
