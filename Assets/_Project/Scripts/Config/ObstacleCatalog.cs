using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Every obstacle the game can build, as data. The single source of truth for geometry and
    /// for what each kind means to the player.
    /// </summary>
    /// <remarks>
    /// Adding an obstacle is now an entry here rather than edits to five switch statements. A
    /// state can eventually swap the whole catalogue — a Delhi metro coach and a Punjab tractor
    /// trolley are the same <see cref="ObstacleForm.Landable"/> with different numbers.
    /// </remarks>
    [CreateAssetMenu(menuName = "Runner/Obstacle Catalog", fileName = "ObstacleCatalog")]
    public sealed class ObstacleCatalog : ScriptableObject
    {
        private const int KindCount = 11;

        [SerializeField] private ObstacleDefinition[] _definitions;

        private ObstacleDefinition[] _byKind;

        public int Count => _definitions == null ? 0 : _definitions.Length;

        public ObstacleDefinition At(int index) => _definitions[index];

        /// <summary>
        /// Indexed by enum value rather than searched. Built once, so lookups on the generation
        /// path cost an array read.
        /// </summary>
        public ObstacleDefinition Get(ObstacleKind kind)
        {
            EnsureIndex();

            int i = (int)kind;
            return (uint)i < (uint)_byKind.Length ? _byKind[i] : null;
        }

        public bool IsLandable(ObstacleKind kind)
        {
            ObstacleDefinition definition = Get(kind);
            return definition != null && definition.IsLandable;
        }

        /// <summary>
        /// True when the lane cannot be survived in place. Data rather than a hardcoded rule,
        /// so a new obstacle declares its own fairness contract.
        /// </summary>
        public bool ClosesLane(ObstacleKind kind)
        {
            ObstacleDefinition definition = Get(kind);
            return definition != null && definition.ClosesLane;
        }

        public float RoofHeight(ObstacleKind kind)
        {
            ObstacleDefinition definition = Get(kind);
            return definition == null ? 0f : definition.RoofHeight;
        }

        /// <summary>Rebuilds the lookup. Call after editing definitions at runtime.</summary>
        public void Invalidate() => _byKind = null;

        public static ObstacleCatalog CreateDefault()
        {
            ObstacleCatalog catalog = CreateInstance<ObstacleCatalog>();
            catalog.name = "ObstacleCatalog (runtime default)";
            catalog.FillDefaults();
            return catalog;
        }

        private void EnsureIndex()
        {
            if (_byKind != null)
            {
                return;
            }

            _byKind = new ObstacleDefinition[KindCount];

            if (_definitions == null)
            {
                return;
            }

            for (int i = 0; i < _definitions.Length; i++)
            {
                ObstacleDefinition definition = _definitions[i];
                if (definition == null)
                {
                    continue;
                }

                int index = (int)definition.Kind;
                if ((uint)index < (uint)_byKind.Length)
                {
                    _byKind[index] = definition;
                }
            }
        }

        private void Reset() => FillDefaults();

        private void OnValidate() => Invalidate();

        /// <summary>
        /// Sized against a player one unit tall whose jump apex puts their feet at 1.5. Those
        /// two numbers decide every height here, and GeometryInvariantTests asserts the
        /// relationships still hold.
        /// </summary>
        private void FillDefaults()
        {
            _definitions = new[]
            {
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.Low, Form = ObstacleForm.Barrier,
                    Size = new Vector3(1.6f, 0.5f, 0.7f), BaseHeight = 0f
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.High, Form = ObstacleForm.Barrier,
                    Size = new Vector3(1.6f, 1.45f, 0.7f), BaseHeight = 0.55f
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.Full, Form = ObstacleForm.Barrier,
                    Size = new Vector3(1.6f, 2.2f, 0.7f), BaseHeight = 0f, ClosesLane = true
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.Underpass, Form = ObstacleForm.Barrier,
                    Size = new Vector3(8f, 3.5f, 1.2f), BaseHeight = 1.05f
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.Ramp, Form = ObstacleForm.Ramp,
                    Size = new Vector3(1.8f, 0.25f, 3f),
                    RampRun = 3f, RampRise = 1.2f, RampThickness = 0.25f
                },
                new ObstacleDefinition
                {
                    // Exactly one row long, so trucks in consecutive rows meet nose to tail and
                    // a convoy's roofs form one continuous road with no gaps underfoot.
                    Kind = ObstacleKind.Truck, Form = ObstacleForm.Landable,
                    Size = new Vector3(1.8f, 1.2f, 5f)
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.MovingTruck, Form = ObstacleForm.Landable,
                    Size = new Vector3(1.8f, 1.2f, 4.6f), Drift = DriftMode.Oscillate
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.OncomingTruck, Form = ObstacleForm.Landable,
                    Size = new Vector3(1.8f, 1.2f, 4.6f), Drift = DriftMode.Approach
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.StoppingBus, Form = ObstacleForm.Landable,
                    Size = new Vector3(1.8f, 1.4f, 4.6f), Drift = DriftMode.Decelerate
                },
                new ObstacleDefinition
                {
                    // Out of reach from the street on purpose: you get here off a truck roof.
                    Kind = ObstacleKind.Train, Form = ObstacleForm.Landable,
                    Size = new Vector3(1.8f, 2.4f, 4.5f), ClosesLane = true
                },
                new ObstacleDefinition
                {
                    Kind = ObstacleKind.Pit, Form = ObstacleForm.Pit,
                    Size = new Vector3(2f, 0.08f, 4.6f), BaseHeight = 0f,
                    Tint = ObstacleTint.Pit, PitTriggerHeight = 0.6f, ClosesLane = true
                }
            };

            Invalidate();
        }
    }
}
