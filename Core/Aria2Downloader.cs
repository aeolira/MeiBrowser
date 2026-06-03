using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ZstdNet;

namespace Core
{
    public class Aria2Downloader
    {
        private static readonly HttpClient http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public event Action<long, long>? DownloadProgressChanged;

        public async Task DownloadFilesAsync(
            List<SophonManifestAssetProperty> assets,
            string downloadUrlTemplate,
            IProgress<double> progress,
            string savePath,
            string aria2Path = "aria2c",
            int maxConcurrent = 64,
            int maxConnections = 16)
        {
            string tempDir = Path.Combine(savePath, ".aria2_temp");
            Directory.CreateDirectory(tempDir);

            var chunks = new List<(string url, string filename, SophonManifestAssetProperty asset, SophonManifestAssetChunk chunk)>();
            foreach (var asset in assets)
            {
                foreach (var c in asset.AssetChunks)
                {
                    string url = downloadUrlTemplate.Replace("$0", c.ChunkName);
                    chunks.Add((url, c.ChunkName, asset, c));
                }
            }

            var chunkNames = new HashSet<string>(chunks.Select(c => c.filename));
            foreach (var f in Directory.GetFiles(tempDir))
            {
                string name = Path.GetFileName(f);
                if (name.EndsWith(".aria2"))
                {
                    string baseName = name[..^6];
                    if (!chunkNames.Contains(baseName))
                        try { File.Delete(f); } catch { }
                }
                else if (name != "urls.txt")
                {
                    if (!chunkNames.Contains(name) && !File.Exists(Path.Combine(tempDir, name + ".aria2")))
                        try { File.Delete(f); } catch { }
                }
            }

            string inputFile = Path.Combine(tempDir, "urls.txt");
            await GenerateInputFileAsync(inputFile, chunks, tempDir);

            long totalCompressed = chunks.Sum(c => c.chunk.ChunkSize);
            long totalDecompressed = chunks.Sum(c => c.chunk.ChunkSizeDecompressed);
            Console.WriteLine($"Aria2: {chunks.Count} chunks, {Utils.FormatSize(totalCompressed)} compressed -> {Utils.FormatSize(totalDecompressed)} target");

            bool aria2Ok = await RunAria2Async(inputFile, tempDir, aria2Path, maxConcurrent, maxConnections, totalCompressed);

            if (!aria2Ok)
            {
                Console.WriteLine("Aria2 download had errors, falling back to HttpClient for failed chunks...");
                await FallbackDownloadFailedChunks(chunks, tempDir);
            }

            Console.WriteLine("Assembling files...");
            long assembled = 0;
            foreach (var asset in assets)
            {
                await AssembleFileAsync(asset, tempDir, savePath);
                assembled += asset.AssetSize;
                progress.Report(assembled);
            }

            try
            {
                Utils.DeleteDirectoryRobust(tempDir);
                Console.WriteLine("Aria2 temp dir cleaned up.");
            }
            catch
            {
                Console.WriteLine($"Warning: could not delete temp dir {tempDir}");
            }
        }

        public static async Task GenerateChunkInputFile(
            string inputFilePath,
            List<(SophonManifestAssetProperty asset, string downloadUrl)> assets,
            string tempDir)
        {
            string dirArg = FormatAria2Dir(tempDir);
            var lines = new List<string>();
            int chunkCount = 0;
            foreach (var (asset, downloadUrl) in assets)
            {
                if (asset.AssetChunks.Count == 0)
                {
                    string fileUrl = downloadUrl;
                    if (!fileUrl.EndsWith(asset.AssetName))
                        fileUrl = $"{downloadUrl}/{asset.AssetName}";
                    string chunkName = asset.AssetName.Replace('/', '_');
                    lines.Add(fileUrl);
                    lines.Add($"  out={chunkName}");
                    lines.Add(dirArg);
                    chunkCount++;
                }
                else
                {
                    foreach (var chunk in asset.AssetChunks)
                    {
                        string url = downloadUrl.Replace("$0", chunk.ChunkName);
                        lines.Add(url);
                        lines.Add($"  out={chunk.ChunkName}");
                        lines.Add(dirArg);
                        chunkCount++;
                    }
                }
            }
            await File.WriteAllLinesAsync(inputFilePath, lines);
            Console.WriteLine($"Generated aria2 input: {inputFilePath} ({chunkCount} chunks from {assets.Count} assets)");
        }

        private async Task GenerateInputFileAsync(
            string inputFilePath,
            List<(string url, string filename, SophonManifestAssetProperty, SophonManifestAssetChunk)> chunks,
            string tempDir)
        {
            string dirArg = FormatAria2Dir(tempDir);
            var lines = new List<string>();
            foreach (var (url, filename, _, _) in chunks)
            {
                lines.Add(url);
                lines.Add($"  out={filename}");
                lines.Add(dirArg);
            }
            await File.WriteAllLinesAsync(inputFilePath, lines);
            Console.WriteLine($"Generated aria2 input: {inputFilePath} ({chunks.Count} URLs)");
        }

