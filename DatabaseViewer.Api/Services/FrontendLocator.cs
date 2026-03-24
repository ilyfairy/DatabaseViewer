namespace DatabaseViewer.Api.Services;

public static class FrontendLocator
{
    public static string? FindDistDirectory()
    {
        var publishedDist = Path.Combine(AppContext.BaseDirectory, "dist");
        if (File.Exists(Path.Combine(publishedDist, "index.html")))
        {
            return publishedDist;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "database-viewer-web", "dist");
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}