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
/// This internal pre-1.0 marker is retained for future payload validation. The current
/// generator does not use it to produce JSON metadata or reliably emit HPDA006.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class HpdBotPayloadAttribute : Attribute { }
