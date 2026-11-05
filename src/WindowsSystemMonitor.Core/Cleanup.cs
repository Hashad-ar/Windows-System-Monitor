using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WindowsSystemMonitor.Core;

/// <summary>Only stale .tmp/.temp files are eligible. A plan is an immutable, single-service preview.</summary>
public sealed class CleanupCandidate
{
    internal CleanupCandidate(string path, long length, DateTime lastWrite, NativeCleanup.Identity? identity)
        => (FullPath, Length, LastWriteTimeUtc, Identity) = (path, length, lastWrite, identity);
    public string FullPath { get; }
    public long Length { get; }
    public DateTime LastWriteTimeUtc { get; }
    internal NativeCleanup.Identity? Identity { get; }
}

public sealed class CleanupPlan
{
    internal CleanupPlan(Guid owner, string root, List<CleanupCandidate> candidates, int skipped, bool truncated)
    {
        Owner = owner; RootPath = root; Candidates = candidates.AsReadOnly();
        SkippedCount = skipped; IsTruncated = truncated;
        EstimatedBytes = candidates.Sum(c => c.Length);
    }
    internal Guid Owner { get; }
    public string RootPath { get; }
    public IReadOnlyList<CleanupCandidate> Candidates { get; }
    public long EstimatedBytes { get; }
    public int SkippedCount { get; }
    public bool IsTruncated { get; }
}

/// <param name="DeletedBytes">Logical size of successfully deleted files, not measured physical disk reclamation.</param>
public sealed record CleanupResult(int DeletedCount, long DeletedBytes, int SkippedCount, bool Canceled, int UnprocessedCount);

public sealed class CleanupService
{
    private readonly string root;
    private readonly Guid owner = Guid.NewGuid();
    private readonly Func<DateTime> utcNow;
    private readonly int entryLimit;
    private int deleting;
    private static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);
    private const FileAttributes UnsafeAttributes = FileAttributes.ReparsePoint | FileAttributes.System |
        FileAttributes.ReadOnly | FileAttributes.Offline | FileAttributes.Encrypted;

    internal CleanupService(string root, Func<DateTime>? clock = null, int entryLimit = 50_000)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (this.root == Path.GetPathRoot(this.root)) throw new ArgumentException("A drive root cannot be cleaned.", nameof(root));
        if (entryLimit < 1) throw new ArgumentOutOfRangeException(nameof(entryLimit));
        utcNow = clock ?? (() => DateTime.UtcNow);
        this.entryLimit = entryLimit;
    }

    /// <summary>Do not trust TEMP/TMP overrides that can point to personal files or system directories.</summary>
    public static CleanupService ForCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Temporary-file cleanup requires Windows.");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local) || local.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("A local user profile is required for safe cleanup. Use Windows Storage Settings instead.");
        return new CleanupService(Path.Combine(local, "Temp"));
    }

    public Task<CleanupPlan> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private CleanupPlan Scan(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var candidates = new List<CleanupCandidate>();
        int skipped = 0, visited = 0;
        if (!Directory.Exists(root)) return new CleanupPlan(owner, root, candidates, 0, false);
        if (!SafeChain(root)) throw new IOException("The Temp path contains a symbolic link, junction, or inaccessible folder. Use Windows Storage Settings.");
        var pending = new Stack<string>();
        pending.Push(root);
        DateTime cutoff = utcNow() - MinimumAge;
        while (pending.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            if (!SafeChain(directory)) { skipped++; continue; }
            try
            {
                // Hold the native chain during enumeration so an attacker cannot swap an ancestor
                // for a junction between the managed precheck and enumerating its children.
                using var traversalLock = OperatingSystem.IsWindows() ? NativeCleanup.LockDirectoryChain(directory) : null;
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > entryLimit) return new CleanupPlan(owner, root, candidates, skipped, true);
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & UnsafeAttributes) != 0) { skipped++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        string extension = Path.GetExtension(path);
                        if (!extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) &&
                            !extension.Equals(".temp", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                        var file = new FileInfo(path);
                        if (file.LastWriteTimeUtc >= cutoff) { skipped++; continue; }
                        NativeCleanup.Identity? identity = null;
                        long length = file.Length;
                        DateTime modified = file.LastWriteTimeUtc;
                        if (OperatingSystem.IsWindows())
                        {
                            var snapshot = NativeCleanup.Inspect(path);
                            if ((snapshot.Attributes & (UnsafeAttributes | FileAttributes.Directory)) != 0 || snapshot.Links != 1 || snapshot.LastWriteUtc >= cutoff)
                            { skipped++; continue; }
                            (length, modified, identity) = (snapshot.Length, snapshot.LastWriteUtc, snapshot.Identity);
                        }
                        candidates.Add(new CleanupCandidate(path, length, modified, identity));
                    }
                    catch (Exception ex) when (ExpectedFileFailure(ex)) { skipped++; }
                }
            }
            catch (Exception ex) when (ExpectedFileFailure(ex)) { skipped++; }
        }
        return new CleanupPlan(owner, root, candidates, skipped, false);
    }

    public Task<CleanupResult> DeleteAsync(CleanupPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Owner != owner || !string.Equals(plan.RootPath, root, StringComparison.Ordinal))
            throw new ArgumentException("This preview belongs to a different cleanup session.", nameof(plan));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Deletion is supported only by the Windows handle-based implementation.");
        if (Interlocked.Exchange(ref deleting, 1) != 0) throw new InvalidOperationException("Cleanup is already running.");
        return Task.Run(() =>
        {
            int deleted = 0, skipped = 0, processed = 0;
            long bytes = 0;
            try
            {
                foreach (CleanupCandidate candidate in plan.Candidates)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        NativeCleanup.DeleteVerified(root, candidate, utcNow() - MinimumAge);
                        deleted++; bytes += candidate.Length;
                    }
                    catch (Exception ex) when (ExpectedFileFailure(ex)) { skipped++; }
                    processed++;
                }
                return new CleanupResult(deleted, bytes, skipped, cancellationToken.IsCancellationRequested, plan.Candidates.Count - processed);
            }
            finally { Volatile.Write(ref deleting, 0); }
        });
    }

    internal static bool SafeChain(string path)
    {
        try
        {
            for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception ex) when (ExpectedFileFailure(ex)) { return false; }
    }

    private static bool ExpectedFileFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception;
}

