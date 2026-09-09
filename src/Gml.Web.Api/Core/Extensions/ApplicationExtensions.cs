using System.Net;
using System.Reactive.Subjects;
using System.Text;
using Gml.Domains.Settings;
using Gml.Web.Api.Core.Authentication;
using Gml.Web.Api.Core.Authorization;
using Gml.Web.Api.Core.Hubs;
using Gml.Web.Api.Core.Hubs.Controllers;
using Gml.Web.Api.Core.Integrations.Auth;
using Gml.Web.Api.Core.MappingProfiles;
using Gml.Web.Api.Core.Middlewares;
using Gml.Web.Api.Core.Options;
using Gml.Web.Api.Core.Services;
using Gml.Web.Api.Data;
using GmlCore.Interfaces.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Gml.Web.Api.Core.Extensions;

public static class ApplicationExtensions
{
    private static string _policyName = string.Empty;

    // The exact value shipped in launchSettings.json/README as the local-dev example —
    // if it ever ends up running a real Production instance, tokens signed with it are
    // forgeable by anyone who's read the public docs.
    private static readonly string[] KnownWeakSecurityKeys =
    {
        "jkuhbsfgvuk4gfikhn8i7wa34rkbqw23"
    };

    public static WebApplication RegisterServices(this WebApplication app)
    {
        var swaggerEnabled = bool.TryParse(GetEnvironmentVariable("SWAGGER_ENABLED"), out var isEnabled) && isEnabled;

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.RegisterEndpoints()
            .UseCors(_policyName)
            .UseMiddleware<BadRequestExceptionMiddleware>()
            .UseMiddleware<PluginRouterMiddleware>();

        if (swaggerEnabled)
        {
            app.UseSwagger().UseSwaggerUI();
        }

        app.MapHealthChecks("/health");

        app.InitializeDatabase();

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider.GetRequiredService<PluginsService>();
        services.RestorePlugins();

        return app;
    }

    public static WebApplicationBuilder RegisterServices(this WebApplicationBuilder builder)
    {
        var serverSettings = GetServerSettings();

        _policyName = serverSettings.PolicyName;

        builder.RegisterEndpointsInfo(serverSettings.ProjectName, serverSettings.ProjectDescription);
        builder.RegisterSystemComponents(serverSettings);
        builder.Services.ConfigureOptions<ProfileHub.ConfigureJsonOptions>();

        return builder;
    }

    private static ServerSettings GetServerSettings()
    {
        var projectName = GetEnvironmentVariable("PROJECT_NAME");
        var marketEndpoint = GetEnvironmentVariable("MARKET_ENDPOINT");
        var projectDescription = GetEnvironmentVariable("PROJECT_DESCRIPTION");
        var policyName = GetEnvironmentVariable("PROJECT_POLICYNAME");
        var clientOriginRaw = GetEnvironmentVariable("PROJECT_CLIENT_ORIGIN");
        var clientOrigins = clientOriginRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (clientOrigins.Length == 0)
            clientOrigins = ["http://localhost:3001"];
        var projectPath = GetEnvironmentVariable("PROJECT_PATH");
        var securityKey = GetEnvironmentVariable("SECURITY_KEY");
        var swaggerEnabled = bool.TryParse(GetEnvironmentVariable("SWAGGER_ENABLED"), out var isEnabled) && isEnabled;

        var textureEndpoint = GetEnvironmentVariable("SERVICE_TEXTURE_ENDPOINT");

        var jwtIssuer = GetEnvironmentVariable("JWT_ISSUER");
        var jwtAudience = GetEnvironmentVariable("JWT_AUDIENCE");
        var accessMinutesStr = GetEnvironmentVariable("JWT_ACCESS_MINUTES");
        var refreshDaysStr = GetEnvironmentVariable("JWT_REFRESH_DAYS");
        var httpProxyAdress = GetEnvironmentVariable("HTTP_PROXY_ADDRESS");
        var httpProxyUsername = GetEnvironmentVariable("HTTP_PROXY_USERNAME");
        var httpProxyPassword = GetEnvironmentVariable("HTTP_PROXY_PASSWORD");
        int.TryParse(accessMinutesStr, out var accessMinutes);
        int.TryParse(refreshDaysStr, out var refreshDays);

        return new ServerSettings
        {
            HttpProxyAddress = httpProxyAdress,
            HttpProxyUsername = httpProxyUsername,
            HttpProxyPassword = httpProxyPassword,
            ProjectDescription = projectDescription,
            ProjectName = projectName,
            PolicyName = policyName,
            ClientOrigins = clientOrigins,
            MarketEndpoint = marketEndpoint,
            IsEnabledApiDocs = swaggerEnabled,
            ProjectVersion = "1.1.0",
            SecurityKey = securityKey,
            ProjectPath = projectPath,
            TextureEndpoint = textureEndpoint,
            JwtIssuer = string.IsNullOrWhiteSpace(jwtIssuer) ? "gml-api" : jwtIssuer,
            JwtAudience = string.IsNullOrWhiteSpace(jwtAudience) ? "gml-clients" : jwtAudience,
            AccessTokenMinutes = accessMinutes > 0 ? accessMinutes : 15,
            RefreshTokenDays = refreshDays > 0 ? refreshDays : 30
        };
    }

