using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// A vehicle that is not keeping pace with the world — it surges ahead and drops back, so
    /// the gap between it and the truck in front opens and closes while you are deciding
    /// whether to jump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drift is a bounded oscillation around the spawn position rather than a constant
    /// relative velocity. Constant velocity would eventually walk the vehicle out of its own
    /// segment and into another's geometry, and it would be somewhere unpredictable by the time
    /// its segment recycled. An oscillation stays in bounds forever, and in traffic it reads the
    /// same: something moving at not-quite-your-speed.
    /// </para>
    /// <para>
    /// No Update of its own — <see cref="SegmentSpawner"/> ticks these in one batched loop,
    /// because a few dozen MonoBehaviour Update calls cost more than one loop that does the
    /// same work.
    /// </para>
    /// </remarks>
    public sealed class MovingObstacle : MonoBehaviour
    {
        private Transform _transform;
        private float _baseZ;
        private float _amplitude;
        private float _angularSpeed;
        private float _phase;
        private float _time;
        private bool _active;
        private DriftMode _mode;
        private float _approachSpeed;
        private float _maxTravel;
        private float _cruiseSpeed;
        private float _brakeTriggerZ;
        private float _brakeDuration;
        private float _brakeStartTime;
        private bool _braking;
        private bool _brakeJustStarted;

        public void Initialize()
        {
            _transform = transform;
        }

        /// <summary>Traffic drifting in your direction. Called on spawn, once positioned.</summary>
        public void BeginOscillation(float amplitude, float period, float phase)
        {
            Reset();
            _mode = DriftMode.Oscillate;
            _amplitude = amplitude;
            _angularSpeed = period > 0.01f ? 2f * Mathf.PI / period : 0f;
            _phase = phase;
        }

        /// <summary>Traffic coming the other way, closing on the player.</summary>
        public void BeginApproach(float speed, float maxTravel)
        {
            Reset();
            _mode = DriftMode.Approach;
            _approachSpeed = speed;
            _maxTravel = maxTravel;
        }

        /// <summary>
        /// Traffic that rolls along and then stops in your lane.
        /// </summary>
        /// <param name="triggerZ">
        /// World Z at which it starts braking. Proximity, not elapsed time — these spawn a
        /// hundred metres out, so a timer would have them stopped long before you arrived and
        /// the whole point would be lost.
        /// </param>
        public void BeginBraking(float cruiseSpeed, float triggerZ, float brakeDuration)
        {
            Reset();
            _mode = DriftMode.Decelerate;
            _cruiseSpeed = cruiseSpeed;
            _brakeTriggerZ = triggerZ;
            _brakeDuration = Mathf.Max(0.05f, brakeDuration);
        }

        /// <summary>True once, on the frame braking begins. Drives the warning colour.</summary>
        public bool ConsumeBrakeStarted()
        {
            if (!_brakeJustStarted)
            {
                return false;
            }

            _brakeJustStarted = false;
            return true;
        }

        private void Reset()
        {
            if (_transform == null)
            {
                _transform = transform;
            }

            _baseZ = _transform.localPosition.z;
            _time = 0f;
            _active = true;
            _braking = false;
            _brakeJustStarted = false;
            _brakeStartTime = 0f;
        }

        public void Stop()
        {
            _active = false;
        }

        public void Tick(float deltaTime)
        {
            if (!_active)
            {
                return;
            }

            _time += deltaTime;

            Vector3 position = _transform.localPosition;

            if (_mode == DriftMode.Decelerate)
            {
                position.z = _baseZ + BrakingTravel();
                _transform.localPosition = position;
                return;
            }

            if (_mode == DriftMode.Approach)
            {
                // Clamped so it settles somewhere predictable rather than running off to
                // infinity — by then it is far behind the player and about to be recycled.
                float travelled = Mathf.Min(_approachSpeed * _time, _maxTravel);
                position.z = _baseZ - travelled;
            }
            else
            {
                position.z = _baseZ + Mathf.Sin(_time * _angularSpeed + _phase) * _amplitude;
            }

            _transform.localPosition = position;
        }

        /// <summary>
        /// Distance covered while cruising, then the integral of a linear ramp down to zero.
        /// It settles at a fixed offset rather than growing without bound, so the bus cannot
        /// wander out of its own segment.
        /// </summary>
        private float BrakingTravel()
        {
            if (!_braking)
            {
                if (_transform.position.z <= _brakeTriggerZ)
                {
                    _braking = true;
                    _brakeJustStarted = true;
                    _brakeStartTime = _time;
                }
                else
                {
                    return _cruiseSpeed * _time;
                }
            }

            float sinceBrake = Mathf.Min(_time - _brakeStartTime, _brakeDuration);
            float decelerating = _cruiseSpeed * (sinceBrake - sinceBrake * sinceBrake / (2f * _brakeDuration));

            return _cruiseSpeed * _brakeStartTime + decelerating;
        }
    }
}
