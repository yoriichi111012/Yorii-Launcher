using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace Quiescent.XboxAuthNet.XboxLive.Responses
{
    public class XboxAuthResponseHandler
    {
        public async Task<T> HandleResponse<T>(HttpResponseMessage res)
        {
            var resBody = await res.Content.ReadAsStringAsync()
                .ConfigureAwait(false);

            try
            {
                res.EnsureSuccessStatusCode();
                return Quiescent.Core.Commons.Serialization.QuiescentJson.Deserialize<T>(resBody)
                    ?? throw new JsonException();
            }
            catch (Exception ex) when (
                ex is JsonException ||
                ex is HttpRequestException)
            {
                try
                {
                    throw XboxAuthException.FromResponseBody(resBody, (int)res.StatusCode);
                }
                catch (FormatException)
                {
                    try
                    {
                        throw XboxAuthException.FromResponseHeaders(res.Headers, (int)res.StatusCode);
                    }
                    catch (FormatException)
                    {
                        throw new XboxAuthException($"[XBOX] {(int)res.StatusCode}: {res.ReasonPhrase} | url: {res.RequestMessage?.RequestUri} | reqBody: {await ReadRequest(res).ConfigureAwait(false)} | respBody: {Truncate(resBody)}", (int)res.StatusCode);
                    }
                }
            }
        }

        private static async Task<string> ReadRequest(HttpResponseMessage res)
        {
            try
            {
                if (res.RequestMessage?.Content == null) return "(none)";
                return Truncate(await res.RequestMessage.Content.ReadAsStringAsync());
            }
            catch { return "(unreadable)"; }
        }

        private static string Truncate(string s) =>
            string.IsNullOrEmpty(s) ? "(empty)" : s.Length <= 300 ? s : s[..300] + "...";
    }
}
