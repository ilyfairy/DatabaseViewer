namespace DatabaseViewer.Api.Services;

public static class FrontendLocator
{
    public static string? FindWwwRootDirectory()
    {
        var publishedWwwRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (File.Exists(Path.Combine(publishedWwwRoot, "index.html")))
        {
            return publishedWwwRoot;
        }

        return null;
    }
}