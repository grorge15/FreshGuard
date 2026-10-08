using UnityEngine;

namespace GameLogic
{
    /// <summary>先初始化两块棋盘及外框，再发射 Main 的现有小球。</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementBoardsBootstrap : MonoBehaviour
    {
        [SerializeField] private PlacementController _controller;
        [SerializeField] private PhysicalCollisionBall _ball;
        private bool _launched;

        private void Start() { Initialize(); }

        public bool Initialize()
        {
            if (_ball == null)
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
            if (!_launched)
            {
                _ball.Launch();
                var body = _ball.GetComponent<Rigidbody2D>();
                _launched = body != null && body.velocity.sqrMagnitude > 0f;
                if (!_launched)
                {
                    _controller.SuspendPlacement();
                    return false;
                }
            }
            return true;
        }
    }
}
