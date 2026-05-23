using System.Text;

namespace HPD.Execution.Local.Security;

internal static class PosixShellQuoter
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
            return "''";

        return $"'{value.Replace("'", "'\\''")}'";
    }

    public static string RenderCommand(CommandInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var command = new StringBuilder(Quote(invocation.FileName));
        foreach (var argument in invocation.ArgumentList)
        {
            command.Append(' ');
            command.Append(Quote(argument));
        }

        return command.ToString();
    }
}
