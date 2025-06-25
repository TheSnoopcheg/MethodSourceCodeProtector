using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Protector.Patcher;

public class NativeResourceUpdater : IDisposable
{
    #region PInvoke
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, IntPtr lpData, uint cbData);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpModuleName, IntPtr file, uint flag);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr handle);

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    #endregion

    private readonly IntPtr _METHODTABLE_START = 101;
    private readonly IntPtr _METHODTABLE_LAST = 102;
    private readonly IntPtr _METHODTABLE_LIST = 103;

    private List<NativeObjectInfo> _methodIdTable = new List<NativeObjectInfo>();
    private int _lastID;
    private string _fileName;

    public NativeResourceUpdater(string dllFileName)
    {
        _fileName = dllFileName;
        IntPtr hModule = LoadLibraryEx(dllFileName, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if(hModule == IntPtr.Zero)
        {
            throw new Exception($"Library ({dllFileName}) is not loaded");
        }

        byte[] nativeObjectsInfoData = RcDataReader.ReadResource(hModule, _METHODTABLE_LIST);
        _methodIdTable = (List<NativeObjectInfo>)BinaryListSerializer<NativeObjectInfo>.Deserialize(nativeObjectsInfoData);
        byte[] lastIdData = RcDataReader.ReadResource(hModule, _METHODTABLE_LAST);
        _lastID = BitConverter.ToInt32(lastIdData, 0);
        FreeLibrary(hModule);
    }


    /// <summary>
    /// Updates a string resource in an unloaded PE file (DLL or EXE).
    /// </summary>
    /// <param name="dllPath">The full path to the DLL to modify.</param>
    /// <param name="resourceId">The IntPtr ID of the resource to update.</param>
    /// <param name="resourceType">The type of the resource (e.g., RT_STRING, or a custom string type).</param>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="languageId">The language ID (MAKELANGID). 1033 is US English.</param>
    private void UpdateResource(IntPtr resourceId, IntPtr resourceType, byte[] newValue, ushort languageId = 1033)
    {
        IntPtr hUpdate = BeginUpdateResource(_fileName, false);
        if (hUpdate == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to begin resource update.");
        }

        bool success = false;
        try
        {
            var handle = GCHandle.Alloc(newValue, GCHandleType.Pinned);
            try
            {
                IntPtr pData = handle.AddrOfPinnedObject();
                if (!UpdateResource(hUpdate, resourceType, resourceId, languageId, pData, (uint)newValue.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to update resource.");
                }
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }

            if (!EndUpdateResource(hUpdate, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to commit resource update.");
            }
            success = true;
        }
        finally
        {
            if (!success)
            {
                EndUpdateResource(hUpdate, true);
            }
        }
    }

    public void AddResource(string methodName, byte[] data)
    {
        var methodIdInfo = _methodIdTable.FirstOrDefault(m => m.MethodName == methodName);
        if (!methodIdInfo.Equals(default(NativeObjectInfo)))
        {
            UpdateResource(methodIdInfo.ResourceID, RTTypes.RCDATA, data);
        }
        else
        {
            methodIdInfo.MethodName = methodName;
            methodIdInfo.ResourceID = ++_lastID;

            _methodIdTable.Add(methodIdInfo);
            UpdateResource(_lastID, RTTypes.RCDATA, data);
        }
    }

    public void Dispose()
    {
        UpdateResource(_METHODTABLE_LIST, RTTypes.RCDATA, BinaryListSerializer<NativeObjectInfo>.Serialize(_methodIdTable));
        UpdateResource(_METHODTABLE_LAST, RTTypes.RCDATA, BitConverter.GetBytes(_lastID));
    }
}
