using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Application;

public sealed class LinkService(AppDbContext dbContext, Func<string>? codeGenerator = null) : ILinkService
{
    public const string UnambiguousAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    public const int CodeLength = 7;
    public const int MaxUrlLength = 2048;
    private const int MaxCollisionRetries = 10;

    private readonly AppDbContext _dbContext = dbContext;
    private readonly Func<string> _codeGenerator = codeGenerator ?? GenerateDefaultCode;

    public async Task<LinkServiceResult<CreateLinkResponse>> CreateAsync(
        CreateLinkRequest request,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OriginalUrl))
        {
            return LinkServiceResult<CreateLinkResponse>.ValidationFailure(
                "originalUrl",
                "The destination URL is required.");
        }

        var originalUrl = request.OriginalUrl.Trim();

        if (originalUrl.Length > MaxUrlLength)
        {
            return LinkServiceResult<CreateLinkResponse>.ValidationFailure(
                "originalUrl",
                "The destination URL must not exceed 2048 characters.");
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return LinkServiceResult<CreateLinkResponse>.ValidationFailure(
                "originalUrl",
                "The destination URL must be a valid absolute HTTP or HTTPS URL.");
        }

        var managementKey = $"mkey_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
        var managementKeyHash = ComputeSha256Hex(managementKey);

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var candidateCode = _codeGenerator().ToLowerInvariant();

            var alreadyExists = await _dbContext.ShortenedLinks
                .AsNoTracking()
                .AnyAsync(x => x.Code == candidateCode, cancellationToken);

            if (alreadyExists)
            {
                continue;
            }

            var entity = new ShortenedLink
            {
                Id = Guid.NewGuid(),
                Code = candidateCode,
                OriginalUrl = originalUrl,
                ClickCount = 0,
                ManagementKeyHash = managementKeyHash,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _dbContext.ShortenedLinks.Add(entity);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                var normalizedBaseUrl = baseUrl.TrimEnd('/');
                var response = new CreateLinkResponse(
                    entity.Code,
                    $"{normalizedBaseUrl}/{entity.Code}",
                    entity.OriginalUrl,
                    entity.ClickCount,
                    entity.CreatedAtUtc,
                    managementKey);

                return LinkServiceResult<CreateLinkResponse>.Success(response);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique short code after maximum retries.");
    }

    public async Task<LinkServiceResult<string>> ResolveAndIncrementClickAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return LinkServiceResult<string>.NotFound();
        }

        var normalizedCode = code.Trim().ToLowerInvariant();

        var updatedRows = await _dbContext.ShortenedLinks
            .Where(x => x.Code == normalizedCode)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ClickCount, x => x.ClickCount + 1),
                cancellationToken);

        if (updatedRows == 0)
        {
            return LinkServiceResult<string>.NotFound();
        }

        var destinationUrl = await _dbContext.ShortenedLinks
            .AsNoTracking()
            .Where(x => x.Code == normalizedCode)
            .Select(x => x.OriginalUrl)
            .FirstOrDefaultAsync(cancellationToken);

        return destinationUrl is null
            ? LinkServiceResult<string>.NotFound()
            : LinkServiceResult<string>.Success(destinationUrl);
    }

    public async Task<LinkServiceResult<LinkStatsResponse>> GetStatsAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return LinkServiceResult<LinkStatsResponse>.NotFound();
        }

        var normalizedCode = code.Trim().ToLowerInvariant();

        var stats = await _dbContext.ShortenedLinks
            .AsNoTracking()
            .Where(x => x.Code == normalizedCode)
            .Select(x => new LinkStatsResponse(
                x.Code,
                x.OriginalUrl,
                x.ClickCount,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return stats is null
            ? LinkServiceResult<LinkStatsResponse>.NotFound()
            : LinkServiceResult<LinkStatsResponse>.Success(stats);
    }

    private static string GenerateDefaultCode() =>
        RandomNumberGenerator.GetString(UnambiguousAlphabet, CodeLength);

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
