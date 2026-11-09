using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public interface IBattleEffectView
    {
        void Play(IReadOnlyList<Vector3> positions);
        bool Advance(float deltaTime);
        void Clear();
    }
}
