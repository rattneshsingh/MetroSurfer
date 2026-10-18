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
        /// <summary>Bumped whenever the built-in defaults change.</summary>
        public const int CurrentDefaultsVersion = 5;

        [HideInInspector]
        [SerializeField] private int _defaultsVersion;

        public int DefaultsVersion => _defaultsVersion;

        [SerializeField] private ObstaclePattern[] _patterns;

        [Header("Multi-row set-pieces")]
        [Tooltip("Chance a row starts a formation instead of picking a single-row pattern. " +
                 "Authored sequences should be what the player mostly meets; scattered single " +
                 "rows are filler between them, not the substance.")]
        [SerializeField, Range(0f, 1f)] private float _formationChance = 0.85f;

        [SerializeField] private ObstacleFormation[] _formations;

        public int Count => _patterns == null ? 0 : _patterns.Length;

        public float FormationChance => _formationChance;

        /// <summary>
        /// Weighted pick among the chunks that fit right now: hard enough, easy enough, starting
        /// where the player actually is, and not one of the last few handed out.
        /// </summary>
        /// <remarks>
        /// Falls back rather than failing. If nothing matches the full query it relaxes the
        /// difficulty band and the repeat rule before giving up, because an empty stretch of
        /// track is a worse outcome than a chunk that is slightly too easy or recently seen.
        /// </remarks>
        public bool TryPickFormation(FormationQuery query, float roll01, out ObstacleFormation formation)
        {
            if (TryPickFiltered(query, roll01, strict: true, out formation))
            {
                return true;
            }

            return TryPickFiltered(query, roll01, strict: false, out formation);
        }

        private bool TryPickFiltered(
            FormationQuery query, float roll01, bool strict, out ObstacleFormation formation)
        {
            formation = null;

            if (_formations == null)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int i = 0; i < _formations.Length; i++)
            {
                if (Fits(_formations[i], query, strict))
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
                if (!Fits(_formations[i], query, strict))
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
                if (Fits(_formations[i], query, strict))
                {
                    formation = _formations[i];
                    return true;
                }
            }

            return false;
        }

        private static bool Fits(ObstacleFormation formation, FormationQuery query, bool strict)
        {
            if (formation == null || formation.RowCount == 0 || formation.Weight <= 0f)
            {
                return false;
            }

            if (query.Distance < formation.MinDistance)
            {
                return false;
            }

            // Entry compatibility is never relaxed. Difficulty and repeats can bend; starting a
            // roof run while the player is on the road cannot.
            if (!query.AcceptsEntry(formation.Entry))
            {
                return false;
            }

            if (!strict)
            {
                return formation.Difficulty <= query.MaxDifficulty;
            }

            return formation.Difficulty <= query.MaxDifficulty
                   && formation.Difficulty >= query.MinDifficulty
                   && !query.WasRecentlyUsed(formation);
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

            _defaultsVersion = CurrentDefaultsVersion;

            // Nearly everything comes from designed chunks; random single obstacles are the
            // seasoning, not the meal.
            _formationChance = 0.95f;

            // Chunks, not obstacles. Each one is a small designed experience with a shape: a
            // demand, a beat to react in, and a resolution. Difficulty decides when it appears,
            // Entry and Exit decide what it can follow and what can follow it.
            _formations = new[]
            {
                // ---- Breathers. Low weight on purpose: the pacing rule forces one after a
                // hard stretch, which is when a breather is worth having. Left at equal weight
                // they simply crowd out the content.
                Chunk("Open road", 0.45f, 0.05f, TrackState.Either, TrackState.Either,
                    Clear(true), Clear(true), Clear(false)),

                Chunk("Easy single", 0.5f, 0.10f, TrackState.Street, TrackState.Street,
                    Clear(false), Row(0b010, ObstacleKind.Low), Clear(true)),

                Chunk("Drift wide", 0.45f, 0.12f, TrackState.Street, TrackState.Street,
                    Row(0b010, ObstacleKind.Full), Clear(true), Clear(false)),

                // ---- Single verbs. The vocabulary, one word at a time. ----
                Chunk("Jump it", 1.0f, 0.20f, TrackState.Street, TrackState.Street,
                    Clear(false), Row(All, ObstacleKind.Low), Clear(true)),

                Chunk("Roll it", 1.0f, 0.22f, TrackState.Street, TrackState.Street,
                    Clear(false), Row(All, ObstacleKind.High), Clear(true)),

                Chunk("Side step", 1.0f, 0.20f, TrackState.Street, TrackState.Street,
                    Clear(false), Row(0b011, ObstacleKind.Full), Clear(true)),

                // ---- Sequences. Two demands close enough to be one movement. ----
                Chunk("Jump then roll", 1.1f, 0.40f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(All, ObstacleKind.Low),
                    Row(All, ObstacleKind.High),
                    Clear(true)),

                Chunk("Roll then jump", 1.1f, 0.42f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(All, ObstacleKind.High),
                    Row(All, ObstacleKind.Low),
                    Clear(true)),

                // Every row closes the lane you just moved into, so the whole chunk is one
                // continuous weave rather than three separate decisions.
                Chunk("Weave", 1.2f, 0.45f, TrackState.Street, TrackState.Street,
                    Row(0b011, ObstacleKind.Full),
                    Row(0b110, ObstacleKind.Full),
                    Row(0b011, ObstacleKind.Full),
                    Clear(true)),

                Chunk("Weave, tight", 0.9f, 0.70f, TrackState.Street, TrackState.Street,
                    Row(0b011, ObstacleKind.Full),
                    Row(0b110, ObstacleKind.Full),
                    Row(0b011, ObstacleKind.Full),
                    Row(0b110, ObstacleKind.Full),
                    Clear(true)),

                Chunk("Dodge and jump", 1.1f, 0.50f, TrackState.Street, TrackState.Street,
                    Row(0b100, ObstacleKind.Full),
                    Row(All, ObstacleKind.Low),
                    Clear(true)),

                Chunk("Dodge and roll", 1.1f, 0.52f, TrackState.Street, TrackState.Street,
                    Row(0b001, ObstacleKind.Full),
                    Row(All, ObstacleKind.High),
                    Clear(true)),

                // ---- Corridors. One lane survives, and the coins are in it. ----
                Chunk("Broken road, right", 1.0f, 0.35f, TrackState.Street, TrackState.Street,
                    RowCoins(0b011, ObstacleKind.Pit),
                    RowCoins(0b011, ObstacleKind.Pit),
                    Clear(true)),

                Chunk("Broken road, left", 1.0f, 0.35f, TrackState.Street, TrackState.Street,
                    RowCoins(0b110, ObstacleKind.Pit),
                    RowCoins(0b110, ObstacleKind.Pit),
                    Clear(true)),

                Chunk("Broken road, crossing", 1.0f, 0.60f, TrackState.Street, TrackState.Street,
                    RowCoins(0b011, ObstacleKind.Pit),
                    RowCoins(0b011, ObstacleKind.Pit),
                    RowCoins(0b110, ObstacleKind.Pit),
                    RowCoins(0b110, ObstacleKind.Pit),
                    Clear(true)),

                // ---- Traffic. Things that move at you, or away from you. ----
                Chunk("Oncoming", 1.1f, 0.45f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(0b010, ObstacleKind.OncomingTruck),
                    Clear(true)),

                Chunk("Oncoming pair", 0.9f, 0.72f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(0b001, ObstacleKind.OncomingTruck),
                    Row(0b100, ObstacleKind.OncomingTruck),
                    Clear(true)),

                Chunk("Bus that stops", 1.0f, 0.50f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(0b010, ObstacleKind.StoppingBus),
                    Clear(true)),

                Chunk("Two buses", 0.8f, 0.75f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(0b101, ObstacleKind.StoppingBus),
                    Clear(true)),

                // ---- Ways up. Street in, roof out. ----
                Chunk("Ramp up", 1.4f, 0.25f, TrackState.Street, TrackState.Elevated,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck)),

                Chunk("Ramp up, left", 1.2f, 0.28f, TrackState.Street, TrackState.Elevated,
                    Row(0b001, ObstacleKind.Ramp),
                    RowCoins(0b001, ObstacleKind.Truck)),

                Chunk("Ramp up, right", 1.2f, 0.28f, TrackState.Street, TrackState.Elevated,
                    Row(0b100, ObstacleKind.Ramp),
                    RowCoins(0b100, ObstacleKind.Truck)),

                Chunk("Wall of ramps", 1.0f, 0.35f, TrackState.Street, TrackState.Elevated,
                    Row(All, ObstacleKind.Ramp),
                    RowCoins(All, ObstacleKind.Truck)),

                // ---- Convoys. Trucks nose to tail turn the roofs into a road you run along,
                // rather than boxes you hop between. Weighted up: these give the track its
                // shape, and a lane that holds a convoy for twenty metres can be read at a
                // glance. Each ends on open road, so they exit on the street.
                Chunk("Convoy", 1.6f, 0.30f, TrackState.Street, TrackState.Street,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(true)),

                Chunk("Convoy, left", 1.3f, 0.30f, TrackState.Street, TrackState.Street,
                    Row(0b001, ObstacleKind.Ramp),
                    RowCoins(0b001, ObstacleKind.Truck),
                    RowCoins(0b001, ObstacleKind.Truck),
                    RowCoins(0b001, ObstacleKind.Truck),
                    RowCoins(0b001, ObstacleKind.Truck),
                    Clear(true)),

                Chunk("Convoy, right", 1.3f, 0.30f, TrackState.Street, TrackState.Street,
                    Row(0b100, ObstacleKind.Ramp),
                    RowCoins(0b100, ObstacleKind.Truck),
                    RowCoins(0b100, ObstacleKind.Truck),
                    RowCoins(0b100, ObstacleKind.Truck),
                    RowCoins(0b100, ObstacleKind.Truck),
                    Clear(true)),

                // Two convoys and an open middle: climb either side, or keep your nerve low.
                Chunk("Twin convoys", 1.3f, 0.45f, TrackState.Street, TrackState.Street,
                    Row(0b101, ObstacleKind.Ramp),
                    RowCoins(0b101, ObstacleKind.Truck),
                    RowCoins(0b101, ObstacleKind.Truck),
                    RowCoins(0b101, ObstacleKind.Truck),
                    RowCoins(0b101, ObstacleKind.Truck),
                    Clear(true)),

                Chunk("Long convoy", 1.0f, 0.55f, TrackState.Street, TrackState.Street,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(true)),

                // A break in the convoy, with the coin arc saying the jump is makeable.
                Chunk("Convoy with a gap", 1.0f, 0.60f, TrackState.Street, TrackState.Street,
                    Row(0b010, ObstacleKind.Ramp),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(true)),

                // ---- Roof runs. Unreachable until something has taken you up. ----
                Chunk("Rooftop run", 1.3f, 0.30f, TrackState.Elevated, TrackState.Elevated,
                    RowCoins(All, ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck)),

                Chunk("Gap jump", 1.3f, 0.50f, TrackState.Elevated, TrackState.Elevated,
                    RowCoins(0b010, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck)),

                Chunk("Double gap", 1.0f, 0.70f, TrackState.Elevated, TrackState.Elevated,
                    RowCoins(0b010, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Truck)),

                Chunk("Shifting convoy", 1.1f, 0.60f, TrackState.Elevated, TrackState.Elevated,
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.MovingTruck),
                    RowCoins(0b010, ObstacleKind.Truck)),

                Chunk("Cross the roofs", 1.0f, 0.65f, TrackState.Elevated, TrackState.Elevated,
                    RowCoins(0b001, ObstacleKind.Truck),
                    RowCoins(0b011, ObstacleKind.Truck),
                    RowCoins(0b110, ObstacleKind.Truck),
                    RowCoins(0b100, ObstacleKind.Truck)),

                // Ends on open road, so it ends on the street: the clear row is a drop, not a gap.
                Chunk("Up to the train", 1.0f, 0.55f, TrackState.Elevated, TrackState.Street,
                    RowCoins(0b010, ObstacleKind.Truck),
                    RowCoins(0b010, ObstacleKind.Train),
                    Clear(false)),

                // ---- Ways down. The counterweight to the whole stack. ----
                // Two clear rows before an underpass is the room a roof runner needs to fall.
                Chunk("Underpass", 1.4f, 0.45f, TrackState.Elevated, TrackState.Street,
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Clear(true)),

                Chunk("Down and dodge", 0.7f, 0.85f, TrackState.Elevated, TrackState.Street,
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Row(0b011, ObstacleKind.Full),
                    Clear(true)),

                // The roofs simply run out. No underpass required.
                Chunk("Roofs run out", 1.2f, 0.40f, TrackState.Elevated, TrackState.Street,
                    RowCoins(0b010, ObstacleKind.Truck),
                    Clear(true),
                    Clear(false)),

                // ---- Late game. Only reachable once the band has climbed. ----
                Chunk("Gauntlet", 0.8f, 0.90f, TrackState.Street, TrackState.Street,
                    Row(0b011, ObstacleKind.Full),
                    Row(All, ObstacleKind.Low),
                    Row(0b110, ObstacleKind.Full),
                    Row(All, ObstacleKind.High),
                    Clear(true)),

                // Opens clear like the other bus chunks: a stopping bus only tests reading
                // ahead if there is room to read it.
                Chunk("Traffic jam", 0.8f, 0.88f, TrackState.Street, TrackState.Street,
                    Clear(false),
                    Row(0b001, ObstacleKind.StoppingBus),
                    Row(0b100, ObstacleKind.OncomingTruck),
                    Row(0b010, ObstacleKind.Full),
                    Clear(true)),

                // The full loop in one piece: climb, get paid, get pushed back down.
                Chunk("High road", 0.7f, 0.92f, TrackState.Street, TrackState.Street,
                    Row(All, ObstacleKind.Ramp),
                    RowCoins(All, ObstacleKind.Truck),
                    ArcRow(ObstacleKind.Truck),
                    RowCoins(All, ObstacleKind.Truck),
                    Clear(false),
                    Clear(false),
                    Row(0b010, ObstacleKind.Underpass),
                    Clear(true))
            };
        }

        private static ObstacleFormation Chunk(
            string name, float weight, float difficulty,
            TrackState entry, TrackState exit, params IntroRow[] rows)
            => new ObstacleFormation
            {
                Name = name,
                Rows = rows,
                Weight = weight,
                Difficulty = difficulty,
                Entry = entry,
                Exit = exit
            };

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
