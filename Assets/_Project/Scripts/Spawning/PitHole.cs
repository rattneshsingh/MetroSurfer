using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// The visible hole of a pit: an interior sunk below the road and the depth cutter over
    /// its mouth. Lets the spawner size it to the lane cell and drop the walls it shares with
    /// neighbouring pits, so a cluster of pits reads as one broken stretch of road rather than
    /// a grid of tiles.
    /// </summary>
    /// <remarks>
    /// Walls are switched with SetActive rather than Renderer.enabled, because Obstacle toggles
    /// renderers when it hides itself behind the player and would switch a merged wall back on.
    /// Purely visual: the lethal volume is not touched. There is no near wall — from the chase
    /// camera it always faces away and is never seen.
    /// </remarks>
    public sealed class PitHole : MonoBehaviour
    {
        private Transform _interior;
        private Transform _cutter;
        private GameObject _leftWall;
        private GameObject _rightWall;
        private GameObject _farWall;
        private float _depth;

        public void Initialize(
            Transform interior, Transform cutter, GameObject leftWall, GameObject rightWall, GameObject farWall,
            float depth)
        {
            _interior = interior;
            _cutter = cutter;
            _leftWall = leftWall;
            _rightWall = rightWall;
            _farWall = farWall;
            _depth = depth;
        }

        /// <summary>Opens the hole across a whole lane cell, so neighbours meet with no road between.</summary>
        public void Fit(float cellWidth, float cellLength)
        {
            _interior.localScale = new Vector3(cellWidth, _depth, cellLength);
            _cutter.localScale = new Vector3(cellWidth, 1f, cellLength);
        }

        /// <summary>Every wall back, for a pit fresh out of the pool.</summary>
        public void ResetWalls()
        {
            SetActive(_leftWall, true);
            SetActive(_rightWall, true);
            SetActive(_farWall, true);
        }

        public void OpenLeft() => SetActive(_leftWall, false);
        public void OpenRight() => SetActive(_rightWall, false);
        public void OpenFar() => SetActive(_farWall, false);

        private static void SetActive(GameObject wall, bool active)
        {
            if (wall != null && wall.activeSelf != active)
            {
                wall.SetActive(active);
            }
        }
    }
}
