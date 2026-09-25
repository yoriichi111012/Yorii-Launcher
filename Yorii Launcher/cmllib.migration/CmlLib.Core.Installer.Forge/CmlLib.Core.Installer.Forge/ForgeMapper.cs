using System.Text.RegularExpressions;

namespace CmlLib.Core.Installer.Forge;

public class ForgeMapper
{
    private static readonly Regex argBracket = new Regex(@"\$?\{(.*?)}");

    public static IEnumerable<string> Map(
        IEnumerable<string> arg, 
        IReadOnlyDictionary<string, string?> dicts, 
        string prepath,
        char directorySeparator)
    {
        var checkPath = !string.IsNullOrEmpty(prepath);
        foreach (string item in arg)
        {
            var a = Interpolation(item, dicts, false);
            if (checkPath)
                a = ToFullPath(a, prepath, directorySeparator);
            yield return HandleEmptyArg(a);
        }
    }

    public static string ToFullPath(string str, string prepath, char directorySeparator)
    {
        if (str.StartsWith("[") && str.EndsWith("]") && !string.IsNullOrEmpty(prepath))
        {
            var innerStr = str.TrimStart('[').TrimEnd(']').Split('@');
            var pathName = innerStr[0];
            var extension = "jar";

            if (innerStr.Length > 1)
                extension = innerStr[1];

            var packagePath = ForgePackageName.GetPath(pathName, extension, directorySeparator);
            return $"{prepath}{directorySeparator}{packagePath}";
        }
        else if (str.StartsWith("\'") && str.EndsWith("\'"))
            return str.Trim('\'');
        else
            return str;
    }

    public static string Interpolation(string str, IReadOnlyDictionary<string, string?> dicts, bool handleEmpty)
    {
        str = argBracket.Replace(str, (match =>
        {
            if (match.Groups.Count < 2)
                return match.Value;

            var key = match.Groups[1].Value;
            if (dicts.TryGetValue(key, out string? value))
            {
                if (value == null)
                    value = "";

                return value;
            }

            return match.Value;
        }));

        if (handleEmpty)
            return HandleEmptyArg(str);
        else
            return str;
    }

    // key=value 1 => key="value 1"
    // key="va  l" => key="va  l"
    // va lue => "va lue"
    // "va lue" => "va lue"
    public static string HandleEmptyArg(string input)
    {
        if (input.Contains("="))
        {
            var s = input.Split('=');

            if (s[1].Contains(" ") && !checkEmptyHandled(s[1]))
                return s[0] + "=\"" + s[1] + "\"";
            else
                return input;
        }
        else if (input.Contains(" ") && !checkEmptyHandled(input))
            return "\"" + input + "\"";
        else
            return input;
    }

    static bool checkEmptyHandled(string str)
    {
        return str.StartsWith("\"") || str.EndsWith("\"");
    }
}
