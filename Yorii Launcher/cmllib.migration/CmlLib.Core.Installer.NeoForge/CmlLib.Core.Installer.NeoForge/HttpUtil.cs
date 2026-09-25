namespace CmlLib.Core.Installer.NeoForge;

internal class HttpUtil
{
    public static async Task DownloadFile(HttpClient httpClient, string url, string dest)
    {
        var dirPath = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dirPath))
            Directory.CreateDirectory(dirPath);

        var httpResult = await httpClient.GetAsync(url);
        using var resultStream = await httpResult.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(dest);
        await resultStream.CopyToAsync(fileStream);
    }
}
