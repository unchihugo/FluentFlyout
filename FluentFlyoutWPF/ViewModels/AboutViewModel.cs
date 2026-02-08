// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FluentFlyoutWPF.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public ObservableCollection<string> Developers { get; } =
    [
        "unchihugo",
        "LiAuTraver",
        "kitUIN",
        "DmitriySalnikov",
        "AksharDP",
        "nopeless",
        "xsm2",
        "Simnico99",
        "neegool",
        "mclt0568"
     ];

    public ObservableCollection<string> Translators { get; } =
    [
        "aic-6301",
        "Aris-Offline",
        "Atalanttore",
        "AttackerMR", // 3mr9
        "avshalombegler",
        "biuseverinoneto",
        "bropines",
        "bywhite0",
        "CC-D-Y",
        "CielWhitefox",
        "ebraheemelteyb",
        "FenrirXVII",
        "fortIItude",
        "genotypePL",
        "gustavo-bozzano",
        "havrlisan",
        "hayiamzhengxum",
        "Hykerisme",
        "junior0liveira",
        "kek353",
        "lechixy",
        "logounet",
        "LOGYT-Eberk",
        "maksymser",
        "manuelitou",
        "nath89-52",
        "naturbrilian",
        "Nikolai-Misha",
        "NimiGames68",
        "oski165",
        "Pigeon0v0",
        "RandomKuchen",
        "se34k",
        "Self4215",
        "Ste3798",
        "theantonyis",
        "ThePerson-o",
        "thinkii",
        "tnhung2011",
        "Tomflame-4ever",
        "trlef19",
        "unchihugo",
        "v3vishal",
        "VeryFat123",
        "VolodymyrBryzh",
        "weiss-rn",
        "Xshadow9",
        "xsm2",
        "Y-PLONI",
        "ysfemreAlbyrk"
    ];


    public string DevelopersText => string.Join(", ", Developers);

    public string TranslatorsText => string.Join(", ", Translators);

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
            Name = "CommunityToolkit.Mvvm",
            Version = "8.4.0-preview3",
            License = "MIT",
            Url = "https://github.com/CommunityToolkit/dotnet"
        },
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
        new LicenseInfo{
            Name = "NAudio",
            Version = "2.2.1",
            License = "MIT",
            Url = "https://github.com/naudio/NAudio"
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
