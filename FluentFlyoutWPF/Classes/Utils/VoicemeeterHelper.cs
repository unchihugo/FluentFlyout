using FluentFlyout.Classes.Settings;

namespace FluentFlyoutWPF.Classes.Utils;

public class VoicemeeterHelper : IDisposable {
    private bool _isLoggedIn;
    
    public const float MIN_GAIN = -60.0f;
    public const float MAX_GAIN = 12.0f;

    public const float AMPLITUDE = MAX_GAIN - MIN_GAIN;

    public static VoicemeeterHelper? Instance = null;
    
    #region Initialization
    public void Login() {
        int result = VoicemeeterRemote.VBVMR_Login();

        _isLoggedIn = (result == 0);

        if (_isLoggedIn) {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            System.Diagnostics.Debug.WriteLine("Logged In into Voicemeeter");
        } else {
            System.Diagnostics.Debug.WriteLine($"Failed to log in to Voicemeeter, error code: {result}");
        }
            
    }
    
    private void LogOut() {
        VoicemeeterRemote.VBVMR_Logout();
        _isLoggedIn = false;
        
        System.Diagnostics.Debug.WriteLine("Logged out of Voicemeeter");
    }

    private void OnProcessExit(object? sender, EventArgs e) {
        Dispose();
    }
    
    public void Dispose() {
        LogOut();
        
        GC.SuppressFinalize(this);
    }
    
    #endregion

    private void EnsureReady() {
        if (!_isLoggedIn) {
            throw new InvalidOperationException("Voicemeeter is not available");
        } 
        
        // Ensure values are up-to-date
        int _ = VoicemeeterRemote.VBVMR_IsParametersDirty();
    }
    
    public float GetComponentGain(int index, VoicemeeterComponent component) {
        EnsureReady();
        
        float gain = 0;
        
        int result = VoicemeeterRemote.VBVMR_GetParameterFloat($"{VoicemeeterComponentExtension.GetVoicemeeterComponentString(component)}[{index}].Gain", ref gain);
        
        // If the result was positive, return the value
        if (result == 0) return gain;
        
        System.Diagnostics.Debug.WriteLine($"Failed to get gain for strip {index}, error code: {result}");

        return gain;
    }

    public void SetComponentGain(int index, VoicemeeterComponent component, float gain) {
        EnsureReady();
        
        VoicemeeterRemote.VBVMR_SetParameterFloat($"{VoicemeeterComponentExtension.GetVoicemeeterComponentString(component)}[{index}].Gain", gain);
    }

    public bool GetComponentMute(int index, VoicemeeterComponent component) {
        EnsureReady();

        float val = 0;
        
        int result = VoicemeeterRemote.VBVMR_GetParameterFloat($"{VoicemeeterComponentExtension.GetVoicemeeterComponentString(component)}[{index}].Mute", ref val);
        
        if (result == 0) return val > 0.0f;
        
        System.Diagnostics.Debug.WriteLine($"Failed to get gain for strip {index}, error code: {result}");
        
        return val > 0.0f;
    }

    public void SetComponentMute(int index, VoicemeeterComponent component, bool mute) {
        EnsureReady();
        
        VoicemeeterRemote.VBVMR_SetParameterFloat($"{VoicemeeterComponentExtension.GetVoicemeeterComponentString(component)}[{index}].Mute", mute ? 1.0f : 0.0f);
    }
}