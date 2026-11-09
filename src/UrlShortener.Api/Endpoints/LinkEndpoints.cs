using UrlShortener.Api.Application;

namespace UrlShortener.Api.Endpoints;

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/links", CreateLinkAsync);
        app.MapGet("/links/{code}", GetLinkStatsAsync);
        app.MapGet("/{code}", RedirectToOriginalUrlAsync);

        return app;
    }

    private static async Task<IResult> CreateLinkAsync(
        CreateLinkRequest request,
        ILinkService linkService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var result = await linkService.CreateAsync(request, baseUrl, cancellationToken);

        if (result.Status == LinkServiceStatus.ValidationError)
        {
            return TypedResults.ValidationProblem(result.ValidationErrors!);
        }

        return TypedResults.Created($"/links/{result.Value!.Code}", result.Value);
    }

    private static async Task<IResult> RedirectToOriginalUrlAsync(
        string code,
        ILinkService linkService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await linkService.ResolveAndIncrementClickAsync(code, cancellationToken);

        if (result.Status == LinkServiceStatus.NotFound)
        {
            return TypedResults.Problem(
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found");
        }

        httpContext.Response.Headers.CacheControl = "no-store, no-cache";
        return TypedResults.Redirect(result.Value!, permanent: false);
    }

    private static async Task<IResult> GetLinkStatsAsync(
        string code,
        ILinkService linkService,
        CancellationToken cancellationToken)
    {
        var result = await linkService.GetStatsAsync(code, cancellationToken);

        if (result.Status == LinkServiceStatus.NotFound)
        {
            return TypedResults.Problem(
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found");
        }

        return TypedResults.Ok(result.Value!);
    }
}
