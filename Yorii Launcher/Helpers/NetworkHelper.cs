using System.Threading.Tasks;

namespace Yorii_Launcher.Helpers
{
    public static class NetworkHelper
    {
        public static async Task<bool> InternetAvailable()
        {
            try
            {
                using var response = await HttpService.Client.GetAsync("https://www.google.com");
                var online = response.IsSuccessStatusCode;
                return online;
            }
            catch
            {
                Logger.Warn("Internet connectivity check failed");
                return false;
            }
        }
    }
}