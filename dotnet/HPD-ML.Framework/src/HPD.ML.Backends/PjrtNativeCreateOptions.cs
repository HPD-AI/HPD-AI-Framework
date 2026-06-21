using System.Runtime.InteropServices;
using System.Text;
using HPD.ML.Backends.Pjrt.Interop;

namespace HPD.ML.Backends.Pjrt;

internal sealed unsafe class PjrtNativeCreateOptions : IDisposable
{
    private readonly List<nint> _nativeAllocations = [];
    private readonly List<GCHandle> _pinnedRoots = [];
    private PjrtNamedValue[] _values = [];
    private GCHandle _valuesHandle;
    private bool _disposed;

    private PjrtNativeCreateOptions()
    {
    }

    public PjrtNamedValue* Values => _values.Length == 0
        ? null
        : (PjrtNamedValue*)_valuesHandle.AddrOfPinnedObject();

    public nuint Count => (nuint)_values.Length;

    public static PjrtNativeCreateOptions From(PjrtClientCreateOptions? options)
    {
        var native = new PjrtNativeCreateOptions();
        if (options is null)
            return native;

        var values = new List<PjrtNamedValue>();
        native.AddString(values, "platform_name", options.PlatformName);
        native.AddString(values, "allocator", options.Allocator);
        native.AddFloat(values, "memory_fraction", options.MemoryFraction);
        native.AddBool(values, "preallocate", options.Preallocate);
        native.AddInt64(values, "collective_memory_size", options.CollectiveMemorySize);
        native.AddInt64List(values, "visible_devices", options.VisibleDevices);
        native.AddInt64(values, "node_id", options.NodeId);
        native.AddInt64(values, "num_nodes", options.NumNodes);
        native.AddBool(values, "should_stage_host_to_device_transfers", options.ShouldStageHostToDeviceTransfers);
        native.AddBool(values, "abort_collectives_on_failure", options.AbortCollectivesOnFailure);
        native.AddBool(values, "use_tfrt_gpu_client", options.UseTfrtGpuClient);
        native.AddBool(values, "enable_mock_nccl", options.EnableMockNccl);
        native.AddString(values, "mock_gpu_topology", options.MockGpuTopology);
        native.AddInt64(values, "partition_index", options.PartitionIndex);

        native._values = values.ToArray();
        if (native._values.Length > 0)
            native._valuesHandle = GCHandle.Alloc(native._values, GCHandleType.Pinned);

        return native;
    }

    private void AddString(List<PjrtNamedValue> values, string name, string? value)
    {
        if (value is null)
            return;

        var valueBytes = NullTerminatedUtf8(value);
        var valueHandle = GCHandle.Alloc(valueBytes, GCHandleType.Pinned);
        _pinnedRoots.Add(valueHandle);
        var valuePtr = (byte*)valueHandle.AddrOfPinnedObject();

        values.Add(CreateNamedValue(name, PjrtNamedValueType.String, new PjrtNamedValueUnion
        {
            StringValue = valuePtr
        }, checked((nuint)Encoding.UTF8.GetByteCount(value))));
    }

    private void AddInt64(List<PjrtNamedValue> values, string name, long? value)
    {
        if (!value.HasValue)
            return;

        values.Add(CreateNamedValue(name, PjrtNamedValueType.Int64, new PjrtNamedValueUnion
        {
            Int64Value = value.Value
        }, 1));
    }

    private void AddInt64List(List<PjrtNamedValue> values, string name, IReadOnlyList<long>? value)
    {
        if (value is null)
            return;

        var array = value.ToArray();
        var arrayHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
        _pinnedRoots.Add(arrayHandle);
        var arrayPtr = (long*)arrayHandle.AddrOfPinnedObject();

        values.Add(CreateNamedValue(name, PjrtNamedValueType.Int64List, new PjrtNamedValueUnion
        {
            Int64ArrayValue = arrayPtr
        }, checked((nuint)array.Length)));
    }

    private void AddFloat(List<PjrtNamedValue> values, string name, float? value)
    {
        if (!value.HasValue)
            return;

        values.Add(CreateNamedValue(name, PjrtNamedValueType.Float, new PjrtNamedValueUnion
        {
            FloatValue = value.Value
        }, 1));
    }

    private void AddBool(List<PjrtNamedValue> values, string name, bool? value)
    {
        if (!value.HasValue)
            return;

        values.Add(CreateNamedValue(name, PjrtNamedValueType.Bool, new PjrtNamedValueUnion
        {
            BoolValue = value.Value ? (byte)1 : (byte)0
        }, 1));
    }

    private PjrtNamedValue CreateNamedValue(
        string name,
        PjrtNamedValueType type,
        PjrtNamedValueUnion value,
        nuint valueSize)
    {
        var nameBytes = NullTerminatedUtf8(name);
        var nameHandle = Marshal.AllocHGlobal(nameBytes.Length);
        _nativeAllocations.Add(nameHandle);
        Marshal.Copy(nameBytes, 0, nameHandle, nameBytes.Length);

        return new PjrtNamedValue
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtNamedValue>(),
            ExtensionStart = null,
            Name = (byte*)nameHandle,
            NameSize = checked((nuint)Encoding.UTF8.GetByteCount(name)),
            Type = type,
            Value = value,
            ValueSize = valueSize
        };
    }

    private static byte[] NullTerminatedUtf8(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value.AsSpan(), bytes);
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_valuesHandle.IsAllocated)
            _valuesHandle.Free();

        foreach (var handle in _pinnedRoots)
        {
            if (handle.IsAllocated)
                handle.Free();
        }

        foreach (var allocation in _nativeAllocations)
            Marshal.FreeHGlobal(allocation);

        _disposed = true;
    }
}
