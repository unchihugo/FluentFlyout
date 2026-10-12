<p align="center">
  <picture>
    <source width="70%" alt="FluentFlyout Title" media="(prefers-color-scheme: dark)" srcset="https://github.com/user-attachments/assets/daa2969f-8ad2-4832-8253-26133a50c921" />
    <source width="70%" alt="FluentFlyout Title" media="(prefers-color-scheme: light)" srcset="https://github.com/user-attachments/assets/3e75e514-b9a6-40e3-b170-af9d29a82bb4" />
    <img width="70%" alt="FluentFlyout Title" src="https://github.com/user-attachments/assets/daa2969f-8ad2-4832-8253-26133a50c921">
  </picture>
</p>

<h1 align="center">✨ FluentFlyout (KoksMen Edition) ✨</h1>
<h3 align="center">🚀 Версия форка: <em>v2.15.0-f2.0.0</em> 🪄</h3>

<p align="center">
  <b>Современные плавные оверлеи Windows 11 с оптимизацией для игр, буфером обмена, регулировкой звука колесиком мыши и автообновлениями через GitHub Releases</b><br>
  <i>Modern fluent flyouts for Windows 11 enhanced with gaming safeguards, clipboard history, mouse wheel volume control, and GitHub Releases auto-updater</i>
</p>

<p align="center">
  <a href="https://github.com/KoksMen/FluentFlyout/releases"><img src="https://img.shields.io/badge/Version-v2.15.0--f2.0.0-7c4dff?style=for-the-badge&logo=github&logoColor=white" alt="FluentFlyout v2.15.0-f2.0.0" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10.0" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/Signature-Authenticode%20SHA--256-success?style=for-the-badge&logo=windows&logoColor=white" alt="Authenticode Signed" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0-orange?style=for-the-badge" alt="License GPL-3.0" /></a>
</p>

<p align="center">
  <a href="#-fluentflyout-на-русском"><b>🇷🇺 Перейти к русской версии</b></a> •
  <a href="#-fluentflyout-in-english"><b>🇬🇧 Switch to English Version</b></a>
</p>

<p align="center">
  <a href="https://github.com/KoksMen/FluentFlyout/releases/latest">
    <img src="https://img.shields.io/badge/⬇️%20Скачать%20релиз-FluentFlyout%20v2.15.0--f2.0.0-blue?style=for-the-badge" alt="Download Release" />
  </a>
</p>

---

# 🇷🇺 FluentFlyout (На русском)

## 💡 О проекте

**FluentFlyout (KoksMen Edition)** — это расширенный и глубоко оптимизированный форк популярного приложения FluentFlyout для Windows 10/11. 

Оригинальное приложение предоставляет красивые оверлеи в стиле **Fluent 2 Design**, однако в игровых сценариях и при активной работе на панели задач сталкивалось с рядом ограничений: случайное сворачивание полноэкранных игр оверлеями, задержки UI Automation в таскбаре, отсутствие прямой регулировки звука колесиком мыши и привязка к оригинальным обновлениям.

Этот форк решает эти проблемы, превращая FluentFlyout в надежный, сверхбыстрый и универсальный инструмент для повседневной работы и гейминга! 🚀

---

## 🌟 Ключевые возможности и улучшения форка

### 🔄 1. Встроенное автообновление через GitHub Releases (как в Portal-Windows)
- **Прямая интеграция с репозиторием:** Приложение больше не опрашивает сторонние домены, а проверяет обновления напрямую из официальных релизов форка `KoksMen/FluentFlyout` через защищённый GitHub REST API.
- **Интеллектуальное семантическое версионирование:** Двухуровневый парсер `ForkVersion` корректно сопоставляет версии форка (`2.15.0-f2.0.0` > `2.13.1-f1.7.0`).
- **Полноценная работа в Portable-режиме:** Устранена ошибка, из-за которой в автономных `.exe` сборках версия определялась как `"debug"` и блокировала автообновления.
- **Автоматический выбор ассета:** Автоматическое определение прямых ссылок на загрузку `FluentFlyout.exe` или `Portable_x64_AMD.zip`.

### 🎮 2. Раздельные режимы полноэкранного экрана (Gaming Safeguard)
- **Exclusive Fullscreen Toggle:** Независимый переключатель для 3D-игр в эксклюзивном полном экране. При его отключении FluentFlyout автоматически уступает системному OSD, **полностью предотвращая случайное сворачивание игр и потерю фокуса**.
- **Borderless Fullscreen:** Отдельная настройка для игр в окне без рамок и полноэкранного просмотра видео (например, в YouTube).
- **Скрытие Caps Lock в играх:** Индикатор Caps Lock больше не сворачивает игры при случайном нажатии.

