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
        private bool _launched;

        public PhysicalCollisionBall PlayerBall => _ball;
        public PhysicalCollisionBall OpponentBall => _opponentBall;

        public void BindBattle(long battleId, System.Func<bool> runningGate)
        {
            _launched = false;
            _ball.BindBattle(battleId, BattleSide.Player, runningGate);
            _opponentBall.BindBattle(battleId, BattleSide.Opponent, runningGate);
        }

        public bool LaunchBoth(PlacementBoardView player, PlacementBoardView opponent)
        {
            if (_launched) return true;
            if (_ball == null || _opponentBall == null) return false;
            var angle = Random.Range(20f, 70f);
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
        }

        public void StopBalls()
        {
            _launched = false;
            if (_ball != null) _ball.Stop();
            if (_opponentBall != null) _opponentBall.Stop();
        }

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
