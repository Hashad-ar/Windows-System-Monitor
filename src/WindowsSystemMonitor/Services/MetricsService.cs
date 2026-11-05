using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WindowsSystemMonitor.Services;

/// <summary>A single inexpensive observation. A null measurement means unavailable.</summary>
public sealed record SystemSnapshot(
    double? CpuPercent,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    double? MemoryPercent,
    long? StorageUsedBytes,
    long? StorageTotalBytes,
    long? StorageFreeBytes,
    double? StoragePercent,
    string SystemDrive,
    string NetworkStatus,
    TimeSpan? Uptime,
    string WindowsVersion,
    string DeviceName,
    string ProcessorName,
    string Architecture);

/// <summary>
/// No timers, WMI, processes, disk scans, or persistent handles. The window owns
/// polling and can call ReadSnapshot on a worker thread every four seconds.
/// </summary>
public sealed class MetricsService
{
    private readonly object _cpuLock = new();
    private CpuTimes? _previousCpu;
    private readonly string _windowsVersion = ReadWindowsVersion();
    private readonly string _deviceName = ReadDeviceName();
    private readonly string _processorName = ReadProcessorName();
    private readonly string _architecture = RuntimeInformation.OSArchitecture.ToString();
    private readonly string _systemDrive = ReadSystemDrive();

    public SystemSnapshot ReadSnapshot()
    {
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var storage = ReadStorage();
        return new SystemSnapshot(
            cpu,
            memory.Used,
            memory.Total,
            memory.Total is > 0 && memory.Used.HasValue
                ? 100d * memory.Used.Value / memory.Total.Value : null,
            storage.Used,
            storage.Total,
            storage.Free,
            storage.Total is > 0 && storage.Used.HasValue
                ? 100d * storage.Used.Value / storage.Total.Value : null,
            _systemDrive,
            ReadNetwork(),
            ReadUptime(),
            _windowsVersion,
            _deviceName,
            _processorName,
            _architecture);
    }

    /// <summary>Use when resuming polling, so CPU is not averaged across the pause.</summary>
    public void ResetCpuSample()
    {
        lock (_cpuLock)
            _previousCpu = null;
    }

    private double? ReadCpu()
    {
        lock (_cpuLock)
        {
            try
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    _previousCpu = null;
                    return null;
                }

                var current = new CpuTimes(idle.Value, kernel.Value, user.Value);
                var previous = _previousCpu;
                _previousCpu = current;
                if (previous is null || current.Idle < previous.Value.Idle ||
                    current.Kernel < previous.Value.Kernel || current.User < previous.Value.User)
                    return null;

                // GetSystemTimes' kernel time includes idle. Difference first to
                // avoid precision loss from converting lifetime counters to double.
                var idleDelta = current.Idle - previous.Value.Idle;
                var kernelDelta = current.Kernel - previous.Value.Kernel;
                var userDelta = current.User - previous.Value.User;
                var totalDelta = (double)kernelDelta + userDelta;
                if (totalDelta <= 0 || idleDelta > totalDelta)
                    return null;
                return Math.Clamp(100d * (totalDelta - idleDelta) / totalDelta, 0d, 100d);
            }
            catch (Exception ex) when (IsMeasurementFailure(ex))
            {
                _previousCpu = null;
                return null;
            }
        }
    }

    private static (ulong? Used, ulong? Total) ReadMemory()
    {
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0 ||
                status.AvailablePhysical > status.TotalPhysical)
                return (null, null);
            return (status.TotalPhysical - status.AvailablePhysical, status.TotalPhysical);
        }
        catch (Exception ex) when (IsMeasurementFailure(ex))
        {
            return (null, null);
        }
    }

    private (long? Used, long? Total, long? Free) ReadStorage()
    {
        if (string.IsNullOrEmpty(_systemDrive))
            return (null, null, null);
        try
        {
            var drive = new DriveInfo(_systemDrive);
            if (!drive.IsReady)
                return (null, null, null);
            var total = drive.TotalSize;
            // TotalFreeSpace makes used + free equal total even with user quotas.
            var free = drive.TotalFreeSpace;
            if (total <= 0 || free < 0 || free > total)
                return (null, null, null);
            return (total - free, total, free);
        }
        catch (Exception ex) when (IsMeasurementFailure(ex))
        {
            return (null, null, null);
        }
    }

    private static string ReadNetwork()
    {
        try
        {
            // Link state only: deliberately no external requests or assertion of
            // Internet reachability. Virtual and VPN adapters may count as links.
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                ? "Network link available" : "No active network link";
        }
        catch (Exception ex) when (IsMeasurementFailure(ex))
        {
            return "Network status unavailable";
        }
    }

    private static TimeSpan? ReadUptime()
    {
        try { return TimeSpan.FromMilliseconds(GetTickCount64()); }
        catch (Exception ex) when (IsMeasurementFailure(ex)) { return null; }
    }

    private static string ReadSystemDrive()
    {
        try { return Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty; }
        catch (Exception ex) when (IsMeasurementFailure(ex)) { return string.Empty; }
    }

    private static string ReadDeviceName()
    {
        try { return Environment.MachineName; }
        catch (Exception ex) when (IsMeasurementFailure(ex)) { return "Device name unavailable"; }
    }

    private static string ReadProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name
                ? name : "Processor information unavailable";
        }
        catch (Exception ex) when (IsMeasurementFailure(ex))
        {
            return "Processor information unavailable";
        }
    }

    private static string ReadWindowsVersion()
    {
        try
        {
            var version = new OsVersionInfo { Size = (uint)Marshal.SizeOf<OsVersionInfo>() };
            if (RtlGetVersion(ref version) != 0)
                return "Windows version unavailable";
            var name = version.Major == 10
                ? version.Build >= 22000 ? "Windows 11" : "Windows 10"
                : $"Windows {version.Major}.{version.Minor}";
            return $"{name} · build {version.Build}";
        }
        catch (Exception ex) when (IsMeasurementFailure(ex))
        {
            return "Windows version unavailable";
        }
    }

    private static bool IsMeasurementFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or
        System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or
        DllNotFoundException or EntryPointNotFoundException or NotSupportedException;

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public uint Size;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint Platform;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? ServicePack;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref OsVersionInfo version);
}
