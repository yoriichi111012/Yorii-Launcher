namespace Quiescent.Core.Internals;

internal static class HttpUtil
{
    public readonly static Lazy<HttpClient> SharedHttpClient = new Lazy<HttpClient>(() => new HttpClient());
}