using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只控制显示相机与窗口区域，保留世界玩法和物理布局。</summary>
    [DisallowMultipleComponent]
    public sealed partial class BattleScreenLayoutController : MonoBehaviour
    {
        [SerializeField] private PlacementController _placement;
        [SerializeField] private PlacementBoardView _playerBoard;
        [SerializeField] private PlacementBoardView _opponentBoard;
        [SerializeField] private SpriteRenderer[] _playerFixedRenderers;
        [SerializeField] private SpriteRenderer[] _opponentFixedRenderers;
        private Camera _sceneCamera;
        private MainCombatUI _window;
        private Bounds _playerBounds;
        private Bounds _opponentBounds;
        private Bounds _combinedBounds;
        private Rect _availableScreenRect;
        private Vector3 _originalPosition;
        private float _originalSize;
        private Rect _originalRect;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;
        private Rect _lastShopRect;
        private float _lastCanvasScale;
        private bool _hasLayout;
        private int _bindingGeneration;

        public bool IsBound => _window != null;
        public Rect AvailableScreenRect => _availableScreenRect;
        public Bounds PlayerBounds => _playerBounds;
        public Bounds OpponentBounds => _opponentBounds;

        public async UniTask BindAsync(MainCombatUI window, CancellationToken token)
        {
            // 同局重绑保留已创建动态对象的所属方，只替换相机和UI绑定。
            Unbind(false);
            if (window == null || window.IsDestroyed || _placement == null ||
                _playerBoard == null || _opponentBoard == null || !_playerBoard.IsInitialized || !_opponentBoard.IsInitialized)
            {
                if (_renderRegistry != null) _renderRegistry.Clear();
                throw new InvalidOperationException("屏幕适配缺少就绪的窗口、棋盘或摆放控制器。");
            }
            _sceneCamera = _placement.SceneCamera;
            if (_sceneCamera == null || !_sceneCamera.orthographic ||
                Quaternion.Angle(_sceneCamera.transform.rotation, Quaternion.identity) > 0.01f)
            {
                if (_renderRegistry != null) _renderRegistry.Clear();
                throw new InvalidOperationException("屏幕适配需要正对世界XY平面的正交相机。");
            }
            _originalPosition = _sceneCamera.transform.position;
            _originalSize = _sceneCamera.orthographicSize;
            _originalRect = _sceneCamera.rect;
            _cameraState = new CameraState(_sceneCamera);
            var generation = ++_bindingGeneration;
            try
            {
                _playerBounds = CalculateBounds(_playerBoard, _playerFixedRenderers);
                _opponentBounds = CalculateBounds(_opponentBoard, _opponentFixedRenderers);
                _combinedBounds = _playerBounds;
                _combinedBounds.Encapsulate(_opponentBounds);
                _window = window;
                window.AttachScreenLayout(this);
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
                token.ThrowIfCancellationRequested();
                if (generation != _bindingGeneration || _window != window || window.IsDestroyed || window.gameObject == null)
                    throw new OperationCanceledException(token);
                Canvas.ForceUpdateCanvases();
                if (!BattleScreenLayout.TryCalculateReference(_playerBounds, _opponentBounds, window.ShopDesignSize,
                    out _referencePixelsPerUnit, out _referenceInfoScale))
                    throw new InvalidOperationException("无法从16:9战场和Prefab基准计算参考显示比例。");
                if (!RefreshLayout()) throw new InvalidOperationException("屏幕适配没有可容纳战场与信息栏的有效区域。");
            }
            catch
            {
                if (generation == _bindingGeneration) Unbind();
                throw;
            }
        }

        private void LateUpdate()
        {
            if (_window == null) return;
            if (_window.IsDestroyed || _window.gameObject == null) { Unbind(); return; }
            if (!_hasLayout) return;
            if (_lastScreenSize == new Vector2Int(Screen.width, Screen.height) && _lastSafeArea == Screen.safeArea &&
                Mathf.Approximately(_lastCanvasScale, _window.ScreenLayoutScale) && _lastShopRect == _window.GetShopScreenRect() &&
                _lastInfoRect == _window.GetInfoScreenRect()) return;
            RefreshLayout();
        }

        public bool RefreshLayout()
        {
            return RefreshSplitLayout();
        }

        public void Unbind() => Unbind(true);

        private void Unbind(bool clearRenderGroups)
        {
            ++_bindingGeneration;
            var window = _window;
            _window = null;
            if (_renderRegistry != null)
            {
                if (clearRenderGroups) _renderRegistry.Clear();
                else _renderRegistry.UnbindCameras();
            }
            if (window != null)
            {
                _placement?.CancelDrag();
                if (_placement != null) _placement.ClearScreenViews();
                DestroyCamera(ref _opponentCamera);
                DestroyCamera(ref _backgroundCamera);
                _backgroundRenderer = null;
                if (_sceneCamera != null)
                {
                    _sceneCamera.transform.position = _originalPosition;
                    _sceneCamera.orthographicSize = _originalSize;
                    _sceneCamera.rect = _originalRect;
                    _cameraState.Restore(_sceneCamera);
                }
                window.DetachScreenLayout(this);
            }
            _hasLayout = false;
            _sceneCamera = null;
            _availableScreenRect = default;
            _layout = default;
        }

        private static Bounds CalculateBounds(PlacementBoardView board, SpriteRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                throw new InvalidOperationException("请绑定战场轨道、传送门、塔身和血条背景。");
            var result = new Bounds(board.transform.position, Vector3.zero);
            var half = board.Size * 0.5f;
            for (var i = 0; i < 4; i++)
                result.Encapsulate(board.transform.TransformPoint(new Vector3((i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y, 0)));
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sprite == null) throw new InvalidOperationException("战场固定显示对象缺少 Sprite。");
                var local = renderer.sprite.bounds;
                for (var i = 0; i < 8; i++)
                    result.Encapsulate(renderer.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z)));
            }
            return result;
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();
    }
}
