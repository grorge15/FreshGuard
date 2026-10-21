using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    // 等级与品质互相独立；品质数值暂不绑定未来 Luban 表的枚举。
    public enum BuffQuality { Blue, Purple, Gold }

    public sealed class BuffSelectOption
    {
        public int BuffId { get; }
        public int Level { get; }
        public BuffQuality Quality { get; }
        public Sprite Icon { get; }
        public string Name { get; }
        public string Description { get; }

        public BuffSelectOption(int buffId, int level, BuffQuality quality, string name,
            string description, Sprite icon = null)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (!Enum.IsDefined(typeof(BuffQuality), quality)) throw new ArgumentOutOfRangeException(nameof(quality));
            BuffId = buffId;
            Level = level;
            Quality = quality;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Icon = icon;
        }
    }

    public sealed class BuffSelectResult
    {
        public int Index { get; }
        public BuffSelectOption Option { get; }
        public bool IsAutomatic { get; }
        public BuffSelectResult(int index, BuffSelectOption option, bool isAutomatic)
        { Index = index; Option = option; IsAutomatic = isAutomatic; }
    }

    public sealed class BuffSelectRequest
    {
        public IReadOnlyList<BuffSelectOption> Options { get; }
        public float DurationSeconds { get; }
        public Action<BuffSelectResult> OnSelected { get; }

        public BuffSelectRequest(IReadOnlyList<BuffSelectOption> options,
            Action<BuffSelectResult> onSelected, float durationSeconds = 5f)
        {
            if (options == null || options.Count != 3)
                throw new ArgumentException("强化选择必须提供三个候选。", nameof(options));
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            var snapshot = new BuffSelectOption[3];
            for (var i = 0; i < snapshot.Length; i++)
                snapshot[i] = options[i] ?? throw new ArgumentException("候选不能为 null。", nameof(options));
            Options = Array.AsReadOnly(snapshot);
            DurationSeconds = durationSeconds;
            OnSelected = onSelected;
        }
    }
}
