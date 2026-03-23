namespace HPD.ML.LightGBM.Native;

using System.Runtime.InteropServices;

/// <summary>
/// P/Invoke bindings to LightGBM's C API (c_api.h).
/// Only the subset needed for training, evaluation, and model export.
/// </summary>
internal static partial class LightGbmApi
{
    private const string LibName = "lib_lightgbm";

    // ── Error ──

    [LibraryImport(LibName, EntryPoint = "LGBM_GetLastError")]
    [return: MarshalAs(UnmanagedType.LPStr)]
    internal static partial string GetLastError();

    // ── Dataset ──

    [LibraryImport(LibName, EntryPoint = "LGBM_DatasetCreateFromMat")]
    internal static partial int DatasetCreateFromMat(
        nint data,            // float* row-major matrix
        int dataType,         // 0 = float32
        int nRow,
        int nCol,
        int isRowMajor,       // 1 = row-major
        [MarshalAs(UnmanagedType.LPUTF8Str)] string parameters,
        nint reference,       // nullptr for training set
        out nint handle);

    [LibraryImport(LibName, EntryPoint = "LGBM_DatasetSetField")]
    internal static partial int DatasetSetField(
        nint handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fieldName,
        nint fieldData,
        int numElement,
        int type);            // 0 = float32, 2 = int32

    [LibraryImport(LibName, EntryPoint = "LGBM_DatasetFree")]
    internal static partial int DatasetFree(nint handle);

    [LibraryImport(LibName, EntryPoint = "LGBM_DatasetGetNumData")]
    internal static partial int DatasetGetNumData(nint handle, out int numData);

    [LibraryImport(LibName, EntryPoint = "LGBM_DatasetGetNumFeature")]
    internal static partial int DatasetGetNumFeature(nint handle, out int numFeature);

    // ── Booster (training) ──

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterCreate")]
    internal static partial int BoosterCreate(
        nint trainData,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string parameters,
        out nint handle);

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterAddValidData")]
    internal static partial int BoosterAddValidData(
        nint handle,
        nint validData);

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterUpdateOneIter")]
    internal static partial int BoosterUpdateOneIter(
        nint handle,
        out int isFinished);

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterGetEvalCounts")]
    internal static partial int BoosterGetEvalCounts(
        nint handle,
        out int outLen);

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterGetEval")]
    internal static partial int BoosterGetEval(
        nint handle,
        int dataIdx,          // 0 = train, 1+ = validation
        out int outLen,
        nint outResult);      // double*

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterSaveModelToString")]
    internal static partial int BoosterSaveModelToString(
        nint handle,
        int startIteration,
        int numIteration,     // 0 = all
        int featureImportanceType,
        long bufferLen,
        out long outLen,
        nint outStr);         // char*

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterFree")]
    internal static partial int BoosterFree(nint handle);

    // ── Feature importance ──

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterFeatureImportance")]
    internal static partial int BoosterFeatureImportance(
        nint handle,
        int numIteration,     // 0 = all
        int importanceType,   // 0 = split, 1 = gain
        nint outResult);      // double*

    [LibraryImport(LibName, EntryPoint = "LGBM_BoosterGetNumFeature")]
    internal static partial int BoosterGetNumFeature(
        nint handle,
        out int outNum);
}
