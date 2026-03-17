namespace HPD.ML.LightGBM.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Helper methods for LightGBM native interop: error checking and model export.
/// </summary>
internal static class NativeHelper
{
    /// <summary>Check LightGBM return code, throw with error message on failure.</summary>
    internal static void Check(int returnCode)
    {
        if (returnCode != 0)
        {
            string message;
            try { message = LightGbmApi.GetLastError(); }
            catch { message = $"Unknown error (code {returnCode})"; }
            throw new InvalidOperationException($"LightGBM error: {message}");
        }
    }

    /// <summary>Export booster model to string.</summary>
    internal static string GetModelString(SafeBoosterHandle booster)
    {
        // First call to get required buffer size
        Check(LightGbmApi.BoosterSaveModelToString(
            booster.Handle, 0, 0, 0, 0, out long requiredLen, nint.Zero));

        // Allocate and get model string
        var buffer = Marshal.AllocHGlobal((int)requiredLen);
        try
        {
            Check(LightGbmApi.BoosterSaveModelToString(
                booster.Handle, 0, 0, 0, requiredLen, out _, buffer));
            return Marshal.PtrToStringUTF8(buffer)!;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Get feature importance from a trained booster.</summary>
    internal static double[] GetFeatureImportance(SafeBoosterHandle booster, int importanceType = 0)
    {
        Check(LightGbmApi.BoosterGetNumFeature(booster.Handle, out int numFeatures));
        var result = new double[numFeatures];
        var pinned = GCHandle.Alloc(result, GCHandleType.Pinned);
        try
        {
            Check(LightGbmApi.BoosterFeatureImportance(
                booster.Handle, 0, importanceType, pinned.AddrOfPinnedObject()));
            return result;
        }
        finally { pinned.Free(); }
    }
}