### ⚡ 3. Молниеносный отклик кнопок Микшера и Буфера обмена
- **Устранение подвисаний UI-потока:** Заменены блокирующие синхронные вызовы UI Automation `.Wait(1000)` на прямой и сверхбыстрый Win32 API (`GetWindowRect`). Всплывающие окна открываются мгновенно при первом же клике.
- **Асинхронный поиск элементов таскбара:** Поиск XAML-островков Windows 11 вынесен в фоновый поток с отрицательным кэшированием (TTL 5000 мс).
- **Исправление повторного клика:** Сброс флагов анимации скрытия и дебаунс 150 мс гарантируют, что кнопка микшера больше не залипает и всегда доступна.

### 🔊 4. Регулировка громкости колесиком мыши (Volume Wheel Scroll)
- **Колесико над кнопкой микшера:** Прокрутка колесика мыши над иконкой микшера в таскбаре плавно меняет общую громкость системы (шаг 2% за клик).
- **Колесико над медиа-виджетом:** Прокрутка над мини-виджетом трека также меняет громкость без необходимости открывать микшер.
- **Моментальный оверлей (Flyout OSD):** При прокрутке сразу показывается красивый оверлей громкости FluentFlyout с текущим значением звука.

### 📋 5. Быстрый доступ к буферу обмена
- Встроенная кнопка буфера обмена на панели задач для быстрого просмотра и вставки недавних фрагментов текста без задержек.

### 🔏 6. Надежная цифровая подпись (Authenticode)
- Все релизные сборки подписаны цифровым сертификатом `CN=xXKoksMenXx, O=xXTeam, C=RU` (Thumbprint: `C6C4A602CD20A275BC0B4E07AC0F73CAC7D414F7`) с RFC 3161 меткой времени доверенного центра Sectigo SHA-256.

---

## 📦 Скачивание и установка

Загрузите актуальную версию из раздела [**Releases**](https://github.com/KoksMen/FluentFlyout/releases/latest):

| Файл | Описание | Тип |
| :--- | :--- | :--- |
| **`FluentFlyout.exe`** | Автономный Single-File исполняемый файл (Self-Contained) | Рекомендуется для большинства пользователей |
| **`Portable_x64_AMD.zip`** | Портативный zip-архив с исполняемым файлом | Для портативной распаковки в любую папку |
| **`FluentFlyout-v2.15.0-f2.0.0-Portable-x64.zip`** | Полный архив релиза | Каталожная копия релиза |

> [!TIP]
> Установка .NET Runtime не требуется: все необходимые компоненты уже встроены внутрь исполняемого файла!

---

## 🛠️ Сборка из исходного кода

Для самостоятельной сборки потребуется [.NET 10.0 SDK](https://dotnet.microsoft.com/download):

```powershell
# Клонирование репозитория
git clone https://github.com/KoksMen/FluentFlyout.git
cd FluentFlyout

# Сборка и публикация автономного Single-File релиза
dotnet publish FluentFlyoutWPF\FluentFlyout.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
```

---

# 🇬🇧 FluentFlyout (In English)

## 💡 About This Project

**FluentFlyout (KoksMen Edition)** is an enhanced and performance-hardened fork of FluentFlyout for Windows 10/11.

While the original application provides gorgeous **Fluent 2 Design** flyouts, it suffered from certain limitations in gaming and taskbar responsiveness: game minimizations during fullscreen gameplay, UI thread automation freezes, lack of mouse wheel volume control, and hardcoded external update endpoints.

This fork addresses these pain points, making FluentFlyout ultra-responsive, lightweight, and truly gaming-friendly! 🚀

---

## 🌟 Key Fork Enhancements

- **🔄 GitHub Releases Auto-Updater:** Integrated directly with `KoksMen/FluentFlyout` GitHub API. SemVer comparator cleanly handles fork versioning (`2.15.0-f2.0.0`).
- **🎮 Dual Fullscreen Modes (Gaming Safeguard):** Separate toggles for exclusive fullscreen games and borderless windows. Flyouts will never minimize your games or steal focus.
- **⚡ Instant Taskbar Button Responsiveness:** Replaced synchronous `.Wait(1000)` UI Automation bottlenecks with instant Win32 `GetWindowRect` calls.
- **🔊 Mouse Wheel Volume Adjustment:** Scroll your mouse wheel directly over the Taskbar Mixer button or Media Widget to change system volume with on-screen flyout feedback.
- **📋 Taskbar Clipboard History:** Dedicated quick-action clipboard button on the taskbar with smooth, instant rendering.
- **🔏 Authenticode Code Signing:** Release binaries signed with SHA-256 Authenticode and Sectigo RFC 3161 timestamps.

---

## 📜 Лицензия / License

Проект распространяется под лицензией **GNU General Public License v3.0 (GPL-3.0)**.  
Подробности доступны в файле [LICENSE](LICENSE).
