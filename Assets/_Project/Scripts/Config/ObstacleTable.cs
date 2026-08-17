using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Weighted pool of obstacle rows. Owned by a state later (see <see cref="StateConfig"/>);
    /// in Phase 0 the spawner uses one shared table.
    /// </summary>
    [CreateAssetMenu(menuName = "Runner/Obstacle Table", fileName = "ObstacleTable")]
    public sealed class ObstacleTable : ScriptableObject
    {
        [SerializeField] private ObstaclePattern[] _patterns;

        [Header("Multi-row set-pieces")]
        [Tooltip("Chance a row starts a formation instead of picking a single-row pattern.")]
        [SerializeField, Range(0f, 1f)] private float _formationChance = 0.35f;

        [SerializeField] private ObstacleFormation[] _formations;

        public int Count => _patterns == null ? 0 : _patterns.Length;

        public float FormationChance => _formationChance;

        /// <summary>Weighted pick among formations unlocked at <paramref name="distance"/>.</summary>
        public bool TryPickFormation(float distance, float roll01, out ObstacleFormation formation)
        {
            formation = null;

            if (_formations == null)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int i = 0; i < _formations.Length; i++)
            {
                if (IsFormationEligible(i, distance))
                {
                    totalWeight += _formations[i].Weight;
                }
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            float target = roll01 * totalWeight;
            for (int i = 0; i < _formations.Length; i++)
            {
                if (!IsFormationEligible(i, distance))
                {
                    continue;
                }

                target -= _formations[i].Weight;
                if (target <= 0f)
                {
                    formation = _formations[i];
                    return true;
                }
            }

            for (int i = _formations.Length - 1; i >= 0; i--)
            {
                if (IsFormationEligible(i, distance))
                {
                    formation = _formations[i];
                    return true;
                }
            }

            return false;
        }

        private bool IsFormationEligible(int index, float distance)
        {
            ObstacleFormation formation = _formations[index];
            return formation != null
                   && formation.RowCount > 0
                   && formation.Weight > 0f
                   && distance >= formation.MinDistance;
        }

        /// <summary>
        /// Weighted pick among patterns unlocked at <paramref name="distance"/>.
        /// Two indexed passes over a small array — no allocation, no LINQ.
        /// </summary>
        /// <param name="roll01">Uniform random value in [0,1).</param>
        public bool TryPick(float distance, float roll01, out ObstaclePattern pattern)
        {
            pattern = default;

            if (_patterns == null)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int i = 0; i < _patterns.Length; i++)
            {
                if (IsEligible(i, distance))
                {
                    totalWeight += _patterns[i].Weight;
                }
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            float target = roll01 * totalWeight;
            for (int i = 0; i < _patterns.Length; i++)
            {
                if (!IsEligible(i, distance))
                {
                    continue;
                }

                target -= _patterns[i].Weight;
                if (target <= 0f)
                {
                    pattern = _patterns[i];
                    return true;
                }
            }

            // Float drift on the accumulator — fall back to the last eligible entry.
            for (int i = _patterns.Length - 1; i >= 0; i--)
            {
                if (IsEligible(i, distance))
                {
                    pattern = _patterns[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Runtime stand-in used when no asset is assigned, so the game runs out of the box.</summary>
        public static ObstacleTable CreateDefault()
        {
            ObstacleTable table = CreateInstance<ObstacleTable>();
            table.name = "ObstacleTable (runtime default)";
            table.FillDefaults();
            return table;
        }

        private bool IsEligible(int index, float distance)
        {
            ObstaclePattern pattern = _patterns[index];
            return pattern.Weight > 0f && !pattern.IsImpassable && distance >= pattern.MinDistance;
        }

        private void Reset() => FillDefaults();

        private void FillDefaults()
        {
            // The intro sequence has already taught all three verbs by the time this table is
            // consulted, so every verb is in play from the first procedural row. Nothing here
            // is gated behind a distance the player has to grind out to see.
            _patterns = new[]
            {
                new ObstaclePattern { LaneMask = 0b001, Kind = ObstacleKind.Low,  Weight = 1.0f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b010, Kind = ObstacleKind.Low,  Weight = 1.0f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b100, Kind = ObstacleKind.Low,  Weight = 1.0f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b111, Kind = ObstacleKind.Low,  Weight = 0.9f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b111, Kind = ObstacleKind.High, Weight = 0.9f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b010, Kind = ObstacleKind.High, Weight = 0.7f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b001, Kind = ObstacleKind.High, Weight = 0.5f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b100, Kind = ObstacleKind.High, Weight = 0.5f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b010, Kind = ObstacleKind.Full, Weight = 0.8f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b001, Kind = ObstacleKind.Full, Weight = 0.6f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b100, Kind = ObstacleKind.Full, Weight = 0.6f, MinDistance = 0f },
                new ObstaclePattern { LaneMask = 0b011, Kind = ObstacleKind.Full, Weight = 0.5f, MinDistance = 250f },
                new ObstaclePattern { LaneMask = 0b110, Kind = ObstacleKind.Full, Weight = 0.5f, MinDistance = 250f }
            };

            _formationChance = 0.35f;
            _formations = new[]
            {
                Formation("Ramp to truck", 1.4f, 0f,
                    Row(0b010, ObstacleKind.Ramp),
                    Row(0b010, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Ramp to truck, side lane", 1.2f, 0f,
                    Row(0b001, ObstacleKind.Ramp),
                    Row(0b001, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Ramp to truck, far lane", 1.2f, 0f,
                    Row(0b100, ObstacleKind.Ramp),
                    Row(0b100, ObstacleKind.Truck),
                    Clear(true)),

                // The point of the whole stack: a roof you cross by jumping, not by riding.
                Formation("Convoy", 1.2f, 90f,
                    Row(0b010, ObstacleKind.Ramp),
                    Row(0b010, ObstacleKind.Truck),
                    Row(0b010, ObstacleKind.MovingTruck),
                    Row(0b010, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Split convoy", 1f, 140f,
                    Row(0b001, ObstacleKind.Ramp),
                    Row(0b001, ObstacleKind.Truck),
                    Row(0b101, ObstacleKind.MovingTruck),
                    Row(0b100, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Up to the train", 1f, 200f,
                    Row(0b010, ObstacleKind.Ramp),
                    Row(0b010, ObstacleKind.Truck),
                    Row(0b010, ObstacleKind.Train),
                    Clear(true),
                    Clear(false)),

                Formation("Moving traffic", 1.1f, 260f,
                    Row(0b001, ObstacleKind.MovingTruck),
                    Row(0b100, ObstacleKind.MovingTruck),
                    Row(0b010, ObstacleKind.MovingTruck),
                    Clear(true)),

                // Every lane ramps up onto a roof. There is no ground route, so the whole
                // stretch is run along the top and the coins are up there with you.
                Formation("Rooftop run", 1.1f, 120f,
                    Row(All, ObstacleKind.Ramp),
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Rooftop run, long", 0.8f, 260f,
                    Row(All, ObstacleKind.Ramp),
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.MovingTruck),
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck),
                    Clear(true)),

                // Something arrives faster than the world does. The clear row in front is the
                // tell — without it there is no time to read the lane.
                Formation("Oncoming", 1.2f, 150f,
                    Clear(false),
                    Row(0b010, ObstacleKind.OncomingTruck),
                    Clear(true)),

                Formation("Oncoming pair", 0.9f, 300f,
                    Clear(false),
                    Row(0b001, ObstacleKind.OncomingTruck),
                    Row(0b100, ObstacleKind.OncomingTruck),
                    Clear(true)),

                // Two lanes gone. The surviving lane is a corner one, so the read is simple
                // even though the margin is not.
                Formation("Broken road, right open", 1.1f, 100f,
                    Row(0b011, ObstacleKind.Pit),
                    Row(0b011, ObstacleKind.Pit),
                    Clear(true)),

                Formation("Broken road, left open", 1.1f, 100f,
                    Row(0b110, ObstacleKind.Pit),
                    Row(0b110, ObstacleKind.Pit),
                    Clear(true)),

                // The open lane switches sides mid-stretch.
                // The counterweight to the whole stack. Height pays a multiplier and street
                // obstacles cannot reach you up there, so without something that punishes
                // altitude the correct play is to climb once and never come down.
                //
                // Two clear rows before every underpass are load-bearing, not decoration:
                // that is the room a roof runner needs to fall to the street in time.
                // The lane you judged safe stops being safe. A clear row in front is the read
                // window; without it there is nothing to have decided early about.
                Formation("Bus that stops", 1.1f, 200f,
                    Clear(false),
                    Row(0b010, ObstacleKind.StoppingBus),
                    Clear(true)),

                Formation("Bus in traffic", 0.9f, 300f,
                    Clear(false),
                    Row(0b001, ObstacleKind.StoppingBus),
                    Row(0b100, ObstacleKind.MovingTruck),
                    Clear(true)),

                // Two lanes rolling, one of them lying about it.
                Formation("Bus pair", 0.8f, 380f,
                    Clear(false),
                    Row(0b101, ObstacleKind.StoppingBus),
                    Clear(true)),

                // A real gap between two roofs, with coins tracing the jump across it. The arc
                // is what turns a hazard into an invitation: same geometry, opposite read.
                Formation("Gap jump", 1.3f, 170f,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Double gap jump", 0.9f, 280f,
                    Row(0b001, ObstacleKind.Ramp),
                    RowCoins(0b001, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b001, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b001, ObstacleKind.Truck),
                    Clear(true)),

                Formation("Underpass", 1.1f, 130f,
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Clear(true)),

                // The full loop in one set-piece: climb, get paid, get pushed back down.
                Formation("Climb then underpass", 1.2f, 150f,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Clear(true)),

                Formation("Rooftop run into underpass", 0.9f, 320f,
                    Row(All, ObstacleKind.Ramp),
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck),
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Clear(true)),

                Formation("Broken road, crossing", 1f, 240f,
                    Row(0b011, ObstacleKind.Pit),
                    Row(0b011, ObstacleKind.Pit),
                    Row(0b110, ObstacleKind.Pit),
                    Row(0b110, ObstacleKind.Pit),
                    Clear(true))
            };
        }

        private static ObstacleFormation Formation(string name, float weight, float minDistance, params IntroRow[] rows)
            => new ObstacleFormation { Name = name, Rows = rows, Weight = weight, MinDistance = minDistance };

        private const int All = ObstaclePattern.AllLanesMask;

        /// <summary>
        /// An empty row whose coins arc across it. Kind names the tier the jump launches from,
        /// since there is nothing to spawn.
        /// </summary>
        private static IntroRow ArcRow(ObstacleKind launchTier)
            => new IntroRow { LaneMask = 0, Kind = launchTier, Coins = false, CoinArc = true };

        private static IntroRow RowCoins(int laneMask, ObstacleKind kind)
            => new IntroRow { LaneMask = laneMask, Kind = kind, Coins = true };

        private static IntroRow Row(int laneMask, ObstacleKind kind)
            => new IntroRow { LaneMask = laneMask, Kind = kind, Coins = false };

        private static IntroRow Clear(bool coins)
            => new IntroRow { LaneMask = 0, Kind = ObstacleKind.Low, Coins = coins };
    }
}
