using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace BikeHub.Api.Authentication;

public static class ReverseProxyExtensions
{
    public static void AddBikeHubReverseProxy(this WebApplicationBuilder builder)
    {
        var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // nginx terminates TLS; client IP/host headers are deliberately not trusted.
            options.ForwardedHeaders = proxies.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
    }
}
