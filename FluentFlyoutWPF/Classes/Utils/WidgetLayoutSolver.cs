// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyout.Classes.Utils;

public enum WidgetLayoutTier
{
    Full,
    Squeezed,
    Compact,
    Minimal,
    Hidden,
}

// persisted order lives in UserSettings.TaskbarWidgetPriorityOrder; lowest entry hides first
public enum WidgetLayoutElement
{
    Icon,
    Controls,
    SongText,
    Visualizer,
}

public static class WidgetLayoutElementNames
{
    public const string Icon = "Icon";
    public const string Controls = "Controls";
    public const string SongText = "SongText";
    public const string Visualizer = "Visualizer";

    public static string[] All { get; } = [Icon, Controls, SongText, Visualizer];

    public static bool IsKnown(string name) => name is Icon or Controls or SongText or Visualizer;

    public static WidgetLayoutElement? ToElement(string name) => name switch
    {
        Icon => WidgetLayoutElement.Icon,
        Controls => WidgetLayoutElement.Controls,
        SongText => WidgetLayoutElement.SongText,
        Visualizer => WidgetLayoutElement.Visualizer,
        _ => null,
    };
}

public sealed record WidgetLayoutInputs
{
    public double AvailableSpan { get; init; } = double.PositiveInfinity;
    public double TitleWidth { get; init; }
    public double ArtistWidth { get; init; }
    public double CoverMargin { get; init; }
    public double ControlsWidth { get; init; }
    public bool ControlsVisible { get; init; }
    public bool IsVertical { get; init; }
    public bool IsSmallTaskbar { get; init; }
    public bool FixedWidth { get; init; }
    public bool AdaptiveEnabled { get; init; }
    public bool VisualizerEnabled { get; init; }
    public double MaxWidgetWidth { get; init; }
    public required string[] PriorityOrder { get; init; }
}

public sealed record WidgetLayoutResult
{
    public static WidgetLayoutResult Hidden { get; } = new()
    {
        Tier = WidgetLayoutTier.Hidden,
        WidgetWidth = 0,
        VisualizerWidth = 0,
        ControlsShown = false,
        TextShown = false,
        IconShown = false,
    };

    public required WidgetLayoutTier Tier { get; init; }
    public required double WidgetWidth { get; init; }
    public required double VisualizerWidth { get; init; }
    public required bool ControlsShown { get; init; }
    public required bool TextShown { get; init; }
    public required bool IconShown { get; init; }
}

public static class WidgetLayoutSolver
{
    // must match TaskbarWidgetControl._scale and TaskbarWindow._scale
    private const double WidgetRenderScale = 0.9;
    // text container gets a small extra allowance over the measured text width
    private const double TextExtraMargin = 6;
    private const double MinTextContainerWidth = 56;
    public const double VisualizerGap = 4;

    public const double VisualizerFullWidth = 84;
    public const double MinimalIconWidth = 44;
    public const double MinimalIconWidthSmall = 32;

    public static WidgetLayoutResult Solve(WidgetLayoutInputs i)
    {
        if (!i.AdaptiveEnabled)
            return NonAdaptive(i);

        bool icon = true;
        bool controls = i.ControlsVisible;
        bool text = true;
        bool visualizer = i.VisualizerEnabled;
        double visualizerWidth = visualizer ? VisualizerFullWidth : 0;

        // order[0] is Icon (never hidden); order[1] hides first, the last entry hides last.
        // Every element eventually hides, so the absolute minimum state is icon-only.
        var order = BuildOrder(i.PriorityOrder);

        for (int index = 1; index < order.Count; index++)
        {
            if (Fits(icon, controls, text, visualizer, visualizerWidth, i))
                break;

            switch (order[index])
            {
                case WidgetLayoutElement.Visualizer when visualizer:
                    visualizer = false;
                    visualizerWidth = 0;
                    break;

                case WidgetLayoutElement.SongText when text:
                    text = false;
                    break;

                case WidgetLayoutElement.Controls when controls:
                    controls = false;
                    break;
            }
        }

        if (!Fits(icon, controls, text, visualizer, visualizerWidth, i))
            return WidgetLayoutResult.Hidden;

        double natural = WidgetWidth(icon, controls, text, i);
        double visReserve = visualizer ? visualizerWidth / WidgetRenderScale + VisualizerGap : 0;
        double span = i.AvailableSpan - visReserve;

        // never narrower than icon + controls: squeezing below that clips the controls
        double minWidth = i.CoverMargin + (controls ? i.ControlsWidth : 0);

        WidgetLayoutTier tier;
        double width;
        if (natural <= span + 0.5)
        {
            tier = WidgetLayoutTier.Full;
            width = natural;
        }
        else if (text && !i.IsVertical && span >= minWidth + MinTextContainerWidth)
        {
            tier = WidgetLayoutTier.Squeezed;
            width = Math.Max(span, minWidth);
        }
        else
        {
            tier = controls ? WidgetLayoutTier.Compact : WidgetLayoutTier.Minimal;
            width = Math.Min(natural, span);
        }

        // the minWidth floor only applies while text is shown; icon-only is allowed below it
        if (width <= 0 || (text && width < minWidth))
            return WidgetLayoutResult.Hidden;

        return new WidgetLayoutResult
        {
            Tier = tier,
            WidgetWidth = width,
            VisualizerWidth = visualizerWidth,
            ControlsShown = controls,
            TextShown = text,
            IconShown = icon,
        };
    }

