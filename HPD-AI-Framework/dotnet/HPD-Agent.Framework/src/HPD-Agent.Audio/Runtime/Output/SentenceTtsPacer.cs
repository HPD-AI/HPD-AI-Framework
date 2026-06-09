using System.Text;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.Runtime.Output;

public sealed class SentenceTtsPacer : ITtsPacer
{
    private readonly StringBuilder _buffer = new();
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e"
    };

    private int _bufferStart;
    private int _nextSequenceNumber;
    private int _totalReceivedChars;

    public IReadOnlyList<TextToSpeechSegment> PushText(
        string textDelta,
        TextToSpeechPacingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(textDelta);

        if (textDelta.Length == 0)
        {
            return [];
        }

        if (_buffer.Length == 0)
        {
            _bufferStart = _totalReceivedChars;
        }

        _buffer.Append(textDelta);
        _totalReceivedChars += textDelta.Length;

        var segments = new List<TextToSpeechSegment>();
        DrainReadyBoundaries(context, segments);
        DrainMaxBufferedChars(context, segments);
        return segments;
    }

    public IReadOnlyList<TextToSpeechSegment> Flush(TextToSpeechPacingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var segments = new List<TextToSpeechSegment>();
        EmitPrefix(_buffer.Length, TextToSpeechSegmentKind.Remainder, isFinal: true, context, segments);
        return segments;
    }

    public void Reset()
    {
        _buffer.Clear();
        _bufferStart = 0;
        _nextSequenceNumber = 0;
        _totalReceivedChars = 0;
    }

    private void DrainReadyBoundaries(TextToSpeechPacingContext context, List<TextToSpeechSegment> segments)
    {
        if (context.Options.Mode is TextToSpeechPacingMode.Manual or TextToSpeechPacingMode.TokenBatch)
        {
            return;
        }

        while (TryFindBoundary(context, out var boundaryLength, out var kind))
        {
            EmitPrefix(boundaryLength, kind, isFinal: false, context, segments);
        }
    }

    private void DrainMaxBufferedChars(TextToSpeechPacingContext context, List<TextToSpeechSegment> segments)
    {
        if (context.Options.Mode is TextToSpeechPacingMode.Manual)
        {
            return;
        }

        var maxChars = _nextSequenceNumber == 0
            ? context.Options.First.MaxCharacters
            : context.Options.Continuation.MaxCharacters;
        if (maxChars <= 0)
        {
            return;
        }

        while (_buffer.Length >= maxChars)
        {
            var boundaryLength = FindFallbackBoundary(maxChars);
            EmitPrefix(boundaryLength, TextToSpeechSegmentKind.TokenBatch, isFinal: false, context, segments);
            DrainReadyBoundaries(context, segments);
        }
    }

    private bool TryFindBoundary(
        TextToSpeechPacingContext context,
        out int boundaryLength,
        out TextToSpeechSegmentKind kind)
    {
        boundaryLength = 0;
        kind = TextToSpeechSegmentKind.Sentence;

        for (var index = 0; index < _buffer.Length; index++)
        {
            var value = _buffer[index];
            if (IsSentenceTerminator(value))
            {
                if (!IsSafeSentenceBoundary(index, context.Options.Boundaries))
                {
                    continue;
                }

                var length = index + 1;
                if (!CanEmitCandidate(length, context))
                {
                    continue;
                }

                boundaryLength = length;
                kind = TextToSpeechSegmentKind.Sentence;
                return true;
            }

            if (context.Options.Mode == TextToSpeechPacingMode.Phrase &&
                context.Options.Continuation.AllowPhraseBoundaries &&
                IsPhraseTerminator(value) &&
                HasLookahead(index))
            {
                var length = index + 1;
                if (!CanEmitCandidate(length, context))
                {
                    continue;
                }

                boundaryLength = index + 1;
                kind = TextToSpeechSegmentKind.Phrase;
                return true;
            }
        }

        return false;
    }

    private bool CanEmitCandidate(int length, TextToSpeechPacingContext context)
    {
        if (_nextSequenceNumber > 0)
        {
            return true;
        }

        var textLength = TrimmedLength(_buffer.ToString(0, length));
        if (textLength >= context.Options.First.MinCharacters)
        {
            return true;
        }

        return context.Options.First.EmitFirstSafeSentenceImmediately &&
            textLength > 0 &&
            (_buffer.Length >= context.Options.First.MinCharacters ||
             length >= context.Options.First.MaxCharacters);
    }

    private bool IsSafeSentenceBoundary(int index, TextToSpeechBoundaryOptions options)
    {
        if (IsProtectedBoundary(index, options))
        {
            return false;
        }

        return !options.RequireLookahead || HasLookahead(index);
    }

    private bool HasLookahead(int index)
    {
        for (var i = index + 1; i < _buffer.Length; i++)
        {
            if (!char.IsWhiteSpace(_buffer[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsProtectedBoundary(int index, TextToSpeechBoundaryOptions options)
    {
        var value = _buffer[index];
        if (value != '.')
        {
            return false;
        }

        if (options.ProtectEllipses &&
            ((index > 0 && _buffer[index - 1] == '.') ||
             (index + 1 < _buffer.Length && _buffer[index + 1] == '.')))
        {
            return true;
        }

        if (options.ProtectDecimals &&
            index > 0 &&
            index + 1 < _buffer.Length &&
            char.IsDigit(_buffer[index - 1]) &&
            char.IsDigit(_buffer[index + 1]))
        {
            return true;
        }

        var token = PreviousToken(index);
        if (options.ProtectAbbreviations && Abbreviations.Contains(token.TrimEnd('.')))
        {
            return true;
        }

        if (options.ProtectInitials && token.Length == 1 && char.IsUpper(token[0]))
        {
            return true;
        }

        return options.ProtectUrls && LooksLikeUrlBoundary(index);
    }

    private string PreviousToken(int index)
    {
        var start = index - 1;
        while (start >= 0 && !char.IsWhiteSpace(_buffer[start]))
        {
            start--;
        }

        return _buffer.ToString(start + 1, index - start - 1);
    }

    private bool LooksLikeUrlBoundary(int index)
    {
        var start = index - 1;
        while (start >= 0 && !char.IsWhiteSpace(_buffer[start]))
        {
            start--;
        }

        var end = index + 1;
        while (end < _buffer.Length && !char.IsWhiteSpace(_buffer[end]))
        {
            end++;
        }

        var token = _buffer.ToString(start + 1, end - start - 1);
        return token.Contains("://", StringComparison.Ordinal) ||
            token.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            (token.Contains('.', StringComparison.Ordinal) && token.Contains('/', StringComparison.Ordinal));
    }

    private int FindFallbackBoundary(int maxChars)
    {
        var limit = Math.Min(maxChars, _buffer.Length);
        for (var index = limit - 1; index > 0; index--)
        {
            if (char.IsWhiteSpace(_buffer[index]))
            {
                return index + 1;
            }
        }

        return limit;
    }

    private void EmitPrefix(
        int length,
        TextToSpeechSegmentKind kind,
        bool isFinal,
        TextToSpeechPacingContext context,
        List<TextToSpeechSegment> segments)
    {
        if (length <= 0)
        {
            return;
        }

        var raw = _buffer.ToString(0, length);
        _buffer.Remove(0, length);

        var leadingWhitespace = CountLeadingWhitespace(raw);
        var trailingWhitespace = CountTrailingWhitespace(raw);
        var textLength = raw.Length - leadingWhitespace - trailingWhitespace;

        if (textLength <= 0)
        {
            _bufferStart += length;
            return;
        }

        var sequenceNumber = _nextSequenceNumber++;
        segments.Add(new TextToSpeechSegment
        {
            SegmentId = new OutputSegmentId($"{context.OutputFlowId.Value}:tts-{sequenceNumber:D4}"),
            Text = raw.Substring(leadingWhitespace, textLength),
            SegmentIndex = sequenceNumber,
            IsFinalSegment = isFinal,
            Kind = kind,
            SourceTextStart = _bufferStart + leadingWhitespace,
            SourceTextLength = textLength
        });
        _bufferStart += length;
    }

    private static bool IsSentenceTerminator(char value)
    {
        return value is '.' or '!' or '?';
    }

    private static bool IsPhraseTerminator(char value)
    {
        return value is ',' or ';' or ':';
    }

    private static int TrimmedLength(string value)
    {
        return value.Trim().Length;
    }

    private static int CountLeadingWhitespace(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsWhiteSpace(value[count]))
        {
            count++;
        }

        return count;
    }

    private static int CountTrailingWhitespace(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsWhiteSpace(value[value.Length - count - 1]))
        {
            count++;
        }

        return count;
    }
}
