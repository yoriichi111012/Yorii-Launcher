using System.Threading.Tasks;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher.Helpers
{
    // simple connectivity check by pinging google
    public static class NetworkHelper
    {
        public static async Task<bool> InternetAvailable()
        {
            try
            {
                using var response = await HttpService.Client.GetAsync("https://www.google.com");

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}