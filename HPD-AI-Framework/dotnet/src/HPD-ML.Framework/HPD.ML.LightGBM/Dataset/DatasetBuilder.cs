namespace HPD.ML.LightGBM;

using System.Runtime.InteropServices;
using HPD.ML.Abstractions;
using HPD.ML.LightGBM.Native;

/// <summary>
/// Converts an IDataHandle into a native LightGBM Dataset.
/// Materializes features as a row-major float[] matrix and sets label/weight/group fields.
/// </summary>
internal static class DatasetBuilder
{
    internal static SafeDatasetHandle Build(
        IDataHandle data,
        string featureColumn,
        string labelColumn,
        string? weightColumn,
        string? groupColumn,
        string parameterString,
        SafeDatasetHandle? reference = null)
    {
        var (matrix, labels, weights, groups, nRows, nCols) =
            Materialize(data, featureColumn, labelColumn, weightColumn, groupColumn);

        var matrixPin = GCHandle.Alloc(matrix, GCHandleType.Pinned);
        try
        {
            NativeHelper.Check(LightGbmApi.DatasetCreateFromMat(
                matrixPin.AddrOfPinnedObject(),
                dataType: 0,  // float32
                nRow: nRows,
                nCol: nCols,
                isRowMajor: 1,
                parameters: parameterString,
                reference: reference?.Handle ?? nint.Zero,
                out var handle));

            var dataset = new SafeDatasetHandle(handle);
            try
            {
                SetFloatField(dataset, "label", labels);

                if (weights is not null)
                    SetFloatField(dataset, "weight", weights);

                if (groups is not null)
                    SetIntField(dataset, "group", groups);

                return dataset;
            }
            catch
            {
                dataset.Dispose();
                throw;
            }
        }
        finally
        {
            matrixPin.Free();
        }
    }

    private static (float[] matrix, float[] labels, float[]? weights, int[]? groups,
                     int nRows, int nCols) Materialize(
        IDataHandle data,
        string featureColumn,
        string labelColumn,
        string? weightColumn,
        string? groupColumn)
    {
        var featureRows = new List<float[]>();
        var labelList = new List<float>();
        var weightList = weightColumn is not null ? new List<float>() : null;
        var groupList = groupColumn is not null ? new List<int>() : null;

        // Determine needed columns
        var columns = new List<string> { featureColumn, labelColumn };
        if (weightColumn is not null) columns.Add(weightColumn);
        if (groupColumn is not null) columns.Add(groupColumn);

        using var cursor = data.GetCursor(columns);
        object? lastGroup = null;
        int currentGroupSize = 0;

        while (cursor.MoveNext())
        {
            var row = cursor.Current;

            // Extract features — try float[] first, then single scalar
            if (row.TryGetValue<float[]>(featureColumn, out var featureArray))
                featureRows.Add(featureArray);
            else
                featureRows.Add([row.GetValue<float>(featureColumn)]);

            // Label — try float directly, fallback to double → float
            if (row.TryGetValue<float>(labelColumn, out var label))
                labelList.Add(label);
            else
                labelList.Add((float)row.GetValue<double>(labelColumn));

            if (weightList is not null)
            {
                if (row.TryGetValue<float>(weightColumn!, out var w))
                    weightList.Add(w);
                else
                    weightList.Add((float)row.GetValue<double>(weightColumn!));
            }

            if (groupList is not null)
            {
                var group = row.GetValue<object>(groupColumn!);
                if (lastGroup is null || !Equals(group, lastGroup))
                {
                    if (currentGroupSize > 0)
                        groupList.Add(currentGroupSize);
                    currentGroupSize = 1;
                    lastGroup = group;
                }
                else
                {
                    currentGroupSize++;
                }
            }
        }

        if (groupList is not null && currentGroupSize > 0)
            groupList.Add(currentGroupSize);

        int nRows = featureRows.Count;
        int nCols = nRows > 0 ? featureRows[0].Length : 0;

        // Flatten to row-major
        var matrix = new float[nRows * nCols];
        for (int i = 0; i < nRows; i++)
            Array.Copy(featureRows[i], 0, matrix, i * nCols, nCols);

        return (matrix, labelList.ToArray(), weightList?.ToArray(),
                groupList?.ToArray(), nRows, nCols);
    }

    private static void SetFloatField(SafeDatasetHandle handle, string name, float[] values)
    {
        var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            NativeHelper.Check(LightGbmApi.DatasetSetField(
                handle.Handle, name, pinned.AddrOfPinnedObject(), values.Length, type: 0));
        }
        finally { pinned.Free(); }
    }

    private static void SetIntField(SafeDatasetHandle handle, string name, int[] values)
    {
        var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            NativeHelper.Check(LightGbmApi.DatasetSetField(
                handle.Handle, name, pinned.AddrOfPinnedObject(), values.Length, type: 2));
        }
        finally { pinned.Free(); }
    }
}
