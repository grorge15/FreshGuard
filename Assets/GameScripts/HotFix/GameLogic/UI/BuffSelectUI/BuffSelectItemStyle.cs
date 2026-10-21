using UnityEngine;

namespace GameLogic
{
    /// <summary>卡片的美术引用，可在 Prefab Inspector 中替换。</summary>
    [DisallowMultipleComponent]
    public sealed class BuffSelectItemStyle : MonoBehaviour
    {
        [SerializeField] private Sprite _blueBackground;
        [SerializeField] private Sprite _purpleBackground;
        [SerializeField] private Sprite _goldBackground;

        public Sprite BackgroundFor(BuffQuality quality)
        {
            switch (quality)
            {
                case BuffQuality.Gold: return _goldBackground;
                case BuffQuality.Purple: return _purpleBackground;
                default: return _blueBackground;
            }
        }
    }
}
