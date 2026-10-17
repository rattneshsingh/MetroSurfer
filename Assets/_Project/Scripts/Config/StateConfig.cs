using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// A themed state (Delhi, Punjab, Kerala, ...) expressed purely as data. This is the seam
    /// that keeps states from becoming scenes — one gameplay scene reads one of these at
    /// runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 0 populates none of this. The type exists so that gameplay code is written
    /// against it from day one; adding state four must never mean editing a gameplay script.
    /// </para>
    /// <para>
    /// Content is referenced by plain string address because com.unity.addressables is not
    /// installed yet. When it lands, every <c>string ...Address</c> field below becomes an
    /// <c>AssetReference</c> and the loader changes — nothing else does.
    /// </para>
    /// <para>
    /// Hard rule: no gameplay system may branch on <see cref="DisplayName"/> or any other
    /// identity field. If a feature needs to know *which* state it is running, that feature
    /// is missing a data field here.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Runner/State Config", fileName = "StateConfig")]
    public sealed class StateConfig : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _displayName;

        [Header("Palette")]
        [SerializeField] private Color _groundColor = new Color(0.32f, 0.32f, 0.34f);
        [SerializeField] private Color _obstacleColor = new Color(0.18f, 0.18f, 0.20f);
        [SerializeField] private Color _coinColor = new Color(0.95f, 0.78f, 0.25f);
        [SerializeField] private Color _skyColor = new Color(0.45f, 0.52f, 0.60f);

        [Header("Content — Addressables addresses (see class remarks)")]
        [SerializeField] private string[] _propSetAddresses;
        [SerializeField] private string _skylineSetAddress;
        [SerializeField] private string _landmarkAddress;
        [SerializeField] private string _trainPrefabAddress;
        [SerializeField] private string _vehiclePrefabAddress;
        [SerializeField] private string _musicTrackAddress;

        [Header("Art — direct references until Addressables lands")]
        [Tooltip("Per-kind models. A kind with no entry keeps the generated grey shape.")]
        [SerializeField] private ObstacleArt[] _obstacleArt;

        [Header("Scenery — the street either side of the track")]
        [Tooltip("Buildings lining the street. Each is scaled evenly to fill its slot, never stretched.")]
        [SerializeField] private GameObject[] _buildings;

        [Tooltip("Kerbside props, one per side at the start of each stretch: street lights, poles.")]
        [SerializeField] private GameObject[] _streetProps;

        [Tooltip("Extra turn in degrees so building fronts face the road. Flip by 180 if you see backs.")]
        [SerializeField] private float _buildingFacing;

        [Tooltip("Metres of pavement between the track edge and the buildings.")]
        [SerializeField] private float _sidewalkWidth = 2.5f;

        [Tooltip("Street frontage each building gets, in metres.")]
        [SerializeField] private float _buildingSlotLength = 9.5f;

        [Tooltip("Height of a kerbside prop, in metres.")]
        [SerializeField] private float _streetPropHeight = 4.5f;

        [SerializeField] private Color _sidewalkColor = new Color(0.62f, 0.60f, 0.56f);
        [SerializeField] private Color _lotColor = new Color(0.40f, 0.39f, 0.36f);

        [Header("Gameplay tuning overrides")]
        [Tooltip("Leave empty to fall back to the GameManager's shared table.")]
        [SerializeField] private ObstacleTable _obstacleTable;

        [Tooltip("Leave empty to fall back to the GameManager's shared curve.")]
        [SerializeField] private DifficultyCurve _difficulty;

        public string DisplayName => _displayName;

        public Color GroundColor => _groundColor;
        public Color ObstacleColor => _obstacleColor;
        public Color CoinColor => _coinColor;
        public Color SkyColor => _skyColor;

        public string[] PropSetAddresses => _propSetAddresses;
        public string SkylineSetAddress => _skylineSetAddress;
        public string LandmarkAddress => _landmarkAddress;
        public string TrainPrefabAddress => _trainPrefabAddress;
        public string VehiclePrefabAddress => _vehiclePrefabAddress;
        public string MusicTrackAddress => _musicTrackAddress;

        public ObstacleTable ObstacleTable => _obstacleTable;
        public DifficultyCurve Difficulty => _difficulty;

        public GameObject[] Buildings => _buildings;
        public GameObject[] StreetProps => _streetProps;
        public float BuildingFacing => _buildingFacing;
        public float SidewalkWidth => _sidewalkWidth;
        public float BuildingSlotLength => _buildingSlotLength;
        public float StreetPropHeight => _streetPropHeight;
        public Color SidewalkColor => _sidewalkColor;
        public Color LotColor => _lotColor;
        public bool HasScenery => _buildings != null && _buildings.Length > 0;

        /// <summary>The art for a kind, or null to keep the generated shape. Pool warm-up only.</summary>
        public ObstacleArt ArtFor(ObstacleKind kind)
        {
            if (_obstacleArt == null)
            {
                return null;
            }

            for (int i = 0; i < _obstacleArt.Length; i++)
            {
                ObstacleArt art = _obstacleArt[i];
                if (art != null && art.Kind == kind && art.HasEffect)
                {
                    return art;
                }
            }

            return null;
        }
    }
}
