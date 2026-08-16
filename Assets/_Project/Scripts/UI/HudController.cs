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
        [SerializeField] private GameObject _readyPanel;
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private TMP_Text _finalScoreText;
        [SerializeField] private Button _restartButton;

        private System.Action _onRestart;
        private int _shownScore = int.MinValue;

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
