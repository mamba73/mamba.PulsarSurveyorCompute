// Plugin/Services/AudioService.cs
using Sandbox.Game;
using VRage.Audio;
using VRage.Game;

namespace Plugin.Services
{
    public class AudioService
    {
        private int _ticksSinceLastSound = 0;

        /// <summary>
        /// Interval between warning beeps (30 ticks = ~0.5s at 60 TPS).
        /// Prevents the alert from becoming an uninterrupted tone during sustained danger.
        /// </summary>
        private const int SOUND_INTERVAL = 30;

        /// <summary>
        /// Plays the native SE "Unable" HUD beep during active collision warnings.
        /// Resets the counter when danger clears so the next event fires immediately.
        /// </summary>
        public void PlayWarningSound(bool isWarning)
        {
            if (!isWarning)
            {
                _ticksSinceLastSound = 0; // Reset so next warning triggers immediately
                return;
            }

            _ticksSinceLastSound++;
            if (_ticksSinceLastSound >= SOUND_INTERVAL)
            {
                MyVisualScriptLogicProvider.PlayHudSound(MyGuiSounds.HudUnable, 0);
                _ticksSinceLastSound = 0;
            }
        }
    }
}
