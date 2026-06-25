# How to contribute
Any help towards improving FluentFlyout is greatly appreciated — whether it's new features, bug fixes, or translations!

## Developing
To start developing, load the repository into Visual Studio (or your IDE of choice), make sure that FluentFlyout is closed (because it's a singleton app), and start writing!
Make sure to create a new [fork](https://github.com/unchihugo/FluentFlyout/fork) of this repository when starting. You can then edit any code in your fork (using branches if needed). If you're uncertain of an implementation, feel free to open an issue to discuss it before starting to work on it.

When writing code, try to adhere to [MVVM principles](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm) and the existing code style. To format your code, you can run the `dotnet format` command.

When designing or editing the User Interface, please follow the [Fluent 2 Design System](https://fluent2.microsoft.design/) guidelines. We also have our own UI Guidelines for designing FluentFlyout (see [UI Guidelines](https://github.com/unchihugo/FluentFlyout?tab=contributing-ov-file#ui-guidelines)).

Once you're ready with the updates, submit a [pull request](https://github.com/unchihugo/FluentFlyout/compare) describing what's changed, and be sure to tag any issues (if applicable) your pull request is taking care of!

AI usage: AI tools may be used to assist with programming, provided that you design the solution yourself and carefully review the generated code for issues and redundancy. Heavy reliance on AI without clear knowledge of what's going on with your changes will not be merged, but having a strong understanding of it will.

Credits: We add your name to the contributors list in the app, on the relevant changelog, and in other related areas when you contribute to the project.

## Translating
You can translate here: https://hosted.weblate.org/engage/fluentflyout/

The majority of FluentFlyout is translated by the community, and we are always looking for more help! We support over 25 languages, and you can help by translating FluentFlyout into your native language. You can also help by reviewing existing translations to ensure they are accurate and up-to-date.

FluentFlyout uses **Weblate** to manage translations, ensuring advanced translation tooling, automated merges to FluentFlyout, and appropriate crediting when you update a language you have worked on. We also add your name to the credits in the app, on the relevant changelog, and in other related areas when you contribute to a translation. 

<a href="https://hosted.weblate.org/engage/fluentflyout/">
<img src="https://hosted.weblate.org/widget/fluentflyout/multi-auto.svg" alt="Translation status" />
</a>

## UI Guidelines

FluentFlyout aims to follow the Fluent 2 Design System as closely as possible. There are some specific guidelines that we have for FluentFlyout, which you can find below. Please follow these guidelines when contributing to the project.

### Settings Menus
FluentFlyout's settings menus are designed to be easy to navigate. Each page should have a clear title, and the settings should be grouped logically.

Generally, pages are structured with a list of options, each with a title and optionally a description. Each option should have a `3px` gap between them. If there's a toggle that is significant or closely related to the page itself (for example, a toggle to enable or disable volume flyouts on the "Volume Flyout" page), it should be placed at the top of the page, with a `24px` margin below it. If this toggle is turned off, the rest of the page should be disabled (grayed out). If a setting page has a lot of options, they should be grouped into sections with a title and a gap between them (generally `24px`). The bottom of the page should have a `76px` bottom margin to ensure that the last option is not too close to the bottom of the window.

If you introduce a new option, consider adding a "NEW" label next to the title to highlight it to users. For premium settings, a "PREMIUM" label should be added, and the toggle should only be interactable for premium users.

Try to avoid cluttering the settings menus with too many options that may be irrelevant to most users, as this can make it difficult or frustrating to find the settings that are needed. If you feel that a setting is important enough to be included, but may not be relevant to most users, consider adding it to the "System > Advanced Settings" page.

### Home Page and Sidebar
The navigational buttons on the dashboard section of the home page are reserved for the most important settings pages ("Volume Flyout", "Taskbar Visualizer", etc.). Each button should have a logical icon and a clear title, and optionally a status indicator (e.g., "Enabled", "Disabled").

The sidebar should be used for navigation between the settings pages that are also accessible from the dashboard section of the home page (aside from the Back, Hamburger menu, and About buttons). They should share the same icon and title as in the home page.