using System;
using System.IO;
using YooAsset.Editor;

namespace FreshGuard.Editor
{
    /// <summary>敌人帧和动画按路径寻址，其他 Actor 资源保留原文件名地址。</summary>
    public sealed class AddressByActorAsset : IAddressRule
    {
        private const string ACTOR_ROOT = "Assets/AssetRaw/Actor/";

        public string GetAssetAddress(AddressRuleData data)
        {
            var path = data.AssetPath.Replace('\\', '/');
            if (path.StartsWith(ACTOR_ROOT + "Enemy/Frame/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Enemy/Animations/", StringComparison.Ordinal))
                return path.Substring(ACTOR_ROOT.Length);
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
