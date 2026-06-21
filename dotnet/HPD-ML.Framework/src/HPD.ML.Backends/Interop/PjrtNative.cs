using System.Runtime.InteropServices;
using System.Text;

namespace HPD.ML.Backends.Pjrt.Interop;

internal static unsafe class PjrtNative
{
    public static T GetFunction<T>(nint pointer, string name)
        where T : Delegate
    {
        if (pointer == 0)
            throw new PjrtException($"PJRT API table does not contain required function: {name}");

        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public static void ThrowIfError(PjrtApi* api, void* error)
    {
        if (error is null)
            return;

        var message = TryReadErrorMessage(api, error);
        DestroyError(api, error);
        throw new PjrtException(message);
    }

    public static string ReadUtf8(byte* pointer, nuint length)
    {
        if (pointer is null || length == 0)
            return string.Empty;

        if (length > int.MaxValue)
            throw new PjrtException($"PJRT returned a string too large to marshal: {length} bytes.");

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(pointer, checked((int)length)));
    }

    public static void AwaitAndDestroyEvent(PjrtApi* api, void* eventHandle)
    {
        if (eventHandle is null)
            return;

        try
        {
            var awaitFn = GetFunction<PjrtEventAwaitDelegate>(api->PjrtEventAwait, "PJRT_Event_Await");
            var awaitArgs = new PjrtEventAwaitArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtEventAwaitArgs>(),
                ExtensionStart = null,
                Event = eventHandle
            };

            ThrowIfError(api, awaitFn(&awaitArgs));
        }
        finally
        {
            var destroyFn = GetFunction<PjrtEventDestroyDelegate>(api->PjrtEventDestroy, "PJRT_Event_Destroy");
            var destroyArgs = new PjrtEventDestroyArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtEventDestroyArgs>(),
                ExtensionStart = null,
                Event = eventHandle
            };

            ThrowIfError(api, destroyFn(&destroyArgs));
        }
    }

    private static string TryReadErrorMessage(PjrtApi* api, void* error)
    {
        try
        {
            var messageFn = GetFunction<PjrtErrorMessageDelegate>(api->PjrtErrorMessage, "PJRT_Error_Message");
            var args = new PjrtErrorMessageArgs
            {
                StructSize = (nuint)Marshal.SizeOf<PjrtErrorMessageArgs>(),
                ExtensionStart = null,
                Error = error,
                Message = null,
                MessageSize = 0
            };

            messageFn(&args);
            var message = ReadUtf8(args.Message, args.MessageSize);
            return string.IsNullOrWhiteSpace(message) ? "PJRT call failed." : message;
        }
        catch (Exception ex) when (ex is not PjrtException)
        {
            return $"PJRT call failed, and error message could not be read: {ex.Message}";
        }
    }

    private static void DestroyError(PjrtApi* api, void* error)
    {
        var destroyFn = GetFunction<PjrtErrorDestroyDelegate>(api->PjrtErrorDestroy, "PJRT_Error_Destroy");
        var args = new PjrtErrorDestroyArgs
        {
            StructSize = (nuint)Marshal.SizeOf<PjrtErrorDestroyArgs>(),
            ExtensionStart = null,
            Error = error
        };

        destroyFn(&args);
    }
}