    /// <summary>
    /// Fails startup in Production if SECURITY_KEY is missing, too short, or set to the
    /// well-known example value shared across this repo's docs/dev config — that key
    /// signs every JWT the API issues, so a weak/public value forges auth for anyone.
    /// </summary>
    private static void ValidateSecurityKey(WebApplicationBuilder builder, string? securityKey)
    {
        if (!builder.Environment.IsProduction())
            return;

        if (string.IsNullOrWhiteSpace(securityKey) ||
            securityKey.Length < 32 ||
            KnownWeakSecurityKeys.Contains(securityKey))
        {
            throw new InvalidOperationException(
                "SECURITY_KEY is missing, shorter than 32 characters, or set to the " +
                "well-known example value from this repo's README/launchSettings.json. " +
                "Set a real, unique secret via the SECURITY_KEY environment variable " +
                "before running in Production.");
        }
    }

    private static WebApplicationBuilder RegisterEndpointsInfo(this WebApplicationBuilder builder,
        string projectName,
        string? projectDescription)
    {
        builder.Services
            .AddEndpointsApiExplorer()
            .RegisterSwagger(projectName, projectDescription);

        return builder;
    }

    private static WebApplicationBuilder RegisterSystemComponents(
        this WebApplicationBuilder builder,
        ServerSettings settings)
    {
        ValidateSecurityKey(builder, settings.SecurityKey);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecurityKey));

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = settings.JwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key
        };

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 209715200; // 200Мб
        });

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 209715200; // 200 MB
        });

        builder.Services
            .AddHttpClient()
            .AddNamedHttpClients(settings.MarketEndpoint)
            .AddMemoryCache()
            .AddDbContext<DatabaseContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("SQLite")))
            .AddAutoMapper(map =>
            {
                map.AddProfile<AuthServerMapper>();
                map.AddProfile<DiscordRpcMapper>();
                map.AddProfile<LauncherMapper>();
                map.AddProfile<ModsMapper>();
                map.AddProfile<NewsMapper>();
                map.AddProfile<PlayerMapper>();
                map.AddProfile<ProfileMapper>();
                map.AddProfile<ServerMapper>();
                map.AddProfile<SettingsMapper>();
                map.AddProfile<SystemIOMapper>();
                map.AddProfile<UserMapper>();
                map.AddProfile<RbacMapper>();
            })
            .ConfigureGmlManager(
                CreateHttpClient(settings.HttpProxyAddress, settings.HttpProxyUsername, settings.HttpProxyPassword),
                settings.ProjectName,
                settings.SecurityKey,
                settings.ProjectPath,
                settings.TextureEndpoint
            )
            .ConfigureRateLimit()
            .AddHealthChecks().Services
            .AddSingleton(settings)
            .AddSingleton<IAuthServiceFactory, AuthServiceFactory>()
            .AddSingleton<PluginsService>()
            .AddSingleton<PluginAssemblyManager>()
            .AddSingleton<HubEvents>()
            .AddSingleton<ISubject<Settings>, Subject<Settings>>()
            .AddSingleton<PlayersController>()
            .AddSingleton<NotificationController>()
            .AddScoped<ISystemService, SystemService>()
            .AddScoped<RestoreService>()
            .AddScoped<ISkinServiceManager, SkinServiceManager>()
            .AddSingleton<IAuthService, AuthService>()
            .AddSingleton<IGitHubService, GitHubService>()
            .AddSingleton<ApplicationContext>()
            .AddSingleton<IAccessTokenService, AccessTokenService>()
            .AddTransient<UndefinedAuthService>()
            .AddTransient<DataLifeEngineAuthService>()
            .AddTransient<UnicoreCMSAuthService>()
            .AddTransient<EasyCabinetAuthService>()
            .AddTransient<CustomEndpointAuthService>()
            .AddTransient<NamelessMCAuthService>()
            .AddTransient<WebMCRAuthService>()
            .AddTransient<AzuriomAuthService>()
            .AddTransient<WordPressAuthService>()
            .AddTransient<AnyAuthService>()
            .RegisterRepositories()
            .RegisterValidators()
            .RegisterCors(settings.PolicyName, settings.ClientOrigins)
            .AddSignalR();

        builder.Services.AddAuthorization();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "MultiScheme";
                options.DefaultScheme = "MultiScheme";
                options.DefaultChallengeScheme = "MultiScheme";
            })
            .AddPolicyScheme("MultiScheme", "JWT or External App", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (authHeader.StartsWith("Bearer "))
                    {
                        var token = authHeader.Substring("Bearer ".Length).Trim();
                        if (token.StartsWith("eyJ"))
                            return JwtBearerDefaults.AuthenticationScheme;
                        else
                            return "ExternalApplication";
                    }

                    return JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer(jwt =>
            {
                jwt.SaveToken = true;
                jwt.TokenValidationParameters = tokenValidationParameters;
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/ws"))
                        {
                            context.Token = accessToken;
                            return Task.CompletedTask;
                        }

                        // Browser dashboard requests carry the access token as an httpOnly cookie
                        // instead of an Authorization header (same pattern as the refreshToken
                        // cookie). Only used as a fallback — an explicit Authorization header (the
                        // launcher and other API consumers) always wins.
                        if (!context.Request.Headers.ContainsKey("Authorization") &&
                            context.Request.Cookies.TryGetValue("accessToken", out var cookieToken) &&
                            !string.IsNullOrEmpty(cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ExternalApplicationAuthenticationHandler>(
                "ExternalApplication", options => { });

        // RBAC dynamic permission policies and handler
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPermissionPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return builder;
    }

    private static HttpClient CreateHttpClient(string? settingsHttpProxyAddress, string? settingsHttpProxyUsername,
        string? settingsHttpProxyPassword)
    {
        if (string.IsNullOrWhiteSpace(settingsHttpProxyAddress))
            return new HttpClient();

        if (!string.IsNullOrWhiteSpace(settingsHttpProxyUsername) &&
            !string.IsNullOrWhiteSpace(settingsHttpProxyPassword))
        {
            var proxy = new WebProxy(settingsHttpProxyAddress)
            {
                Credentials = new NetworkCredential(settingsHttpProxyUsername, settingsHttpProxyPassword)
            };

            var handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true
            };

            return new HttpClient(handler);
        }
        else
        {
            var proxy = new WebProxy(settingsHttpProxyAddress);

            var handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true
            };

            return new HttpClient(handler);
        }
    }

    private static string GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? string.Empty;
    }
}
