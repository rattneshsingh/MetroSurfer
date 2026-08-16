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

        public int Count => _patterns == null ? 0 : _patterns.Length;

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
        }
    }
}
