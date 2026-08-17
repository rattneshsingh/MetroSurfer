using Runner.Config;
using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Builds, pools and animates obstacles. Everything about making an obstacle exist lives
    /// here; nothing about where the track goes does.
    /// </summary>
    /// <remarks>
    /// Split out of SegmentSpawner, which was doing eight jobs at once. Scrolling a world and
    /// constructing a truck have nothing to do with each other, and only one of them needs to
    /// know what a lethal volume is.
    /// </remarks>
    public sealed class ObstacleFactory
    {
        /// <summary>The pool array is indexed by ObstacleKind, so it must span the enum.</summary>
        private const int KindCount = 11;

        private readonly ObstacleCatalog _catalog;
        private readonly Transform _poolRoot;
        private readonly ObstacleFactorySettings _settings;
        private readonly System.Func<float> _roll;

        private readonly ObjectPool<Obstacle>[] _poolsByKind;
        private readonly Material _obstacleMaterial;
        private readonly Material _pitMaterial;
        private readonly Material _brakeMaterial;

        public ObstacleFactory(
            ObstacleCatalog catalog,
            Transform poolRoot,
            ObstacleFactorySettings settings,
            System.Func<float> roll)
        {
            _catalog = catalog;
            _poolRoot = poolRoot;
            _settings = settings;
            _roll = roll;

            _obstacleMaterial = PrimitiveFactory.CreateSharedMaterial(settings.ObstacleColor, "M_Obstacle");
            _pitMaterial = PrimitiveFactory.CreateSharedMaterial(settings.PitColor, "M_Pit");
            _brakeMaterial = PrimitiveFactory.CreateSharedMaterial(settings.BrakeColor, "M_Brake");

            _poolsByKind = new ObjectPool<Obstacle>[KindCount];
            BuildPools();
        }

        public float RoofHeight(ObstacleKind kind) => _catalog.RoofHeight(kind);

        public Obstacle Get(ObstacleKind kind, out int handle)
        {
            ObjectPool<Obstacle> pool = PoolFor(kind);
            if (pool == null)
            {
                handle = -1;
                return null;
            }

            Obstacle obstacle = pool.Get(out handle);
            ApplyDrift(obstacle, kind);
            return obstacle;
        }

        public void Release(ObstacleKind kind, int handle) => PoolFor(kind)?.Release(handle);

        /// <summary>
        /// Advances every drifting vehicle in one batched sweep. A few dozen MonoBehaviour
        /// Update calls cost more than one loop doing the same work.
        /// </summary>
        public void TickDrift(float deltaTime)
        {
            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);
                if (definition == null || definition.Drift == DriftMode.None)
                {
                    continue;
                }

                Material alert = definition.Drift == DriftMode.Decelerate ? _brakeMaterial : null;
                TickDriftPool(PoolFor(definition.Kind), deltaTime, alert);
            }
        }

        /// <summary>Materials are created here and owned by nobody else.</summary>
        public void Dispose()
        {
            ObjectUtility.Destroy(_obstacleMaterial);
            ObjectUtility.Destroy(_pitMaterial);
            ObjectUtility.Destroy(_brakeMaterial);
        }

        private ObjectPool<Obstacle> PoolFor(ObstacleKind kind)
        {
            int index = (int)kind;
            return (uint)index < (uint)_poolsByKind.Length ? _poolsByKind[index] : null;
        }

        private static void TickDriftPool(ObjectPool<Obstacle> pool, float deltaTime, Material alertMaterial)
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Capacity; i++)
            {
                if (!pool.TryGetActive(i, out Obstacle obstacle) || obstacle.Drift == null)
                {
                    continue;
                }

                obstacle.Drift.Tick(deltaTime);

                // Consumed rather than polled, so the swap costs one call on one frame.
                if (alertMaterial != null && obstacle.Drift.ConsumeBrakeStarted())
                {
                    obstacle.SetMaterial(alertMaterial);
                }
            }
        }

        private void BuildPools()
        {
            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);
                if (definition == null)
                {
                    continue;
                }

                int index = (int)definition.Kind;
                if ((uint)index >= (uint)_poolsByKind.Length)
                {
                    continue;
                }

                int capacity = definition.PoolCapacity > 0 ? definition.PoolCapacity : _settings.AutoCapacity;

                // Captured once at build time. The closure allocates here and never on a spawn,
                // which is the only place it would matter.
                ObstacleDefinition captured = definition;
                _poolsByKind[index] = new ObjectPool<Obstacle>(
                    () => CreateFor(captured), _poolRoot, capacity, "Obstacle." + definition.Kind);
            }
        }

        /// <summary>
        /// Builds one obstacle from its definition. Dispatching on form rather than on kind is
        /// the whole point of the catalogue: a new obstacle is a new entry, and only a genuinely
        /// new shape reaches this method at all.
        /// </summary>
        private Obstacle CreateFor(ObstacleDefinition definition)
        {
            Obstacle obstacle;

            if (definition.Prefab != null)
            {
                GameObject authored = Object.Instantiate(definition.Prefab);
                Obstacle existing = authored.GetComponent<Obstacle>();
                obstacle = existing != null ? existing : authored.AddComponent<Obstacle>();
            }
            else
            {
                switch (definition.Form)
                {
                    case ObstacleForm.Landable:
                        obstacle = BuildLandable(definition);
                        break;

                    case ObstacleForm.Ramp:
                        obstacle = BuildRamp(definition);
                        break;

                    case ObstacleForm.Pit:
                        obstacle = BuildPit(definition);
                        break;

                    default:
                        obstacle = BuildBarrier(definition);
                        break;
                }
            }

            if (definition.Drift != DriftMode.None && obstacle.GetComponent<MovingObstacle>() == null)
            {
                obstacle.gameObject.AddComponent<MovingObstacle>().Initialize();
            }

            obstacle.Initialize(definition.Kind);
            return obstacle;
        }

        /// <summary>
        /// A solid, lethal box. Ground barriers and overhead slabs are the same thing at
        /// different base heights.
        /// </summary>
        private Obstacle BuildBarrier(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject body = PrimitiveFactory.CreateCube(
                "Body", definition.Size, MaterialFor(definition.Tint), _settings.ObstacleLayer,
                PrimitiveFactory.ColliderMode.Trigger, castShadows: true);

            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            return root.AddComponent<Obstacle>();
        }

        /// <summary>
        /// Solid body on the Surface layer for the ground probe, plus a lethal trigger inset
        /// below the roof. That inset is the entire mechanic — land on top and you are above the
        /// lethal volume, clip the side and you are inside it.
        /// </summary>
        private Obstacle BuildLandable(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject body = PrimitiveFactory.CreateCube(
                "Body", definition.Size, MaterialFor(definition.Tint), _settings.SurfaceLayer,
                PrimitiveFactory.ColliderMode.Solid, castShadows: true);

            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            AddLethalVolume(root, definition);
            return root.AddComponent<Obstacle>();
        }

        /// <summary>Never lethal. It exists purely to raise the ground.</summary>
        private Obstacle BuildRamp(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            float angle = Mathf.Atan2(definition.RampRise, definition.RampRun) * Mathf.Rad2Deg;
            float slopeLength = Mathf.Sqrt(
                definition.RampRise * definition.RampRise + definition.RampRun * definition.RampRun);

            GameObject slope = PrimitiveFactory.CreateCube(
                "Slope",
                new Vector3(definition.Size.x, definition.RampThickness, slopeLength),
                MaterialFor(definition.Tint), _settings.SurfaceLayer,
                PrimitiveFactory.ColliderMode.Solid, castShadows: true);

            slope.transform.SetParent(root.transform, false);

            // Negative pitch lifts the far end, which is the end the player reaches last as the
            // world scrolls toward them.
            slope.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            slope.transform.localPosition = new Vector3(
                0f, definition.RampRise * 0.5f, definition.RampRun * 0.5f);

            return root.AddComponent<Obstacle>();
        }

        /// <summary>
        /// A hole, rendered as a near-black slab just proud of the road. Punching a real gap
        /// would mean tiling every segment floor per lane and per row, and this plays the same:
        /// jump it, or be somewhere else.
        /// </summary>
        private Obstacle BuildPit(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject slab = PrimitiveFactory.CreateCube(
                "Slab", definition.Size, MaterialFor(definition.Tint), root.layer,
                PrimitiveFactory.ColliderMode.None, castShadows: false);

            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            AddLethalVolume(root, definition);
            return root.AddComponent<Obstacle>();
        }

        /// <summary>The kill volume, whose bounds the definition works out per form.</summary>
        private void AddLethalVolume(GameObject root, ObstacleDefinition definition)
        {
            GameObject lethal = new GameObject("Lethal");
            lethal.layer = _settings.ObstacleLayer;
            lethal.transform.SetParent(root.transform, false);

            float bottom = definition.LethalBottom;
            float height = Mathf.Max(0.05f, definition.LethalTop - bottom);

            BoxCollider box = lethal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, bottom + height * 0.5f, 0f);
            box.size = new Vector3(definition.Size.x, height, definition.Size.z);
        }

        private Material MaterialFor(ObstacleTint tint) =>
            tint == ObstacleTint.Pit ? _pitMaterial : _obstacleMaterial;

        /// <summary>
        /// Drift is applied by mode, so the parameters stay shared across kinds rather than
        /// being duplicated into every definition that moves.
        /// </summary>
        private void ApplyDrift(Obstacle obstacle, ObstacleKind kind)
        {
            if (obstacle == null || obstacle.Drift == null)
            {
                return;
            }

            ObstacleDefinition definition = _catalog.Get(kind);
            DriftMode mode = definition == null ? DriftMode.Oscillate : definition.Drift;

            switch (mode)
            {
                case DriftMode.Approach:
                    obstacle.Drift.BeginApproach(_settings.OncomingSpeed, _settings.OncomingMaxTravel);
                    break;

                case DriftMode.Decelerate:
                    obstacle.Drift.BeginBraking(
                        _settings.BusCruiseSpeed, _settings.BusBrakeTriggerZ, _settings.BusBrakeDuration);
                    break;

                default:
                    // Phase varies per placement so a convoy ripples rather than moving as a block.
                    obstacle.Drift.BeginOscillation(
                        _settings.DriftAmplitude, _settings.DriftPeriod, _roll() * Mathf.PI * 2f);
                    break;
            }
        }
    }
}
