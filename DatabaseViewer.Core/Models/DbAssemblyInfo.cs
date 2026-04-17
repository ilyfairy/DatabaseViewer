namespace DatabaseViewer.Core.Models;

public sealed class DbAssemblyInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string ClrName { get; set; } = string.Empty;

    public string PermissionSet { get; set; } = string.Empty;

    public bool IsVisible { get; set; }
}
