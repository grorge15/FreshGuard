using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public enum OpponentAiStrategyPhase { GlassExpansion, ComboControl }

    /// <summary>保存本轮候选共用的评分尺度，提交时用新几何计算、沿用同一验收口径。</summary>
    public readonly struct OpponentAiScoreReference
    {
        public readonly Vector4 Maxima;
        public readonly bool Precise;
        public OpponentAiScoreReference(Vector4 maxima, bool precise) { Maxima = maxima; Precise = precise; }
        public float Score(OpponentAiRegionEvaluation value, Vector4 weights)
            => Score(value.GlassValue, value.RobotValue, value.Control, value.Mobility, weights);
        public float Score(float glass, float robot, float control, float mobility, Vector4 weights)
            => weights.x * glass / Math.Max(.0001f, Maxima.x) +
               weights.y * robot / Math.Max(.0001f, Maxima.y) +
               weights.z * control / Math.Max(.0001f, Maxima.z) +
               weights.w * mobility / Math.Max(.0001f, Maxima.w);
        public static OpponentAiScoreReference ForPair(OpponentAiRegionEvaluation before,
            OpponentAiRegionEvaluation after, bool precise) => new OpponentAiScoreReference(new Vector4(
                Math.Max(before.GlassValue, after.GlassValue), Math.Max(before.RobotValue, after.RobotValue),
                Math.Max(before.Control, after.Control), Math.Max(before.Mobility, after.Mobility)), precise);
    }

    [Serializable]
    public sealed class OpponentAiStrategySettings
    {
        public Vector4 GlassWeights = new Vector4(.45f, .20f, .25f, .10f);
        public Vector4 ComboWeights = new Vector4(.05f, .35f, .45f, .15f);
        public float MinimumScoreGain = .05f;
        public float LowGlassRatio = .2f;
        public int LowGlassMinimum = 2;
        public float PredictionSeconds = .12f;
        public float ReverseMoveBlock = 6f;
        public int FinalistCount = 8;

        internal OpponentAiStrategySettings Copy()
        {
            var copy = (OpponentAiStrategySettings)MemberwiseClone();
            copy.MinimumScoreGain = Positive(MinimumScoreGain, .05f);
            copy.LowGlassRatio = Mathf.Clamp01(Positive(LowGlassRatio, .2f));
            copy.LowGlassMinimum = Math.Max(0, LowGlassMinimum);
            copy.PredictionSeconds = Positive(PredictionSeconds, .12f);
            copy.ReverseMoveBlock = Positive(ReverseMoveBlock, 6f);
            copy.FinalistCount = Math.Max(1, Math.Min(8, FinalistCount));
            copy.GlassWeights = Weights(GlassWeights, new Vector4(.45f, .20f, .25f, .10f));
            copy.ComboWeights = Weights(ComboWeights, new Vector4(.05f, .35f, .45f, .15f));
            return copy;
        }
        private static float Positive(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) || v < 0 ? fallback : v;
        private static Vector4 Weights(Vector4 values, Vector4 fallback)
        {
            float total = 0;
            for (var i = 0; i < 4; i++) { values[i] = Positive(values[i], fallback[i]); total += values[i]; }
            return total > .0001f ? values / total : fallback;
        }
    }

    // 坐标均为棋盘格单位，以棋盘左下角为原点，避免世界缩放污染几何判断。
    public readonly struct OpponentAiBallState
    {
        public readonly Vector2 Position, Direction;
        public readonly float Speed, Radius;
        public readonly int Combo;
        public readonly bool IsLaunched, IsBerserk;
        public readonly PlacementRecoveryState RecoveryState;
        public OpponentAiBallState(Vector2 position, Vector2 direction, float speed, float radius,
            bool isLaunched = true, bool isBerserk = false, int combo = 0,
            PlacementRecoveryState recoveryState = PlacementRecoveryState.None)
        {
            Position = position; Direction = direction; Speed = speed; Radius = radius;
            IsLaunched = isLaunched; IsBerserk = isBerserk; Combo = combo; RecoveryState = recoveryState;
        }
    }

    public readonly struct OpponentAiGlassState
    {
        public readonly BoardCoordinate Coordinate;
        public readonly int Durability, HitLoss, Reward;
        public OpponentAiGlassState(BoardCoordinate coordinate, int durability, int hitLoss, int reward)
        { Coordinate = coordinate; Durability = durability; HitLoss = hitLoss; Reward = reward; }
        public float Value => Reward / Math.Max(1f, (float)Math.Ceiling((double)Durability / Math.Max(1, HitLoss)));
    }

    public sealed class OpponentAiColliderSnapshot
    {
        public string DefinitionId { get; }
        public BoardCoordinate Anchor { get; }
        public IReadOnlyList<Vector2[]> Paths { get; }
        public OpponentAiColliderSnapshot(string definitionId, BoardCoordinate anchor, IReadOnlyList<Vector2[]> paths)
        {
            DefinitionId = definitionId; Anchor = anchor;
            var copy = new List<Vector2[]>();
            foreach (var path in paths) copy.Add((Vector2[])path.Clone());
            Paths = copy.AsReadOnly();
        }
    }

    public sealed class OpponentAiWorldSnapshot
    {
        public OpponentAiBallState Ball { get; }
        public IReadOnlyList<OpponentAiGlassState> Glass { get; }
        public IReadOnlyList<OpponentAiColliderSnapshot> Colliders { get; }
        public OpponentAiStrategyPhase Phase { get; }
        public int BoardRevision { get; }
        public OpponentAiStrategySettings Settings { get; }
        public OpponentAiWorldSnapshot(OpponentAiBallState ball, IReadOnlyList<OpponentAiGlassState> glass,
            IReadOnlyList<OpponentAiColliderSnapshot> colliders = null, int boardRevision = 0,
            OpponentAiStrategyPhase phase = OpponentAiStrategyPhase.GlassExpansion, OpponentAiStrategySettings settings = null)
        {
            Ball = ball; Glass = new List<OpponentAiGlassState>(glass ?? Array.Empty<OpponentAiGlassState>()).AsReadOnly();
            Colliders = new List<OpponentAiColliderSnapshot>(colliders ?? Array.Empty<OpponentAiColliderSnapshot>()).AsReadOnly();
            BoardRevision = boardRevision; Phase = phase;
            Settings = (settings ?? new OpponentAiStrategySettings()).Copy();
        }
    }

    public readonly struct OpponentAiMoveMemory
    {
        public readonly int InstanceId;
        public readonly BoardCoordinate From, To;
        public readonly float ActiveTime;
        public OpponentAiMoveMemory(int instanceId, BoardCoordinate from, BoardCoordinate to, float activeTime)
        { InstanceId = instanceId; From = from; To = to; ActiveTime = activeTime; }
    }

    public readonly struct OpponentAiFollowUp
    {
        public readonly OpponentAiActionKind Kind;
        public readonly int SlotId, SourceInstanceId, TargetInstanceId;
        public readonly long OfferId, BattleId;
        public readonly BoardCoordinate Anchor;
        public readonly bool RequireClosed;
        public readonly OpponentAiEnclosureKind MinimumEnclosure;
        public OpponentAiFollowUp(OpponentAiAction action, bool requireClosed)
        {
            Kind = action.Kind; SlotId = action.SlotId; SourceInstanceId = action.SourceInstanceId;
            TargetInstanceId = action.TargetInstanceId; OfferId = action.OfferId; BattleId = action.BattleId;
            Anchor = action.Anchor; RequireClosed = requireClosed;
            MinimumEnclosure = requireClosed
                ? (action.MinimumEnclosure == OpponentAiEnclosureKind.None ? OpponentAiEnclosureKind.Glass : action.MinimumEnclosure)
                : OpponentAiEnclosureKind.None;
        }
        public OpponentAiAction ToAction() => new OpponentAiAction(Kind, SlotId, OfferId, SourceInstanceId,
            TargetInstanceId, Anchor, BattleId, minimumEnclosure: MinimumEnclosure);
    }
}
