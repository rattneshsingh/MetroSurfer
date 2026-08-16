namespace Runner.Utils
{
    /// <summary>
    /// Optional contract for pooled components. <see cref="ObjectPool{T}"/> resolves this
    /// once per instance during warm-up, so spawning never costs a GetComponent.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
