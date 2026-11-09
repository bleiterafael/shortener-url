namespace UrlShortener.Api.Application;

public interface ILinkService
{
    Task<LinkServiceResult<CreateLinkResponse>> CreateAsync(
        CreateLinkRequest request,
        string baseUrl,
        CancellationToken cancellationToken = default);

    Task<LinkServiceResult<string>> ResolveAndIncrementClickAsync(
        string code,
        CancellationToken cancellationToken = default);

    Task<LinkServiceResult<LinkStatsResponse>> GetStatsAsync(
        string code,
        CancellationToken cancellationToken = default);
}

