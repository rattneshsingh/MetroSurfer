using System.Text.RegularExpressions;
using NUnit.Framework;
using Runner.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Runner.Tests
{
    public sealed class ObjectPoolTests
    {
        private Transform _root;
        private int _created;

        private sealed class Marker : MonoBehaviour, IPoolable
        {
            public int SpawnCount;
            public int DespawnCount;

            public void OnSpawn() => SpawnCount++;
            public void OnDespawn() => DespawnCount++;
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PoolRoot").transform;
            _created = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
        }

        private Marker Create()
        {
            _created++;
            return new GameObject($"Pooled{_created}").AddComponent<Marker>();
        }

        private ObjectPool<Marker> NewPool(int capacity) =>
            new ObjectPool<Marker>(Create, _root, capacity, "Test");

        [Test]
        public void WarmUpCreatesEverythingUpFrontAndLeavesItInactive()
        {
            ObjectPool<Marker> pool = NewPool(8);

            Assert.AreEqual(8, _created, "Pool must build its whole capacity at construction.");
            Assert.AreEqual(8, pool.Capacity);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void GetActivatesAndReleaseDeactivates()
        {
            ObjectPool<Marker> pool = NewPool(4);

            Marker instance = pool.Get(out int handle);

            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(1, instance.SpawnCount);

            pool.Release(handle);

            Assert.IsFalse(instance.gameObject.activeSelf);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, instance.DespawnCount);
        }

        [Test]
        public void HandlesAreUniqueWhileLive()
        {
            ObjectPool<Marker> pool = NewPool(16);
            var handles = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < 16; i++)
            {
                pool.Get(out int handle);
                Assert.IsTrue(handles.Add(handle), $"Handle {handle} was handed out twice.");
            }
        }

        /// <summary>
        /// A double release would push the same handle onto the free stack twice, and the pool
        /// would then hand one instance to two owners. Both would write to it.
        /// </summary>
        [Test]
        public void DoubleReleaseIsIgnored()
        {
            ObjectPool<Marker> pool = NewPool(4);
            Marker instance = pool.Get(out int handle);

            pool.Release(handle);
            pool.Release(handle);

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, instance.DespawnCount, "OnDespawn ran twice for one spawn.");

            for (int i = 0; i < 4; i++)
            {
                pool.Get(out int _);
            }

            Assert.AreEqual(4, pool.ActiveCount, "Free stack was corrupted by the double release.");
        }

        [Test]
        public void ReleasedHandlesComeBackAround()
        {
            ObjectPool<Marker> pool = NewPool(2);

            pool.Get(out int first);
            pool.Get(out int second);
            pool.Release(first);
            pool.Release(second);

            pool.Get(out int _);
            pool.Get(out int _);

            Assert.AreEqual(2, pool.ActiveCount);
            Assert.AreEqual(2, _created, "Pool instantiated extra instances instead of reusing.");
        }

        [Test]
        public void ReleaseAllReturnsEveryLiveInstance()
        {
            ObjectPool<Marker> pool = NewPool(6);

            for (int i = 0; i < 6; i++)
            {
                pool.Get(out int _);
            }

            pool.ReleaseAll();

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(6, _created, "ReleaseAll must not create anything.");
        }

        [Test]
        public void ExhaustionGrowsRatherThanReturningNull()
        {
            ObjectPool<Marker> pool = NewPool(2);

            pool.Get(out int _);
            pool.Get(out int _);

            LogAssert.Expect(LogType.Warning, new Regex("exhausted"));
            Marker overflow = pool.Get(out int handle);

            Assert.IsNotNull(overflow, "Exhaustion must never hand back null.");
            Assert.GreaterOrEqual(pool.Capacity, 3);
            Assert.AreEqual(3, pool.ActiveCount);

            pool.Release(handle);
            Assert.AreEqual(2, pool.ActiveCount);
        }

        [Test]
        public void OutOfRangeReleaseIsHarmless()
        {
            ObjectPool<Marker> pool = NewPool(2);
            pool.Get(out int _);

            pool.Release(-1);
            pool.Release(999);

            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void TryGetActiveOnlyReportsLiveInstances()
        {
            ObjectPool<Marker> pool = NewPool(4);
            Marker instance = pool.Get(out int handle);

            Assert.IsTrue(pool.TryGetActive(handle, out Marker fetched));
            Assert.AreSame(instance, fetched);

            pool.Release(handle);

            Assert.IsFalse(pool.TryGetActive(handle, out Marker _));
            Assert.IsFalse(pool.TryGetActive(999, out Marker _));
        }
    }
}
