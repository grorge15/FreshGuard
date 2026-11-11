using System;
using System.IO;
using YooAsset.Editor;

namespace FreshGuard.Editor
{
    /// <summary>序列帧、效果动画和物理图片按路径寻址，其他 Actor 资源保留原文件名地址。</summary>
    public sealed class AddressByActorAsset : IAddressRule
    {
        private const string ACTOR_ROOT = "Assets/AssetRaw/Actor/";

        public string GetAssetAddress(AddressRuleData data)
        {
            var path = data.AssetPath.Replace('\\', '/');
            if (path.StartsWith(ACTOR_ROOT + "Sprites/Enemy/Frame/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Sprites/Bullets/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Sprites/Robots/Frames/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Animations/Effects/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Animations/Enemy/", StringComparison.Ordinal) ||
                path.StartsWith(ACTOR_ROOT + "Sprites/PhysicsSprites/", StringComparison.Ordinal))
                return path.Substring(ACTOR_ROOT.Length);
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
