namespace CarbonFiles.Core.Utilities;

public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty");

        // Backslash to forward slash
        path = path.Replace('\\', '/');

        // Remove leading/trailing slashes
        path = path.Trim('/');

        // Collapse double slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty");

        // Reject path traversal and empty/dot-only components
        var components = path.Split('/');
        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component) || component == ".")
                throw new ArgumentException("Empty path component");
            if (component == "..")
                throw new ArgumentException("Path traversal not allowed");
        }

        return path;
    }
}
