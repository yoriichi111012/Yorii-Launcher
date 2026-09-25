using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace Quiescent.XboxAuthNet
{
    public class HttpHelper
    {
        public const string UserAgent = "Mozilla/5.0 (XboxReplay; XboxLiveAuth/3.0) " +
                                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                        "Chrome/71.0.3578.98 Safari/537.36";

        public static string GetQueryString(Dictionary<string, string?> queries)
        {
            return string.Join("&",
                queries.Select(x => $"{x.Key}={HttpUtility.UrlEncode(x.Value)}"));
        }

        public static System.Net.Http.HttpContent CreateJsonContent<T>(T obj)
        {
            return new System.Net.Http.StringContent(
                Quiescent.Core.Commons.Serialization.QuiescentJson.Serialize(obj),
                Encoding.UTF8, "application/json");
        }
    }
}
