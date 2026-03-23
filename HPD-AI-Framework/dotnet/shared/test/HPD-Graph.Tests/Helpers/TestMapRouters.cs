using HPDAgent.Graph.Abstractions.Routing;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Test router that routes based on item type name.
/// </summary>
[MapRouter]
public class TypeBasedRouter : IMapRouter
{
    public string RouterName => "TypeBasedRouter";

    public string Route(object item)
    {
        return item.GetType().Name switch
        {
            "String" => "string",
            "Int32" => "int",
            "Double" => "double",
            _ => "unknown"
        };
    }
}

/// <summary>
/// Test router that routes based on a "type" property.
/// </summary>
[MapRouter]
public class PropertyBasedRouter : IMapRouter
{
    public string RouterName => "PropertyBasedRouter";

    public string Route(object item)
    {
        if (item is TestDocument doc)
        {
            return doc.Type;
        }
        return "unknown";
    }
}

/// <summary>
/// Test router that routes based on priority.
/// </summary>
[MapRouter]
public class PriorityRouter : IMapRouter
{
    public string RouterName => "PriorityRouter";

    public string Route(object item)
    {
        if (item is TestTask task)
        {
            return task.Priority switch
            {
                > 7 => "high",
                < 3 => "low",
                _ => "medium"
            };
        }
        return "medium";
    }
}

/// <summary>
/// Test document model for router tests.
/// </summary>
public class TestDocument
{
    public string Type { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Test task model for priority routing tests.
/// </summary>
public class TestTask
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
}
