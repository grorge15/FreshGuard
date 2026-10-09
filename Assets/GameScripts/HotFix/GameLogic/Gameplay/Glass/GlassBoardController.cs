using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    [DisallowMultipleComponent]
    public sealed class GlassBoardController : MonoBehaviour
    {
        private readonly List<GlassView> _views = new List<GlassView>();
        private readonly Dictionary<BoardCoordinate, GlassView> _byCell = new Dictionary<BoardCoordinate, GlassView>();
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private PlacementBoardView _board;
        private BattleSide _side;
        private bool _prepared;
        public BattleSide Side => _side;
        public long BattleId => _context != null ? _context.BattleId : 0;
        public int LiveCount => _byCell.Count;
        public bool IsRunning => _prepared && _host != null && _host.IsCombatRunning && _host.Context == _context;

        public async UniTask PrepareAsync(BattleShopSceneController host, BattleShopContext context,
            PlacementBoardView board, BattleSide side, BoardLayoutRules layout, Tables tables, CancellationToken token)
        {
            if (_prepared && _context == context) return;
            if (_views.Count != 0) throw new InvalidOperationException("旧局玻璃尚未清理。");
            _host = host;
            _context = context;
            _board = board;
            _side = side;
            var interval = tables.TbGlobal.Get(9).Value;
            GameObject pending = null;
            try
            {
                for (var column = 0; column < layout.Columns; column++)
                    for (var row = 0; row < layout.Rows; row++)
                    {
                        var cell = new BoardCoordinate(column, row);
                        if (!layout.HasInitialGlass(cell)) continue;
                        var config = tables.TbGlass.Get(cell.Equals(layout.ColoredGlassCell) ? layout.ColoredGlassId : layout.NormalGlassId);
                        pending = await GameModule.Resource.LoadGameObjectAsync(config.PrefabLocation, transform, token);
                        token.ThrowIfCancellationRequested();
                        if (_context != context || host == null || host.Context != context || context.IsEnded)
                            throw new OperationCanceledException();
                        var view = pending != null ? pending.GetComponent<GlassView>() : null;
                        if (view == null) throw new InvalidOperationException("玻璃Prefab缺少GlassView：" + config.Id);
                        pending.SetActive(false);
                        var entity = context.Registry.CreateGlassFromConfig(config, side, cell, interval);
                        _views.Add(view);
                        _byCell.Add(cell, view);
                        pending = null;
                        view.transform.localPosition = board.GetCellLocalPosition(cell);
                        view.transform.localRotation = Quaternion.identity;
                        view.transform.localScale = Vector3.one;
                        view.name = "Glass_" + column + "_" + row;
                        view.Bind(this, entity, config, layout.CellSize);
                        view.gameObject.SetActive(true);
                    }
                _prepared = true;
            }
            catch
            {
                if (pending != null) Destroy(pending);
                if (_context == context) Clear();
                throw;
            }
        }

        public bool TryGetView(BoardCoordinate cell, out GlassView view) => _byCell.TryGetValue(cell, out view);

        public GlassCollisionResult ApplyCollision(GlassView view, PhysicalCollisionBall ball)
        {
            if (!IsRunning || view == null || ball == null || ball.Side != _side || ball.BattleId != BattleId || view.Owner != this)
                return GlassCollisionResult.Ignored;
            var result = _context.Registry.ApplyGlassCollision(view.InstanceId, ball.IsBerserk, out var snapshot);
            if (result == GlassCollisionResult.Damaged)
            {
                if (_context.Registry.TryGet(view.InstanceId, out var entity))
                    view.ShowDurability(((GlassEntity)entity).CurrentDurability);
            }
            else if (result == GlassCollisionResult.Broken)
            {
                // 先提交通行和部署事实，奖励通知可能同步结束整局。
                view.BeginBreak();
                _byCell.Remove(view.Coordinate);
                _board.Model.SetOpen(view.Coordinate, true);
                _board.RefreshAllCells();
                var source = snapshot.GlassKind == GlassKind.Normal ? ShopRewardSource.NormalGlass : ShopRewardSource.ColoredGlass;
                GameEvent.Send<BattleRewardEvent>(BattleRewardAdapter.REWARD_EVENT,
                    new BattleRewardEvent(BattleId, snapshot.Side, source, snapshot.InstanceId));
            }
            return result;
        }

        public void Advance(float deltaTime)
        {
            for (var i = _views.Count - 1; i >= 0; i--)
            {
                var view = _views[i];
                if (view == null || view.Advance(deltaTime))
                {
                    if (view != null) Destroy(view.gameObject);
                    _views.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            _prepared = false;
            foreach (var view in _views)
            {
                if (view == null) continue;
                view.BeginBreak();
                if (_context != null && !_context.IsEnded)
                    _context.Registry.Remove(view.InstanceId, EntityRemovalReason.Manual, out _);
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
            _views.Clear();
            _byCell.Clear();
            _context = null;
            _host = null;
        }

        private void OnDestroy() { Clear(); }
    }
}
