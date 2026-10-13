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
        private BallSession _player;
        private BallSession _opponent;

        private sealed class BallSession
        {
            public PhysicalCollisionBall Main;
            public PhysicalCollisionBall[] Temporary;
            public bool Berserk;
            public float Remaining;
        }

        public event Action<PhysicalCollisionBall, int> RobotContactDetected;
        public event Action<BattleSide, bool> BerserkChanged;

        public PhysicalCollisionBall PlayerBall => _ball;
        public PhysicalCollisionBall OpponentBall => _opponentBall;
        public bool IsBerserk(BattleSide side) => Session(side)?.Berserk == true;
        public float BerserkRemaining(BattleSide side) => Session(side)?.Remaining ?? 0f;
        private BallSession Session(BattleSide side) => side == BattleSide.Player ? _player : _opponent;

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
            _berserkDuration = _ball.GlobalValueProvider(41);
            if (float.IsNaN(_berserkDuration) || float.IsInfinity(_berserkDuration) || _berserkDuration <= 0f)
                throw new InvalidOperationException("Global41 暴走时间必须为有限正数。");
            _player = BindSession(_ball, _playerBerserkBalls, registry);
            _opponent = BindSession(_opponentBall, _opponentBerserkBalls, registry);
        }

        private BallSession BindSession(PhysicalCollisionBall main, PhysicalCollisionBall[] temporary, BattleEntityRegistry registry)
        {
            if (temporary == null || temporary.Length != 2 || temporary[0] == null || temporary[1] == null ||
                temporary[0] == temporary[1] || temporary[0] == main || temporary[1] == main)
                throw new InvalidOperationException("每方必须装配两颗不同的临时暴走球。");
            var session = new BallSession { Main = main, Temporary = temporary };
            foreach (var ball in temporary)
            {
                ball.gameObject.SetActive(false);
                ball.BindBattle(_battleId, main.Side, _runningGate);
                ball.BindRobotContacts(registry);
                ball.RobotContactDetected += ForwardContact;
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
                    ball.LaunchAt(position, UnityEngine.Random.Range(0f, 360f));
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
            var first = session.Main.GetComponent<Collider2D>();
            var second = session.Temporary[0].GetComponent<Collider2D>();
            var third = session.Temporary[1].GetComponent<Collider2D>();
            Physics2D.IgnoreCollision(first, second, true);
            Physics2D.IgnoreCollision(first, third, true);
            Physics2D.IgnoreCollision(second, third, true);
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
            if (!session.Main.IsLaunched || session.Temporary[0] == null || session.Temporary[1] == null ||
                !session.Temporary[0].IsLaunched || !session.Temporary[1].IsLaunched)
            { EndBerserk(session); return; }
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
            foreach (var ball in session.Temporary) if (ball != null) ball.RobotContactDetected -= ForwardContact;
        }

        public bool LaunchBoth(PlacementBoardView player, PlacementBoardView opponent)
        {
            if (_launched) return true;
            if (_ball == null || _opponentBall == null) return false;
            var angle = UnityEngine.Random.Range(20f, 70f);
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
