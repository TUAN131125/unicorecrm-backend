using System.Diagnostics;
using System.Text;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

/// <summary>A killed/cancelled render cannot leave an unbounded background task in the API process.</summary>
internal sealed class WorkerQuotePdfRenderer : IQuotePdfRenderer
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    internal const long MemoryLimit = 256L * 1024 * 1024;
    public async Task<QuotePdfRenderResult> RenderAsync(string snapshotJson, CancellationToken ct)
    {
        var input = Encoding.UTF8.GetBytes(snapshotJson);
        if (input.Length > 8 * 1024 * 1024) throw new QuotePdfException("RENDER_INPUT_LIMIT");
        if (!await Slots.WaitAsync(0, ct)) throw new QuotePdfException("RENDER_CAPACITY");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process(); var watch = Stopwatch.StartNew(); long peak = 0;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "QuotePdfWorker", "UnicoreCRM.QuotePdfWorker.dll");
            if (!File.Exists(path)) throw new QuotePdfException("BLOCKED_CONTRACT_RENDERER");
            process.StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            process.StartInfo.ArgumentList.Add(path);
            process.StartInfo.Environment["DOTNET_GCHeapHardLimit"] = "0x8000000"; // 128 MiB managed heap, independent of sampled process working set.
            process.StartInfo.Environment["DOTNET_GCServer"] = "0";
            // Font/parser libraries inherit only server configuration, never caller-provided paths or arguments.
            if (!process.Start()) throw new QuotePdfException("RENDER_START_FAILED");
            using var cancellation = timeout.Token.Register(() => Kill(process));
            var read = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token); process.StandardInput.Close();
            while (!process.HasExited)
            {
                // The worker can exit between HasExited and the process metrics read.
                // A completed render must still have its output/exit code validated below.
                try { process.Refresh(); peak = Math.Max(peak, process.WorkingSet64); }
                catch (InvalidOperationException) when (process.HasExited) { break; }
                if (peak > MemoryLimit) { Kill(process); throw new QuotePdfException("RENDER_MEMORY_LIMIT"); }
                await Task.Delay(25, timeout.Token);
            }
            var bytes = await read; await errors;
            if (process.ExitCode != 0 || bytes.Length < 9) throw new QuotePdfException(process.ExitCode == 22 ? "RENDER_DOCUMENT_LIMIT" : "RENDER_FAILED");
            var pages = BitConverter.ToInt32(bytes, 0); var pdf = bytes[4..];
            if (pages is < 1 or > 100 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new QuotePdfException("RENDER_INVALID_PDF");
            return new(pdf, pages, watch.ElapsedMilliseconds, peak);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new QuotePdfException("RENDER_TIMEOUT"); }
        finally { Kill(process); Slots.Release(); }
    }
    private static void Kill(Process process)
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var data = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        { if (data.Length + count > 10485764) throw new QuotePdfException("RENDER_FILE_LIMIT"); await data.WriteAsync(buffer.AsMemory(0, count), ct); }
        return data.ToArray();
    }
}
