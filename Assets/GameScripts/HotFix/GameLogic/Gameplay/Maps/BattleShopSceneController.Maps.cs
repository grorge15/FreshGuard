using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        [SerializeField, Tooltip("二进制地图的YooAsset地址，例如Map_Demo；留空沿用旧布局配置。更改在新局生效。")]
        private string _mapLocation = "";
        private BattleMapLayoutController _mapLayout;
        private bool _usingTerrainMap;
        public string MapLocation => _mapLocation;
        public bool UsesTerrainMap => _usingTerrainMap;

        private async UniTask<BoardLayoutRules> ReadBoardLayoutAsync(Tables tables, CancellationToken token)
        {
            _usingTerrainMap = !string.IsNullOrWhiteSpace(_mapLocation);
            if (!_usingTerrainMap) return new BoardLayoutRules(tables.TbBoardLayout.Get(_layoutConfigId));
            var map = await MapLoader.LoadAssetAsync(_mapLocation, token);
            token.ThrowIfCancellationRequested();
            if (_mapLayout == null) _mapLayout = GetComponent<BattleMapLayoutController>();
            if (_mapLayout == null) _mapLayout = gameObject.AddComponent<BattleMapLayoutController>();
            // 必须在ResetForBattle改变棋盘尺寸前捕获作者布局。
            _mapLayout.Configure(_playerBoard, _opponentBoard,
                _bootstrap.PlayerBall, _bootstrap.OpponentBall,
                _bootstrap.PlayerBerserkBalls, _bootstrap.OpponentBerserkBalls);
            return new BoardLayoutRules(map);
        }

        private void ApplyMapLayout()
        {
            if (_usingTerrainMap) _mapLayout.Apply();
        }

        private void ValidateMapSpawn()
        {
            if (!_usingTerrainMap) return;
            MapBattleValidation.RequireClearSpawn(_playerBoard, _bootstrap.PlayerBall);
            MapBattleValidation.RequireClearSpawn(_opponentBoard, _bootstrap.OpponentBall);
        }

        private void RestoreMapLayout()
        {
            _usingTerrainMap = false;
            if (_mapLayout != null) _mapLayout.Restore();
        }
    }
}
