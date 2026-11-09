namespace UrlShortener.Api.Application;

public enum LinkServiceStatus
{
    Success,
    ValidationError,
    NotFound
}

public sealed record LinkServiceResult<T>(
    LinkServiceStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == LinkServiceStatus.Success;

    public static LinkServiceResult<T> Success(T value) =>
        new(LinkServiceStatus.Success, Value: value);

    public static LinkServiceResult<T> ValidationFailure(string field, params string[] errors) =>
        new(
            LinkServiceStatus.ValidationError,
            ValidationErrors: new Dictionary<string, string[]> { [field] = errors });

    public static LinkServiceResult<T> NotFound(string message = "Short link not found.") =>
        new(LinkServiceStatus.NotFound, ErrorMessage: message);
}

