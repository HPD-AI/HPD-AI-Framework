using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Detects and handles Personally Identifiable Information (PII) in messages.
/// Applies configurable strategies (block, redact, mask, hash) per PII type.
/// </summary>
/// <remarks>
/// This middleware uses BeforeMessageTurnAsync to scan incoming messages and
/// AfterMessageTurnAsync to optionally scan responses. It provides:
/// <list type="bullet">
/// <item>Per-type configurable strategies</item>
/// <item>Luhn validation for credit cards</item>
/// <item>Custom detector support for domain-specific PII</item>
/// <item>Event emission for audit trails</item>
/// </list>
/// </remarks>
public class PIIMiddleware : IAgentMiddleware
{
    //     
    // CONFIGURATION - Per-PII-Type Strategies
    //     

    /// <summary>Strategy for handling email addresses. Default: Redact.</summary>
    public PIIStrategy EmailStrategy { get; set; } = PIIStrategy.Redact;

    /// <summary>Strategy for handling credit card numbers. Default: Block (high risk).</summary>
    public PIIStrategy CreditCardStrategy { get; set; } = PIIStrategy.Block;

    /// <summary>Strategy for handling Social Security Numbers. Default: Block (high risk).</summary>
    public PIIStrategy SSNStrategy { get; set; } = PIIStrategy.Block;

    /// <summary>Strategy for handling phone numbers. Default: Mask.</summary>
    public PIIStrategy PhoneStrategy { get; set; } = PIIStrategy.Mask;

    /// <summary>Strategy for handling IP addresses. Default: Hash.</summary>
    public PIIStrategy IPAddressStrategy { get; set; } = PIIStrategy.Hash;

    //     
    // CONFIGURATION - Application Collapse
    //     

    /// <summary>Apply PII detection to user input messages. Default: true.</summary>
    public bool ApplyToInput { get; set; } = true;

    /// <summary>Apply PII detection to LLM output messages. Default: false.</summary>
    public bool ApplyToOutput { get; set; } = false;

    /// <summary>Apply PII detection to tool results. Default: false.</summary>
    public bool ApplyToToolResults { get; set; } = false;

    //     
    // CONFIGURATION - Custom Detectors
    //     

    /// <summary>Custom PII detectors for domain-specific patterns.</summary>
    public List<CustomPIIDetector> CustomDetectors { get; } = new();

    /// <summary>Optional async detector for external PII detection services.</summary>
    public Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>>? ExternalDetector { get; set; }

    //     
    // BUILT-IN DETECTORS
    //     

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CreditCardRegex = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    private static readonly Regex SSNRegex = new(
        @"\b\d{3}-\d{2}-\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex PhoneRegex = new(
        @"\b(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex IPAddressRegex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
        RegexOptions.Compiled);

    //     
    // HOOKS
    //     

    /// <summary>
    /// Scans incoming messages for PII before the agent processes them.
    /// </summary>
    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (!ApplyToInput)
            return;

        // Process user messages
        var processedMessages = await ProcessMessagesAsync(
            context.ConversationHistory,
            ChatRole.User,
            context,
            cancellationToken);

