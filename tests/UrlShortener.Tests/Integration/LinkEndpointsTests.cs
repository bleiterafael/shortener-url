using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrlShortener.Api.Application;
using UrlShortener.Api.Data;

namespace UrlShortener.Tests.Integration;

public sealed class LinkEndpointsTests : IDisposable
{
    private readonly string _dbFilePath;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public LinkEndpointsTests()
    {
        _dbFilePath = Path.Combine(Path.GetTempPath(), $"urlshortener_test_{Guid.NewGuid():N}.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlite($"Data Source={_dbFilePath}"));
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();

        try
        {
            if (File.Exists(_dbFilePath))
            {
                File.Delete(_dbFilePath);
            }
        }
        catch
        {
            // Ignore cleanup locks on temp files
        }
    }

    [Fact]
    public async Task PostLinks_HappyPath_Returns201CreatedWithSevenCharCodeAndZeroClicks()
    {
        // Act
        var response = await _client.PostAsJsonAsync(
            "/links",
            new CreateLinkRequest("https://balta.io/eventos/halloween"));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.NotNull(payload);
        Assert.Equal(7, payload.Code.Length);
        Assert.Equal("https://balta.io/eventos/halloween", payload.OriginalUrl);
        Assert.Equal(0, payload.ClickCount);
        Assert.False(string.IsNullOrWhiteSpace(payload.ManagementKey));
        Assert.Equal($"/links/{payload.Code}", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://invalid-scheme.example.com")]
    public async Task PostLinks_InvalidUrl_Returns400BadRequestWithProblemDetails(string invalidUrl)
    {
        // Act
        var response = await _client.PostAsJsonAsync("/links", new CreateLinkRequest(invalidUrl));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Contains("originalUrl", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetRedirect_HappyPathAndCaseInsensitive_Returns302FoundWithNoCacheAndIncrementsCounter()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync(
            "/links",
            new CreateLinkRequest("https://balta.io"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.NotNull(created);

        // Act - first click with uppercase code (case-insensitive)
        var redirect1 = await _client.GetAsync($"/{created.Code.ToUpperInvariant()}");
        // Act - second click with original lowercase code
        var redirect2 = await _client.GetAsync($"/{created.Code}");

        // Assert
        Assert.Equal(HttpStatusCode.Found, redirect1.StatusCode);
        Assert.Equal("https://balta.io", redirect1.Headers.Location?.OriginalString);
        Assert.True(redirect1.Headers.CacheControl?.NoStore);
        Assert.True(redirect1.Headers.CacheControl?.NoCache);

        Assert.Equal(HttpStatusCode.Found, redirect2.StatusCode);

        var statsResponse = await _client.GetAsync($"/links/{created.Code}");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        var stats = await statsResponse.Content.ReadFromJsonAsync<LinkStatsResponse>();
        Assert.NotNull(stats);
        Assert.Equal(2, stats.ClickCount);
    }

    [Fact]
    public async Task GetRedirect_UnknownCode_Returns404NotFoundWithProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/zzzzzzz");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Short link not found.", problem.Detail);
    }

    [Fact]
    public async Task GetLinkStats_HappyPathAndAcrossApplicationRestart_PreservesLinkAndClickCount()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync(
            "/links",
            new CreateLinkRequest("https://balta.io/cursos"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.NotNull(created);

        await _client.GetAsync($"/{created.Code}");
        await _client.GetAsync($"/{created.Code}");
        await _client.GetAsync($"/{created.Code}");

        // Simulate application restart using a new WebApplicationFactory instance pointing to the same SQLite file
        using var restartedFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlite($"Data Source={_dbFilePath}"));
                });
            });
        using var restartedClient = restartedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Act
        var statsResponse1 = await restartedClient.GetAsync($"/links/{created.Code.ToUpperInvariant()}");
        var statsResponse2 = await restartedClient.GetAsync($"/links/{created.Code}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, statsResponse1.StatusCode);
        var stats1 = await statsResponse1.Content.ReadFromJsonAsync<LinkStatsResponse>();
        var stats2 = await statsResponse2.Content.ReadFromJsonAsync<LinkStatsResponse>();

        Assert.NotNull(stats1);
        Assert.NotNull(stats2);
        Assert.Equal(3, stats1.ClickCount);
        Assert.Equal(3, stats2.ClickCount);
    }
}

