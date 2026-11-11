using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>信息栏独立绘制在环境背景之后、战场之前，让传送带上的敌人可以覆盖信息栏。</summary>
    internal sealed class BattleInfoRenderLayer : IDisposable
    {
        private readonly RectTransform _info;
        private readonly RectTransform _originalParent;
        private readonly int _siblingIndex;
        private readonly Canvas _sourceCanvas;
        private readonly Camera _uiCamera;
        private readonly float _depth;
        private readonly Dictionary<GameObject, int> _originalLayers = new Dictionary<GameObject, int>();
        private readonly Canvas _canvas;
        private readonly GraphicRaycaster _raycaster;
        private readonly Camera _camera;
        private bool _disposed;

        public BattleInfoRenderLayer(RectTransform info, Canvas sourceCanvas, Camera uiCamera, Transform cameraParent, float depth)
        {
            var layer = LayerMask.NameToLayer("BattleInfo");
            if (info == null || sourceCanvas == null || uiCamera == null || layer < 0)
                throw new InvalidOperationException("信息栏绘制需要有效的界面、相机和 BattleInfo 显示层。");
            _info = info;
            _originalParent = info.parent as RectTransform;
            if (_originalParent == null) throw new InvalidOperationException("信息栏缺少界面父节点。");
            _siblingIndex = info.GetSiblingIndex();
            _sourceCanvas = sourceCanvas;
            _uiCamera = uiCamera;
            _depth = depth;
            _camera = new GameObject("BattleInfoCamera", typeof(Camera)).GetComponent<Camera>();
            _camera.enabled = false;
            _camera.transform.SetParent(cameraParent, false);
            _canvas = new GameObject("BattleInfoRenderCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            _raycaster = _canvas.GetComponent<GraphicRaycaster>();
            _canvas.gameObject.layer = layer;
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _camera;
            foreach (var child in info.GetComponentsInChildren<Transform>(true))
            {
                _originalLayers.Add(child.gameObject, child.gameObject.layer);
                child.gameObject.layer = layer;
            }
            Refresh(false);
            info.SetParent(_canvas.transform, false);
        }

        public void Refresh(bool visible)
        {
            if (_disposed || _originalParent == null || _sourceCanvas == null || _uiCamera == null) return;
            // 镜像原父节点的坐标系，保留原有信息栏布局计算和屏幕位置。
            var rect = (RectTransform)_canvas.transform;
            rect.pivot = _originalParent.pivot;
            rect.sizeDelta = _originalParent.rect.size;
            rect.SetPositionAndRotation(_originalParent.position, _originalParent.rotation);
            rect.localScale = _originalParent.lossyScale;
            _canvas.sortingLayerID = _sourceCanvas.sortingLayerID;
            _canvas.sortingOrder = _sourceCanvas.sortingOrder;
            _camera.CopyFrom(_uiCamera);
            _camera.transform.SetPositionAndRotation(_uiCamera.transform.position, _uiCamera.transform.rotation);
            _camera.cullingMask = 1 << _canvas.gameObject.layer;
            _camera.clearFlags = CameraClearFlags.Depth;
            _camera.depth = _depth;
            var enabled = visible && _originalParent.gameObject.activeInHierarchy;
            _canvas.enabled = enabled;
            _raycaster.enabled = enabled;
            _camera.enabled = enabled;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _camera.enabled = false;
            _canvas.enabled = false;
            _raycaster.enabled = false;
            foreach (var pair in _originalLayers)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            if (_info != null && _originalParent != null)
            {
                _info.SetParent(_originalParent, false);
                _info.SetSiblingIndex(_siblingIndex);
            }
            Destroy(_canvas.gameObject);
            Destroy(_camera.gameObject);
        }

        private static void Destroy(GameObject value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
