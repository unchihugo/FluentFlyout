using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FluentFlyoutWPF.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public ObservableCollection<string> Contributors { get; } =
    [
        "unchihugo",
           "LiAuTraver",
           "kitUIN",
           "DmitriySalnikov",
           "AksharDP",
           "nopeless",
           "tnhung2011",
           "xsm2",
           "Ste3798",
           "weiss-rn",
           "lechixy",
           "VeryFat123",
           "ysfemreAlbyrk",
           "CC-D-Y",
           "LOGYT-Eberk",
           "logounet",
           "Nikolai-Misha",
           "bropines",
           "RandomKuchen",
           "ebraheemelteyb",
           "genotypePL",
           "Pigeon0v0",
           "fortIItude",
           "maksymser",
           "se34k",
           "nath89-52",
           "yoyo435",
           "gustavo-bozzano",
           "Xshadow9",
           "Aris-Offline",
           "Y-PLONI",
           "bywhite0",
           "Tomflame-4ever",
           "Hykerisme",
           "oski165",
           "thinkii",
           "trlef19",
           "v3vishal",
           "CielWhitefox",
           "kek353",
           "ThePerson-o",
           "naturbrilian",
           "avshalombegler",
           "Atalanttore",
           "Simnico99",
            "NimiGames68"
     ];

    public string ContributorsText => string.Join(", ", Contributors);

    public class LicenseInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string License { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public ObservableCollection<LicenseInfo> Licenses { get; } =
    [
        new LicenseInfo
            {
                Name = "Dubya.WindowsMediaController",
                Version = "2.5.5",
                License = "MIT",
                Url = "https://github.com/DubyaDude/WindowsMediaController"
            },
            new LicenseInfo
            {
                Name = "MicaWPF",
                Version = "6.3.2",
                License = "MIT",
                Url = "https://github.com/Simnico99/MicaWPF"
            },
            new LicenseInfo
            {
                Name = "Microsoft.Toolkit.Uwp.Notifications",
                Version = "7.1.3",
                License = "MIT",
                Url = "https://github.com/CommunityToolkit/WindowsCommunityToolkit"
            },
            new LicenseInfo
            {
                Name = "NLog",
                Version = "6.0.6",
                License = "BSD-3-Clause",
                Url = "https://nlog-project.org/"
            },
            new LicenseInfo
            {
                Name = "System.Drawing.Common",
                Version = "10.0.0",
                License = "MIT",
                Url = "https://dot.net/"
            },
            new LicenseInfo
            {
                Name = "WPF-UI",
                Version = "4.2.0",
                License = "MIT",
                Url = "https://github.com/lepoco/wpfui"
            },
            new LicenseInfo
            {
                Name = "WPF-UI.Tray",
                Version = "4.2.0",
                License = "MIT",
                Url = "https://github.com/lepoco/wpfui"
            },
            new LicenseInfo
            {
                Name = "CommunityToolkit.Mvvm",
                Version = "8.4.0-preview3",
                License = "MIT",
                Url = "https://github.com/CommunityToolkit/dotnet"
            }
    ];

    [RelayCommand]
    private void OpenLicenseUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Silently fail if URL cannot be opened
            }
        }
    }
}