using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>一局共用提交队列；跨容器请求也等待当前回调结束。独立容器使用自己的队列。</summary>
    internal sealed class BuffMutationQueue
    {
        private readonly Queue<Action> _pending = new Queue<Action>();
        private int _dispatchDepth;
        private bool _flushing;
        internal void Mutate(Action action)
        {
            if (_dispatchDepth > 0 || _flushing) { _pending.Enqueue(action); return; }
            action(); Flush();
        }
        internal void Dispatch(Action action)
        {
            _dispatchDepth++;
            try { action(); }
            finally { _dispatchDepth--; if (_dispatchDepth == 0) Flush(); }
        }
        private void Flush()
        {
            if (_flushing || _dispatchDepth > 0) return;
            _flushing = true;
            try { while (_pending.Count > 0) _pending.Dequeue()(); }
            finally { _flushing = false; }
        }
    }
}
