namespace FluentFlyoutWPF.Classes.Utils;

public enum VoicemeeterComponent
{
    STRIP,
    BUS
}

public static class VoicemeeterComponentExtension
{
    public static string GetVoicemeeterComponentString(VoicemeeterComponent component)
    {
        switch (component)
        {
            case VoicemeeterComponent.STRIP:
                return "Strip";
            case VoicemeeterComponent.BUS:
                return "Bus";
            default:
                throw new ArgumentOutOfRangeException(nameof(component), component, null);
        }
    }

    public static int GetVoicemeeterComponentInt(VoicemeeterComponent component)
    {
        switch (component)
        {
            case VoicemeeterComponent.STRIP:
                return 0;
            case VoicemeeterComponent.BUS:
                return 1;
            default:
                throw new ArgumentOutOfRangeException(nameof(component), component, null);
        }
    }

    public static VoicemeeterComponent GetVoicemeeterComponentFromString(string component)
    {
        switch (component)
        {
            case "Strip":
                return VoicemeeterComponent.STRIP;
            case "Bus":
                return VoicemeeterComponent.BUS;
            default:
                throw new ArgumentOutOfRangeException(nameof(component), component, null);
        }
    }

    public static VoicemeeterComponent GetVoicemeeterComponentFromInt(int component)
    {
        switch (component)
        {
            case 0:
                return VoicemeeterComponent.STRIP;
            case 1:
                return VoicemeeterComponent.BUS;
            default:
                throw new ArgumentOutOfRangeException(nameof(component), component, null);
        }
    }
}