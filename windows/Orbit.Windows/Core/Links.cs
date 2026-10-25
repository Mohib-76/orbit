using System.Text.RegularExpressions;

namespace Orbit.Core;

/// <summary>Web links passed between the devices (<c>orbit.url</c>). Only http and https ever open.</summary>
public static partial class Links
{
    /// <summary>Longest link Orbit sends or opens.</summary>
    public const int MaxLength = 4096;

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>The URL itself when it's an absolute http(s) link; null for anything else (file:, custom schemes…).</summary>
    public static string? Normalize(string? url)
    {
        url = url?.Trim();
        if (string.IsNullOrEmpty(url) || url.Length > MaxLength) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return null;
        return string.IsNullOrEmpty(u.Host) ? null : u.AbsoluteUri;
    }

    /// <summary>The clipboard holds just a link (nothing but the URL, maybe surrounded by whitespace).</summary>
    public static string? FromClipboard(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return null;
        return Normalize(text);
    }

    /// <summary>The first link in shared text such as "Article title https://example.com/a".</summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (Match m in UrlPattern().Matches(text))
            if (Normalize(m.Value.TrimEnd('.', ',', ')', ']', '!', '?', ';', ':')) is { } url) return url;
        return null;
    }

    /// <summary>"example.com/path" for display: no scheme, no "www.", no trailing slash.</summary>
    public static string Short(string url)
    {
        var s = url;
        foreach (var p in new[] { "https://", "http://" })
            if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s[p.Length..]; break; }
        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s.TrimEnd('/');
    }
}
