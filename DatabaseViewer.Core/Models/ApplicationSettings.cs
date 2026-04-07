namespace DatabaseViewer.Core.Models;

public sealed class ApplicationSettings
{
    public bool ShowTableRowCounts { get; set; } = true;

    public WorkspaceLayoutSettings WorkspaceLayout { get; set; } = new();
}

public sealed class WorkspaceLayoutSettings
{
    public double SidebarPaneSize { get; set; } = 22d;

    public double DetailPaneSize { get; set; } = 32d;
}