// Destructive operations are performed against an exclusively opened file handle, never File.Delete(path).
// Every ancestor is held without write/delete sharing until the deletion completes, preventing a junction
// insertion or rename between checking the path and marking that exact previewed file for deletion.
internal static class NativeCleanup
{
    internal readonly record struct Identity(uint Volume, ulong File);
    internal readonly record struct Snapshot(FileAttributes Attributes, long Length, DateTime LastWriteUtc, uint Links, Identity Identity);
    private const uint OpenExisting = 3, OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    private const uint GenericRead = 0x80000000, DeleteAccess = 0x00010000;

    internal static Snapshot Inspect(string path)
    {
        using SafeFileHandle handle = Open(path, GenericRead, FileShare.ReadWrite | FileShare.Delete, OpenReparsePoint);
        return Read(handle);
    }

    internal static void DeleteVerified(string root, CleanupCandidate candidate, DateTime cutoff)
    {
        string fullPath = Path.GetFullPath(candidate.FullPath);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The file is outside the cleanup root.");
        using (LockDirectoryChain(Path.GetDirectoryName(fullPath)!))
        {
            using SafeFileHandle file = Open(fullPath, GenericRead | DeleteAccess, FileShare.None, OpenReparsePoint);
            Snapshot current = Read(file);
            if (candidate.Identity is null || current.Identity != candidate.Identity || current.Links != 1 ||
                current.Length != candidate.Length || current.LastWriteUtc != candidate.LastWriteTimeUtc || current.LastWriteUtc >= cutoff ||
                (current.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Offline | FileAttributes.Encrypted)) != 0)
                throw new IOException("The previewed file changed or is no longer safe to delete.");
            var disposition = new Disposition { DeleteFile = 1 };
            if (!SetFileInformationByHandle(file, 4, ref disposition, (uint)Marshal.SizeOf<Disposition>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static IDisposable LockDirectoryChain(string path)
    {
        var ancestors = new Stack<string>();
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            ancestors.Push(directory.FullName);
        var locks = new DirectoryLocks();
        try
        {
            while (ancestors.TryPop(out string? ancestor))
            {
                SafeFileHandle directory = Open(ancestor, GenericRead, FileShare.Read, OpenReparsePoint | BackupSemantics);
                locks.Handles.Add(directory);
                Snapshot info = Read(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || (info.Attributes & FileAttributes.Directory) == 0)
                    throw new IOException("A cleanup ancestor is a link or is no longer a directory.");
            }
            return locks;
        }
        catch { locks.Dispose(); throw; }
    }

    private sealed class DirectoryLocks : IDisposable
    {
        internal readonly List<SafeFileHandle> Handles = [];
        public void Dispose() { for (int i = Handles.Count - 1; i >= 0; i--) Handles[i].Dispose(); }
    }

    private static SafeFileHandle Open(string path, uint access, FileShare sharing, uint flags)
    {
        SafeFileHandle handle = CreateFileW(path, access, (uint)sharing, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    private static Snapshot Read(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        long modified = ((long)info.LastWriteHigh << 32) | info.LastWriteLow;
        return new Snapshot((FileAttributes)info.Attributes, ((long)info.SizeHigh << 32) | info.SizeLow,
            DateTime.FromFileTimeUtc(modified), info.Links, new Identity(info.VolumeSerial, ((ulong)info.IndexHigh << 32) | info.IndexLow));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, LastWriteLow, LastWriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Disposition { public byte DeleteFile; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref Disposition information, uint size);
}
