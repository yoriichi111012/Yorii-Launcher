namespace CmlLib.Core.Installer.Forge;

public class ForgePackageName
{
    public static string GetPath(string name, char directorySeparator)
    {
        return GetPath(name, "jar", directorySeparator);
    }

    public static string GetPath(string name, string extension, char directorySeparator)
    {
        var names = name.Split(':');

        var filename = string.Join("-", names, 1, names.Length - 1);
        filename += "." + extension;

        return string.Join(directorySeparator.ToString(),
            names[0].Replace('.', directorySeparator),
            names[1],
            names[2],
            filename);
    }
}
