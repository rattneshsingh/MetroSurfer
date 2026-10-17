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
        private readonly StateConfig _state;

        private readonly ObjectPool<Obstacle>[] _poolsByKind;
        private readonly Material _obstacleMaterial;
        private readonly Material _pitMaterial;
        private readonly Material _brakeMaterial;

        /// <summary>One shared material per painted kind, made the first time that kind is built.</summary>
        private readonly Material[] _paintMaterials = new Material[KindCount];

        /// <summary>
        /// A pit is a real hole: an open box sunk below the road, drawn first, then a depth-only
        /// cutter over its mouth, then the road — which fails the depth test there and leaves
        /// the box visible. Queues fix that order; Geometry is 2000.
        /// </summary>
        private const int HoleInteriorQueue = 1980;
        private const int HoleCutterQueue = 1990;

        /// <summary>How deep the hole looks, in metres. Purely visual; the lethal volume decides play.</summary>
        private const float HoleDepth = 1.2f;

        /// <summary>Cutter height above the road: enough to win the depth test, too little to see.</summary>
        private const float HoleLift = 0.005f;

        private const string DepthMaskShaderPath = "Shaders/RunnerDepthMask";

        private static readonly Color HoleWallColor = new Color(0.30f, 0.27f, 0.24f);

        private readonly Material _holeCutterMaterial;
        private readonly Material _holeWallMaterial;
        private readonly Material _holeFloorMaterial;

        private static Mesh _holeMesh;
        private static Mesh _wallMesh;

        private static Texture2D _hazardStripes;

        public ObstacleFactory(
            ObstacleCatalog catalog,
            Transform poolRoot,
            ObstacleFactorySettings settings,
            System.Func<float> roll,
            StateConfig state = null)
        {
            _catalog = catalog;
            _poolRoot = poolRoot;
            _settings = settings;
            _roll = roll;

            // Optional: without a state every obstacle keeps its generated grey shape.
            _state = state;

            _obstacleMaterial = PrimitiveFactory.CreateSharedMaterial(settings.ObstacleColor, "M_Obstacle");
            _pitMaterial = PrimitiveFactory.CreateSharedMaterial(settings.PitColor, "M_Pit");
            _brakeMaterial = PrimitiveFactory.CreateSharedMaterial(settings.BrakeColor, "M_Brake");

            _holeWallMaterial = PrimitiveFactory.CreateSharedMaterial(HoleWallColor, "M_PitWall");
            _holeWallMaterial.renderQueue = HoleInteriorQueue;
            _holeFloorMaterial = PrimitiveFactory.CreateSharedMaterial(settings.PitColor, "M_PitFloor");
            _holeFloorMaterial.renderQueue = HoleInteriorQueue;

            Shader depthMask = Resources.Load<Shader>(DepthMaskShaderPath);
            if (depthMask != null)
            {
                _holeCutterMaterial = new Material(depthMask) { name = "M_PitCutter", renderQueue = HoleCutterQueue };
            }
            else
            {
                Debug.LogWarning($"Depth mask shader missing at Resources/{DepthMaskShaderPath}; pits will draw " +
                                 "as dark patches instead of holes.");
            }

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
            ObjectUtility.Destroy(_holeCutterMaterial);
            ObjectUtility.Destroy(_holeWallMaterial);
            ObjectUtility.Destroy(_holeFloorMaterial);

            for (int i = 0; i < _paintMaterials.Length; i++)
            {
                ObjectUtility.Destroy(_paintMaterials[i]);
                _paintMaterials[i] = null;
            }
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

                // A state's model replaces only what is drawn. The generated physics stays.
                ObstacleArt art = _state != null ? _state.ArtFor(definition.Kind) : null;
                if (art != null && art.Visual != null)
                {
                    ObstacleArtFitter.Dress(obstacle.gameObject, definition, art.Visual, art.Yaw);
                }
                else if (art != null && art.UsePaint)
                {
                    Repaint(obstacle.gameObject, PaintFor(definition.Kind, art));
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

        /// <summary>
        /// Raises the ground, and is solid underneath.
        /// </summary>
        /// <remarks>
        /// The slope itself is never lethal — riding it is the point. But the wedge *below* the
        /// slope is a solid object, and sliding into it from an adjacent lane halfway along used
        /// to do nothing at all: the controller refused to step up, so the player kept running
        /// at street level inside the geometry. That is now a stumble.
        /// </remarks>
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

            AddRampUnderside(root, definition);
            return root.AddComponent<Obstacle>();
        }

        /// <summary>
        /// The solid part of the wedge, kept clear of the surface people ride on.
        /// </summary>
        /// <remarks>
        /// A box cannot follow a slope, so it covers the far portion of the run only, and its
        /// top sits at the slope's height where that portion begins. Anyone correctly riding the
        /// ramp is above it by then; anyone arriving from the side at street level is inside it.
        /// The near portion is left open because that is the entrance.
        /// </remarks>
        private void AddRampUnderside(GameObject root, ObstacleDefinition definition)
        {
            const float SolidFromFraction = 0.45f;

            float startZ = definition.RampRun * SolidFromFraction;
            float depth = definition.RampRun - startZ;
            float height = definition.RampRise * SolidFromFraction;

            if (depth <= 0.01f || height <= 0.01f)
            {
                return;
            }

            GameObject lethal = new GameObject("Lethal");
            lethal.layer = _settings.ObstacleLayer;
            lethal.transform.SetParent(root.transform, false);

            BoxCollider box = lethal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, height * 0.5f, startZ + depth * 0.5f);
            box.size = new Vector3(definition.Size.x, height, depth);
        }

        /// <summary>
        /// A hole, rendered as a near-black slab just proud of the road. Punching a real gap
        /// would mean tiling every segment floor per lane and per row, and this plays the same:
        /// jump it, or be somewhere else.
        /// </summary>
        private Obstacle BuildPit(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            // The interior: a floor and three walls sunk below the road, in a unit frame that
            // PitHole scales to the lane cell. Walls are separate so neighbours can drop them.
            Transform interior = new GameObject("Interior").transform;
            interior.gameObject.layer = root.layer;
            interior.SetParent(root.transform, false);
            interior.localPosition = new Vector3(0f, definition.BaseHeight, 0f);
            interior.localScale = new Vector3(definition.Size.x, HoleDepth, definition.Size.z);

            AddMeshChild(interior, "Floor", HoleMesh, _holeFloorMaterial,
                new Vector3(0f, -1f, 0f), Quaternion.identity);
            GameObject far = AddMeshChild(interior, "WallFar", WallMesh, _holeWallMaterial,
                new Vector3(0f, 0f, 0.5f), Quaternion.identity);
            GameObject left = AddMeshChild(interior, "WallLeft", WallMesh, _holeWallMaterial,
                new Vector3(-0.5f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            GameObject right = AddMeshChild(interior, "WallRight", WallMesh, _holeWallMaterial,
                new Vector3(0.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

            // The cutter: invisible, but the road cannot draw through it, so the hole shows. With
            // no depth-mask shader it falls back to a dark patch, which still reads as a hazard.
            Material mouth = _holeCutterMaterial != null ? _holeCutterMaterial : _holeFloorMaterial;
            Transform cutter = AddMeshChild(root.transform, "Cutter", HoleMesh, mouth,
                new Vector3(0f, definition.BaseHeight + HoleLift, 0f), Quaternion.identity).transform;
            cutter.localScale = new Vector3(definition.Size.x, 1f, definition.Size.z);

            root.AddComponent<PitHole>().Initialize(interior, cutter, left, right, far, HoleDepth);

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

        /// <summary>Shared per kind, so every painted instance of a kind batches together.</summary>
        private Material PaintFor(ObstacleKind kind, ObstacleArt art)
        {
            int index = (int)kind;
            if (_paintMaterials[index] == null)
            {
                Material material = PrimitiveFactory.CreateSharedMaterial(art.Paint, "M_Paint_" + kind);
                if (art.HazardStripes)
                {
                    material.mainTexture = HazardStripes;
                }

                _paintMaterials[index] = material;
            }

            return _paintMaterials[index];
        }

        private static void Repaint(GameObject obstacle, Material material)
        {
            Renderer[] renderers = obstacle.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        /// <summary>
        /// Diagonal light and dark bands, generated once. The material colour tints the light
        /// bands, so one texture serves any hazard colour. Seamless when tiled, because the band
        /// pattern repeats an even number of times across the texture.
        /// </summary>
        private static Texture2D HazardStripes
        {
            get
            {
                if (_hazardStripes != null)
                {
                    return _hazardStripes;
                }

                const int size = 32;
                const int band = 8;
                Color32 light = new Color32(255, 255, 255, 255);
                Color32 dark = new Color32(38, 38, 40, 255);

                Color32[] pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        pixels[y * size + x] = ((x + y) / band) % 2 == 0 ? light : dark;
                    }
                }

                _hazardStripes = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "HazardStripes",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear
                };
                _hazardStripes.SetPixels32(pixels);
                _hazardStripes.Apply(false, true);
                return _hazardStripes;
            }
        }

        /// <summary>
        /// A unit quad lying flat, UV v running near to far (−Z to +Z), so the hole texture's far
        /// wall always lands on the side facing the camera. Shared by every pit.
        /// </summary>
        private static Mesh HoleMesh
        {
            get
            {
                if (_holeMesh != null)
                {
                    return _holeMesh;
                }

                _holeMesh = new Mesh
                {
                    name = "PitHole",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                        new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
                    },
                    uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                    normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                    triangles = new[] { 0, 2, 1, 2, 3, 1 }
                };
                _holeMesh.RecalculateBounds();
                return _holeMesh;
            }
        }

        /// <summary>
        /// A unit upright panel hanging from y = 0 down to y = −1, facing −Z (towards the chase
        /// camera). Turned and placed to make each wall of a pit; rotations are multiples of 90
        /// degrees, so the pit's non-uniform scale never shears it. Shared by every pit.
        /// </summary>
        private static Mesh WallMesh
        {
            get
            {
                if (_wallMesh != null)
                {
                    return _wallMesh;
                }

                // Clockwise as seen from −Z, the side the panel faces.
                _wallMesh = new Mesh
                {
                    name = "PitWall",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, -1f, 0f), new Vector3(-0.5f, 0f, 0f),
                        new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, -1f, 0f)
                    },
                    normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 }
                };
                _wallMesh.RecalculateBounds();
                return _wallMesh;
            }
        }

        /// <summary>A visual-only mesh child: no collider, no shadow casting.</summary>
        private static GameObject AddMeshChild(
            Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            child.layer = parent.gameObject.layer;
            child.GetComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = localRotation;
            return child;
        }

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
