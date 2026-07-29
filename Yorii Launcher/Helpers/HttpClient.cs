using System;
using System.Net.Http;

namespace Yorii_Launcher.Helpers
{
    public static class HttpService
    {
        // shared connection pool so api calls and downloads dont fight for ports
        private static readonly SocketsHttpHandler sharedHandler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10
        };

        // quick api calls — modrinth search, version checks, etc
        public static HttpClient Client { get; } = new(sharedHandler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        // file downloads — mods, resource packs
        public static HttpClient DownloadClient { get; } = new(sharedHandler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }
}
