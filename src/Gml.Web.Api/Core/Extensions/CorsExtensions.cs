namespace Gml.Web.Api.Core.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection RegisterCors(this IServiceCollection serviceCollection, string policyName,
        string[] allowedOrigins)
    {
        serviceCollection
            .AddCors(o => o.AddPolicy(policyName, policyBuilder =>
            {
                policyBuilder.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();

            }));

        return serviceCollection;
    }
}
