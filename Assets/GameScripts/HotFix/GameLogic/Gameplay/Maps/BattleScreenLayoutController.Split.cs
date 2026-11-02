using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleScreenLayoutController
    {
        [SerializeField] private Transform _playerWorldRoot;
        [SerializeField] private Transform _opponentWorldRoot;
        [SerializeField] private BattleSideRenderRegistry _renderRegistry;
        [SerializeField, Range(0.01f, 1f)] private float _opponentMaxSizeRatio = 1f;
        [SerializeField] private Color _playerClearColor = new Color32(33, 174, 216, 255);
        [SerializeField] private Color _opponentClearColor = new Color32(234, 178, 79, 255);
        private Camera _opponentCamera;
        private Camera _backgroundCamera;
        private CameraState _cameraState;
        private BattleScreenLayoutResult _layout;
        private Rect _lastInfoRect;
        private float _referencePixelsPerUnit;
        private float _referenceInfoScale;

        public Rect PlayerScreenRect => _layout.PlayerArea;
        public Rect OpponentScreenRect => _layout.OpponentArea;
        public Camera OpponentCamera => _opponentCamera;
        public BattleScreenLayoutResult Layout => _layout;

        private bool RefreshSplitLayout()
        {
            if (_window == null) return false;
            var window = _window;
            var generation = _bindingGeneration;
            var screen = new Vector2(Screen.width, Screen.height);
            var safe = BattleScreenLayout.Intersect(new Rect(Vector2.zero, screen), Screen.safeArea);
            Rect shop;
            Vector3 shopPosition, shopScale, infoPosition, infoScale;
            BattleScreenLayoutResult candidate;
            Vector2 playerCenter, opponentCenter;
            float playerSize, opponentSize;
            if (screen.x <= 0 || screen.y <= 0 || !window.TryCalculateShopLayout(safe, out shop, out shopPosition, out shopScale) ||
                !BattleScreenLayout.TryCalculateSplit(screen, safe, shop, window.ScreenLayoutScale, _playerBounds, _opponentBounds,
                    _referencePixelsPerUnit, _referenceInfoScale, _opponentMaxSizeRatio, out candidate) ||
                !window.TryCalculateInfoLayout(candidate.InfoTarget, out infoPosition, out infoScale) ||
                !BattleScreenLayout.TryCalculateCamera(candidate.PlayerViewport, candidate.PlayerTarget, _playerBounds,
                    candidate.PlayerPixelsPerUnit, out playerCenter, out playerSize) ||
                !BattleScreenLayout.TryCalculateCamera(candidate.OpponentViewport, candidate.OpponentTarget, _opponentBounds,
                    candidate.OpponentPixelsPerUnit, out opponentCenter, out opponentSize))
            {
                _placement.InvalidateScreenViews();
                window.Shop.Drag?.Cancel();
                return false;
            }
            window.Shop.Drag?.Cancel();
            // 事务取消通知可以同步终局或重绑；所有计算通过后也必须再次验证归属。
            if (_window != window || generation != _bindingGeneration || window.IsDestroyed || window.gameObject == null) return false;
            _placement.CancelDrag();
            EnsureCameras();
            window.ApplyScreenLayout(shopPosition, shopScale, infoPosition, infoScale);
            ApplyCamera(_sceneCamera, candidate.PlayerViewport, screen, playerCenter, playerSize);
            ApplyCamera(_opponentCamera, candidate.OpponentViewport, screen, opponentCenter, opponentSize);
            _sceneCamera.backgroundColor = _playerClearColor;
            _opponentCamera.backgroundColor = _opponentClearColor;
            _layout = candidate;
            _availableScreenRect = Rect.MinMaxRect(candidate.PlayerArea.xMin, candidate.PlayerArea.yMin,
                candidate.OpponentArea.xMax, candidate.OpponentArea.yMax);
            _placement.SetScreenViews(_sceneCamera, candidate.PlayerArea, _opponentCamera, candidate.OpponentArea);
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;
            _lastCanvasScale = window.ScreenLayoutScale;
            _lastShopRect = window.GetShopScreenRect();
            _lastInfoRect = window.GetInfoScreenRect();
            _hasLayout = true;
            return true;
        }

        private void EnsureCameras()
        {
            if (_opponentCamera != null) return;
            _opponentCamera = CreateCamera("OpponentBattleCamera");
            _backgroundCamera = CreateCamera("BattleBackgroundCamera");
            var ui = _window.Canvas.rootCanvas.worldCamera;
            var depth = ui != null ? Mathf.Min(_cameraState.Depth, ui.depth - 2f) : _cameraState.Depth;
            _backgroundCamera.rect = new Rect(0, 0, 1, 1);
            _backgroundCamera.cullingMask = 0;
            _backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            _backgroundCamera.backgroundColor = _cameraState.Background;
            _backgroundCamera.depth = depth - 1f;
            _sceneCamera.depth = depth;
            _opponentCamera.depth = depth + 0.1f;
            _sceneCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
            _opponentCamera.cullingMask = _sceneCamera.cullingMask;
            _sceneCamera.clearFlags = _opponentCamera.clearFlags = CameraClearFlags.SolidColor;
            if (_renderRegistry == null) _renderRegistry = GetComponent<BattleSideRenderRegistry>();
            if (_renderRegistry == null) _renderRegistry = gameObject.AddComponent<BattleSideRenderRegistry>();
            _renderRegistry.BindCameras(_sceneCamera, _opponentCamera);
            RegisterStatic(_playerWorldRoot, _playerBoard, _playerFixedRenderers, BattleSide.Player);
            RegisterStatic(_opponentWorldRoot, _opponentBoard, _opponentFixedRenderers, BattleSide.Opponent);
            _backgroundCamera.enabled = true;
            _opponentCamera.enabled = true;
        }

        private Camera CreateCamera(string cameraName)
        {
            var root = new GameObject(cameraName);
            root.transform.SetParent(transform, false);
            var camera = root.AddComponent<Camera>();
            camera.CopyFrom(_sceneCamera);
            camera.enabled = false;
            return camera;
        }

        private void RegisterStatic(Transform root, PlacementBoardView board, SpriteRenderer[] renderers, BattleSide side)
        {
            if (root != null) _renderRegistry.RegisterRoot(root.gameObject, side);
            _renderRegistry.RegisterRoot(board.gameObject, side);
            foreach (var renderer in renderers) _renderRegistry.RegisterRoot(renderer.gameObject, side);
        }

        private void ApplyCamera(Camera camera, Rect viewport, Vector2 screen, Vector2 center, float size)
        {
            camera.rect = new Rect(viewport.x / screen.x, viewport.y / screen.y, viewport.width / screen.x, viewport.height / screen.y);
            camera.orthographicSize = size;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, _originalPosition.z), Quaternion.identity);
        }

        private static void DestroyCamera(ref Camera camera)
        {
            if (camera == null) return;
            camera.enabled = false;
            if (Application.isPlaying) Destroy(camera.gameObject);
            else DestroyImmediate(camera.gameObject);
            camera = null;
        }

        private readonly struct CameraState
        {
            public readonly Color Background;
            public readonly float Depth;
            private readonly CameraClearFlags _clear;
            private readonly int _mask;

            public CameraState(Camera camera)
            {
                Background = camera.backgroundColor;
                Depth = camera.depth;
                _clear = camera.clearFlags;
                _mask = camera.cullingMask;
            }
            public void Restore(Camera camera)
            {
                camera.backgroundColor = Background;
                camera.depth = Depth;
                camera.clearFlags = _clear;
                camera.cullingMask = _mask;
            }
        }
    }
}
