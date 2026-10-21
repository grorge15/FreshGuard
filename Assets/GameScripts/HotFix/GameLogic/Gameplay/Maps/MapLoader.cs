using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public static class MapLoader
    {
        public static MapData Deserialize(byte[] bytes) => MapBinarySerializer.Deserialize(bytes);

        public static async UniTask<MapData> LoadAssetAsync(string location, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("地图资源地址不能为空。", nameof(location));
            token.ThrowIfCancellationRequested();
            if (!GameModule.Resource.CheckLocationValid(location)) throw new InvalidDataException("地图未加入资源收集：" + location);
            TextAsset asset = null;
            try
            {
                asset = await GameModule.Resource.LoadAssetAsync<TextAsset>(location, cancellationToken: token);
                token.ThrowIfCancellationRequested();
                if (asset == null) throw new InvalidDataException("地图资源为空：" + location);
                return Deserialize(asset.bytes);
            }
            finally { if (asset != null) GameModule.Resource.UnloadAsset(asset); }
        }

        public static async UniTask<MapData> LoadFileAsync(string path, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("地图路径不能为空。", nameof(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                if (stream.Length < MapBinarySerializer.HEADER_SIZE || stream.Length > MapBinarySerializer.HEADER_SIZE + MapData.MAX_DIMENSION * MapData.MAX_DIMENSION)
                    throw new InvalidDataException("地图文件大小无效。");
                var bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token);
                    if (read == 0) throw new EndOfStreamException("读取地图时文件被截断。");
                    offset += read;
                }
                token.ThrowIfCancellationRequested();
                return Deserialize(bytes);
            }
        }
    }
}
