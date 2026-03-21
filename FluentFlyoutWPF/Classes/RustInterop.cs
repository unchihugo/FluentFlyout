using System;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.Classes
{
    public static class RustInterop
    {
        private const string DllName = "fluent_flyout_core.dll";

        [DllImport(DllName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr get_media_title(bool exclusive);

        [DllImport(DllName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr get_media_artist(bool exclusive);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void free_string(IntPtr s);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool media_play_pause(bool exclusive);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool media_next(bool exclusive);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool media_previous(bool exclusive);

        public static string GetMediaTitle(bool exclusive)
        {
            IntPtr ptr = get_media_title(exclusive);
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            finally
            {
                free_string(ptr);
            }
        }

        public static string GetMediaArtist(bool exclusive)
        {
            IntPtr ptr = get_media_artist(exclusive);
            if (ptr == IntPtr.Zero) return string.Empty;
            try
            {
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            finally
            {
                free_string(ptr);
            }
        }
    }
}