    private static WidgetLayoutResult NonAdaptive(WidgetLayoutInputs i)
    {
        bool vis = i.VisualizerEnabled;
        return new WidgetLayoutResult
        {
            Tier = WidgetLayoutTier.Full,
            WidgetWidth = ComputeFullWidth(i),
            VisualizerWidth = vis ? VisualizerFullWidth : 0,
            ControlsShown = true,
            TextShown = true,
            IconShown = true,
        };
    }

    private static List<WidgetLayoutElement> BuildOrder(string[]? order)
    {
        var list = new List<WidgetLayoutElement>();
        if (order != null)
        {
            foreach (var name in order)
            {
                var element = WidgetLayoutElementNames.ToElement(name);
                if (element.HasValue && !list.Contains(element.Value))
                    list.Add(element.Value);
            }
        }

        // self-heal missing entries
        if (!list.Contains(WidgetLayoutElement.Icon))
            list.Insert(0, WidgetLayoutElement.Icon);
        foreach (var element in new[] { WidgetLayoutElement.SongText, WidgetLayoutElement.Controls, WidgetLayoutElement.Visualizer })
            if (!list.Contains(element))
                list.Add(element);

        return list;
    }

    private static bool Fits(bool icon, bool controls, bool text, bool visualizer, double visualizerWidth, WidgetLayoutInputs i)
    {
        if (!icon)
            return false;

        double visReserve = visualizer ? visualizerWidth / WidgetRenderScale + VisualizerGap : 0;
        double span = i.AvailableSpan - visReserve;
        double ctrl = controls ? i.ControlsWidth : 0;

        if (WidgetWidth(icon, controls, text, i) <= span + 0.5)
            return true;

        // squeezed text is acceptable while the containers stay readable
        return text && !i.IsVertical && span - ctrl - i.CoverMargin >= MinTextContainerWidth;
    }

    private static double WidgetWidth(bool icon, bool controls, bool text, WidgetLayoutInputs i)
    {
        if (!icon)
            return 0;

        // text sizing: info margin + text width + panel padding (both sides)

        double cover = i.CoverMargin;
        double ctrl = controls ? i.ControlsWidth : 0;

        if (i.IsVertical)
            return cover + ctrl;

        if (!text)
            return MinimalIconSize(i) + (controls ? i.ControlsWidth : 0);

        double textWidth = i.IsSmallTaskbar ? i.TitleWidth : Math.Max(i.TitleWidth, i.ArtistWidth);
        double textBlock = textWidth > 0 ? textWidth + TextExtraMargin : 0;
        double baseWidth = i.FixedWidth
            ? i.MaxWidgetWidth - cover
            : Math.Min(textBlock, i.MaxWidgetWidth - cover);

        return baseWidth + cover + ctrl;
    }        // icon-only mode renders a square
    private static double MinimalIconSize(WidgetLayoutInputs i) =>
        i.IsSmallTaskbar ? MinimalIconWidthSmall : MinimalIconWidth;

    private static double ComputeFullWidth(WidgetLayoutInputs i) => WidgetWidth(true, i.ControlsVisible, true, i);
}
