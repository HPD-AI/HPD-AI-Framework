namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

/// <summary>Installs the direct-connect block for a pre-provisioned Windows sandbox account.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsNetworkFence
{
    private const uint Persistent = 1;
    private const uint SecurityDescriptorType = 14;
    private const uint BlockAction = 0x1001;
    private const uint FilterNotFound = 0x80320003;
    private const uint SublayerNotFound = 0x80320007;

    private static readonly Guid ConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid ConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid AleUserId = new("af043a0a-b34d-4f86-979c-c90371af6e66");

    // Provisioning owns these keys and must withhold the account from sessions until the
    // actual launch token has passed the network probes.
    public static void Install(string accountName, Guid sublayerKey, Guid ipv4Key, Guid ipv6Key)
    {
        ValidateKeys(sublayerKey, ipv4Key, ipv6Key);

        if (string.IsNullOrWhiteSpace(accountName) || accountName.Contains('\\') || accountName.Contains('@'))
            throw new ArgumentException("A local sandbox account name is required.", nameof(accountName));

        var account = new NTAccount(System.Environment.MachineName, accountName);
        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
        if (sid.IsWellKnown(WellKnownSidType.AccountAdministratorSid) ||
            sid.IsWellKnown(WellKnownSidType.AccountGuestSid))
            throw new ArgumentException("A dedicated sandbox account is required.", nameof(accountName));

        using var current = WindowsIdentity.GetCurrent();
        if (sid.AccountDomainSid is null || sid == current.User)
            throw new ArgumentException("The sandbox account must differ from the provisioning identity.", nameof(accountName));

        string sddl = $"O:LSG:LSD:(A;;CC;;;{sid.Value})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr descriptor, out uint size))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not encode the WFP account condition.");

        try
        {
            RunTransaction(engine =>
            {
            fixed (char* name = "HPD Windows sandbox direct egress")
            {
                var sublayer = new Sublayer
                {
                    Key = sublayerKey,
                    Display = new DisplayData { Name = (IntPtr)name },
                    Flags = Persistent,
                    Weight = 0x8000,
                };
                Check(FwpmSubLayerAdd0(engine, in sublayer, IntPtr.Zero), "add sublayer");

                var blob = new ByteBlob { Size = size, Data = descriptor };
                var condition = new FilterCondition
                {
                    Field = AleUserId,
                    Value = new ConditionValue { Type = SecurityDescriptorType, Pointer = (IntPtr)(&blob) },
                };
                AddBlock(engine, ipv4Key, ConnectV4, sublayerKey, (IntPtr)name, &condition);
                AddBlock(engine, ipv6Key, ConnectV6, sublayerKey, (IntPtr)name, &condition);
            }
            });
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    public static void Remove(Guid sublayerKey, Guid ipv4Key, Guid ipv6Key)
    {
        ValidateKeys(sublayerKey, ipv4Key, ipv6Key);

        RunTransaction(engine =>
        {
            Delete(FwpmFilterDeleteByKey0(engine, in ipv4Key), FilterNotFound, "delete IPv4 block");
            Delete(FwpmFilterDeleteByKey0(engine, in ipv6Key), FilterNotFound, "delete IPv6 block");
            Delete(FwpmSubLayerDeleteByKey0(engine, in sublayerKey), SublayerNotFound, "delete sublayer");
        });
    }

    private static void ValidateKeys(Guid sublayerKey, Guid ipv4Key, Guid ipv6Key)
    {
        if (sublayerKey == Guid.Empty || ipv4Key == Guid.Empty || ipv6Key == Guid.Empty ||
            sublayerKey == ipv4Key || sublayerKey == ipv6Key || ipv4Key == ipv6Key)
            throw new ArgumentException("Three distinct, nonempty WFP keys are required.");
    }

    private static void RunTransaction(Action<IntPtr> update)
    {
        Check(FwpmEngineOpen0(IntPtr.Zero, uint.MaxValue, IntPtr.Zero, IntPtr.Zero, out IntPtr engine), "open");
        try
        {
            Check(FwpmTransactionBegin0(engine, 0), "begin transaction");
            bool committed = false;
            try
            {
                update(engine);
                Check(FwpmTransactionCommit0(engine), "commit transaction");
                committed = true;
            }
            finally
            {
                if (!committed)
                    FwpmTransactionAbort0(engine);
            }
        }
        finally
        {
            FwpmEngineClose0(engine);
        }
    }

    private static void Delete(uint result, uint notFound, string operation)
    {
        if (result != notFound)
            Check(result, operation);
    }

    private static void AddBlock(
        IntPtr engine, Guid key, Guid layer, Guid sublayer, IntPtr name, FilterCondition* condition)
    {
        var filter = new Filter
        {
            Key = key,
            Display = new DisplayData { Name = name },
            Flags = Persistent,
            Layer = layer,
            Sublayer = sublayer,
            ConditionCount = 1,
            Conditions = (IntPtr)condition,
            Action = new FilterAction { Type = BlockAction },
        };
        Check(FwpmFilterAdd0(engine, in filter, IntPtr.Zero, out _), "add direct-connect block");
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0)
            throw new Win32Exception(unchecked((int)result), $"WFP could not {operation} (0x{result:x8}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayData { public IntPtr Name, Description; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByteBlob { public uint Size; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ConditionValue { public uint Type; public IntPtr Pointer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterCondition { public Guid Field; public uint MatchType; public ConditionValue Value; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterAction { public uint Type; public Guid Key; }

    [StructLayout(LayoutKind.Explicit)]
    private struct FilterContext
    {
        [FieldOffset(0)] public ulong Raw;
        [FieldOffset(0)] public Guid Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Sublayer
    {
        public Guid Key;
        public DisplayData Display;
        public uint Flags;
        public IntPtr Provider;
        public ByteBlob ProviderData;
        public ushort Weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Filter
    {
        public Guid Key;
        public DisplayData Display;
        public uint Flags;
        public IntPtr Provider;
        public ByteBlob ProviderData;
        public Guid Layer, Sublayer;
        public ConditionValue Weight;
        public uint ConditionCount;
        public IntPtr Conditions;
        public FilterAction Action;
        public FilterContext Context;
        public IntPtr Reserved;
        public ulong Id;
        public ConditionValue EffectiveWeight;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string text, uint revision, out IntPtr descriptor, out uint size);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmEngineOpen0(
        IntPtr server, uint authentication, IntPtr identity, IntPtr session, out IntPtr engine);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmEngineClose0(IntPtr engine);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmTransactionBegin0(IntPtr engine, uint flags);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmTransactionCommit0(IntPtr engine);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmTransactionAbort0(IntPtr engine);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmSubLayerAdd0(IntPtr engine, in Sublayer sublayer, IntPtr security);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmFilterAdd0(IntPtr engine, in Filter filter, IntPtr security, out ulong id);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmFilterDeleteByKey0(IntPtr engine, in Guid key);

    [LibraryImport("fwpuclnt.dll")]
    private static partial uint FwpmSubLayerDeleteByKey0(IntPtr engine, in Guid key);
}
