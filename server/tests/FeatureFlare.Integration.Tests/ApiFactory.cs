using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFlare.Application.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FeatureFlare.Integration.Tests;

/// <summary>Hosts the API in-process with the in-memory provider and well-known test keys.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key";
    public const string SdkKey = "test-sdk-key";

    public static readonly JsonSerializerOptions Json = FeatureFlareJson.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Persistence:Provider", "InMemory");
        builder.UseSetting("Auth:AdminKeys:0", AdminKey);
        builder.UseSetting("Auth:SdkKeys:0", SdkKey);
    }

    public HttpClient CreateClient(string? apiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return client;
    }
}

internal static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostJson<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PutJson<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json, TestContext.Current.CancellationToken);

    public static async Task<JsonElement> ReadJson(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement;
    }
}
