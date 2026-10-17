using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Runner.UI
{
    /// <summary>
    /// Every screen the player sees: title, running HUD, pause and game over. Knows nothing
    /// about GameManager — it is driven by calls in and three callbacks out.
    /// </summary>
    /// <remarks>
    /// All per-frame work is allocation-free: numbers go through SetText's numeric overloads
    /// behind an unchanged-value early-out, and the animations only write transforms and
    /// alpha. Animations run on unscaled time so they keep moving while the game is paused.
    /// </remarks>
    public sealed class HudController : MonoBehaviour
    {
        [Header("Running")]
        [SerializeField] private GameObject _runningRoot;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _coinsText;
        [SerializeField] private TMP_Text _distanceText;
        [SerializeField] private GameObject _multiplierBadge;
        [SerializeField] private TMP_Text _multiplierText;
        [SerializeField] private Button _pauseButton;

        [Header("Title")]
        [SerializeField] private GameObject _readyPanel;
        [SerializeField] private TMP_Text _readyBestText;
        [SerializeField] private TMP_Text _readyPrompt;

        [Header("Pause")]
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _pauseRestartButton;

        [Header("Game over")]
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private TMP_Text _finalScoreText;
        [SerializeField] private TMP_Text _finalCoinsText;
        [SerializeField] private TMP_Text _finalDistanceText;
        [SerializeField] private TMP_Text _finalBestText;
        [SerializeField] private GameObject _newBestBadge;
        [SerializeField] private Button _restartButton;

        [Header("Feel")]
        [Tooltip("How big the score and coin readouts swell when a coin lands.")]
        [SerializeField] private float _punchScale = 1.22f;
        [SerializeField] private float _punchDuration = 0.18f;
        [Tooltip("Breathing speed of the title-screen prompt.")]
        [SerializeField] private float _promptPulseSpeed = 3.2f;

        private System.Action _onRestart;
        private System.Action _onPause;
        private System.Action _onResume;

        private int _shownScore = int.MinValue;
        private int _shownCoins = int.MinValue;
        private int _shownDistance = int.MinValue;
        private int _shownMultiplier = int.MinValue;

        private float _scorePunch;
        private float _coinsPunch;
        private float _multiplierPunch;

        public void Bind(System.Action onRestart, System.Action onPause, System.Action onResume)
        {
            _onRestart = onRestart;
            _onPause = onPause;
            _onResume = onResume;

            // Remove first: this can be called again after a mid-play recompile, and
            // AddListener happily registers the same callback twice.
            Rebind(_restartButton, InvokeRestart);
            Rebind(_pauseRestartButton, InvokeRestart);
            Rebind(_pauseButton, InvokePause);
            Rebind(_resumeButton, InvokeResume);
        }

        private void OnDestroy()
        {
            Unbind(_restartButton, InvokeRestart);
            Unbind(_pauseRestartButton, InvokeRestart);
            Unbind(_pauseButton, InvokePause);
            Unbind(_resumeButton, InvokeResume);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _scorePunch = Settle(_scoreText, _scorePunch, dt);
            _coinsPunch = Settle(_coinsText, _coinsPunch, dt);
            _multiplierPunch = Settle(_multiplierText, _multiplierPunch, dt);

            if (_readyPrompt != null && _readyPanel != null && _readyPanel.activeSelf)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * _promptPulseSpeed);
                _readyPrompt.alpha = Mathf.Lerp(0.45f, 1f, wave);
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

            // A rising score earns a punch; a reset to zero does not.
            if (score > _shownScore && _shownScore != int.MinValue)
            {
                _scorePunch = _punchDuration;
            }

            _shownScore = score;
            _scoreText.SetText("{0:0}", score);
        }

        public void SetCoins(int coins)
        {
            if (coins == _shownCoins || _coinsText == null)
            {
                return;
            }

            if (coins > _shownCoins && _shownCoins != int.MinValue)
            {
                _coinsPunch = _punchDuration;
            }

            _shownCoins = coins;
            _coinsText.SetText("{0:0}", coins);
        }

        /// <summary>Whole metres, so the text only rebuilds when the number on screen changes.</summary>
        public void SetDistance(int metres)
        {
            if (metres == _shownDistance || _distanceText == null)
            {
                return;
            }

            _shownDistance = metres;
            _distanceText.SetText("{0:0} m", metres);
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

            bool rising = multiplier > _shownMultiplier && _shownMultiplier != int.MinValue;
            _shownMultiplier = multiplier;

            bool earned = multiplier > 1;
            SetActive(_multiplierBadge != null ? _multiplierBadge : _multiplierText.gameObject, earned);

            if (earned)
            {
                _multiplierText.SetText("x{0:0}", multiplier);

                if (rising)
                {
                    _multiplierPunch = _punchDuration * 1.5f;
                }
            }
        }

        public void ShowReady(int bestScore)
        {
            SetActive(_readyPanel, true);
            SetActive(_runningRoot, false);
            SetActive(_pausePanel, false);
            SetActive(_gameOverPanel, false);

            if (_readyBestText != null)
            {
                bool hasBest = bestScore > 0;
                SetActive(_readyBestText.gameObject, hasBest);

                if (hasBest)
                {
                    _readyBestText.SetText("BEST  {0:0}", bestScore);
                }
            }
        }

        public void ShowPlaying()
        {
            SetActive(_readyPanel, false);
            SetActive(_runningRoot, true);
            SetActive(_pausePanel, false);
            SetActive(_gameOverPanel, false);
            SetActive(_pauseButton != null ? _pauseButton.gameObject : null, true);
        }

        public void ShowPaused()
        {
            SetActive(_pausePanel, true);
            SetActive(_pauseButton != null ? _pauseButton.gameObject : null, false);
        }

        public void ShowGameOver(int finalScore, int coins, int metres, int bestScore, bool isNewBest)
        {
            SetActive(_readyPanel, false);
            SetActive(_pausePanel, false);
            SetActive(_gameOverPanel, true);
            SetActive(_pauseButton != null ? _pauseButton.gameObject : null, false);
            SetActive(_newBestBadge, isNewBest);

            if (_finalScoreText != null)
            {
                _finalScoreText.SetText("{0:0}", finalScore);
            }

            if (_finalCoinsText != null)
            {
                _finalCoinsText.SetText("{0:0}", coins);
            }

            if (_finalDistanceText != null)
            {
                _finalDistanceText.SetText("{0:0} m", metres);
            }

            if (_finalBestText != null)
            {
                _finalBestText.SetText("{0:0}", bestScore);
            }
        }

        /// <summary>Eases a punched readout back to its resting size.</summary>
        private float Settle(TMP_Text text, float remaining, float deltaTime)
        {
            if (remaining <= 0f || text == null)
            {
                return 0f;
            }

            remaining = Mathf.Max(0f, remaining - deltaTime);
            float t = _punchDuration > 0f ? Mathf.Clamp01(remaining / _punchDuration) : 0f;
            float scale = 1f + (_punchScale - 1f) * t;
            text.rectTransform.localScale = new Vector3(scale, scale, 1f);
            return remaining;
        }

        private void InvokeRestart()
        {
            _onRestart?.Invoke();
        }

        private void InvokePause()
        {
            _onPause?.Invoke();
        }

        private void InvokeResume()
        {
            _onResume?.Invoke();
        }

        private static void Rebind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
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
