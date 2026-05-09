using System;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.Classes;

public class VoicemeeterRemote
{
    private const string DllName = "VoicemeeterRemote64.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int VBVMR_Login();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int VBVMR_Logout();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int VBVMR_IsParametersDirty();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int VBVMR_GetParameterFloat([MarshalAs(UnmanagedType.LPStr)] string name, ref float value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int VBVMR_SetParameterFloat([MarshalAs(UnmanagedType.LPStr)] string name, float value);
}