using System.Runtime.InteropServices;
using System.Text;

namespace ScrapSAE.Infrastructure.Sae;

public sealed class SaeNativeClient : IDisposable
{
    private readonly string _dllPath;
    private IntPtr _moduleHandle;
    private EjecutaComandoDelegate? _executeCommand;
    private EjecutaComandoDelegate? _executeCommandXe;
    private bool _loaded;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int EjecutaComandoDelegate(string command, StringBuilder response, int responseLength);

    public SaeNativeClient(string dllPath)
    {
        _dllPath = dllPath;
    }

    public bool IsLoaded => _loaded;

    public bool Load()
    {
        if (_loaded)
        {
            return true;
        }

        var directory = Path.GetDirectoryName(_dllPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            NativeLibraryLoader.SetDllDirectory(directory);
        }

        _moduleHandle = NativeLibraryLoader.LoadLibrary(_dllPath);
        if (_moduleHandle == IntPtr.Zero)
        {
            return false;
        }

        _executeCommand = ResolveFunction("EjecutaComando");
        _executeCommandXe = ResolveFunction("EjecutaComandoXE");
        _loaded = _executeCommand != null || _executeCommandXe != null;
        return _loaded;
    }

    public int ExecuteCommand(string command, out string response, int bufferSize = 65536)
    {
        if (!_loaded)
        {
            throw new InvalidOperationException("SAE native client not loaded.");
        }

        var handler = _executeCommandXe ?? _executeCommand;
        if (handler == null)
        {
            throw new InvalidOperationException("EjecutaComando entry point not found.");
        }

        var buffer = new StringBuilder(bufferSize);
        var result = handler(command, buffer, bufferSize);
        response = buffer.ToString();
        return result;
    }

    private EjecutaComandoDelegate? ResolveFunction(string name)
    {
        var proc = NativeLibraryLoader.GetProcAddress(_moduleHandle, name);
        if (proc == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<EjecutaComandoDelegate>(proc);
    }

    public void Dispose()
    {
        if (_moduleHandle != IntPtr.Zero)
        {
            NativeLibraryLoader.FreeLibrary(_moduleHandle);
            _moduleHandle = IntPtr.Zero;
        }
    }
}
