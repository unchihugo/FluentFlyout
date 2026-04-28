namespace FluentFlyoutWPF.Classes.Utils;

public class VoicemeeterHelper : IDisposable {
    public static bool IsLoggedIn { get; private set; }

    public const float MAX_GAIN = 12.0f;
    
    public static bool Initialize() {
        // Ensure dll import
        bool success = VoicemeeterLoader.Load();

        if (!success) {
            IsLoggedIn = false;
            return false;
        }
        
        int result = VoicemeeterRemote.VBVMR_Login();

        IsLoggedIn = (result == 0);
        
        return IsLoggedIn;
    }
    
    private static void LogOut() {
        VoicemeeterRemote.VBVMR_Logout();
        IsLoggedIn = false;
        
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
    
    public void Dispose() {
        LogOut();
    }
}