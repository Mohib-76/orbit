using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Orbit.Core;

/// <summary>
/// File transfer. Control packets (offer / accept / done / cancel) go over the main link; the bytes
/// go over a separate TLS connection the PC opens to the phone, so a big file never blocks
/// clipboard or notifications. "accept" carries the byte offset to resume from.
/// </summary>
public sealed partial class OrbitCore
{
    readonly Dictionary<string, CancellationTokenSource> _running = new();

    // ---------------- PC → phone ----------------

    public void SendFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                SendFiles(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
                continue;
            }
            if (!File.Exists(path)) continue;
            var t = new Transfer
            {
                Name = Path.GetFileName(path), Size = new FileInfo(path).Length,
                Outgoing = true, LocalPath = path, State = TransferState.Queued
            };
            Ui.Run(() => Transfers.Insert(0, t));
            Offer(t);
        }
    }

    void Offer(Transfer t) =>
        _link?.Post("orbit.file.offer", new JsonObject { ["id"] = t.Id, ["name"] = t.Name, ["size"] = t.Size });

    void ResumeTransfers()
    {
        List<Transfer> list = new();
        Ui.Run(() =>
        {
            list = Transfers.Where(t => t.Outgoing && t.State is TransferState.Queued or TransferState.Paused).ToList();
            foreach (var t in list) Offer(t);
        });
    }

    void OnFileAccept(JsonObject b)
    {
        var id = b["id"]?.GetValue<string>();
        var offset = b["offset"]?.GetValue<long>() ?? 0;
        Ui.Run(() =>
        {
            var t = Transfers.FirstOrDefault(x => x.Id == id && x.Outgoing);
            if (t == null || !t.IsActive) return;
            _ = Task.Run(() => PushAsync(t, offset));
        });
    }

    async Task PushAsync(Transfer t, long offset)
    {
        var cts = Track(t.Id);
        try
        {
            Ui.Run(() => { t.State = TransferState.Running; t.Done = offset; });
            using var conn = await OpenDataAsync(t.Id, "push", offset, cts.Token);
            await using var fs = new FileStream(t.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, true);
            fs.Seek(offset, SeekOrigin.Begin);
            await Pump(fs, conn.ssl, t, offset, t.Size, cts.Token);
            await conn.ssl.FlushAsync(cts.Token);
            // Completion is confirmed by the phone's "orbit.file.done".
        }
        catch (Exception ex) { Fail(t, ex, cts); }
        finally { Untrack(t.Id, cts); }
    }

    // ---------------- phone → PC ----------------

    void OnFileOffer(JsonObject b)
    {
        var id = b["id"]!.GetValue<string>();
        var name = SafeName(b["name"]?.GetValue<string>() ?? "file");
        var size = b["size"]?.GetValue<long>() ?? 0;
        Ui.Run(() =>
        {
            var t = Transfers.FirstOrDefault(x => x.Id == id);
            if (t == null)
            {
                t = new Transfer { Id = id, Name = name, Size = size, Outgoing = false, State = TransferState.Queued };
                Transfers.Insert(0, t);
            }
            else if (!t.IsActive) return; // already finished or cancelled
            if (Settings.AutoAcceptFiles || t.State is TransferState.Paused or TransferState.Running) Accept(t);
            else
            {
                t.State = TransferState.Waiting;
                Toasts.FileOffer(t, Phone.Name);
            }
        });
    }

    public void Accept(Transfer t)
    {
        Directory.CreateDirectory(Settings.SaveFolder);
        var part = PartPath(t.Id);
        var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (offset > t.Size) { File.Delete(part); offset = 0; }
        t.State = TransferState.Queued;
        t.Done = offset;
        _link?.Post("orbit.file.accept", new JsonObject { ["id"] = t.Id, ["offset"] = offset });
        _ = Task.Run(() => PullAsync(t, offset));
    }

    public void Decline(Transfer t)
    {
        t.State = TransferState.Cancelled;
        _link?.Post("orbit.file.cancel", new JsonObject { ["id"] = t.Id });
    }

    async Task PullAsync(Transfer t, long offset)
    {
        var cts = Track(t.Id);
        var part = PartPath(t.Id);
        try
        {
            Ui.Run(() => t.State = TransferState.Running);
            using (var conn = await OpenDataAsync(t.Id, "pull", offset, cts.Token))
            await using (var fs = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1 << 20, true))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                fs.SetLength(offset);
                await Pump(conn.ssl, fs, t, offset, t.Size, cts.Token);
            }
            var final = UniquePath(Path.Combine(Settings.SaveFolder, t.Name));
            File.Move(part, final);
            t.LocalPath = final;
            _link?.Post("orbit.file.done", new JsonObject { ["id"] = t.Id });
            Ui.Run(() => { t.Done = t.Size; t.State = TransferState.Done; TransferFinished?.Invoke(t); });
            Toasts.FileReceived(t, Phone.Name);
        }
        catch (Exception ex) { Fail(t, ex, cts); }
        finally { Untrack(t.Id, cts); }
    }

    // ---------------- shared ----------------

    void OnFileDone(JsonObject b)
    {
        var id = b["id"]?.GetValue<string>();
        Ui.Run(() =>
        {
            var t = Transfers.FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            t.Done = t.Size; t.State = TransferState.Done;
            TransferFinished?.Invoke(t);
        });
    }

    void OnFileCancel(JsonObject b)
    {
        var id = b["id"]?.GetValue<string>();
        Ui.Run(() =>
        {
            var t = Transfers.FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            t.State = TransferState.Cancelled;
            lock (_running) if (_running.TryGetValue(t.Id, out var c)) c.Cancel();
        });
    }

    public void Cancel(Transfer t)
    {
        t.State = TransferState.Cancelled;
        lock (_running) if (_running.TryGetValue(t.Id, out var c)) c.Cancel();
        _link?.Post("orbit.file.cancel", new JsonObject { ["id"] = t.Id });
        if (!t.Outgoing) try { File.Delete(PartPath(t.Id)); } catch { }
    }

    public void Retry(Transfer t)
    {
        if (t.Outgoing) { t.State = TransferState.Queued; Offer(t); }
        else Accept(t);
    }

    public void ClearFinished()
    {
        foreach (var t in Transfers.Where(t => !t.IsActive).ToList()) Transfers.Remove(t);
    }

    sealed class DataConn : IDisposable
    {
        public required System.Net.Sockets.TcpClient tcp;
        public required System.Net.Security.SslStream ssl;
        public void Dispose() { try { ssl.Dispose(); } catch { } try { tcp.Dispose(); } catch { } }
    }

    /// <summary>Open a byte channel to the phone: one header line, then raw file bytes.</summary>
    async Task<DataConn> OpenDataAsync(string id, string dir, long offset, CancellationToken ct)
    {
        var link = _link ?? throw new IOException("not connected");
        var fp = Settings.Phone?.Fingerprint ?? throw new IOException("not paired");
        var (tcp, ssl, _) = await Proto.DialAsync(link.Host, link.Port, f => f == fp, 5000, ct);
        var header = Encoding.UTF8.GetBytes(Proto.Encode("orbit.data", new JsonObject
        {
            ["id"] = id, ["dir"] = dir, ["offset"] = offset, ["pc"] = Settings.DeviceId
        }));
        await ssl.WriteAsync(header, ct);
        await ssl.FlushAsync(ct);
        return new DataConn { tcp = tcp, ssl = ssl };
    }

    static async Task Pump(Stream from, Stream to, Transfer t, long offset, long size, CancellationToken ct)
    {
        var buf = new byte[256 * 1024];
        long done = offset, lastBytes = offset;
        var clock = Stopwatch.StartNew();
        double lastTick = 0;
        while (done < size)
        {
            int want = (int)Math.Min(buf.Length, size - done);
            int n = await from.ReadAsync(buf.AsMemory(0, want), ct);
            if (n <= 0) throw new IOException("connection closed early");
            await to.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            var now = clock.Elapsed.TotalSeconds;
            if (now - lastTick >= 0.4 || done == size)
            {
                var speed = (done - lastBytes) / Math.Max(0.001, now - lastTick);
                lastTick = now; lastBytes = done;
                var d = done;
                Ui.Run(() => { t.Done = d; t.Speed = speed; });
            }
        }
        await to.FlushAsync(ct);
    }

    void Fail(Transfer t, Exception ex, CancellationTokenSource cts)
    {
        Log.Write($"transfer {t.Name}: {ex.Message}");
        Ui.Run(() =>
        {
            if (t.State == TransferState.Cancelled) return;
            if (cts.IsCancellationRequested) { t.State = TransferState.Cancelled; return; }
            // Lost connection → resume automatically on reconnect. Anything else → failed.
            if (_link == null || ex is IOException) t.State = TransferState.Paused;
            else { t.Error = ex.Message; t.State = TransferState.Failed; }
        });
    }

    CancellationTokenSource Track(string id)
    {
        var c = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        lock (_running) _running[id] = c;
        return c;
    }

    void Untrack(string id, CancellationTokenSource c)
    {
        lock (_running) if (_running.TryGetValue(id, out var cur) && cur == c) _running.Remove(id);
        c.Dispose();
    }

    string PartPath(string id) => Path.Combine(Settings.SaveFolder, $".orbit-{id}.part");

    static string SafeName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "file" : name;
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
