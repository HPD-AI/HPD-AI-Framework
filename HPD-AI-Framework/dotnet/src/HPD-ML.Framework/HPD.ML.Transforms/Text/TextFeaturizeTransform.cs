namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// All-in-one text featurization: tokenize, lowercase, remove stopwords, n-grams, TF-IDF.
/// </summary>
public sealed class TextFeaturizeLearner : ILearner
{
    private readonly string _columnName;
    private readonly string _outputColumnName;
    private readonly TextFeaturizeOptions _options;

    public TextFeaturizeLearner(
        string columnName,
        string? outputColumnName = null,
        TextFeaturizeOptions? options = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName ?? $"{columnName}_Features";
        _options = options ?? new TextFeaturizeOptions();
    }

    public IObservable<ProgressEvent> Progress => new ProgressSubject();

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column(_outputColumnName, FieldType.Vector<float>(_options.MaxFeatures)));
        return new Schema(columns, inputSchema.Level);
    }

    public IModel Fit(LearnerInput input)
    {
        var docFreq = new Dictionary<string, int>();
        int totalDocs = 0;

        using var cursor = input.TrainData.GetCursor([_columnName]);
        while (cursor.MoveNext())
        {
            var text = cursor.Current.GetValue<string>(_columnName) ?? "";
            var tokens = Tokenize(text, _options);
            var ngrams = ExtractNgrams(tokens, _options);
            var unique = new HashSet<string>(ngrams);
            foreach (var ng in unique)
                docFreq[ng] = docFreq.GetValueOrDefault(ng) + 1;
            totalDocs++;
        }

        var topFeatures = docFreq
            .OrderByDescending(kv => kv.Value)
            .Take(_options.MaxFeatures)
            .Select((kv, idx) => (kv.Key, Index: idx, Idf: Math.Log((double)totalDocs / kv.Value)))
            .ToList();

        var featureIndex = topFeatures.ToDictionary(f => f.Key, f => f.Index);
        var idfWeights = new double[topFeatures.Count];
        foreach (var f in topFeatures)
            idfWeights[f.Index] = f.Idf;

        var transform = new TextFeaturizeTransform(
            _columnName, _outputColumnName, featureIndex, idfWeights, _options);

        return new Model(transform, new TextFeaturizeParameters(featureIndex, idfWeights));
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.FromResult(Fit(input));

    internal static string[] Tokenize(string text, TextFeaturizeOptions options)
    {
        if (options.CaseNormalize)
            text = text.ToLowerInvariant();
        return text.Split(options.Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static IEnumerable<string> ExtractNgrams(string[] tokens, TextFeaturizeOptions options)
    {
        if (options.RemoveStopWords)
            tokens = tokens.Where(t => !StopWords.English.Contains(t)).ToArray();

        for (int n = options.NgramMin; n <= options.NgramMax; n++)
        {
            for (int i = 0; i <= tokens.Length - n; i++)
            {
                yield return string.Join("|", tokens[i..(i + n)]);
            }
        }
    }
}

public sealed class TextFeaturizeTransform : ITransform
{
    private readonly string _columnName;
    private readonly string _outputColumnName;
    private readonly IReadOnlyDictionary<string, int> _featureIndex;
    private readonly double[] _idfWeights;
    private readonly TextFeaturizeOptions _options;

    public TextFeaturizeTransform(
        string columnName,
        string outputColumnName,
        IReadOnlyDictionary<string, int> featureIndex,
        double[] idfWeights,
        TextFeaturizeOptions options)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
        _featureIndex = featureIndex;
        _idfWeights = idfWeights;
        _options = options;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column(_outputColumnName, FieldType.Vector<float>(_featureIndex.Count)));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var text = row.GetValue<string>(_columnName) ?? "";
                    var tokens = TextFeaturizeLearner.Tokenize(text, _options);
                    var ngrams = TextFeaturizeLearner.ExtractNgrams(tokens, _options).ToArray();

                    var tf = new Dictionary<string, int>();
                    foreach (var ng in ngrams)
                        tf[ng] = tf.GetValueOrDefault(ng) + 1;

                    var vector = new float[_featureIndex.Count];
                    foreach (var (term, count) in tf)
                    {
                        if (_featureIndex.TryGetValue(term, out int idx))
                            vector[idx] = (float)(count * _idfWeights[idx]);
                    }

                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    values[_outputColumnName] = vector;

                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}

public sealed record TextFeaturizeOptions
{
    public bool CaseNormalize { get; init; } = true;
    public bool RemoveStopWords { get; init; } = true;
    public int NgramMin { get; init; } = 1;
    public int NgramMax { get; init; } = 2;
    public int MaxFeatures { get; init; } = 10000;
    public char[] Separators { get; init; } = [' ', '\t', '\n', '\r', '.', ',', ';', '!', '?'];
}

public sealed record TextFeaturizeParameters(
    IReadOnlyDictionary<string, int> FeatureIndex,
    double[] IdfWeights) : ILearnedParameters;

internal static class StopWords
{
    public static readonly HashSet<string> English =
    [
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "by",
        "for", "from", "had", "has", "have", "he", "her", "his", "how",
        "i", "if", "in", "into", "is", "it", "its", "me", "my", "no",
        "nor", "not", "of", "on", "or", "our", "out", "own", "she",
        "so", "some", "than", "that", "the", "their", "them", "then",
        "there", "these", "they", "this", "to", "too", "up", "us",
        "very", "was", "we", "were", "what", "when", "where", "which",
        "while", "who", "why", "will", "with", "you", "your"
    ];
}