        private static string FormatAria2Dir(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains(' ') ? $"  dir=\"{normalized}\"" : $"  dir={normalized}";
        }

        private async Task<bool> RunAria2Async(string inputFile, string tempDir, string aria2Path,
            int maxConcurrent, int maxConnections, long totalBytes)
        {
            var psi = new ProcessStartInfo
            {
                FileName = aria2Path,
                Arguments = BuildAria2Args(inputFile, maxConcurrent, maxConnections),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Console.WriteLine($"Running: {aria2Path} {psi.Arguments}");

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Console.WriteLine("Failed to start aria2c process.");
                    return false;
                }

                var dlSpeedRegex = new Regex(@"\[DL:(?<speed>[\d.]+)(?<unit>[KMGT]?)i?B\]");
                long currentSpeed = 0;
                var speedLock = new object();
                var cts = new CancellationTokenSource();

                var stdoutTask = Task.Run(() =>
                {
                    while (!proc.StandardOutput.EndOfStream)
                    {
                        string line = proc.StandardOutput.ReadLine();
                        if (line == null) continue;

                        var m = dlSpeedRegex.Match(line);
                        if (m.Success)
                        {
                            double speed = double.Parse(m.Groups["speed"].Value);
                            string unit = m.Groups["unit"].Value;
                            long speedBps = unit switch
                            {
                                "G" => (long)(speed * 1_073_741_824),
                                "M" => (long)(speed * 1_048_576),
                                "K" => (long)(speed * 1_024),
                                _ => (long)speed
                            };
                            lock (speedLock) { currentSpeed = speedBps; }
                        }

                        if (!string.IsNullOrWhiteSpace(line))
                            Console.WriteLine($"[aria2] {line}");
                    }
                }, cts.Token);

                var stderrTask = Task.Run(() =>
                {
                    while (!proc.StandardError.EndOfStream)
                    {
                        string line = proc.StandardError.ReadLine();
                        if (line != null && !string.IsNullOrWhiteSpace(line))
                            Console.WriteLine($"[aria2:err] {line}");
                    }
                }, cts.Token);

                var monitorTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        long downloaded = 0;
                        try
                        {
                            if (Directory.Exists(tempDir))
                            {
                                foreach (var f in Directory.GetFiles(tempDir))
                                {
                                    try { downloaded += new FileInfo(f).Length; } catch { }
                                }
                            }
                        }
                        catch { }

                        long speed;
                        lock (speedLock) { speed = currentSpeed; }

                        DownloadProgressChanged?.Invoke(downloaded, speed);

                        try { await Task.Delay(500, cts.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                });

                await proc.WaitForExitAsync();
                cts.Cancel();

                try { await Task.WhenAll(stdoutTask, stderrTask, monitorTask); } catch { }

                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"aria2c not found or failed: {ex.Message}");
                return false;
            }
        }

        private static string BuildAria2Args(string inputFile, int maxConcurrent, int maxConnections)
        {
            return string.Join(" ", [
                $"-i \"{inputFile}\"",
                $"-j {maxConcurrent}",
                $"-x {maxConnections}",
                $"-s {maxConnections}",
                "--continue=true",
                "--auto-file-renaming=false",
                "--allow-overwrite=true",
                "--file-allocation=none",
                "--max-connection-per-server=16",
                "--min-split-size=1M",
                "--console-log-level=warn",
                "--summary-interval=0",
                "--download-result=default",
                "--async-dns=true"
            ]);
        }

        private async Task FallbackDownloadFailedChunks(
            List<(string url, string filename, SophonManifestAssetProperty asset, SophonManifestAssetChunk chunk)> chunks,
            string tempDir)
        {
            var failed = new ConcurrentBag<(string url, string filename)>();
            await Parallel.ForEachAsync(chunks, async (item, _) =>
            {
                string filePath = Path.Combine(tempDir, item.filename);
                if (File.Exists(filePath) && new FileInfo(filePath).Length >= item.chunk.ChunkSize)
                    return;

                Console.WriteLine($"Fallback download: {item.filename}");
                try
                {
                    var data = await http.GetByteArrayAsync(item.url);
                    await File.WriteAllBytesAsync(filePath, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Failed: {ex.Message}");
                    failed.Add((item.url, item.filename));
                }
            });

            if (failed.Count > 0)
                Console.WriteLine($"{failed.Count} chunks failed even after fallback.");
        }

        private static async Task AssembleFileAsync(SophonManifestAssetProperty asset, string tempDir, string savePath)
        {
            string normalized = asset.AssetName.Replace('/', Path.DirectorySeparatorChar);
            if (!IsSafePath(normalized))
            {
                Console.WriteLine($"  Skipping unsafe asset path: {asset.AssetName}");
                return;
            }
            string filePath = Path.Combine(savePath, normalized);
            string fullPath = Path.GetFullPath(filePath);
            string fullSavePath = Path.GetFullPath(savePath);
            if (!fullPath.StartsWith(fullSavePath + Path.DirectorySeparatorChar) && fullPath != fullSavePath)
            {
                Console.WriteLine($"  Skipping path traversal attempt: {asset.AssetName}");
                return;
            }
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (asset.AssetChunks.Count == 0)
            {
                string chunkPath = Path.Combine(tempDir, asset.AssetName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(chunkPath))
                {
                    File.Move(chunkPath, filePath, true);
                }
                else
                {
                    Console.WriteLine($"  Missing data for unchunked asset: {asset.AssetName}");
                }
                return;
            }

            int missingChunks = 0;
            int failedDecompress = 0;
            int failedMd5 = 0;

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            fs.SetLength(asset.AssetSize);

            foreach (var chunk in asset.AssetChunks)
            {
                string chunkPath = Path.Combine(tempDir, chunk.ChunkName);
                if (!File.Exists(chunkPath))
                {
                    missingChunks++;
                    Console.WriteLine($"  Missing chunk: {chunk.ChunkName} for {asset.AssetName}");
                    continue;
                }

                byte[] compressed = await File.ReadAllBytesAsync(chunkPath);
                byte[] decompressed;

                try
                {
                    using var msIn = new MemoryStream(compressed);
                    using var msOut = new MemoryStream();
                    using var dctx = new DecompressionStream(msIn);
                    dctx.CopyTo(msOut);
                    decompressed = msOut.ToArray();
                }
                catch
                {
                    failedDecompress++;
                    Console.WriteLine($"  Decompression failed for chunk {chunk.ChunkName} in {asset.AssetName}");
                    continue;
                }

                if (decompressed.Length != (long)chunk.ChunkSizeDecompressed)
                {
                    failedDecompress++;
                    Console.WriteLine($"  Decompressed size mismatch for chunk {chunk.ChunkName} in {asset.AssetName}: expected {chunk.ChunkSizeDecompressed}, got {decompressed.Length}");
                    continue;
                }

                string actualMd5 = Utils.GetMd5(decompressed);
                if (actualMd5 != chunk.ChunkDecompressedHashMd5)
                {
                    failedMd5++;
                    Console.WriteLine($"  MD5 mismatch for chunk {chunk.ChunkName} in {asset.AssetName}");
                    continue;
                }

                fs.Seek((long)chunk.ChunkOnFileOffset, SeekOrigin.Begin);
                fs.Write(decompressed, 0, decompressed.Length);
            }

            fs.Flush();
            fs.Close();

            if (missingChunks > 0 || failedDecompress > 0 || failedMd5 > 0)
            {
                Console.WriteLine($"  Assembly errors for {asset.AssetName}: {missingChunks} missing, {failedDecompress} decompress, {failedMd5} md5");
            }

            string finalMd5 = Utils.GetMd5(await File.ReadAllBytesAsync(filePath));
            if (finalMd5 != asset.AssetHashMd5)
                Console.WriteLine($"  Final MD5 mismatch for {asset.AssetName}: expected {asset.AssetHashMd5}");
        }

        public static async Task AssembleOnlyAsync(
            List<SophonManifestAssetProperty> assets,
            string tempDir,
            IProgress<double> progress,
            string savePath)
        {
            int totalChunks = assets.Sum(a => a.AssetChunks.Count);
            int missing = 0;
            foreach (var asset in assets)
            {
                foreach (var chunk in asset.AssetChunks)
                {
                    if (!File.Exists(Path.Combine(tempDir, chunk.ChunkName)))
                        missing++;
                }
            }
            if (missing > 0)
                Console.WriteLine($"Warning: {missing}/{totalChunks} chunks are missing from {tempDir}");

            Console.WriteLine($"Assembling {assets.Count} files ({totalChunks} chunks) from {tempDir}...");
            long assembled = 0;
            int errors = 0;
            foreach (var asset in assets)
            {
                await AssembleFileAsync(asset, tempDir, savePath);
                assembled += asset.AssetSize;
                progress.Report(assembled);

                string filePath = Path.Combine(savePath, asset.AssetName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(filePath))
                {
                    string md5 = Utils.GetMd5(await File.ReadAllBytesAsync(filePath));
                    if (md5 != asset.AssetHashMd5)
                        errors++;
                }
                else
                {
                    errors++;
                }
            }
            if (errors > 0)
                Console.WriteLine($"Assembly complete with {errors}/{assets.Count} files failed verification.");
            else
                Console.WriteLine("Assembly complete — all files verified.");
        }

        private static bool IsSafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !parts.Any(p => p == ".." || p == ".");
        }
    }
}
