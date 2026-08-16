using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Runner.Player
{
    /// <summary>
    /// Turns keyboard and touch into a per-frame <see cref="PlayerCommand"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the New Input System's device state directly rather than going through an
    /// .inputactions asset. Two reasons: a swipe gesture cannot be expressed as an action
    /// binding anyway, and this needs zero Editor wiring — no generated C# wrapper, no
    /// PlayerInput component, nothing to drag.
    /// </para>
    /// <para>
    /// <see cref="Poll"/> is pull-based and caches per frame, so it does not matter whether
    /// this component's Update runs before or after its consumer. There is no script
    /// execution order to get wrong.
    /// </para>
    /// </remarks>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Swipe — thresholds are a fraction of the shorter screen edge, so they are DPI independent")]
        [SerializeField, Range(0.01f, 0.25f)] private float _swipeThresholdFraction = 0.045f;
        [SerializeField] private float _swipeThresholdMinPixels = 24f;

        [Tooltip("A drag slower than this never registers as a swipe.")]
        [SerializeField] private float _swipeMaxDuration = 0.45f;

        [Tooltip("A press released within this distance and time counts as a tap (= jump).")]
        [SerializeField, Range(0.005f, 0.1f)] private float _tapThresholdFraction = 0.02f;
        [SerializeField] private float _tapMaxDuration = 0.25f;

        [Header("Keyboard")]
        [SerializeField] private bool _keyboardEnabled = true;

        private PlayerCommand _cached;
        private int _cachedFrame = -1;

        private bool _touchActive;
        private bool _swipeResolved;
        private Vector2 _touchStart;
        private float _touchStartTime;

        /// <summary>Intent for the current frame. Safe to call more than once per frame.</summary>
        public PlayerCommand Poll()
        {
            int frame = Time.frameCount;
            if (_cachedFrame == frame)
            {
                return _cached;
            }

            _cachedFrame = frame;
            _cached = ReadKeyboard() | ReadTouch();
            return _cached;
        }

        /// <summary>Drops a half-finished swipe. Called on death and restart.</summary>
        public void ResetGesture()
        {
            _touchActive = false;
            _swipeResolved = false;
            _cached = PlayerCommand.None;
            _cachedFrame = -1;
        }

        private PlayerCommand ReadKeyboard()
        {
            if (!_keyboardEnabled)
            {
                return PlayerCommand.None;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return PlayerCommand.None;
            }

            PlayerCommand command = PlayerCommand.None;

            // Named key properties are cached controls — no lookup, no allocation.
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
            {
                command |= PlayerCommand.Left;
            }

            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                command |= PlayerCommand.Right;
            }

            if (keyboard.wKey.wasPressedThisFrame ||
                keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame)
            {
                command |= PlayerCommand.Jump;
            }

            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            {
                command |= PlayerCommand.Slide;
            }

            return command;
        }

        private PlayerCommand ReadTouch()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null)
            {
                return PlayerCommand.None;
            }

            TouchControl touch = screen.primaryTouch;
            float shortEdge = Mathf.Min(Screen.width, Screen.height);
            float swipeThreshold = Mathf.Max(_swipeThresholdMinPixels, shortEdge * _swipeThresholdFraction);
            float tapThreshold = shortEdge * _tapThresholdFraction;

            if (touch.press.wasPressedThisFrame)
            {
                _touchActive = true;
                _swipeResolved = false;
                _touchStart = touch.position.ReadValue();
                _touchStartTime = Time.unscaledTime;
                return PlayerCommand.None;
            }

            if (!_touchActive)
            {
                return PlayerCommand.None;
            }

            Vector2 delta = touch.position.ReadValue() - _touchStart;
            float held = Time.unscaledTime - _touchStartTime;

            if (touch.press.wasReleasedThisFrame)
            {
                _touchActive = false;

                bool wasTap = !_swipeResolved &&
                              held <= _tapMaxDuration &&
                              delta.sqrMagnitude <= tapThreshold * tapThreshold;

                return wasTap ? PlayerCommand.Jump : PlayerCommand.None;
            }

            if (_swipeResolved || held > _swipeMaxDuration)
            {
                return PlayerCommand.None;
            }

            // Fire the moment the threshold is crossed rather than waiting for release —
            // waiting adds the length of the swipe to input latency and feels sluggish.
            float absX = Mathf.Abs(delta.x);
            float absY = Mathf.Abs(delta.y);

            if (absX < swipeThreshold && absY < swipeThreshold)
            {
                return PlayerCommand.None;
            }

            _swipeResolved = true;

            if (absX >= absY)
            {
                return delta.x > 0f ? PlayerCommand.Right : PlayerCommand.Left;
            }

            return delta.y > 0f ? PlayerCommand.Jump : PlayerCommand.Slide;
        }
    }
}
