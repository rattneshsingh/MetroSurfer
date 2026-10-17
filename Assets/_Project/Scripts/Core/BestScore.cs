using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// The highest score this device has seen, kept in PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Read once and written only when a run ends, so it never touches the frame loop.
    /// </remarks>
    public sealed class BestScore
    {
        private const string Key = "runner.best_score";

        private int _value;

        public int Value => _value;

        public BestScore()
        {
            _value = PlayerPrefs.GetInt(Key, 0);
        }

        /// <summary>Records a finished run. True when it beat the previous best.</summary>
        public bool Submit(int score)
        {
            if (score <= _value)
            {
                return false;
            }

            _value = score;
            PlayerPrefs.SetInt(Key, score);
            PlayerPrefs.Save();
            return true;
        }
    }
}
