using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    internal sealed class BasicBulletTrail
    {
        // B preview measured from its attachment point, in projectile lengths.
        private const float Length = 2.65f * 0.90f;
        private readonly SpriteRenderer _sprite;
        private readonly LineRenderer _amber, _core;
        private readonly List<Vector3> _path = new List<Vector3>(32);
        private float _unit;
        private bool _active;

        internal BasicBulletTrail(SpriteRenderer sprite)
        {
            _sprite = sprite;
            _amber = Ribbon("AmberTrail", 2, new Color32(255,188,43,255), new Color32(225,111,36,255));
            _core = Ribbon("GoldCore", 1, new Color32(255,240,148,255), new Color32(255,192,46,255));
        }

        private LineRenderer Ribbon(string name, int behind, Color start, Color end)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.gameObject.layer = _sprite.gameObject.layer;
            line.transform.SetParent(_sprite.transform, false);
            line.useWorldSpace = true;
            line.sharedMaterial = _sprite.sharedMaterial;
            line.sortingLayerID = _sprite.sortingLayerID;
            line.sortingOrder = _sprite.sortingOrder - behind;
            line.numCornerVertices = 3;
            line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            line.colorGradient = gradient;
            line.positionCount = 0;
            return line;
        }

        internal void Reset(bool active)
        {
            _active = active;
            _path.Clear();
            _amber.positionCount = _core.positionCount = 0;
            _unit = _sprite.sprite.bounds.size.x * Mathf.Abs(_sprite.transform.lossyScale.x);
            _amber.widthMultiplier = _unit * 0.44f;
            _core.widthMultiplier = _amber.widthMultiplier * 0.38f;
        }

        internal void Record(Vector3 position)
        {
            if (!_active || _unit <= 0f) return;
            if (_path.Count > 0 && (position - _path[_path.Count - 1]).sqrMagnitude < 1e-10f) return;
            _path.Add(position);
            float start = _unit * 0.24f, end = start + _unit * Length;
            float distance = 0f;
            // Clip by distance along the travelled curve, independent of frame rate and bullet speed.
            _amber.positionCount = _core.positionCount = _path.Count;
            int count = 0;
            for (int i = _path.Count - 1; i > 0; i--)
            {
                var from = _path[i];
                var to = _path[i - 1];
                float step = Vector3.Distance(from, to);
                if (distance + step > start)
                {
                    if (count == 0) Point(count++, Vector3.Lerp(from, to, (start - distance) / step));
                    Point(count++, Vector3.Lerp(from, to, (end - distance) / step));
                    if (distance + step >= end)
                    {
                        if (i > 1) _path.RemoveRange(0, i - 1);
                        break;
                    }
                }
                distance += step;
            }
            _amber.positionCount = _core.positionCount = count;
        }

        private void Point(int index, Vector3 value)
        {
            _amber.SetPosition(index, value);
            _core.SetPosition(index, value);
        }
    }
}
