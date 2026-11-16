using Newtonsoft.Json;

namespace Gml.Web.Api.Core.Options;

public class ServerSettings
{
    [JsonProperty(nameof(PolicyName))] public string PolicyName { get; set; } = "GmlPolicy";
    [JsonProperty(nameof(ProjectName))] public string ProjectName { get; set; } = "GmlServer";
    [JsonProperty(nameof(SecurityKey))] public string SecurityKey { get; set; } = "SecretGmlKey";
    public string JwtIssuer { get; set; } = "gml-api";
    public string JwtAudience { get; set; } = "gml-clients";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public string ProjectVersion { get; set; } = null!;
    // Extra domains the game accepts skins from (authlib-injector skinDomains), on top of the
    // request host and the skin/cloak services. SKIN_DOMAINS, e.g. ".example.com".
    public string[] SkinDomains { get; set; } = [];

    // Host that skin/cape URLs are built on instead of the request host. TEXTURE_PUBLIC_HOST.
    public string? TexturePublicHost { get; set; }
    public string[] ClientOrigins { get; set; } = [];

    [JsonProperty(nameof(ProjectDescription))]
    public string? ProjectDescription { get; set; }

    public string? ProjectPath { get; set; }
    public string? TextureEndpoint { get; set; }
    public string MarketEndpoint { get; set; } = "https://gml-market.recloud.tech";
    public bool IsEnabledApiDocs { get; set; }
    public string? HttpProxyAddress { get; set; }
    public string? HttpProxyUsername { get; set; }
    public string? HttpProxyPassword { get; set; }
}
