using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// Lightweight hit-effect budget. Grants the "big" hit effect to only the FIRST hit
    /// within a short time window; every other hit inside that window is told to use a
    /// cheaper "light" effect instead. This caps how many expensive hit VFX play at once
    /// when many enemies are struck almost simultaneously (AoE / multi-hit / crowds).
    ///
    /// Global + static so every victim shares one budget. Uses unscaled time so hitstop /
    /// timescale freezes do not stall the window. Reset on play start for Fast Play Mode
    /// (domain reload disabled).
    /// </summary>
    public static class HitEffectBudget
    {
        /// <summary>Length of the "one big effect" window, in seconds (unscaled time).</summary>
        public static float WindowDuration = 0.08f;

        private static float _windowEndTime = -999f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _windowEndTime = -999f;
        }

        /// <summary>
        /// Returns true if this hit should play the BIG effect (it is the first hit of a new
        /// window), or false if it should fall back to the LIGHT effect (another hit already
        /// claimed the current window). Calling this consumes the budget for the window.
        /// </summary>
        public static bool TryConsumeBig()
        {
            float now = Time.unscaledTime;
            if (now >= _windowEndTime)
            {
                _windowEndTime = now + Mathf.Max(0f, WindowDuration);
                return true;
            }
            return false;
        }

        /// <summary>Manually clears the current window so the next hit is treated as big.</summary>
        public static void Reset() => _windowEndTime = -999f;
    }
}
