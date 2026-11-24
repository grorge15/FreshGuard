using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class EnemyBattleController
    {
        internal const int PlayerBlastDamage = 50;
        internal const float BlastDamageRadiusScale = 1.2f;
        // 原画每帧 256px，fx_04 落地帧的锅盖实体横跨 x=95..160，不含冲击火花。
        private const float LidContactWidthPixels = 66f;
        private const int LidContactFrame = 4;
        private const double LidFallFrameSeconds = .060;
        private const double LidAfterContactFrameSeconds = .030;
        [SerializeField] private Sprite[] _potLidFrames;
        [SerializeField] private Sprite[] _blastRangeFrames;
        [SerializeField] private Sprite _potLidDisabled;
        internal Sprite PotLidDisabled => _potLidDisabled;
        [SerializeField] private Sprite[] _potLidReadyOnFrames;
        [SerializeField] private Sprite[] _potLidReadyLoopFrames;
        internal Sprite GetPotLidReadyFrame(int frame)
        {
            int intro = _potLidReadyOnFrames?.Length ?? 0;
            if (frame < intro) return _potLidReadyOnFrames[frame];
            return _potLidReadyLoopFrames != null && _potLidReadyLoopFrames.Length > 0
                ? _potLidReadyLoopFrames[(frame - intro) % _potLidReadyLoopFrames.Length] : null;
        }

        internal Sprite GetBlastRangeFrame(int frame) => _blastRangeFrames != null && _blastRangeFrames.Length > 0
            ? _blastRangeFrames[frame % _blastRangeFrames.Length] : null;
        private SpriteRenderer _potLid;
        private double _potLidStart;
        private double LidContactAt => _potLidStart + LidContactFrame * LidFallFrameSeconds;
        private bool _blastPending;
        private Vector3 _blastCenter;
        private float _blastRadius;
        private SkillOwnerRef _blastSource;
        internal const float PlayerBlastCooldown = 15f;
        [SerializeField, Tooltip("测试用：锅盖落地后可立即再次施放。")] private bool _skipBlastCooldown;
        private double _blastReadyAt;
        internal float BlastRemaining => (float)Math.Max(0d, _blastReadyAt - _elapsed);
        private IReadOnlyList<Vector3> BlastRoute => _centerRoutes[(int)BattleSide.Player];
        internal bool BlastReady => IsRunning && !_blastPending && BlastRemaining <= 0f;

        private const float PlacementToleranceInCells = .75f;

        private bool IsOnBlastRoute(Vector2 pointer)
        {
            if (!BlastReady || BlastRoute == null) return false;
            var nearest = float.PositiveInfinity;
            for (int i = 1; i < BlastRoute.Count; i++)
            {
                var a = (Vector2)BlastRoute[i - 1];
                var edge = (Vector2)BlastRoute[i] - a;
                if (edge.sqrMagnitude <= 0f) continue;
                var t = Mathf.Clamp01(Vector2.Dot(pointer - a, edge) / edge.sqrMagnitude);
                var p = a + t * edge;
                var d = (p - pointer).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
            }
            var tolerance = _host.PlayerBlastRadius * PlacementToleranceInCells;
            return tolerance > 0f && nearest <= tolerance * tolerance;
        }

        internal bool TryReleaseBlast(Vector2 pointer, out Vector3 center)
        {
            center = pointer;
            if (!IsOnBlastRoute(pointer) ||
                !_host.TryGetTowerTarget(BattleSide.Player, out var tower, out _)) return false;
            _blastCenter = center;
            _blastRadius = _host.PlayerBlastRadius * BlastDamageRadiusScale;
            _blastSource = SkillOwnerRef.Entity(_context.BattleId, BattleSide.Player, tower);
            _potLidStart = _elapsed;
            _blastPending = true;
            _blastReadyAt = _elapsed + (_skipBlastCooldown ? 0f : PlayerBlastCooldown);
            PlayPotLid(center, _host.PlayerBlastRadius * 2f);
            return true;
        }

        private void ResolveLidContact()
        {
            if (!_blastPending || _elapsed < LidContactAt) return;
            _blastPending = false;
            _host.PlayPotLidImpactFeedback();
            var targets = new List<int>();
            foreach (var id in _active.Keys)
                if (TryGetTargetPosition(BattleSide.Player, id, out var feet, true) &&
                    ((Vector2)(feet - _blastCenter)).sqrMagnitude <= _blastRadius * _blastRadius) targets.Add(id);
            var source = _blastSource;
            foreach (var id in targets)
            {
                if (!OwnsBattle) break;
                _context.Combat.ResolvePlayerBlast(source, SkillOwnerRef.Entity(source.BattleId, source.Side, id), PlayerBlastDamage);
            }
        }

        private void PlayPotLid(Vector3 center, float previewDiameter)
        {
            if (_potLidFrames == null || _potLidFrames.Length == 0 || _potLidFrames[0] == null) return;
            if (_potLid == null)
            {
                var go = new GameObject("PlayerBlastPotLid");
                go.transform.SetParent(transform, false);
                _potLid = go.AddComponent<SpriteRenderer>();
                _potLid.sortingOrder = short.MaxValue - 1;
                BattleSideRenderGroup.BindRoot(go, _host.GetComponent<BattleSideRenderRegistry>(), BattleSide.Player);
            }
            var worldScale = previewDiameter * _potLidFrames[0].pixelsPerUnit / LidContactWidthPixels;
            var parentScale = transform.lossyScale;
            _potLid.transform.localScale = new Vector3(worldScale / Mathf.Abs(parentScale.x), worldScale / Mathf.Abs(parentScale.y), 1f);
            _potLid.transform.position = center;
            _potLid.sprite = _potLidFrames[0];
            _potLid.enabled = true;
        }

        private void SamplePotLid()
        {
            if (_potLid == null || !_potLid.enabled) return;
            var frame = _elapsed < LidContactAt ? Math.Min(LidContactFrame - 1, (int)((_elapsed - _potLidStart) / LidFallFrameSeconds))
                : LidContactFrame + (int)((_elapsed - LidContactAt) / LidAfterContactFrameSeconds);
            if (frame < _potLidFrames.Length) _potLid.sprite = _potLidFrames[frame];
            else _potLid.enabled = false;
        }
    }
}
