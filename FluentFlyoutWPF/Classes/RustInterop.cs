using System;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.Classes
{
    public static class RustInterop
    {
        private const string DllName = "fluent_flyout_core.dll";

        [DllImport(DllName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr get_tidal_title();

        [DllImport(DllName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr get_tidal_artist();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void free_string(IntPtr s);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool tidal_play_pause();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool tidal_next();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool tidal_previous();

        public static string GetTidalTitle()
        {
            IntPtr ptr = get_tidal_title();
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
            }
            finally
            {
                free_string(ptr);
            }
        }

        public static string GetTidalArtist()
        {
            IntPtr ptr = get_tidal_artist();
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
            }
            finally
            {
                free_string(ptr);
            }
        }
    }
}
