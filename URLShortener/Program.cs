using MongoDB.Driver;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(static siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.UseMongoDBClient(serviceProvider =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var connectionString = configuration.GetConnectionString("default");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("MongoDB connection string 'orleans-default' not found in configuration.");
        }

        return MongoClientSettings.FromConnectionString(connectionString);
    });
    siloBuilder.AddMongoDBGrainStorage("urls", 
        opt => { opt.DatabaseName = "urls-shortener"; });
    
});

builder.AddServiceDefaults();

using var app = builder.Build();

app.MapGet("/", static () => "Welcome to the URL shortener, powered by Orleans!");

app.MapGet("/shorten",
    static async (IGrainFactory grains, HttpRequest request, string url) =>
    {
        var host = $"{request.Scheme}://{request.Host.Value}";

        if (string.IsNullOrWhiteSpace(url) ||
            Uri.IsWellFormedUriString(url, UriKind.Absolute) is false)
        {
            return Results.BadRequest($"""
                                       The URL query string is required and needs to be well formed.
                                       Consider, ${host}/shorten?url=https://www.microsoft.com.
                                       """);
        }

        var shortenedRouteSegment = Guid.NewGuid().GetHashCode().ToString("X");

        var shortenerGrain =
            grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegment);

        await shortenerGrain.SetUrl(url);

        var resultBuilder = new UriBuilder(host)
        {
            Path = $"/go/{shortenedRouteSegment}"
        };

        return Results.Ok(resultBuilder.Uri);
    });

app.MapGet("/go/{shortenedRouteSegment:required}",
    static async (IGrainFactory grains, string shortenedRouteSegment) =>
    {
        var shortenerGrain =
            grains.GetGrain<IUrlShortenerGrain>(shortenedRouteSegment);

        var url = await shortenerGrain.GetUrl();
        
        var redirectBuilder = new UriBuilder(url);

        return Results.Redirect(redirectBuilder.Uri.ToString());
    });

app.Run();

internal interface IUrlShortenerGrain : IGrainWithStringKey
{
    [Alias("SetUrl")]
    Task SetUrl(string fullUrl);

    [Alias("GetUrl")]
    Task<string> GetUrl();
}

public sealed class UrlShortenerGrain(
    [PersistentState(
        stateName: "url",
        storageName: "urls")]
    IPersistentState<UrlDetails> state)
    : Grain, IUrlShortenerGrain
{
    public async Task SetUrl(string fullUrl)
    {
        state.State = new UrlDetails
        {
            ShortenedRouteSegment = this.GetPrimaryKeyString(),
            FullUrl = fullUrl
        };

        await state.WriteStateAsync();
    }

    public Task<string> GetUrl() =>
        Task.FromResult(state.State.FullUrl);
}

[GenerateSerializer, Alias(nameof(UrlDetails))]
public sealed record UrlDetails
{
    [Id(0)] public string FullUrl { get; init; } = "";

    [Id(1)] public string ShortenedRouteSegment { get; init; } = "";
}
