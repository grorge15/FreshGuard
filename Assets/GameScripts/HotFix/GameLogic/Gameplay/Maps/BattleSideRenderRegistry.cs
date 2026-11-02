using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>内置管线的每局双相机隔离；只临时修改已登记 Renderer，不改变物理层或世界布局。</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BattleSideRenderRegistry : MonoBehaviour
    {
        private readonly Dictionary<BattleSideRenderGroup, Renderer[]> _groups =
            new Dictionary<BattleSideRenderGroup, Renderer[]>();
        private readonly Dictionary<Renderer, BattleSideRenderGroup> _owners =
            new Dictionary<Renderer, BattleSideRenderGroup>();
        private readonly Dictionary<Renderer, bool> _originalFlags = new Dictionary<Renderer, bool>();
        private readonly List<Camera> _renderStack = new List<Camera>();
        private readonly List<Renderer> _removed = new List<Renderer>();
        private Camera _playerCamera;
        private Camera _opponentCamera;
        private bool _subscribed;

        public void BindCameras(Camera player, Camera opponent)
        {
            if (player == null || opponent == null || player == opponent)
                throw new ArgumentException("双战场必须绑定不同的有效相机。");
            RestorePending();
            _playerCamera = player;
            _opponentCamera = opponent;
            Subscribe();
        }

        public void RegisterRoot(GameObject root, BattleSide side) => BattleSideRenderGroup.BindRoot(root, this, side);

        public void UnregisterRoot(GameObject root)
        {
            if (root == null) return;
            var group = root.GetComponent<BattleSideRenderGroup>();
            if (group != null && group.Registry == this) group.Unbind();
        }

        internal void Attach(BattleSideRenderGroup group) { Refresh(group); }

        internal void Refresh(BattleSideRenderGroup group)
        {
            _groups[group] = group.GetComponentsInChildren<Renderer>(true);
            RebuildOwners();
        }

        internal void Detach(BattleSideRenderGroup group)
        {
            _groups.Remove(group);
            RebuildOwners();
        }

        private void RebuildOwners()
        {
            _owners.Clear();
            foreach (var pair in _groups)
            {
                var group = pair.Key;
                if (group == null || !group.isActiveAndEnabled) continue;
                foreach (var renderer in pair.Value)
                {
                    if (renderer == null) continue;
                    // 独立子组优先；禁用的池对象也不能退回祖先组而携带临时隐藏状态。
                    var owner = FindOwner(renderer.transform);
                    if (owner == group) _owners[renderer] = group;
                }
            }
            _removed.Clear();
            foreach (var pair in _originalFlags)
                if (!_owners.ContainsKey(pair.Key))
                {
                    if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                    _removed.Add(pair.Key);
                }
            foreach (var renderer in _removed) _originalFlags.Remove(renderer);
            ApplyMask();
        }

        private static BattleSideRenderGroup FindOwner(Transform node)
        {
            while (node != null)
            {
                var group = node.GetComponent<BattleSideRenderGroup>();
                if (group != null && group.Registry != null) return group;
                node = node.parent;
            }
            return null;
        }

        private void OnEnable() { Subscribe(); }
        private void OnDisable() { Unsubscribe(); RestorePending(); }
        private void OnDestroy() { Clear(); }

        // 上一帧渲染被中断、未收到 postRender 时，不能把临时状态带入下一帧。
        private void LateUpdate() { if (_renderStack.Count > 0) RestorePending(); }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled || _playerCamera == null || _opponentCamera == null) return;
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPostRender += OnCameraPostRender;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPostRender -= OnCameraPostRender;
            _subscribed = false;
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (!isActiveAndEnabled || camera == null) return;
            if (camera != _playerCamera && camera != _opponentCamera && _renderStack.Count == 0) return;
            _renderStack.Add(camera);
            ApplyMask();
        }

        private void OnCameraPostRender(Camera camera)
        {
            var index = _renderStack.LastIndexOf(camera);
            if (index < 0) return;
            _renderStack.RemoveRange(index, _renderStack.Count - index);
            if (_renderStack.Count == 0) RestorePending();
            else ApplyMask();
        }

        private void ApplyMask()
        {
            if (_renderStack.Count == 0) return;
            var camera = _renderStack[_renderStack.Count - 1];
            var isSideCamera = camera == _playerCamera || camera == _opponentCamera;
            var side = camera == _playerCamera ? BattleSide.Player : BattleSide.Opponent;
            foreach (var pair in _owners)
            {
                if (pair.Key == null || pair.Value == null) continue;
                if (!_originalFlags.TryGetValue(pair.Key, out var original))
                {
                    original = pair.Key.forceRenderingOff;
                    _originalFlags.Add(pair.Key, original);
                }
                pair.Key.forceRenderingOff = original || isSideCamera && pair.Value.Side != side;
            }
        }

        private void RestorePending()
        {
            foreach (var pair in _originalFlags)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            _originalFlags.Clear();
            _renderStack.Clear();
        }

        public void UnbindCameras()
        {
            Unsubscribe();
            RestorePending();
            _playerCamera = _opponentCamera = null;
        }

        public void Clear()
        {
            UnbindCameras();
            var groups = new List<BattleSideRenderGroup>(_groups.Keys);
            foreach (var group in groups) if (group != null && group.Registry == this) group.Unbind();
            _groups.Clear();
            _owners.Clear();
        }
    }
}
