// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyout.Classes.Utils;

/// <summary>How much of the widget the available span can hold, from widest to narrowest.</summary>
public enum WidgetLayoutTier
{
    Full,
    Squeezed,
    Compact,
    Minimal,
    Hidden,
}

// Hide order: the first entry is never hidden, the last hides last.
public enum WidgetLayoutElement
{
    Icon,
    Controls,
    SongText,
    Visualizer,
}

/// <summary>Measurements and flags describing the widget and the space available for it.</summary>
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
    /// <summary>Hide order; the first entry is never hidden.</summary>
    public required WidgetLayoutElement[] PriorityOrder { get; init; }
}

/// <summary>The resolved tier, sizes and per-element visibility for one layout pass.</summary>
public sealed record WidgetLayoutResult
{
    public required WidgetLayoutTier Tier { get; init; }
    public required double WidgetWidth { get; init; }
    public required double VisualizerWidth { get; init; }
    public required bool ControlsShown { get; init; }
    public required bool TextShown { get; init; }
    public required bool IconShown { get; init; }

    // width the content takes before squeezing
    /// <summary>Width the content wants before anything is squeezed or hidden.</summary>
    public double NaturalWidth { get; init; }

    /// <summary>Result used when not even the album icon fits.</summary>
    public static WidgetLayoutResult Hidden { get; } = new()
    {
        Tier = WidgetLayoutTier.Hidden,
        WidgetWidth = 0,
        VisualizerWidth = 0,
        ControlsShown = false,
        TextShown = false,
        IconShown = false,
    };
}

public static class WidgetLayoutSolver
{
    // must match TaskbarWidgetControl._scale and TaskbarWindow._scale
    private const double WidgetRenderScale = 0.9;
    // text container gets a small extra allowance over the measured text width
    private const double TextExtraMargin = 6;
    private const double MinTextContainerWidth = 56;

    public const double VisualizerFullWidth = 84;
    public const double MinimalIconWidth = 44;
    public const double MinimalIconWidthSmall = 32;

    // how many span units the icon-only state may overlap into measured phantom padding
    private const double IconOnlyGraceUnits = 12;

    /// <summary>
    /// Picks the widest tier that fits <see cref="WidgetLayoutInputs.AvailableSpan"/>, hiding
    /// elements in <see cref="WidgetLayoutInputs.PriorityOrder"/> until it does.
    /// </summary>
    public static WidgetLayoutResult Solve(WidgetLayoutInputs i)
    {
        if (!i.AdaptiveEnabled)
            return NonAdaptive(i);

        bool icon = true;
        bool controls = i.ControlsVisible;
        bool text = true;
        bool visualizer = i.VisualizerEnabled;
        double visualizerWidth = visualizer ? VisualizerFullWidth : 0;

        // order[0] is never hidden, so the absolute minimum state is icon-only.
        var order = i.PriorityOrder;

        for (int index = 1; index < order.Length; index++)
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

        bool iconOnlyGrace = false;
        if (!Fits(icon, controls, text, visualizer, visualizerWidth, i))
        {
            // button/tray rects include invisible hit-test padding, so tolerate a small overlap
            bool iconOnly = !controls && !text && !visualizer;
            if (!iconOnly || i.AvailableSpan < MinimalIconSize(i) - IconOnlyGraceUnits)
                return WidgetLayoutResult.Hidden;
            iconOnlyGrace = true;
        }

        double natural = WidgetWidth(icon, controls, text, i);
        double span = i.AvailableSpan - (visualizer ? visualizerWidth / WidgetRenderScale : 0);

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
            width = iconOnlyGrace ? natural : Math.Min(natural, span);
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
            NaturalWidth = natural,
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

    private static bool Fits(bool icon, bool controls, bool text, bool visualizer, double visualizerWidth, WidgetLayoutInputs i)
    {
        if (!icon)
            return false;

        double span = i.AvailableSpan - (visualizer ? visualizerWidth / WidgetRenderScale : 0);
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

        // text sizing: info margin + text width + panel padding on both sides

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