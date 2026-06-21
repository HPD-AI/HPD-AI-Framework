namespace HPD.ML.DeepLearning;

using HPD.ML.Abstractions;

internal static class TrainingDataLoader
{
    public static (float[][] Features, float[][] Labels) Load(NeuralNetworkDefinition definition, IDataHandle data)
    {
        var features = new List<float[]>();
        var labels = new List<float[]>();
        using var cursor = data.GetCursor([definition.FeatureColumn, definition.LabelColumn]);
        while (cursor.MoveNext())
        {
            var row = cursor.Current;
            var x = NeuralNetworkScoringTransform.ExtractFeatures(row, definition.FeatureColumn);
            if (x.Length != definition.Layers[0].InputSize)
                throw new ArgumentException($"Feature vector length must be {definition.Layers[0].InputSize}.");

            features.Add(x);
            labels.Add(ExtractLabel(row, definition.LabelColumn, definition.Layers[^1].OutputSize));
        }

        return ([.. features], [.. labels]);
    }

    private static float[] ExtractLabel(IRow row, string labelColumn, int outputSize)
    {
        var column = row.Schema.Columns.First(column => column.Name == labelColumn);
        if (column.Type.ClrType == typeof(float[]))
        {
            var vector = row.GetValue<float[]>(labelColumn);
            if (vector.Length != outputSize)
                throw new ArgumentException($"Label vector length must be {outputSize}.");
            return [.. vector];
        }

        if (outputSize != 1)
            throw new ArgumentException($"Scalar labels can only train single-output networks.");

        return [Convert.ToSingle(row.GetValue<object>(labelColumn))];
    }
}
