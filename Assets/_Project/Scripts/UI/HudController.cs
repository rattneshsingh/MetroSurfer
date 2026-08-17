using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runner.UI
{
    /// <summary>
    /// Score readout and game-over panel. Knows nothing about GameManager — it is driven by
    /// calls in and one restart callback out.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _multiplierText;
        [SerializeField] private GameObject _readyPanel;
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private TMP_Text _finalScoreText;
        [SerializeField] private Button _restartButton;

        private System.Action _onRestart;
        private int _shownScore = int.MinValue;
        private int _shownMultiplier = int.MinValue;

        public void BindRestart(System.Action onRestart)
        {
            _onRestart = onRestart;

            if (_restartButton != null)
            {
                // Remove first: this can be called again after a mid-play recompile, and
                // AddListener happily registers the same callback twice.
                _restartButton.onClick.RemoveListener(InvokeRestart);
                _restartButton.onClick.AddListener(InvokeRestart);
            }
        }

        private void OnDestroy()
        {
            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveListener(InvokeRestart);
            }
        }

        /// <summary>
        /// Called every frame. Two things keep it allocation-free: the early-out when the
        /// integer has not changed, and SetText's numeric overload. Writing
        /// <c>_scoreText.text = score.ToString()</c> instead would allocate a string every
        /// single frame — the classic HUD leak.
        /// </summary>
        public void SetScore(int score)
        {
            if (score == _shownScore || _scoreText == null)
            {
                return;
            }

            _shownScore = score;
            _scoreText.SetText("{0:0}", score);
        }

        /// <summary>
        /// Hidden at x1. A multiplier that is always on screen reads as decoration; one that
        /// appears when you earn it reads as something you can lose.
        /// </summary>
        public void SetMultiplier(int multiplier)
        {
            if (multiplier == _shownMultiplier || _multiplierText == null)
            {
                return;
            }

            _shownMultiplier = multiplier;

            bool earned = multiplier > 1;
            if (_multiplierText.gameObject.activeSelf != earned)
            {
                _multiplierText.gameObject.SetActive(earned);
            }

            if (earned)
            {
                _multiplierText.SetText("x{0:0}", multiplier);
            }
        }

        public void ShowReady()
        {
            SetActive(_readyPanel, true);
            SetActive(_gameOverPanel, false);
        }

        public void ShowPlaying()
        {
            SetActive(_readyPanel, false);
            SetActive(_gameOverPanel, false);
        }

        public void ShowGameOver(int finalScore)
        {
            SetActive(_readyPanel, false);
            SetActive(_gameOverPanel, true);

            if (_finalScoreText != null)
            {
                _finalScoreText.SetText("{0:0}", finalScore);
            }
        }

        private void InvokeRestart()
        {
            _onRestart?.Invoke();
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
