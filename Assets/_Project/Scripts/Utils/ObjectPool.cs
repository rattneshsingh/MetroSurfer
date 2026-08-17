using UnityEngine;

namespace Runner.Utils
{
    /// <summary>
    /// Pre-warmed component pool with no allocation on the Get/Release path.
    /// </summary>
    /// <remarks>
    /// Callers keep the integer handle returned by <see cref="Get"/> and hand it back to
    /// <see cref="Release"/>. That is deliberately less convenient than Release(instance),
    /// but the alternative is a Dictionary keyed on a UnityEngine.Object — whose default
    /// comparer routes through Unity's overridden Equals — or a GetComponent on every
    /// despawn. Callers already store their spawned objects in arrays, so a parallel int
    /// array is free. Perf wins over ergonomics here.
    /// </remarks>
    public sealed class ObjectPool<T> where T : Component
    {
        private readonly System.Func<T> _factory;
        private readonly Transform _parent;
        private readonly string _label;

        private T[] _instances;
        private IPoolable[] _poolables;
        private int[] _freeStack;
        private bool[] _isFree;
        private int _freeCount;

        private bool _warnedExhausted;

        public int Capacity => _instances.Length;
        public int ActiveCount => _instances.Length - _freeCount;

        /// <param name="factory">
        /// Invoked only during warm-up and growth. A method group here allocates one
        /// delegate at load, never per spawn.
        /// </param>
        public ObjectPool(System.Func<T> factory, Transform parent, int capacity, string label = null)
        {
            _factory = factory;
            _parent = parent;
            _label = label ?? typeof(T).Name;

            capacity = Mathf.Max(1, capacity);
            _instances = new T[capacity];
            _poolables = new IPoolable[capacity];
            _freeStack = new int[capacity];
            _isFree = new bool[capacity];

            for (int i = 0; i < capacity; i++)
            {
                CreateAt(i);
                // Fill descending so the first Get() hands out index 0 — keeps the
                // Hierarchy readable while debugging.
                _freeStack[i] = capacity - 1 - i;
                _isFree[i] = true;
            }

            _freeCount = capacity;
        }

        /// <summary>
        /// Direct access for callers that must sweep every live instance each frame. Indexed
        /// rather than enumerated, because an enumerator would allocate.
        /// </summary>
        public bool TryGetActive(int index, out T instance)
        {
            if ((uint)index >= (uint)_instances.Length || _isFree[index])
            {
                instance = null;
                return false;
            }

            instance = _instances[index];
            return instance != null;
        }

        public T Get(out int handle)
        {
            if (_freeCount == 0)
            {
                Grow();
            }

            handle = _freeStack[--_freeCount];
            _isFree[handle] = false;

            T instance = _instances[handle];
            instance.gameObject.SetActive(true);

            IPoolable poolable = _poolables[handle];
            if (poolable != null)
            {
                poolable.OnSpawn();
            }

            return instance;
        }

        public void Release(int handle)
        {
            if ((uint)handle >= (uint)_instances.Length || _isFree[handle])
            {
                return;
            }

            IPoolable poolable = _poolables[handle];
            if (poolable != null)
            {
                poolable.OnDespawn();
            }

            T instance = _instances[handle];
            if (instance == null)
            {
                // Unity's overloaded == catches both a real null and a destroyed object here.
                // Note the interface check above cannot do that: comparing an interface
                // reference to null is plain reference equality and sails past a destroyed
                // component.
                Debug.LogError($"ObjectPool<{_label}> has no live instance at handle {handle} " +
                               $"(capacity {_instances.Length}). It was destroyed or never created.");
                _isFree[handle] = true;
                _freeStack[_freeCount++] = handle;
                return;
            }

            instance.gameObject.SetActive(false);
            _isFree[handle] = true;
            _freeStack[_freeCount++] = handle;
        }

        /// <summary>Returns every live instance. Used on run restart — no scene reload.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                Release(i);
            }
        }

        private void CreateAt(int index)
        {
            T instance = _factory();
            instance.transform.SetParent(_parent, false);
            instance.gameObject.SetActive(false);

            _instances[index] = instance;
            // Warm-up only. Never called again for the lifetime of this instance.
            _poolables[index] = instance.GetComponent<IPoolable>();
        }

        /// <summary>
        /// Doubles capacity. This allocates and instantiates mid-run, which violates the
        /// project's pooling rule — the warning is the signal that the serialized capacity
        /// is too low. Growing beats returning null and turning an undersized pool into a
        /// NullReferenceException somewhere less obvious.
        /// </summary>
        private void Grow()
        {
            int oldCapacity = _instances.Length;
            int newCapacity = oldCapacity * 2;

            if (!_warnedExhausted)
            {
                _warnedExhausted = true;
                Debug.LogWarning($"ObjectPool<{_label}> exhausted at {oldCapacity} and grew to {newCapacity}. " +
                                 "Raise the serialized capacity — growth allocates during gameplay.");
            }

            System.Array.Resize(ref _instances, newCapacity);
            System.Array.Resize(ref _poolables, newCapacity);
            System.Array.Resize(ref _freeStack, newCapacity);
            System.Array.Resize(ref _isFree, newCapacity);

            for (int i = oldCapacity; i < newCapacity; i++)
            {
                CreateAt(i);
                _freeStack[_freeCount++] = i;
                _isFree[i] = true;
            }
        }
    }
}
