using Helium.Primitives;

namespace Helium.Algorithms;

public static class GraphAlgorithms
{
    public static IReadOnlyList<V> Bfs<V>(FiniteGraph<V> graph, V start)
        where V : notnull, ITotalOrder<V>
    {
        RequireVertex(graph, start);

        var visited = Finset<V>.Empty;
        var order = new List<V>();
        var queue = new Queue<V>();
        visited = visited.Insert(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);

            foreach (var neighbor in graph.Neighbors(current).Elements)
            {
                if (visited.Contains(neighbor))
                    continue;

                visited = visited.Insert(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return order;
    }

    public static IReadOnlyList<V> Dfs<V>(FiniteGraph<V> graph, V start)
        where V : notnull, ITotalOrder<V>
    {
        RequireVertex(graph, start);

        var visited = Finset<V>.Empty;
        var order = new List<V>();
        Visit(start);
        return order;

        void Visit(V current)
        {
            if (visited.Contains(current))
                return;

            visited = visited.Insert(current);
            order.Add(current);

            foreach (var neighbor in graph.Neighbors(current).Elements)
                Visit(neighbor);
        }
    }

    public static IReadOnlyList<Finset<V>> ConnectedComponents<V>(FiniteGraph<V> graph)
        where V : notnull, ITotalOrder<V>
    {
        var visited = Finset<V>.Empty;
        var components = new List<Finset<V>>();

        foreach (var vertex in graph.Vertices.Elements)
        {
            if (visited.Contains(vertex))
                continue;

            var component = Finset<V>.FromElements(Bfs(graph, vertex));
            components.Add(component);
            visited = visited.Union(component);
        }

        return components;
    }

    public static bool IsConnected<V>(FiniteGraph<V> graph)
        where V : notnull, ITotalOrder<V>
    {
        if (graph.Vertices.IsEmpty)
            return true;

        return ConnectedComponents(graph).Count == 1;
    }

    public static bool IsEulerian<V>(FiniteGraph<V> graph)
        where V : notnull, ITotalOrder<V>
    {
        if (!IsConnected(graph))
            return false;

        return graph.Vertices.Elements.All(vertex => graph.Degree(vertex) % 2 == 0);
    }

    public static bool IsBipartite<V>(FiniteGraph<V> graph)
        where V : notnull, ITotalOrder<V>
    {
        var colors = new Dictionary<V, int>();

        foreach (var start in graph.Vertices.Elements)
        {
            if (colors.ContainsKey(start))
                continue;

            colors[start] = 0;
            var queue = new Queue<V>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in graph.Neighbors(current).Elements)
                {
                    var color = 1 - colors[current];
                    if (colors.TryAdd(neighbor, color))
                    {
                        queue.Enqueue(neighbor);
                    }
                    else if (colors[neighbor] != color)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void RequireVertex<V>(FiniteGraph<V> graph, V vertex)
        where V : notnull, ITotalOrder<V>
    {
        if (!graph.Vertices.Contains(vertex))
            throw new ArgumentException("Vertex is not in the graph.", nameof(vertex));
    }
}
