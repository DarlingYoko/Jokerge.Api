using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Gml.Web.Api.Core.Extensions;

public static class ForwardedHeadersExtension
{
    // The API is only reachable through Gml.Web.Proxy (and usually a TLS-terminating proxy in front
    // of it), so the TCP peer is always a proxy container. Restoring the client IP from
    // X-Forwarded-For lets per-client rate limits see the player instead of the proxy. Only peers in
    // the private ranges are trusted, so a client hitting the API directly cannot spoof its IP.
    //
    // Only X-Forwarded-For is processed: the middleware consumes the headers it handles, and the
    // handlers read X-Forwarded-Host/-Scheme themselves to build public URLs.
    public static ForwardedHeadersOptions CreateOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };

        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));

        return options;
    }
}
