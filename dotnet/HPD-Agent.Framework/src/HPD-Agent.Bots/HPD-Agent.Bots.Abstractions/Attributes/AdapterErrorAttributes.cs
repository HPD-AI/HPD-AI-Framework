namespace HPD.Agent.Bots;

/// <summary>
/// Marks a partial class as the error handler for a specific adapter.
/// The source generator emits a <c>ThrowMapped(string errorCode, Exception inner)</c>
/// method using the <see cref="ErrorCodeAttribute"/> declarations on the same class.
/// </summary>
/// <param name="adapterName">Lowercase adapter identifier matching <see cref="HpdBotAttribute.Name"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BotErrorsAttribute(string adapterName) : Attribute
{
    /// <summary>Bot this error handler belongs to.</summary>
    public string BotName => adapterName;
}

/// <summary>
/// Maps a platform error code string to an <see cref="HPD.Agent.Bots.Contracts.BotException"/> subtype.
/// Applied alongside <see cref="BotErrorsAttribute"/> on the same partial class.
/// </summary>
/// <param name="code">Platform error code string (e.g. "ratelimited", "not_in_channel").</param>
/// <param name="exceptionType">
/// The <see cref="HPD.Agent.Bots.Contracts.BotException"/> subtype to throw.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ErrorCodeAttribute(string code, Type exceptionType) : Attribute
{
    /// <summary>Platform error code string.</summary>
    public string Code => code;

    /// <summary>Exception type to throw when this error code is encountered.</summary>
    public Type ExceptionType => exceptionType;
}
