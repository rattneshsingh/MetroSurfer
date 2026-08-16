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
    }
}
