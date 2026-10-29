using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>由单局装配入口统一准备和发射，避免Start次序导致球先于玻璃启动。</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementBoardsBootstrap : MonoBehaviour
    {
        [SerializeField] private PlacementController _controller;
        [SerializeField] private PhysicalCollisionBall _ball;
        [SerializeField] private PhysicalCollisionBall _opponentBall;
        [SerializeField] private PhysicalCollisionBall[] _playerBerserkBalls;
        [SerializeField] private PhysicalCollisionBall[] _opponentBerserkBalls;
        private bool _launched;
        private Func<bool> _runningGate;
        private long _battleId;
        private float _berserkDuration;
        private BallGlobalRules _rules;
        private BallSession _player;
        private BallSession _opponent;

        private sealed class BallSession
        {
            public PhysicalCollisionBall Main;
            public PhysicalCollisionBall[] Temporary;
            public int AuthoredCount;
            public bool Berserk;
            public float Remaining;
        }

        public event Action<PhysicalCollisionBall, int> RobotContactDetected;
        public event Action<BattleSide, bool> BerserkChanged;

        public PhysicalCollisionBall PlayerBall => _ball;
        public PhysicalCollisionBall OpponentBall => _opponentBall;
        public bool IsBerserk(BattleSide side) => Session(side)?.Berserk == true;
        public float BerserkRemaining(BattleSide side) => Session(side)?.Remaining ?? 0f;
        public float BerserkDuration => _berserkDuration;
        private BallSession Session(BattleSide side) => side == BattleSide.Player ? _player : _opponent;

        public void ConfigureFromTables(GameConfig.Tables tables)
        {
            if (_launched) throw new InvalidOperationException("发射后不能修改能源球规则。");
            _rules = BallGlobalRules.FromTables(tables);
            _ball.Configure(_rules);
            _opponentBall.Configure(_rules);
        }

        public void BindBattle(long battleId, System.Func<bool> runningGate,
            BattleEntityRegistry registry, Transform popupRoot)
        {
            UnbindBattle();
            _battleId = battleId;
            _runningGate = runningGate;
            _ball.BindBattle(battleId, BattleSide.Player, runningGate);
            _opponentBall.BindBattle(battleId, BattleSide.Opponent, runningGate);
            _ball.BindCombo(registry, popupRoot);
            _opponentBall.BindCombo(registry, popupRoot);
            _berserkDuration = _ball.GlobalValueProvider(15);
            if (float.IsNaN(_berserkDuration) || float.IsInfinity(_berserkDuration) || _berserkDuration <= 0f)
                throw new InvalidOperationException("Global15 暴走时间必须为有限正数。");
            _player = BindSession(_ball, _playerBerserkBalls, registry);
            _opponent = BindSession(_opponentBall, _opponentBerserkBalls, registry);
        }

        private BallSession BindSession(PhysicalCollisionBall main, PhysicalCollisionBall[] temporary, BattleEntityRegistry registry)
        {
            var count = _rules?.ExtraBallCount ?? 2;
            var authoredCount = Mathf.Min(temporary?.Length ?? 0, count);
            for (var i = 0; i < authoredCount; i++)
            {
                if (temporary[i] == null || temporary[i] == main || Array.IndexOf(temporary, temporary[i]) != i)
                    throw new InvalidOperationException("临时暴走球引用必须有效且不能重复。");
            }
            var session = new BallSession { Main = main, Temporary = new PhysicalCollisionBall[count], AuthoredCount = authoredCount };
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var ball = i < authoredCount ? temporary[i] : main.CreateTemporary();
                    session.Temporary[i] = ball;
                    ball.gameObject.SetActive(false);
                    if (_rules != null) ball.Configure(_rules);
                    ball.BindBattle(_battleId, main.Side, _runningGate);
                    ball.BindRobotContacts(registry);
                    ball.RobotContactDetected += ForwardContact;
                }
            }
            catch
            {
                DetachSession(session);
                throw;
            }
            main.RobotContactDetected += ForwardContact;
            main.ComboThresholdReached += EnterBerserk;
            return session;
        }

        private void ForwardContact(PhysicalCollisionBall ball, int robotId)
        {
            if (ball != null && ball.BattleId == _battleId && _runningGate != null && _runningGate())
                RobotContactDetected?.Invoke(ball, robotId);
        }

        private void EnterBerserk(PhysicalCollisionBall main)
        {
            if (main == null || main.BattleId != _battleId || _runningGate == null || !_runningGate()) return;
            var session = Session(main.Side);
            if (session == null || session.Main != main || session.Berserk) return;
            session.Berserk = true;
            session.Remaining = _berserkDuration;
            try
            {
                var position = main.GetComponent<Rigidbody2D>().position;
                main.SetBerserk(true);
                BerserkChanged?.Invoke(main.Side, true);
                if (!session.Berserk || _runningGate == null || !_runningGate()) return;
                foreach (var ball in session.Temporary)
                {
                    ball.gameObject.SetActive(true);
                    ball.SetBerserk(true);
                }
                IgnoreSessionBalls(session);
                foreach (var ball in session.Temporary)
                {
                    ball.LaunchAt(position, ball.SampleLaunchAngle());
                    if (!ball.IsLaunched) throw new InvalidOperationException("临时暴走球发射失败。");
                }
                IgnoreSessionBalls(session);
            }
            catch (Exception e)
            {
                EndBerserk(session);
                Debug.LogError("暴走启动失败，已恢复普通状态：" + e.Message, this);
            }
        }

        private static void IgnoreSessionBalls(BallSession session)
        {
            var main = session.Main.GetComponent<Collider2D>();
            for (var i = 0; i < session.Temporary.Length; i++)
            {
                var current = session.Temporary[i].GetComponent<Collider2D>();
                Physics2D.IgnoreCollision(main, current, true);
                for (var j = 0; j < i; j++)
                    Physics2D.IgnoreCollision(current, session.Temporary[j].GetComponent<Collider2D>(), true);
            }
        }

        public void AdvanceBerserk(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (_runningGate == null || !_runningGate()) return;
            AdvanceSession(_player, deltaTime);
            AdvanceSession(_opponent, deltaTime);
        }

        private void AdvanceSession(BallSession session, float deltaTime)
        {
            if (session == null || !session.Berserk) return;
            if (!session.Main.IsLaunched) { EndBerserk(session); return; }
            foreach (var ball in session.Temporary)
                if (ball == null || !ball.IsLaunched) { EndBerserk(session); return; }
            session.Remaining = Mathf.Max(0f, session.Remaining - deltaTime);
            if (session.Remaining <= 0f) EndBerserk(session);
        }

        private void EndBerserk(BallSession session)
        {
            if (session == null) return;
            foreach (var ball in session.Temporary)
            {
                if (ball == null) continue;
                ball.Stop();
                ball.gameObject.SetActive(false);
            }
            var wasBerserk = session.Berserk;
            session.Berserk = false;
            session.Remaining = 0f;
            if (session.Main != null) session.Main.SetBerserk(false);
            if (wasBerserk && session.Main != null) BerserkChanged?.Invoke(session.Main.Side, false);
        }

        public void UnbindBattle()
        {
            StopBalls();
            DetachSession(_player);
            DetachSession(_opponent);
            _player = _opponent = null;
            _runningGate = null;
            _battleId = 0;
        }

        private void DetachSession(BallSession session)
        {
            if (session == null) return;
            if (session.Main != null)
            {
                session.Main.RobotContactDetected -= ForwardContact;
                session.Main.ComboThresholdReached -= EnterBerserk;
            }
            for (var i = 0; i < session.Temporary.Length; i++)
            {
                var ball = session.Temporary[i];
                if (ball == null) continue;
                ball.RobotContactDetected -= ForwardContact;
                if (i < session.AuthoredCount) continue;
                if (Application.isPlaying) Destroy(ball.gameObject);
                else DestroyImmediate(ball.gameObject);
            }
        }

        public bool LaunchBoth(PlacementBoardView player, PlacementBoardView opponent)
        {
            if (_launched) return true;
            if (_ball == null || _opponentBall == null) return false;
            var angle = _ball.SampleLaunchAngle();
            _ball.LaunchAt(player.transform.position, angle);
            _opponentBall.LaunchAt(opponent.transform.position, angle);
            _launched = _ball.IsLaunched && _opponentBall.IsLaunched;
            if (!_launched) StopBalls();
            return _launched;
        }

        public void SetFrozen(bool frozen)
        {
            if (_ball != null) _ball.SetFrozen(frozen);
            if (_opponentBall != null) _opponentBall.SetFrozen(frozen);
            FreezeTemporary(_player, frozen);
            FreezeTemporary(_opponent, frozen);
            if (!frozen)
            {
                if (_player?.Berserk == true) IgnoreSessionBalls(_player);
                if (_opponent?.Berserk == true) IgnoreSessionBalls(_opponent);
            }
        }

        private static void FreezeTemporary(BallSession session, bool frozen)
        {
            if (session == null) return;
            foreach (var ball in session.Temporary) if (ball != null) ball.SetFrozen(frozen);
        }

        public void StopBalls()
        {
            _launched = false;
            EndBerserk(_player);
            EndBerserk(_opponent);
            if (_ball != null) _ball.Stop();
            if (_opponentBall != null) _opponentBall.Stop();
        }

        private void OnDestroy() { UnbindBattle(); }

        public bool Initialize()
        {
            if (_ball == null || _opponentBall == null)
            {
                if (_controller != null) _controller.SuspendPlacement();
                Debug.LogError("棋盘启动器缺少小球引用，小球不会发射。", this);
                return false;
            }
            if (_controller == null || !_controller.Initialize())
            {
                Debug.LogError("棋盘尚未就绪，小球不会发射。修复引用后调用 Initialize 重试。", this);
                return false;
            }
            StopBalls();
            return true;
        }
    }
}
