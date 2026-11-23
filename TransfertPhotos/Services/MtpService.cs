using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TransfertPhotos.Services;

public sealed class MtpService
{
    private const int MaxDepth = 12;
    private const int MaxFolders = 20000;

    private readonly StaWorker _worker = new();
    private int _foldersVisited;

    private static readonly HashSet<string> PhotoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif",
        ".bmp", ".dng", ".tif", ".tiff", ".avif"
    };

    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".3gp", ".3g2", ".m4v",
        ".webm", ".wmv", ".mpg", ".mpeg", ".mts", ".m2ts"
    };

    public IReadOnlyList<PhoneInfo> GetDevices() =>
        _worker.Invoke(() =>
        {
            dynamic shell = CreateShell();
            var devices = new List<PhoneInfo>();
            dynamic thisPc = shell.NameSpace(17); // 17 = "Ce PC"
            if (IsNull(thisPc)) return devices;

            dynamic items = thisPc.Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                bool isFileSystem;
                try { isFileSystem = (bool)item.IsFileSystem; }
                catch { continue; }
                if (isFileSystem) continue;

                try
                {
                    if (string.IsNullOrWhiteSpace((string)item.Name)) continue;
                    dynamic folder = shell.NameSpace(item);
                    if (IsNull(folder)) continue;
                    devices.Add(new PhoneInfo(item.Name));
                }
                catch { }
            }
            return devices;
        });

    public ScanResult Scan(PhoneInfo device, CancellationToken ct, IProgress<string>? log) =>
        _worker.Invoke(() =>
        {
            dynamic shell = CreateShell();
            dynamic deviceFolder = ResolveDeviceFolder(shell, device.Name)
                ?? throw new InvalidOperationException(
                    "L'appareil est inaccessible. Vérifiez qu'il est déverrouillé et en mode « Transfert de fichiers USB (MTP) ».");
            _foldersVisited = 0;
            ct.ThrowIfCancellationRequested();

            int photos = 0, videos = 0, seen = 0;
            long bytes = 0;

            object shellObj = shell;
            object folderObj = deviceFolder;
            Walk(shellObj, folderObj, new List<string>(), 0, (item, name, segs) =>
            {
                if (++seen % 200 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    log?.Report($"Analyse... {seen} fichiers trouvés ({CombineSegments(segs)})");
                }
                long? size = TryGetSize(item);
                if (IsPhotoExt(name)) photos++; else videos++;
                if (size is long s) bytes += s;
            }, ct, log);

            return new ScanResult(photos, videos, bytes);
        });

    public TransferResult Transfer(PhoneInfo device, string destination, TransferOptions options,
        CancellationToken ct, IProgress<string>? log) =>
        _worker.Invoke(() =>
        {
            dynamic shell = CreateShell();
            dynamic deviceFolder = ResolveDeviceFolder(shell, device.Name)
                ?? throw new InvalidOperationException(
                    "L'appareil est inaccessible. Vérifiez qu'il est déverrouillé et en mode « Transfert de fichiers USB (MTP) ».");
            _foldersVisited = 0;
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destination);

            int copied = 0, skipped = 0;
            long bytes = 0;

            object shellObj = shell;
            object folderObj = deviceFolder;
            Walk(shellObj, folderObj, new List<string>(), 0, (item, name, segs) =>
            {
                ct.ThrowIfCancellationRequested();
                long? size = TryGetSize(item);
                DateTime? date = options.OrganizeByDate ? TryGetDateTaken(item) : null;

                string destDir = options.OrganizeByDate && date is DateTime d
                    ? Path.Combine(destination, d.ToString("yyyy-MM-dd"))
                    : Path.Combine(destination, CombineSegments(segs));

                string target = Path.Combine(destDir, name);
                if (options.SkipDuplicates && File.Exists(target))
                {
                    try
                    {
                        if (!size.HasValue || new FileInfo(target).Length == size.Value)
                        {
                            skipped++;
                            return;
                        }
                    }
                    catch (IOException) { }
                    target = UniqueTarget(destDir, name);
                }

                Directory.CreateDirectory(destDir);
                dynamic destFolder = shell.NameSpace(destDir);
                if (IsNull(destFolder)) throw new InvalidOperationException("Dossier de destination inaccessible : " + destDir);

                destFolder.CopyHere(item, 20); // 4 = pas de boîte de dialogue, 16 = tout remplacer
                WaitForCopy(target, size, ct);

                copied++;
                if (size is long done) bytes += done;
                log?.Report($"Copié : {name}  ({size?.ToString() ?? "?"} octets)");
            }, ct, log);

            return new TransferResult(copied, skipped, bytes);
        });

    private void Walk(object shellObj, object folderObj, List<string> segments, int depth,
        Action<object, string, List<string>> onFile, CancellationToken ct, IProgress<string>? log)
    {
        dynamic shell = shellObj;
        dynamic folder = folderObj;
        if (Interlocked.Increment(ref _foldersVisited) > MaxFolders) return;

        dynamic items;
        int count;
        try
        {
            items = folder.Items();
            count = items.Count;
        }
        catch { return; }

        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            dynamic item = items.Item(i);
            if (IsNull(item)) continue;

            string name;
            bool isFolder = false;
            try
            {
                name = item.Name;
                isFolder = (bool)item.IsFolder;
            }
            catch { continue; }

            if (string.IsNullOrWhiteSpace(name)) continue;

            if (!isFolder)
            {
                if (IsMedia(name)) onFile(item, name, segments);
                continue;
            }

            if (depth >= MaxDepth || name.StartsWith(".")) continue;

            segments.Add(name);
            try
            {
                dynamic sub = shell.NameSpace(item);
                if (!IsNull(sub)) Walk((object)shell, (object)sub, segments, depth + 1, onFile, ct, log);
            }
            finally
            {
                segments.RemoveAt(segments.Count - 1);
            }
        }
    }

    private static object? ResolveDeviceFolder(dynamic shell, string deviceName)
    {
        dynamic thisPc = shell.NameSpace(17);
        if (IsNull(thisPc)) return null;

        dynamic items = thisPc.Items();
        int count = items.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic item = items.Item(i);
            bool isFileSystem;
            try { isFileSystem = (bool)item.IsFileSystem; }
            catch { continue; }
            if (isFileSystem) continue;

            try
            {
                if (!string.Equals((string)item.Name, deviceName, StringComparison.OrdinalIgnoreCase)) continue;
                dynamic folder = shell.NameSpace(item);
                if (!IsNull(folder)) return folder;
            }
            catch { }
        }
        return null;
    }

    private static void WaitForCopy(string path, long? size, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long last = -1;
        int stable = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(600))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    if (size is long expected)
                    {
                        if (fi.Length == expected) return;
                    }
                    else
                    {
                        if (fi.Length > 0 && fi.Length == last && ++stable >= 3) return;
                        else if (fi.Length != last) stable = 0;
                        last = fi.Length;
                    }
                }
                else stable = 0;
            }
            catch (IOException) { }
            Thread.Sleep(250);
        }
        throw new TimeoutException("Transfert interrompu ou trop lent : " + path);
    }

    private static string UniqueTarget(string dir, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        int n = 2;
        string candidate;
        do { candidate = Path.Combine(dir, $"{stem} ({n++}){ext}"); } while (File.Exists(candidate));
        return candidate;
    }

    private static dynamic CreateShell() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("L'interface Shell de Windows est indisponible."));

    private static bool IsNull(object? value) => value is null || value is DBNull;

    private static bool IsMedia(string name) => IsPhotoExt(name) || IsVideoExt(name);
    private static bool IsPhotoExt(string name) => PhotoExt.Contains(Path.GetExtension(name));
    private static bool IsVideoExt(string name) => VideoExt.Contains(Path.GetExtension(name));

    private static string CombineSegments(List<string> segments) =>
        string.Join(Path.DirectorySeparatorChar, segments);

    private static long? TryGetSize(dynamic item)
    {
        try
        {
            object? value = item.Size;
            return IsNull(value) ? null : Convert.ToInt64(value);
        }
        catch { return null; }
    }

    private static DateTime? TryGetDateTaken(dynamic item)
    {
        try
        {
            object? value = item.ExtendedProperty("System.Photo.DateTaken");
            if (value is DateTime taken) return taken;
        }
        catch { }
        try
        {
            object? value = item.ModifyDate;
            if (value is DateTime modified) return modified;
        }
        catch { }
        return null;
    }
}
