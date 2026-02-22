// Plugin/Services/AudioService.cs
using Sandbox.Game;
using VRage.Audio;   // FIX: MyGuiSounds lives here
using VRage.Game;

namespace Plugin.Services
{
    public class AudioService
    {
        private int _ticksSinceLastSound = 0;
        private const int SOUND_INTERVAL = 30; // ~0.5s at 60 TPS

        /// <summary>
        /// Plays a native SE HUD alert beep during active collision warnings.
        /// Resets counter when danger clears so the next event fires immediately.
        /// </summary>
        public void PlayWarningSound(bool isWarning)
        {
            if (!isWarning)
            {
                _ticksSinceLastSound = 0;
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
