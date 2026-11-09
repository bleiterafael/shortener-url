using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Application;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;

namespace UrlShortener.Tests.Unit;

public sealed class LinkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _dbContext;
    private readonly LinkService _sut;

    public LinkServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new AppDbContext(options);
        _dbContext.Database.Migrate();

        _sut = new LinkService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidHttpsUrl_GeneratesSevenCharUnambiguousCodeAndStoresHashedManagementKey()
    {
        // Arrange
        var request = new CreateLinkRequest("https://balta.io/carreiras/dotnet");

        // Act
        var result = await _sut.CreateAsync(request, "https://short.local");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(7, result.Value.Code.Length);
        Assert.All(result.Value.Code, c => Assert.Contains(c, LinkService.UnambiguousAlphabet));
        Assert.DoesNotContain(result.Value.Code, c => c is '0' or 'o' or '1' or 'l' or 'i');
        Assert.Equal("https://balta.io/carreiras/dotnet", result.Value.OriginalUrl);
        Assert.Equal($"https://short.local/{result.Value.Code}", result.Value.ShortUrl);
        Assert.Equal(0, result.Value.ClickCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ManagementKey));

        var saved = await _dbContext.ShortenedLinks.SingleAsync(x => x.Code == result.Value.Code);
        Assert.NotEqual(result.Value.ManagementKey, saved.ManagementKeyHash);
        Assert.Equal(64, saved.ManagementKeyHash.Length);

        var expectedHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Value.ManagementKey)));
        Assert.Equal(expectedHash, saved.ManagementKeyHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithEmptyUrl_ReturnsRequiredValidationErrorInEnglish(string? invalidUrl)
    {
        // Act
        var result = await _sut.CreateAsync(new CreateLinkRequest(invalidUrl), "http://localhost");

        // Assert
        Assert.Equal(LinkServiceStatus.ValidationError, result.Status);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("The destination URL is required.", result.ValidationErrors["originalUrl"]);
    }

    [Fact]
    public async Task CreateAsync_WithUrlExactly2048Characters_Succeeds()
    {
        // Arrange
        const string prefix = "https://example.com/";
        var url2048 = prefix + new string('a', 2048 - prefix.Length);
        Assert.Equal(2048, url2048.Length);

        // Act
        var result = await _sut.CreateAsync(new CreateLinkRequest(url2048), "http://localhost");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(url2048, result.Value!.OriginalUrl);
    }

    [Fact]
    public async Task CreateAsync_WithUrlExceeding2048Characters_ReturnsMaxLengthValidationErrorInEnglish()
    {
        // Arrange
        const string prefix = "https://example.com/";
        var url2049 = prefix + new string('a', 2049 - prefix.Length);
        Assert.Equal(2049, url2049.Length);

        // Act
        var result = await _sut.CreateAsync(new CreateLinkRequest(url2049), "http://localhost");

        // Assert
        Assert.Equal(LinkServiceStatus.ValidationError, result.Status);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("The destination URL must not exceed 2048 characters.", result.ValidationErrors["originalUrl"]);
    }

    [Theory]
    [InlineData("ftp://files.example.com/resource")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-valid-url")]
    [InlineData("/relative/path/only")]
    public async Task CreateAsync_WithInvalidSchemeOrRelativeUrl_ReturnsSchemeValidationErrorInEnglish(string invalidUrl)
    {
        // Act
        var result = await _sut.CreateAsync(new CreateLinkRequest(invalidUrl), "http://localhost");

        // Assert
        Assert.Equal(LinkServiceStatus.ValidationError, result.Status);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains(
            "The destination URL must be a valid absolute HTTP or HTTPS URL.",
            result.ValidationErrors["originalUrl"]);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateDestinationUrl_GeneratesDistinctIndependentCodeWithZeroCounter()
    {
        // Arrange
        var request = new CreateLinkRequest("https://balta.io");

        // Act
        var first = await _sut.CreateAsync(request, "http://localhost");
        var second = await _sut.CreateAsync(request, "http://localhost");

        // Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.Code, second.Value!.Code);
        Assert.Equal(0, first.Value.ClickCount);
        Assert.Equal(0, second.Value.ClickCount);
    }

    [Fact]
    public async Task CreateAsync_WhenGeneratedCodeCollidesWithExistingCode_RetriesUntilUniqueCodeIsFound()
    {
        // Arrange
        _dbContext.ShortenedLinks.Add(new ShortenedLink
        {
            Code = "k9m2x7p",
            OriginalUrl = "https://existing.example.com",
            ClickCount = 0,
            ManagementKeyHash = new string('a', 64)
        });
        await _dbContext.SaveChangesAsync();

        var sequence = new Queue<string>(["k9m2x7p", "q8n3z4r"]);
        var serviceWithCollision = new LinkService(_dbContext, () => sequence.Dequeue());

        // Act
        var result = await serviceWithCollision.CreateAsync(
            new CreateLinkRequest("https://new.example.com"),
            "http://localhost");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("q8n3z4r", result.Value!.Code);
    }

    [Fact]
    public async Task ResolveAndIncrementClickAsync_WithExistingCodeCaseInsensitive_AtomicallyIncrementsCounterAndReturnsUrl()
    {
        // Arrange
        var created = await _sut.CreateAsync(new CreateLinkRequest("https://balta.io"), "http://localhost");
        var upperCaseCode = created.Value!.Code.ToUpperInvariant();

        // Act
        var firstRedirect = await _sut.ResolveAndIncrementClickAsync(upperCaseCode);
        var secondRedirect = await _sut.ResolveAndIncrementClickAsync(created.Value.Code);

        // Assert
        Assert.True(firstRedirect.IsSuccess);
        Assert.Equal("https://balta.io", firstRedirect.Value);
        Assert.True(secondRedirect.IsSuccess);

        var stats = await _sut.GetStatsAsync(created.Value.Code);
        Assert.True(stats.IsSuccess);
        Assert.Equal(2, stats.Value!.ClickCount);
    }

    [Fact]
    public async Task ResolveAndIncrementClickAsync_WithNonExistentCode_ReturnsNotFoundInEnglish()
    {
        // Act
        var result = await _sut.ResolveAndIncrementClickAsync("zzzzzzz");

        // Assert
        Assert.Equal(LinkServiceStatus.NotFound, result.Status);
        Assert.Equal("Short link not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatsAsync_WithExistingCodeCaseInsensitive_ReturnsClickCountWithoutIncrementingCounter()
    {
        // Arrange
        var created = await _sut.CreateAsync(new CreateLinkRequest("https://balta.io"), "http://localhost");
        await _sut.ResolveAndIncrementClickAsync(created.Value!.Code);

        // Act
        var firstQuery = await _sut.GetStatsAsync(created.Value.Code.ToUpperInvariant());
        var secondQuery = await _sut.GetStatsAsync(created.Value.Code);

        // Assert
        Assert.True(firstQuery.IsSuccess);
        Assert.Equal(1, firstQuery.Value!.ClickCount);
        Assert.True(secondQuery.IsSuccess);
        Assert.Equal(1, secondQuery.Value!.ClickCount);
    }

    [Fact]
    public async Task GetStatsAsync_WithNonExistentCode_ReturnsNotFoundInEnglish()
    {
        // Act
        var result = await _sut.GetStatsAsync("zzzzzzz");

        // Assert
        Assert.Equal(LinkServiceStatus.NotFound, result.Status);
        Assert.Equal("Short link not found.", result.ErrorMessage);
    }
}

