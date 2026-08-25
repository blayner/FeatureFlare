using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FeatureFlare.Api.Auth;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string QueryParameter = "sdkKey";
    public const string StreamPath = "/api/stream";
    public const string KeyTypeClaim = "featureflare:key-type";
    public const string AdminKeyType = "admin";
    public const string SdkKeyType = "sdk";
}

public static class Policies
{
    /// <summary>Management API: create/update/delete flags, segments and experiments.</summary>
    public const string Admin = "admin";

    /// <summary>Read-only SDK access: config download, streaming, evaluation and event ingestion.</summary>
    public const string Sdk = "sdk";
}

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public List<string> AdminKeys { get; set; } = [];

    public List<string> SdkKeys { get; set; } = [];
}

/// <summary>
/// Accepts <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key: &lt;key&gt;</c>. Two key types exist so that
/// SDK keys, which ship inside services, can never modify configuration.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var (presented, fromQuery) = ReadKey();
        if (presented is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? keyType = null;
        if (!fromQuery && Matches(presented, Options.AdminKeys))
        {
            keyType = ApiKeyDefaults.AdminKeyType;
        }
        else if (Matches(presented, Options.SdkKeys))
        {
            keyType = ApiKeyDefaults.SdkKeyType;
        }

        if (keyType is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, $"{keyType}-key"),
                new Claim(ApiKeyDefaults.KeyTypeClaim, keyType),
            },
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private (string? Key, bool FromQuery) ReadKey()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerKey) &&
            !string.IsNullOrWhiteSpace(headerKey))
        {
            return (headerKey.ToString().Trim(), false);
        }

        var authorization = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return (authorization[bearer.Length..].Trim(), false);
        }

        // The browser-native EventSource API cannot set headers, so the stream endpoint (and only it)
        // also accepts the key as a query parameter. Only SDK keys are honoured there: admin keys must
        // never end up in URLs and access logs.
        if (HttpMethods.IsGet(Request.Method) &&
            Request.Path.Equals(ApiKeyDefaults.StreamPath, StringComparison.OrdinalIgnoreCase) &&
            Request.Query.TryGetValue(ApiKeyDefaults.QueryParameter, out var queryKey) &&
            !string.IsNullOrWhiteSpace(queryKey))
        {
            return (queryKey.ToString().Trim(), true);
        }

        return (null, false);
    }

    /// <summary>Constant-time comparison to avoid leaking key prefixes through timing.</summary>
    private static bool Matches(string presented, IEnumerable<string> configured)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var match = false;
        foreach (var key in configured)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            match |= CryptographicOperations.FixedTimeEquals(presentedBytes, Encoding.UTF8.GetBytes(key));
        }

        return match;
    }
}
