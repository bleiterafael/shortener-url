namespace UrlShortener.Api.Application;

public sealed record CreateLinkRequest(string? OriginalUrl);

public sealed record CreateLinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    long ClickCount,
    DateTimeOffset CreatedAtUtc,
    string ManagementKey);

public sealed record LinkStatsResponse(
    string Code,
    string OriginalUrl,
    long ClickCount,
    DateTimeOffset CreatedAtUtc);

