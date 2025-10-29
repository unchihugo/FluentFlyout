<p align="center">
  <img width="65%" src="https://github.com/user-attachments/assets/56e921ff-e463-4ab3-b687-92f248dc727e">
</p>
<p align="center">
	<img alt="GitHub Release" src="https://img.shields.io/github/v/release/unchihugo/FluentFlyout">
	<img alt="Static Badge" src="https://img.shields.io/badge/downloads-50K%2B-blue?color=limegreen">
	<a href="https://hosted.weblate.org/engage/fluentflyout/"><img src="https://hosted.weblate.org/widget/fluentflyout/svg-badge.svg" alt="Translation status"/></a>
	<img alt="GitHub contributors" src="https://img.shields.io/github/contributors-anon/unchihugo/fluentflyout?labelColor=midnightblue&color=goldenrod">
</p>
<p align="center">
  <strong>English</strong> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.zh.md">简体中文</a> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.nl.md">Nederlands</a>
</p>

---
FluentFlyout is a simple and modern audio flyout for Windows, built with Fluent 2 Design principles.  
The UI seemingly blends in with Windows 11, providing you an uninterrupted, clean, and native-like experience when controlling your media.  

FluentFlyout features smooth animations, blends with your system's color themes, includes multiple layout positions and a suite of personalization settings while providing media controls and information in a nice and modern looking popup flyout. 

<a href="https://apps.microsoft.com/detail/9N45NSM4TNBP?mode=direct">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

## Features ✨
- Native Windows-like design
- Uses Fluent 2 components
- Utilises Windows Mica blur
- Supports Light and Dark mode
- Matches your device color theme
- Smooth animations
- Customizable flyout positions
- Includes Repeat All, Repeat One and Shuffle
- Listens to both volume and media inputs
- Sits unobtrusively in system tray
- **Audio flyout: Displays Cover, Title, Artist and media controls**
- **"Up Next" flyout: shows what's next when a song ends**
- **Lock Keys flyout: displays the status of lock keys at a glance**

## Audio flyout 🎵
<div align="center">
	<img height="205px" width="auto" src="https://github.com/user-attachments/assets/4dab1c12-594a-4785-bddc-0da1783bf1c8"> <img height="205px" src="https://github.com/user-attachments/assets/b4306026-b274-418b-a39e-78877e7610a7"> 	<img height="190px" src="https://github.com/user-attachments/assets/39de69fe-54c8-4b22-880c-7f0370b8dd9c"> <img height="190px" src="https://github.com/user-attachments/assets/a25adb0e-963a-49a5-8abb-d9a288c2ad9a"> <img height="190px" src="https://github.com/user-attachments/assets/2de44e7b-7e6c-4575-bf3b-0be2f741c994">
</div>
<details open>
<summary>v2.0 screenshots</summary>
<div align="center">
	<img height="220px" width="auto" src="https://github.com/user-attachments/assets/e45592d5-8576-4d6a-8679-56baacccd585"> <img height="220px" width="auto" src="https://github.com/user-attachments/assets/ff2fcfab-8e24-48cf-9bdf-d35252eb3e67">
</div>
</details>

## How to install
### Using Microsoft Store (Recommended)
<a href="https://apps.microsoft.com/detail/9N45NSM4TNBP?mode=direct">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="300"/>
</a>

> Looking for FluentFlyout Settings? You can access it by clicking the system tray icon
### Using .msixbundle installer
> [!Important]
> It's highly recommended to download FluentFlyout from MS store, as it's more convenient and provides auto updates
1. Go to the [latest release](https://github.com/unchihugo/FluentFlyout/releases/latest) page
2. Download the **"*.cer"** file *(real certificates cost a lot of money)*
3. Open the certificate and press **"Install Certificate..."**
4. On the Certificate Import Wizard, select **"Local Machine"**, press **"Next"** and grant Admin Access
5. Select **"Place all certificates in the following store"**, then **"Browse..."**, choose **"Trusted Root Certification Authorities"** and **"OK"**
6. Finally, press **"Next"** and then **"Finish"**. It might ask you to confirm, press **Yes**
7. Download **"FluentFlyout_*.msixbundle"**
8. The App Installer will pop up, press **"Install"**, or **"Update"** if you've installed FluentFlyout before
9. done! try playing music and use your media or volume keys

## Upcoming features 📝
- [x] Settings
- [x] Editable flyout timeout
- [x] Implement compact layout
- [x] Remove Windows Forms dependency
- [x] Add more media controls (repeat✅, shuffle✅, seek slider✅)
- [ ] More animations
- [x] Remove windows from `alt+tab`
### Issues
- Issue #5, fixed (~~FluentFlyout might interfere with certain apps/games in **Fullscreen**, try setting the program's window mode to **Borderless Fullscreen** for now~~)
- Windows 10 UI might not look as expected

## Contributing 💖
Please feel free to contribute in any way you can! Check out [CONTRIBUTING.md](https://github.com/unchihugo/FluentFlyout/blob/master/.github/CONTRIBUTING.md) to get started.
If you want to help with translations, please visit our [Weblate page](https://hosted.weblate.org/engage/fluentflyout/).

### Translation Status
<a href="https://hosted.weblate.org/engage/fluentflyout/">
<img src="https://hosted.weblate.org/widget/fluentflyout/multi-auto.svg" alt="Translation status" />
</a>

### Thanks to our amazing team of contributors!
<a href="https://github.com/unchihugo/fluentflyout/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=unchihugo/fluentflyout&anon=1" />
</a>

## Credits 🙌
- [Hugo Li](https://unchihugo.github.io) - Original Developer, Microsoft Store Publisher, CN & NL Translations
- [LiAuTraver](https://github.com/LiAuTraver) - Code Contributor (app theme switcher)
- [AksharDP](https://github.com/AksharDP) - Code Contributor (media seekbar & duration)
- [Hykerisme](https://github.com/Hykerisme) - CN Translation
- [nopeless](https://github.com/nopeless) - Code Contributor (QoL features)

### Dependencies
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [MicaWPF](https://github.com/Simnico99/MicaWPF)
- [WPF-UI](https://github.com/lepoco/wpfui)
