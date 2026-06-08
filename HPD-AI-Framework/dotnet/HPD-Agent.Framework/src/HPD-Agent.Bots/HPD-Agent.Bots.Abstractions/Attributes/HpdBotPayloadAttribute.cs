namespace HPD.Agent.Bots;

/// <summary>
/// Marks a <c>record</c> type as a bot payload that can be used by a generated
/// adapter dispatch method.
/// </summary>
/// <remarks>
/// The bot source generator does not emit <c>JsonSerializerContext</c> types.
/// Adapter projects must declare a hand-written context named after the adapter
/// class, such as <c>SlackBotJsonContext</c>, and include each payload with
/// <c>[JsonSerializable]</c>.
/// <para>
/// The type must be a <c>record</c> — the generator emits diagnostic <c>HPD-A006</c>
/// if a non-record type is decorated.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdBotPayloadAttribute : Attribute { }