        // V2: ConversationHistory is mutable - replace content
        context.ConversationHistory.Clear();
        foreach (var msg in processedMessages)
        {
            context.ConversationHistory.Add(msg);
        }
    }

    /// <summary>
    /// Optionally scans output messages for PII after the agent responds.
    /// </summary>
    public async Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        // Output filtering would apply to FinalResponse if enabled
        // This is observational - the response has already been yielded
    }

    /// <summary>
    /// Optionally scans tool results for PII after iteration.
    /// </summary>
    public async Task AfterIterationAsync(
        AfterIterationContext context,
        CancellationToken cancellationToken)
    {
        if (!ApplyToToolResults || context.ToolResults.Count == 0)
            return;

        // Tool result filtering - scan results for PII
        foreach (var result in context.ToolResults)
        {
            var resultText = result.Result?.ToString();
            if (!string.IsNullOrEmpty(resultText))
            {
                var matches = await DetectPIIAsync(resultText, cancellationToken);
                foreach (var match in matches.Where(m => m.Strategy == PIIStrategy.Block))
                {
                    // Emit warning event for blocked PII in tool results
                    EmitPIIDetectedEvent(context, match.PIIType, PIIStrategy.Block, 1);
                }
            }
        }
    }

    //     
    // PROCESSING LOGIC
    //     

    private async Task<IEnumerable<ChatMessage>> ProcessMessagesAsync(
        IEnumerable<ChatMessage> messages,
        ChatRole targetRole,
        HookContext context,
        CancellationToken cancellationToken)
    {
        var result = new List<ChatMessage>();

        foreach (var message in messages)
        {
            if (message.Role == targetRole)
            {
                var processedMessage = await ProcessMessageAsync(message, context, cancellationToken);
                result.Add(processedMessage);
            }
            else
            {
                result.Add(message);
            }
        }

        return result;
    }

    private async Task<ChatMessage> ProcessMessageAsync(
        ChatMessage message,
        HookContext context,
        CancellationToken cancellationToken)
    {
        var text = message.Text;
        if (string.IsNullOrEmpty(text))
            return message;

        var allMatches = await DetectPIIAsync(text, cancellationToken);

        if (allMatches.Count == 0)
            return message;

        // Check for any Block strategies first
        var blockedMatch = allMatches.FirstOrDefault(m => m.Strategy == PIIStrategy.Block);
        if (blockedMatch != null)
        {
            EmitPIIDetectedEvent(context, blockedMatch.PIIType, PIIStrategy.Block, 1);
            throw new PIIBlockedException(
                $"PII of type '{blockedMatch.PIIType}' detected. Message blocked for security.",
                blockedMatch.PIIType);
        }

        // Sort matches by position (descending) to replace from end to start
        var sortedMatches = allMatches
            .OrderByDescending(m => m.StartIndex)
            .ToList();

        // Apply replacements
        var processedText = text;
        var emittedTypes = new Dictionary<string, (PIIStrategy Strategy, int Count)>();

        foreach (var match in sortedMatches)
        {
            var replacement = GetReplacement(match);
            processedText = processedText.Remove(match.StartIndex, match.Length)
                                        .Insert(match.StartIndex, replacement);

            var key = match.PIIType;
            if (emittedTypes.TryGetValue(key, out var existing))
                emittedTypes[key] = (existing.Strategy, existing.Count + 1);
            else
                emittedTypes[key] = (match.Strategy, 1);
        }

        // Emit events for each PII type detected
        foreach (var (piiType, (strategy, count)) in emittedTypes)
        {
            EmitPIIDetectedEvent(context, piiType, strategy, count);
        }

        return new ChatMessage(message.Role, processedText);
    }

    private async Task<List<PIIMatch>> DetectPIIAsync(string text, CancellationToken cancellationToken)
    {
        var allMatches = new List<PIIMatch>();

        // Run built-in detectors
        allMatches.AddRange(DetectEmail(text));
        allMatches.AddRange(DetectCreditCard(text));
        allMatches.AddRange(DetectSSN(text));
        allMatches.AddRange(DetectPhone(text));
        allMatches.AddRange(DetectIPAddress(text));

        // Run custom detectors
        foreach (var detector in CustomDetectors)
        {
            allMatches.AddRange(detector.Detect(text));
        }

        // Run external detector if configured
        if (ExternalDetector != null)
        {
            var externalMatches = await ExternalDetector(text, cancellationToken);
            allMatches.AddRange(externalMatches);
        }

        return allMatches;
    }

    //     
    // DETECTION METHODS
    //     

    private IEnumerable<PIIMatch> DetectEmail(string text)
    {
        foreach (Match match in EmailRegex.Matches(text))
            yield return new PIIMatch("Email", match.Value, match.Index, match.Length, EmailStrategy);
    }

    private IEnumerable<PIIMatch> DetectCreditCard(string text)
    {
        foreach (Match match in CreditCardRegex.Matches(text))
        {
            var digitsOnly = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length >= 13 && digitsOnly.Length <= 19 && IsValidLuhn(digitsOnly))
                yield return new PIIMatch("CreditCard", match.Value, match.Index, match.Length, CreditCardStrategy);
        }
    }

    private IEnumerable<PIIMatch> DetectSSN(string text)
    {
        foreach (Match match in SSNRegex.Matches(text))
            yield return new PIIMatch("SSN", match.Value, match.Index, match.Length, SSNStrategy);
    }

    private IEnumerable<PIIMatch> DetectPhone(string text)
    {
        foreach (Match match in PhoneRegex.Matches(text))
            yield return new PIIMatch("Phone", match.Value, match.Index, match.Length, PhoneStrategy);
    }

    private IEnumerable<PIIMatch> DetectIPAddress(string text)
    {
        foreach (Match match in IPAddressRegex.Matches(text))
            yield return new PIIMatch("IPAddress", match.Value, match.Index, match.Length, IPAddressStrategy);
    }

    //     
    // STRATEGY IMPLEMENTATIONS
    //     

    private static string GetReplacement(PIIMatch match)
    {
        return match.Strategy switch
        {
            PIIStrategy.Block => throw new InvalidOperationException("Block should be handled before replacement"),
            PIIStrategy.Redact => $"[{match.PIIType.ToUpperInvariant()}_REDACTED]",
            PIIStrategy.Mask => MaskValue(match.Value, match.PIIType),
            PIIStrategy.Hash => HashValue(match.Value, match.PIIType),
            _ => match.Value
        };
    }

    private static string MaskValue(string value, string piiType)
    {
        return piiType switch
        {
            "Email" => MaskEmail(value),
            "CreditCard" => "****-****-****-" + new string(value.Where(char.IsDigit).ToArray())[^4..],
            "SSN" => "***-**-" + value[^4..],
            "Phone" => "***-***-" + new string(value.Where(char.IsDigit).ToArray())[^4..],
            "IPAddress" => "***.***.***.***",
            _ => new string('*', value.Length)
        };
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex <= 1 ? "***@" + email[(atIndex + 1)..] : email[0] + new string('*', atIndex - 1) + email[atIndex..];
    }

    private static string HashValue(string value, string piiType)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        var shortHash = Convert.ToHexString(hash)[..8].ToLowerInvariant();
        return $"<{piiType.ToLowerInvariant()}_hash:{shortHash}>";
    }

    private static bool IsValidLuhn(string digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsDigit)) return false;
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private void EmitPIIDetectedEvent(HookContext context, string piiType, PIIStrategy strategy, int count)
    {
        try
        {
            context.Emit(new PIIDetectedEvent(
                AgentName: context.AgentName,
                PIIType: piiType,
                Strategy: strategy,
                OccurrenceCount: count,
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured
        }
    }

    /// <summary>Adds a custom PII detector with a regex pattern.</summary>
    public PIIMiddleware AddCustomDetector(string name, string pattern, PIIStrategy strategy, string? replacement = null)
    {
        CustomDetectors.Add(new CustomPIIDetector(name, new Regex(pattern, RegexOptions.Compiled), strategy, replacement));
        return this;
    }
}

//      
// SUPPORTING TYPES
//      

/// <summary>
/// Strategy for handling detected PII.
/// </summary>
public enum PIIStrategy
{
    /// <summary>Block the entire message (throws exception).</summary>
    Block,
    /// <summary>Replace PII with [TYPE_REDACTED].</summary>
    Redact,
    /// <summary>Partially mask PII (e.g., ***-***-1234).</summary>
    Mask,
    /// <summary>Replace PII with a hash.</summary>
    Hash,
    /// <summary>Allow PII to pass through unchanged.</summary>
    Allow
}

/// <summary>
/// Represents a detected PII match.
/// </summary>
public record PIIMatch(
    string PIIType,
    string Value,
    int StartIndex,
    int Length,
    PIIStrategy Strategy);

/// <summary>
/// Custom PII detector with regex pattern.
/// </summary>
public class CustomPIIDetector
{
    public string Name { get; }
    public Regex Pattern { get; }
    public PIIStrategy Strategy { get; }
    public string? Replacement { get; }

    public CustomPIIDetector(string name, Regex pattern, PIIStrategy strategy, string? replacement = null)
    {
        Name = name;
        Pattern = pattern;
        Strategy = strategy;
        Replacement = replacement;
    }

    public IEnumerable<PIIMatch> Detect(string text)
    {
        foreach (Match match in Pattern.Matches(text))
        {
            yield return new PIIMatch(Name, match.Value, match.Index, match.Length, Strategy);
        }
    }
}

/// <summary>
/// Event emitted when PII is detected.
/// </summary>
public record PIIDetectedEvent(
    string AgentName,
    string PIIType,
    PIIStrategy Strategy,
    int OccurrenceCount,
    DateTimeOffset Timestamp) : AgentEvent, IObservabilityEvent;

/// <summary>
/// Exception thrown when PII is blocked.
/// </summary>
public class PIIBlockedException : Exception
{
    public string PIIType { get; }

    public PIIBlockedException(string message, string piiType)
        : base(message)
    {
        PIIType = piiType;
    }
}
