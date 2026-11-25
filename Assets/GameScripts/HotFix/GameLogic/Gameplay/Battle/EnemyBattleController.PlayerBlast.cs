using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class EnemyBattleController
    {
        internal const int PlayerBlastSkillId = 1608;
        private const string BlastFallAction = "battlefield.pot_lid.fall";
        private const string BlastImpactAction = "battlefield.pot_lid.impact";
        // 原画每帧 256px，fx_04 落地帧的锅盖实体横跨 x=95..160，不含冲击火花。
        private const float LidContactWidthPixels = 66f;
        private const int LidContactFrame = 4;
        private const double LidFallFrameSeconds = .060;
        private const float LidContactDelay = (float)(LidContactFrame * LidFallFrameSeconds);
        private const double LidAfterContactFrameSeconds = .030;
        private const float PlacementToleranceInCells = .75f;
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
        private bool _playBlastReadySound;
        [SerializeField, Tooltip("测试用：锅盖落地后可立即再次施放。")] private bool _skipBlastCooldown;
        private SkillObj _blastSkill;
        internal float BlastRemaining => _blastSkill?.CooldownRemaining ?? 0f;
        internal float BlastCooldownFraction => _blastSkill != null && _blastSkill.Model.Cooldown > 0f
            ? Mathf.Clamp01(BlastRemaining / _blastSkill.Model.Cooldown) : 0f;
        internal float BlastRadiusInCells => _blastSkill != null &&
            _context.Battle.Catalog.TryGetLevel(_blastSkill.Model.Id, _blastSkill.Level, out var level) &&
            level.Parameters.TryGetValue("areaRadiusCells", out var radius) ? radius : 0f;
        private IReadOnlyList<Vector3> BlastRoute => _centerRoutes[(int)BattleSide.Player];
        internal bool BlastReady => IsRunning && _blastSkill != null && BlastRemaining <= 0f &&
            !_context.Battle.Timelines.HasChannel(_context.Battle.Player.OwnerRef);

        private void RegisterPlayerBlast()
        {
            var battle = _context.Battle;
            if (!battle.Catalog.TryGet(PlayerBlastSkillId, out var model)) return;
            battle.Timelines.RegisterAction(BlastFallAction, new PlayerBlastAction(this, false));
            battle.Timelines.RegisterAction(BlastImpactAction, new PlayerBlastAction(this, true));
            battle.Timelines.Register(new TimelineModel(model.TimelineKey, LidContactDelay, new[]
            {
                new TimelineNode(0f, BlastFallAction),
                new TimelineNode(LidContactDelay, BlastImpactAction)
            }));
            _blastSkill = battle.Grant(battle.Player, model);
        }

        private void UnregisterPlayerBlast()
        {
            if (_blastSkill == null) return;
            var battle = _context.Battle;
            battle.Timelines.Unregister(_blastSkill.Model.TimelineKey, BlastFallAction, BlastImpactAction);
            _blastSkill = null;
            _playBlastReadySound = false;
        }

        private bool IsOnBlastRoute(Vector2 pointer)
        {
            if (BlastRoute == null) return false;
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
            if (!BlastReady) return false;
            var battle = _context.Battle;
            var request = new SkillCastRequest(battle.Player.OwnerRef, _blastSkill.Model.Id, SkillTriggerSource.Button,
                SkillTarget.AtPosition(BattleSide.Player, center));
            return battle.Skills.TryCast(request).Accepted;
        }

        private void StartLidFall(Vector3 center, float cellSize)
        {
            if (_skipBlastCooldown) _blastSkill.CooldownRemaining = 0f;
            _playBlastReadySound = BlastRemaining > 0f;
            _potLidStart = _elapsed;
            PlayPotLid(center, cellSize * 2f);
        }

        private void ResolveLidContact(Vector3 center, float radius, SkillOwnerRef source, int skillId, float damageRatio)
        {
            GameSoundService.Play(GameSound.PotLidImpact);
            _host.PlayPotLidImpactFeedback();
            var targets = new List<int>();
            foreach (var id in _active.Keys)
                if (TryGetTargetPosition(BattleSide.Player, id, out var feet, true) &&
                    AoeManager.ContainsCircle(center, radius, feet))
                    targets.Add(id);
            foreach (var id in targets)
            {
                if (!OwnsBattle) break;
                if (_context.Registry.TryGet(id, out var entity) && entity is EnemyEntity enemy)
                {
                    var damage = (int)Math.Ceiling(enemy.MaxHp * (double)damageRatio);
                    _context.Combat.ResolvePlayerBlast(source, enemy.OwnerRef, skillId, damage);
                }
            }
        }

        private sealed class PlayerBlastAction : ITimelineAction
        {
            private readonly EnemyBattleController _controller;
            private readonly bool _impact;

            public PlayerBlastAction(EnemyBattleController controller, bool impact)
            { _controller = controller; _impact = impact; }

            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
            {
                execute = null;
                if (!_controller.IsRunning || !cast.Owner.Equals(_controller._context.Battle.Player.OwnerRef) ||
                    cast.Trigger != SkillTriggerSource.Button || cast.Target.Kind != SkillTargetKind.Position ||
                    !_controller.IsOnBlastRoute(cast.Target.Position) ||
                    !_controller._host.TryGetTowerTarget(BattleSide.Player, out var tower, out _))
                    return false;

                var center = cast.Target.Position;
                var cellSize = _controller._host.PlayerBlastRadius;
                var radiusInCells = cast.Get("areaRadiusCells");
                var damageRatio = cast.Get("maxHpDamageRatio");
                if (radiusInCells <= 0f || damageRatio <= 0f) return false;
                var source = SkillOwnerRef.Entity(cast.Owner.BattleId, cast.Owner.Side, tower);
                if (_impact)
                    execute = () => _controller.ResolveLidContact(center, cellSize * radiusInCells, source, cast.SkillId, damageRatio);
                else
                    execute = () => _controller.StartLidFall(center, cellSize);
                return true;
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
            var elapsed = _elapsed - _potLidStart;
            var frame = elapsed < LidContactDelay ? Math.Min(LidContactFrame - 1, (int)(elapsed / LidFallFrameSeconds))
                : LidContactFrame + (int)((elapsed - LidContactDelay) / LidAfterContactFrameSeconds);
            if (frame < _potLidFrames.Length) _potLid.sprite = _potLidFrames[frame];
            else _potLid.enabled = false;
        }
    }
}
