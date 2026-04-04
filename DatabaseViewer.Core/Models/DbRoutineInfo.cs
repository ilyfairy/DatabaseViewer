namespace DatabaseViewer.Core.Models;

/// <summary>
/// 表示数据库中的存储过程或函数。
/// </summary>
public sealed class DbRoutineInfo
{
    public string DatabaseName { get; set; } = string.Empty;

    public string? SchemaName { get; set; }

    public string RoutineName { get; set; } = string.Empty;

    /// <summary>
    /// 例程类型：Procedure / ScalarFunction / TableFunction / AggregateFunction
    /// </summary>
    public string RoutineType { get; set; } = string.Empty;

    /// <summary>
    /// 例程参数列表（在 bootstrap 时一次性查询）。
    /// </summary>
    public List<DbRoutineParameter> Parameters { get; set; } = [];
}

/// <summary>
/// 存储过程或函数的参数。
/// </summary>
public sealed class DbRoutineParameter
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// 参数方向：IN / OUT / INOUT
    /// </summary>
    public string Direction { get; set; } = "IN";

    /// <summary>
    /// 默认值（如果有）。
    /// </summary>
    public string? DefaultValue { get; set; }
}
