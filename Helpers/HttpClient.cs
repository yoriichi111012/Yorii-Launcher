using System;
using System.Net.Http;

namespace Yorii_Launcher.Helpers
{
    public static class HttpService
    {
        public static HttpClient Client { get; } = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }
}
