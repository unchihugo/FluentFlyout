using FluentFlyout.Classes.Settings;

namespace FluentFlyoutWPF.Classes.Utils;

public class VoicemeeterHelper : IDisposable {
    public static bool IsLoggedIn { get; private set; }

    public const float MIN_GAIN = -60.0f;
    public const float MAX_GAIN = 12.0f;

    public const float AMPLITUDE = MAX_GAIN - MIN_GAIN;
    
    public static bool Initialize() {
        // Ensure dll import
        bool success = VoicemeeterLoader.Load();

        if (!success) {
            IsLoggedIn = false;
            return false;
        }
        
        int result = VoicemeeterRemote.VBVMR_Login();

        IsLoggedIn = (result == 0);

        SettingsManager.Current.IsVoicemeeterLoaded = IsLoggedIn;
        
        return IsLoggedIn;
    }
    
    private static void LogOut() {
        VoicemeeterRemote.VBVMR_Logout();
        IsLoggedIn = false;
        SettingsManager.Current.IsVoicemeeterLoaded = IsLoggedIn;
        
        Console.WriteLine("Logged out of Voicemeeter");
    }
    
    public static float GetStripGain(int stripIndex) {
        // Ensure values are up-to-date
        int _ = VoicemeeterRemote.VBVMR_IsParametersDirty();
        
        float gain = 0;
        
        int result = VoicemeeterRemote.VBVMR_GetParameterFloat($"Strip[{stripIndex}].Gain", ref gain);
        
        // If the result was positive, return the value
        if (result == 0) return gain;
        
        System.Diagnostics.Debug.WriteLine($"Failed to get gain for strip {stripIndex}, error code: {result}");

        return gain;
    }

    public static void setStripGain(int stripIndex, float gain) {
        VoicemeeterRemote.VBVMR_SetParameterFloat($"Strip[{stripIndex}].Gain", gain);
    }

    public static bool GetStripMute(int stripIndex) {
        // Ensure values are up-to-date
        int _ = VoicemeeterRemote.VBVMR_IsParametersDirty();

        float val = 0;
        
        int result = VoicemeeterRemote.VBVMR_GetParameterFloat($"Strip[{stripIndex}].Mute", ref val);
        
        if (result == 0) return val > 0.0f;
        
        System.Diagnostics.Debug.WriteLine($"Failed to get gain for strip {stripIndex}, error code: {result}");
        
        return val > 0.0f;
    }

    public static void SetStripMute(int stripIndex, bool mute) {
        VoicemeeterRemote.VBVMR_SetParameterFloat($"Strip[{stripIndex}].Mute", mute ? 1.0f : 0.0f);
    }
    
    public void Dispose() {
        LogOut();
    }
}