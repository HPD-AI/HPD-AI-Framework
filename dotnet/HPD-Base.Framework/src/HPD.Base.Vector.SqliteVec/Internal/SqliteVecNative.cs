using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace HPD.Base.Vector.SqliteVec;

internal static class SqliteVecNative
{
    internal static void Load(SqliteConnection connection)
    {
        string fileName = OperatingSystem.IsWindows() ? "vec0.dll" : OperatingSystem.IsMacOS() ? "vec0.dylib" : "vec0.so";
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);
        if (!File.Exists(path)) throw new PlatformNotSupportedException("base.vector.providerUnsupportedPlatform: the certified sqlite-vec native asset is unavailable.");
        connection.LoadExtension(path);
    }
}
