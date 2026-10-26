using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class BulletManager : IDisposable
    {
        private sealed class Entry
        {
            internal BulletState State;
            internal Action<BulletState> Present;
            internal Action Release;
        }
        private readonly List<Entry> _entries = new List<Entry>();
        public int Count => _entries.Count;
        public bool IsDisposed { get; private set; }
        public void Add(BulletState state, Action<BulletState> present, Action release)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(BulletManager));
            if (state == null || release == null) throw new ArgumentNullException(nameof(state));
            foreach (var entry in _entries) if (ReferenceEquals(entry.State, state))
                throw new InvalidOperationException("子弹已注册。");
            _entries.Add(new Entry { State = state, Present = present, Release = release });
        }
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsDisposed) return;
            foreach (var entry in _entries.ToArray())
            {
                if (IsDisposed) break;
                if (!_entries.Contains(entry)) continue;
                var ended = entry.State.Advance(deltaTime);
                if (IsDisposed) break;
                entry.Present?.Invoke(entry.State);
                if (ended && _entries.Remove(entry)) entry.Release();
            }
        }
        public void Clear()
        {
            var entries = _entries.ToArray(); _entries.Clear();
            Exception failure = null;
            foreach (var entry in entries)
            {
                entry.State.Cancel();
                try { entry.Release(); }
                catch (Exception exception) { if (failure == null) failure = exception; }
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        public void Dispose() { if (IsDisposed) return; IsDisposed = true; Clear(); }
    }
}
