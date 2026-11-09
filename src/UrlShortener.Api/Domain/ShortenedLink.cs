namespace UrlShortener.Api.Domain;

public sealed class ShortenedLink
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Code { get; init; }

    public required string OriginalUrl { get; init; }

    public long ClickCount { get; init; }

    public required string ManagementKeyHash { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

