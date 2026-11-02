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
        private readonly HashSet<Renderer> _affected = new HashSet<Renderer>();
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
            _affected.Clear();
            if (_groups.TryGetValue(group, out var previous)) CollectAffected(previous);
            var current = group.GetComponentsInChildren<Renderer>(true);
            _groups[group] = current;
            CollectAffected(current);
            RefreshAffectedOwners();
        }

        internal void Detach(BattleSideRenderGroup group)
        {
            if (!_groups.TryGetValue(group, out var previous)) return;
            _groups.Remove(group);
            _affected.Clear();
            CollectAffected(previous);
            RefreshAffectedOwners();
        }

        private void CollectAffected(Renderer[] renderers)
        {
            foreach (var renderer in renderers) _affected.Add(renderer);
        }

        private void RefreshAffectedOwners()
        {
            // 启用/回收一个动态对象只更新其旧、新子树的并集，不能重扫整张静态棋盘。
            foreach (var renderer in _affected)
            {
                var owner = renderer != null ? FindOwner(renderer.transform) : null;
                // 最近的独立子组优先；禁用子组不能回退到祖先。解绑后则允许归还祖先。
                if (owner != null && owner.Registry == this && owner.isActiveAndEnabled && _groups.ContainsKey(owner))
                {
                    _owners[renderer] = owner;
                    ApplyMask(renderer, owner);
                }
                else
                {
                    _owners.Remove(renderer);
                    if (_originalFlags.TryGetValue(renderer, out var original))
                    {
                        if (renderer != null) renderer.forceRenderingOff = original;
                        _originalFlags.Remove(renderer);
                    }
                }
            }
            _affected.Clear();
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
            foreach (var pair in _owners) ApplyMask(pair.Key, pair.Value);
        }

        private void ApplyMask(Renderer renderer, BattleSideRenderGroup owner)
        {
            if (_renderStack.Count == 0 || renderer == null || owner == null) return;
            var camera = _renderStack[_renderStack.Count - 1];
            var isSideCamera = camera == _playerCamera || camera == _opponentCamera;
            var side = camera == _playerCamera ? BattleSide.Player : BattleSide.Opponent;
            if (!_originalFlags.TryGetValue(renderer, out var original))
            {
                original = renderer.forceRenderingOff;
                _originalFlags.Add(renderer, original);
            }
            renderer.forceRenderingOff = original || isSideCamera && owner.Side != side;
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
            // 已恢复全部临时标志，批量清理无需为每组反复寻找祖先归属。
            _groups.Clear();
            _owners.Clear();
            _affected.Clear();
            foreach (var group in groups) if (group != null && group.Registry == this) group.Unbind();
        }
    }
}
