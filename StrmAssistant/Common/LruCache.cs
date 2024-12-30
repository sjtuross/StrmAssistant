using System;
using System.Collections.Generic;
using System.Threading;

namespace StrmAssistant.Common
{
    public class LruCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, object>>> _cacheMap;
        private readonly LinkedList<KeyValuePair<string, object>> _orderList;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        public LruCache(int capacity = 20)
        {
            _capacity = capacity;
            _cacheMap = new Dictionary<string, LinkedListNode<KeyValuePair<string, object>>>(capacity,
                StringComparer.OrdinalIgnoreCase);
            _orderList = new LinkedList<KeyValuePair<string, object>>();
        }

        public void AddOrUpdateCache<T>(string key, T value)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_cacheMap.TryGetValue(key, out var existingNode))
                {
                    _orderList.Remove(existingNode);
                }
                else if (_cacheMap.Count >= _capacity)
                {
                    var leastUsed = _orderList.Last;
                    _orderList.RemoveLast();
                    _cacheMap.Remove(leastUsed.Value.Key);
                }

                var newNode =
                    new LinkedListNode<KeyValuePair<string, object>>(new KeyValuePair<string, object>(key, value));
                _orderList.AddFirst(newNode);
                _cacheMap[key] = newNode;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool TryGetFromCache<T>(string key, out T value) where T : class
        {
            _lock.EnterWriteLock();
            try
            {
                value = default;
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    _orderList.Remove(node);
                    _orderList.AddFirst(node);

                    value = node.Value.Value as T;
                    return true;
                }

                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}
