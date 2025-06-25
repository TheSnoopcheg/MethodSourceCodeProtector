using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Protector.Patcher;

public static class RcDataReader
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

    #endregion

    /// <summary>
    /// Reads a specified RCDATA resource from the current executable or DLL.
    /// </summary>
    /// <param name="resourceName">The name/ID of the RCDATA resource.</param>
    /// <returns>A byte array containing the resource data.</returns>
    /// <exception cref="Win32Exception">Thrown if any Win32 API call fails.</exception>
    public static byte[] ReadResource(IntPtr handle, IntPtr resourceId)
    {

        IntPtr hResource = FindResource(handle, resourceId, RTTypes.RCDATA);
        if (hResource == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to find resource '{resourceId}'.");
        }

        uint resourceSize = SizeofResource(handle, hResource);
        if (resourceSize == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Resource size is zero.");
        }

        IntPtr hResourceLoad = LoadResource(handle, hResource);
        if (hResourceLoad == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load resource.");
        }

        IntPtr pResourceData = LockResource(hResourceLoad);
        if (pResourceData == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to lock resource.");
        }

        byte[] data = new byte[resourceSize];

        Marshal.Copy(pResourceData, data, 0, (int)resourceSize);

        return data;
    }
}