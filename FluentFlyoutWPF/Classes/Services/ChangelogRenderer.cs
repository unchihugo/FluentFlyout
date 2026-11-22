// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using Emoji.Wpf;
using FluentFlyout.Classes;
using HtmlAgilityPack;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace FluentFlyoutWPF.Classes.Services;

internal static class ChangelogRenderer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static async Task RenderAsync(string html, Panel target, CancellationToken cancellationToken)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        TextBlock? text = null;
        bool firstContent = true;
        Task? leadingImage = null;
        foreach (var child in document.DocumentNode.ChildNodes)
        {
            await target.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
            if (firstContent && child.NodeType == HtmlNodeType.Element && child.Name == "img")
            {
                leadingImage = AddImage(child, target);
            }
            else
            {
                RenderChild(child, target, ref text);
            }
            if (child.NodeType == HtmlNodeType.Element
                || child.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(child.InnerText))
            {
                firstContent = false;
            }
        }
        if (leadingImage != null)
        {
            try
            {
                await leadingImage.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (TimeoutException)
            {
                Logger.Warn("Timed out waiting for the leading changelog image; displaying the release while it loads");
            }
        }
    }

    private static void RenderChildren(HtmlNode node, Panel target)
    {
        TextBlock? text = null;
        foreach (var child in node.ChildNodes)
        {
            RenderChild(child, target, ref text);
        }
    }

    private static void RenderChild(HtmlNode child, Panel target, ref TextBlock? text)
    {
        if (IsBlock(child))
        {
            text = null;
            RenderBlock(child, target);
        }
        else if (child.NodeType != HtmlNodeType.Text || !string.IsNullOrWhiteSpace(child.InnerText) || text != null)
        {
            if (text == null)
            {
                text = CreateText();
                target.Children.Add(text);
            }
            AddInline(child, text.Inlines);
        }
    }

    private static bool IsBlock(HtmlNode node) => node.NodeType == HtmlNodeType.Element
        && node.Name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "ul" or "ol" or "li" or "img" or "table" or "details" or "div" or "pre"
            or "blockquote" or "hr";

    private static TextBlock CreateText() => new()
    {
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static void RenderBlock(HtmlNode node, Panel target)
    {
        switch (node.Name)
        {
            case "img":
                AddImage(node, target);
                break;
            case "ul":
            case "ol":
                AddList(node, target);
                break;
            case "table":
                AddTable(node, target);
                break;
            case "details":
                var content = new StackPanel();
                foreach (var child in node.ChildNodes.Where(child => child.Name != "summary"))
                {
                    if (IsBlock(child))
                    {
                        RenderBlock(child, content);
                    }
                }
                target.Children.Add(new CardExpander
                {
                    Header = WebUtility.HtmlDecode(node.SelectSingleNode("summary")?.InnerText ?? string.Empty),
                    Content = content,
                    ContentPadding = new Thickness(16),
                    Margin = new Thickness(0, 4, 0, 12)
                });
                break;
            case "hr":
                var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 16) };
                separator.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
                target.Children.Add(separator);
                break;
            case "pre":
                var preformatted = CreateText();
                preformatted.FontFamily = new FontFamily("Consolas");
                AddText(preformatted.Inlines, WebUtility.HtmlDecode(node.InnerText));
                target.Children.Add(preformatted);
                break;
            case "p":
            case "div":
            case "li":
            case "blockquote":
                var panel = new StackPanel { Margin = new Thickness(node.Name == "blockquote" ? 16 : 0, 0, 0, 0) };
                RenderChildren(node, panel);
                target.Children.Add(panel);
                break;
            default:
                var heading = CreateText();
                heading.Margin = new Thickness(0, 14, 0, 8);
                heading.FontSize = node.Name switch { "h1" => 24, "h2" => 20, "h3" => 16, _ => 14 };
                heading.FontWeight = FontWeights.SemiBold;
                foreach (var child in node.ChildNodes)
                {
                    AddInline(child, heading.Inlines);
                }
                if (node.Name is "h1" or "h2")
                {
                    heading.Margin = new Thickness(0);
                    var headingBorder = new Border
                    {
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(0, 0, 0, 6),
                        Margin = new Thickness(0, 14, 0, 8),
                        Child = heading
                    };
                    headingBorder.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                    target.Children.Add(headingBorder);
                }
                else
                {
                    target.Children.Add(heading);
                }
                break;
        }
    }

    private static void AddInline(HtmlNode node, InlineCollection inlines)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            AddText(inlines, Regex.Replace(WebUtility.HtmlDecode(node.InnerText), @"\s+", " "));
            return;
        }
        if (node.NodeType != HtmlNodeType.Element)
        {
            return;
        }
        if (node.Name == "br")
        {
            inlines.Add(new LineBreak());
            return;
        }

        Span span = node.Name switch
        {
            "strong" or "b" => new Bold(),
            "em" or "i" => new Italic(),
            "a" => CreateLink(node),
            _ => new Span()
        };
        if (node.Name == "code")
        {
            span.FontFamily = new FontFamily("Consolas");
        }
        foreach (var child in node.ChildNodes)
        {
            AddInline(child, span.Inlines);
        }
        inlines.Add(span);
    }

    private static void AddText(InlineCollection inlines, string text)
    {
        int position = 0;
        foreach (Match match in EmojiData.MatchOne.Matches(text))
        {
            if (match.Index > position)
            {
                inlines.Add(new Run(text[position..match.Index]));
            }
            var emoji = new EmojiInline { Foreground = Brushes.Black, Text = match.Value };
            AutomationProperties.SetName(emoji.Child, match.Value);
            inlines.Add(emoji);
            position = match.Index + match.Length;
        }
        if (position < text.Length)
        {
            inlines.Add(new Run(text[position..]));
        }
    }

    private static Span CreateLink(HtmlNode node)
    {
        if (!Uri.TryCreate(node.GetAttributeValue("href", string.Empty), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new Span();
        }

        var link = new Hyperlink { NavigateUri = uri };
        link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        link.RequestNavigate += (_, e) =>
        {
            Notifications.OpenUrlInBrowser(e.Uri.AbsoluteUri);
            e.Handled = true;
        };
        return link;
    }

    private static Task AddImage(HtmlNode node, Panel target)
    {
        var alt = WebUtility.HtmlDecode(node.GetAttributeValue("alt", string.Empty));
        if (!Uri.TryCreate(node.GetAttributeValue("src", string.Empty), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            target.Children.Add(new TextBlock { Text = alt, TextWrapping = TextWrapping.Wrap });
            return Task.CompletedTask;
        }

        var image = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 12)
        };
        image.SetBinding(FrameworkElement.MaxWidthProperty, new Binding("ActualWidth") { Source = target });
        image.SizeChanged += (_, _) =>
            image.Clip = new RectangleGeometry(new Rect(0, 0, image.ActualWidth, image.ActualHeight), 4, 4);
        AutomationProperties.SetName(image, alt);
        var loading = new ProgressRing
        {
            IsIndeterminate = true,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var container = new Grid();
        container.Children.Add(image);
        container.Children.Add(loading);
        image.ImageFailed += (_, e) =>
        {
            Logger.Warn(e.ErrorException, "Failed to load changelog image {0}", uri);
            image.Visibility = Visibility.Collapsed;
            loading.Visibility = Visibility.Collapsed;
            container.Children.Add(new TextBlock { Text = alt, TextWrapping = TextWrapping.Wrap });
        };
        var bitmap = new BitmapImage();
        var imageReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bitmap.DownloadCompleted += (_, _) =>
        {
            loading.Visibility = Visibility.Collapsed;
            imageReady.TrySetResult();
        };
        bitmap.DownloadFailed += (_, _) => imageReady.TrySetResult();
        bitmap.DecodeFailed += (_, _) => imageReady.TrySetResult();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.EndInit();
        image.Source = bitmap;
        loading.Visibility = bitmap.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        if (!bitmap.IsDownloading)
        {
            imageReady.TrySetResult();
        }
        target.Children.Add(container);
        return imageReady.Task;
    }

    private static void AddList(HtmlNode node, Panel target)
    {
        int number = int.TryParse(node.GetAttributeValue("start", "1"), out int start) ? start : 1;
        foreach (var item in node.ChildNodes.Where(child => child.Name == "li"))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = node.Name == "ol" ? $"{number++}." : "•", FontSize = 14 });
            var content = new StackPanel();
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            RenderChildren(item, content);
            target.Children.Add(row);
        }
    }

    private static void AddTable(HtmlNode node, Panel target)
    {
        var rows = node.SelectNodes("./tr|./thead/tr|./tbody/tr")?.ToArray() ?? [];
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        var occupied = new HashSet<(int Row, int Column)>();
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int columnIndex = 0;
            foreach (var cell in rows[rowIndex].ChildNodes.Where(cell => cell.Name is "th" or "td"))
            {
                while (occupied.Contains((rowIndex, columnIndex)))
                {
                    columnIndex++;
                }
                int columnSpan = Math.Clamp(cell.GetAttributeValue("colspan", 1), 1, 100);
                int rowSpan = Math.Clamp(cell.GetAttributeValue("rowspan", 1), 1, rows.Length - rowIndex);
                while (grid.ColumnDefinitions.Count < columnIndex + columnSpan)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                }
                for (int row = rowIndex; row < rowIndex + rowSpan; row++)
                {
                    for (int column = columnIndex; column < columnIndex + columnSpan; column++)
                    {
                        occupied.Add((row, column));
                    }
                }
                var cellContent = new StackPanel { Margin = new Thickness(8, 6, 8, 0) };
                if (cell.Name == "th")
                {
                    cellContent.SetValue(System.Windows.Controls.TextBlock.FontWeightProperty, FontWeights.SemiBold);
                }
                RenderChildren(cell, cellContent);
                var border = new Border { BorderThickness = new Thickness(1, 1, 0, 0), Child = cellContent };
                border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                Grid.SetColumnSpan(border, columnSpan);
                Grid.SetRowSpan(border, rowSpan);
                columnIndex += columnSpan;
                grid.Children.Add(border);
            }
        }
        if (grid.ColumnDefinitions.Count == 0)
        {
            return;
        }
        var outer = new Border { BorderThickness = new Thickness(0, 0, 1, 1), Child = grid };
        outer.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        target.Children.Add(outer);
    }
}