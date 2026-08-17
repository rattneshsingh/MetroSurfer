using System.Collections;
using NUnit.Framework;
using Runner.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace Runner.PlayTests
{
    /// <summary>
    /// The tier that needs a running game loop. LaneController does its work in Update and
    /// probes the world with a raycast, so nothing here can be checked without real frames and
    /// real colliders.
    /// </summary>
    /// <remarks>
    /// This is where the vertical-movement bugs lived. Both of them — the ramp that launched the
    /// player on its own, and ground handling generally — are invisible to a unit test, because
    /// they only emerge from state carried across frames.
    /// </remarks>
    public sealed class LaneControllerPlayTests
    {
        private const float LaneWidth = 2.2f;
        private const float ScrollSpeed = 8f;

        private GameObject _player;
        private LaneController _lane;
        private GameObject _surface;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("Player");
            _player.layer = LayerMask.NameToLayer("Player");
            _player.transform.position = Vector3.zero;

            BoxCollider hitbox = _player.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.center = new Vector3(0f, 0.5f, 0f);
            hitbox.size = new Vector3(0.45f, 0.85f, 0.45f);

            Rigidbody body = _player.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            _lane = _player.AddComponent<LaneController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                Object.Destroy(_player);
            }

            if (_surface != null)
            {
                Object.Destroy(_surface);
            }
        }

        [UnityTest]
        public IEnumerator PlayerStartsGroundedOnTheStreet()
        {
            _lane.Configure(LaneWidth);
            _lane.SetRunning(true);

            yield return null;
            yield return null;

            Assert.IsTrue(_lane.IsGrounded, "Player should be standing on the street at y=0.");
            Assert.AreEqual(0f, _lane.SupportHeight, 0.01f);
            Assert.AreEqual(0f, _player.transform.position.y, 0.01f);
        }

        /// <summary>
        /// The regression this exists for: leaving a ramp used to apply a launch computed from a
        /// height change measured across several airborne frames but divided by one frame's
        /// delta. That read as tens of metres per second of climb, threw the player upward, and
        /// repeated on landing — an endless bounce. Riding a slope must keep both feet down.
        /// </summary>
        [UnityTest]
        public IEnumerator RidingASlopeNeverLaunchesThePlayer()
        {
            _surface = BuildSlope(startZ: 12f, run: 14f, rise: 1.2f);

            _lane.Configure(LaneWidth);
            _lane.SetRunning(true);

            yield return null;

            float highestSupport = 0f;
            float elapsed = 0f;

            // Scroll the slope past the stationary player, exactly as the world does.
            while (elapsed < 3.5f)
            {
                _surface.transform.position += Vector3.back * ScrollSpeed * Time.deltaTime;
                elapsed += Time.deltaTime;

                yield return null;

                highestSupport = Mathf.Max(highestSupport, _lane.SupportHeight);

                Assert.IsTrue(_lane.IsGrounded,
                    $"Player left the ground at {elapsed:0.00}s while riding a slope — it is launching them.");
            }

            Assert.Greater(highestSupport, 0.5f,
                "The player never actually climbed, so the test proved nothing about slopes.");
        }

        /// <summary>Standing on something raised has to register, or the tier multiplier is meaningless.</summary>
        [UnityTest]
        public IEnumerator RidingASlopeRaisesSupportHeight()
        {
            _surface = BuildSlope(startZ: 10f, run: 12f, rise: 1.2f);

            _lane.Configure(LaneWidth);
            _lane.SetRunning(true);

            yield return null;

            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                _surface.transform.position += Vector3.back * ScrollSpeed * Time.deltaTime;
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(_lane.SupportHeight, 0.5f,
                $"Support height stayed at {_lane.SupportHeight:0.00} — the ground probe is not finding the slope.");
            Assert.Greater(_player.transform.position.y, 0.5f, "The player's transform did not follow the surface.");
        }

        /// <summary>Losing the ground has to put the player in the air, or falling off a roof is impossible.</summary>
        [UnityTest]
        public IEnumerator PlayerFallsWhenTheSurfaceIsTakenAway()
        {
            _surface = BuildSlope(startZ: 8f, run: 10f, rise: 1.2f);

            _lane.Configure(LaneWidth);
            _lane.SetRunning(true);

            yield return null;

            float elapsed = 0f;
            while (elapsed < 2.2f)
            {
                _surface.transform.position += Vector3.back * ScrollSpeed * Time.deltaTime;
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(_lane.SupportHeight, 0.5f, "Player never got up the slope.");

            Object.Destroy(_surface);
            _surface = null;

            // Long enough to fall from roof height and settle, whatever the frame rate.
            float falling = 0f;
            while (falling < 1.5f)
            {
                falling += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(0f, _lane.SupportHeight, 0.05f, "Player did not return to street level.");
            Assert.IsTrue(_lane.IsGrounded, "Player never landed.");
        }

        [UnityTest]
        public IEnumerator ResetForRunReturnsThePlayerToTheStart()
        {
            _lane.Configure(LaneWidth);
            _lane.SetRunning(true);

            yield return null;

            _lane.ResetForRun();

            yield return null;

            Assert.AreEqual(0f, _player.transform.position.y, 0.01f);
            Assert.AreEqual(0f, _lane.SupportHeight, 0.01f);
            Assert.IsTrue(_lane.IsGrounded);
            Assert.IsFalse(_lane.IsSliding);
        }

        /// <summary>
        /// A slope on the Surface layer, low end nearest the player. Gentle enough that the rise
        /// per frame stays inside the controller's step tolerance, which is what makes it a ramp
        /// rather than a wall.
        /// </summary>
        private static GameObject BuildSlope(float startZ, float run, float rise)
        {
            GameObject slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.name = "TestSlope";
            slope.layer = LayerMask.NameToLayer("Surface");

            float length = Mathf.Sqrt(run * run + rise * rise);
            slope.transform.localScale = new Vector3(4f, 0.25f, length);
            slope.transform.rotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            slope.transform.position = new Vector3(0f, rise * 0.5f, startZ + run * 0.5f);

            return slope;
        }
    }
}
