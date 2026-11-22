// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using HtmlAgilityPack;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentFlyoutWPF.Classes.Services;

public sealed record ChangelogRelease(
    string Version,
    string Title,
    string ReleaseDate,
    string ContentHtml,
    bool IsNew)
{
    public string NavigationTitle => string.IsNullOrWhiteSpace(Version)
        ? Title
        : $"{Version} - {Title}";
}

public sealed record ChangelogFeed(
    IReadOnlyList<ChangelogRelease> Releases,
    string ViewAllUrl);

public static class ChangelogService
{
    private const string ChangelogPageUrl = "https://fluentflyout.com/changelog/";
    // TODO: change to real URL
    private const string ChangelogApiUrl = "http://localhost:4321/api/changelogs";

    private static readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly Regex _versionRegex = new(
        @"(?<!\d)\d+(?:\.\d+){1,3}(?!\d)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> _allowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "strong", "b", "em", "i",
        "code", "pre", "blockquote", "a", "img", "br", "hr", "table", "thead", "tbody", "tr",
        "th", "td", "details", "summary", "div", "span"
    };

    private static readonly HashSet<string> _voidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "br", "hr"
    };

    private static readonly HashSet<string> _blockedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "form", "input", "button", "textarea",
        "select", "option", "meta", "link", "base"
    };

    public static bool HasUnseenUpdate(string currentVersion, string lastViewedVersion)
    {
        // TODO: Remove this check once the changelog endpoint is live and stable
        return true;
        if (string.IsNullOrWhiteSpace(currentVersion)
            || currentVersion.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(lastViewedVersion))
        {
            return false;
        }

        if (TryParseVersion(currentVersion, out var current) && TryParseVersion(lastViewedVersion, out var lastViewed))
        {
            return current!.CompareTo(lastViewed) > 0;
        }

        return !string.Equals(
            currentVersion.Trim().TrimStart('v', 'V'),
            lastViewedVersion.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ChangelogFeed> GetReleasesAsync(
        string lastViewedVersion,
        CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(ChangelogApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<ChangelogApiResponse>(json, _jsonOptions);
        if (payload?.Releases == null || payload.Releases.Count == 0)
        {
            throw new InvalidDataException("The changelog endpoint did not contain any releases.");
        }

        var viewAllUrl = ValidateViewAllUrl(payload.ViewAllUrl);
        var releases = new List<ChangelogRelease>(payload.Releases.Count);
        foreach (var apiRelease in payload.Releases)
        {
            if (string.IsNullOrWhiteSpace(apiRelease.Version)
                || string.IsNullOrWhiteSpace(apiRelease.Title)
                || string.IsNullOrWhiteSpace(apiRelease.Date))
            {
                throw new InvalidDataException("The changelog endpoint returned a release with missing metadata.");
            }

            var contentDocument = new HtmlDocument();
            contentDocument.LoadHtml(apiRelease.HtmlContent ?? string.Empty);
            var contentHtml = string.Concat(contentDocument.DocumentNode.ChildNodes.Select(SanitizeNode));

            releases.Add(new ChangelogRelease(
                apiRelease.Version,
                apiRelease.Title,
                FormatReleaseDate(apiRelease.Date),
                contentHtml,
                IsNewerThan(apiRelease.Version, lastViewedVersion)));
        }

        if (releases.Count == 0)
        {
            throw new InvalidDataException("The changelog endpoint did not contain any readable releases.");
        }

        return new ChangelogFeed(releases, viewAllUrl);
    }

    public static string CreateHtmlDocument(ChangelogRelease release, bool isDarkTheme)
    {
        string background = isDarkTheme ? "#202020" : "#F3F3F3";
        string foreground = isDarkTheme ? "#F1F1F1" : "#202020";
        string muted = isDarkTheme ? "#C8C8C8" : "#5D5D5D";
        string border = isDarkTheme ? "#484848" : "#D0D0D0";
        string link = isDarkTheme ? "#7CC7FF" : "#0067C0";
        string scrollbarTrack = isDarkTheme ? "#202020" : "#F3F3F3";
        string scrollbarThumb = isDarkTheme ? "#454545" : "#C7C7C7";

        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        html.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><style>");
        html.Append("html,body{margin:0;min-height:100%;overflow-x:hidden;background:").Append(background).Append(";}");
        html.Append("html,body{scrollbar-base-color:").Append(scrollbarThumb);
        html.Append(";scrollbar-face-color:").Append(scrollbarThumb);
        html.Append(";scrollbar-track-color:").Append(scrollbarTrack);
        html.Append(";scrollbar-arrow-color:").Append(muted);
        html.Append(";scrollbar-shadow-color:").Append(scrollbarTrack);
        html.Append(";scrollbar-highlight-color:").Append(scrollbarThumb);
        html.Append(";scrollbar-3dlight-color:").Append(scrollbarTrack);
        html.Append(";scrollbar-darkshadow-color:").Append(scrollbarTrack).Append(";}");
        html.Append("body{box-sizing:border-box;padding:20px;font-family:'Segoe UI Variable','Segoe UI',sans-serif;");
        html.Append("font-size:15px;line-height:1.55;color:").Append(foreground).Append(";}");
        html.Append("h1{font-size:25px;line-height:1.25;margin:0 0 4px;font-weight:600;}");
        html.Append("h2{font-size:21px;margin:1.5em 0 .55em;}h3{font-size:17px;margin:1.2em 0 .4em;}");
        html.Append(".release-date{margin:0 0 24px;color:").Append(muted).Append(";font-size:13px;}");
        html.Append("a{color:").Append(link).Append(";}img{max-width:100%;height:auto;}");
        html.Append("blockquote{border-left:3px solid ").Append(link).Append(";margin-left:0;padding-left:14px;}");
        html.Append("table{border-collapse:collapse;max-width:100%;}th,td{border:1px solid ").Append(border);
        html.Append(";padding:6px 9px;}pre,code{font-family:Consolas,monospace;word-wrap:break-word;}");
        html.Append("pre{white-space:pre-wrap;padding:10px;background:").Append(isDarkTheme ? "#2A2A2A" : "#E8E8E8").Append(";}");
        html.Append("</style></head><body><h1>");
        html.Append(WebUtility.HtmlEncode(release.NavigationTitle));
        html.Append("</h1>");

        if (!string.IsNullOrWhiteSpace(release.ReleaseDate))
        {
            html.Append("<p class=\"release-date\">");
            html.Append(WebUtility.HtmlEncode(release.ReleaseDate));
            html.Append("</p>");
        }

        html.Append(release.ContentHtml);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static bool IsNewerThan(string releaseVersion, string lastViewedVersion)
    {
        if (string.IsNullOrWhiteSpace(lastViewedVersion))
        {
            return false;
        }

        if (!TryParseVersion(releaseVersion, out var release))
        {
            return false;
        }

        return !TryParseVersion(lastViewedVersion, out var lastViewed)
            || release!.CompareTo(lastViewed) > 0;
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        var match = _versionRegex.Match(value ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value
            .Split('.')
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : -1)
            .ToArray();

        if (parts.Any(part => part < 0))
        {
            return false;
        }

        Array.Resize(ref parts, 4);
        version = new Version(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    private static string FormatReleaseDate(string value)
    {
        return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var releaseDate)
            ? releaseDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            : value;
    }

    private static string ValidateViewAllUrl(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "fluentflyout.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri;
        }

        throw new InvalidDataException("The changelog endpoint returned an invalid view-all URL.");
    }

    private static string SanitizeNode(HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            return WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText));
        }

        if (node.NodeType == HtmlNodeType.Document)
        {
            return string.Concat(node.ChildNodes.Select(SanitizeNode));
        }

        if (node.NodeType != HtmlNodeType.Element)
        {
            return string.Empty;
        }

        var tag = node.Name.ToLowerInvariant();
        if (_blockedTags.Contains(tag))
        {
            return string.Empty;
        }

        var children = string.Concat(node.ChildNodes.Select(SanitizeNode));
        if (!_allowedTags.Contains(tag))
        {
            return children;
        }

        string attributes = GetSafeAttributes(node, tag);
        return _voidTags.Contains(tag)
            ? $"<{tag}{attributes} />"
            : $"<{tag}{attributes}>{children}</{tag}>";
    }

    private static string GetSafeAttributes(HtmlNode node, string tag)
    {
        var attributes = new List<string>();
        if (tag == "a" && TryGetSafeHttpsUrl(node.GetAttributeValue("href", string.Empty), out var href))
        {
            attributes.Add($"href=\"{WebUtility.HtmlEncode(href)}\"");
        }
        else if (tag == "img")
        {
            if (TryGetSafeHttpsUrl(node.GetAttributeValue("src", string.Empty), out var source))
            {
                attributes.Add($"src=\"{WebUtility.HtmlEncode(source)}\"");
            }

            var alt = node.GetAttributeValue("alt", string.Empty);
            if (!string.IsNullOrWhiteSpace(alt))
            {
                attributes.Add($"alt=\"{WebUtility.HtmlEncode(alt)}\"");
            }
        }

        if (tag is "td" or "th")
        {
            AddPositiveIntegerAttribute(attributes, node, "colspan");
            AddPositiveIntegerAttribute(attributes, node, "rowspan");
        }
        else if (tag == "ol")
        {
            AddPositiveIntegerAttribute(attributes, node, "start");
        }

        return attributes.Count == 0 ? string.Empty : $" {string.Join(" ", attributes)}";
    }

    private static void AddPositiveIntegerAttribute(List<string> attributes, HtmlNode node, string name)
    {
        if (int.TryParse(node.GetAttributeValue(name, string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value > 0)
        {
            attributes.Add($"{name}=\"{value.ToString(CultureInfo.InvariantCulture)}\"");
        }
    }

    private static bool TryGetSafeHttpsUrl(string value, out string absoluteUrl)
    {
        absoluteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(new Uri(ChangelogPageUrl), value, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        absoluteUrl = uri.AbsoluteUri;
        return true;
    }

    private sealed class ChangelogApiResponse
    {
        public ChangelogApiResponse()
        {
        }

        public List<ChangelogApiRelease>? Releases { get; init; }
        public string? ViewAllUrl { get; init; }
    }

    private sealed class ChangelogApiRelease
    {
        public ChangelogApiRelease()
        {
        }

        public string Version { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Date { get; init; } = string.Empty;
        public string HtmlContent { get; init; } = string.Empty;
    }
}
