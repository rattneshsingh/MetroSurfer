using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// What one kind of obstacle looks like in one state. Visual only: the generated
    /// colliders, roof and lethal volume stay exactly as the catalogue builds them. Either a
    /// model is stretched to fit that box, or — with no model — the generated shape is
    /// repainted.
    /// </summary>
    [System.Serializable]
    public sealed class ObstacleArt
    {
        public ObstacleKind Kind;

        [Tooltip("Model shown instead of the grey box. Any colliders on it are removed.")]
        public GameObject Visual;

        [Tooltip("Extra turn in degrees after the model is lined up with the track. 180 makes " +
                 "a vehicle face the player, for oncoming traffic.")]
        public float Yaw;

        [Header("Paint — used when there is no model")]
        [Tooltip("Repaint the generated shape instead of leaving it the default grey.")]
        public bool UsePaint;

        public Color Paint = Color.white;

        [Tooltip("Diagonal hazard stripes, in Paint and black. For things that must be read at a glance.")]
        public bool HazardStripes;

        /// <summary>True when this entry changes anything at all.</summary>
        public bool HasEffect => Visual != null || UsePaint;
    }
}
