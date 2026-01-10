using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Services
{
    public class SmartContentExtractor : IDisposable
    {
        private readonly HttpClient _tikaHttp;
        private readonly HttpClient _fopHttp;

        private static readonly string[] FoMimeTypes =
            { "application/xslfo", "text/xslfo", "application/vnd.xslfo+xml" };

        public SmartContentExtractor(string tikaBaseUrl, string fopBaseUrl)
        {
            _tikaHttp = new HttpClient { BaseAddress = new Uri(tikaBaseUrl) };
            _fopHttp = new HttpClient { BaseAddress = new Uri(fopBaseUrl) };
        }

        public void Dispose()
        {
            _tikaHttp?.Dispose();
            _fopHttp?.Dispose();
        }

        public async Task<string> ExtractContentAsync(
            Stream input,
            string? password = null,
            CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var (stream, owned) = await EnsureSeekableAsync(input, ct);
            try
            {
                stream.Position = 0;
                bool isFo = await LooksLikeFoAsync(stream, ct);

                if (isFo)
                {
                    stream.Position = 0;
                    using var pdfStream = await ConvertFoToPdfAsync(stream, ct);
                    pdfStream.Position = 0;

                    var (metaFo, textFo) = await ParseMetadataAndContentAsync(pdfStream, password, ct);

                    var result = new
                    {
                        metadata = metaFo,
                        content = textFo
                    };

                    return JsonSerializer.Serialize(result);
                }

                stream.Position = 0;
                var (metaNormal, textNormal) = await ParseMetadataAndContentAsync(stream, password, ct);
                var normalResult = new
                {
                    metadata = metaNormal,
                    content = textNormal
                };
                return JsonSerializer.Serialize(normalResult);
            }
            finally
            {
                if (owned)
                    CleanupTemp(stream);
            }
        }

        private async Task<(Dictionary<string, string> meta, string text)>
            ParseMetadataAndContentAsync(Stream stream, string? password, CancellationToken ct)
        {
            string path = "rmeta/text";
            if (password != null)
                path += $"?password={Uri.EscapeDataString(password)}";

            using var req = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = new StreamContent(stream)
            };

            using var resp = await _tikaHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            string jsonResponse = await resp.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                JsonElement first = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement[0]
                    : doc.RootElement;

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string content = string.Empty;

                foreach (var prop in first.EnumerateObject())
                {
                    if (prop.Name.Contains("content", StringComparison.OrdinalIgnoreCase))
                        content = prop.Value.ToString();
                    else
                        metadata[prop.Name] = prop.Value.ToString();
                }

                return (metadata, content);
            }
            catch
            {
                return (new Dictionary<string, string>(), jsonResponse);
            }
        }

        private async Task<Stream> ConvertFoToPdfAsync(Stream stream, CancellationToken ct)
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(stream);
            content.Add(streamContent, "file", "document.fo");

            using var resp = await _fopHttp.PostAsync("convert", content, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errorBody = await resp.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"FOP conversion failed with status {resp.StatusCode}. Details: {errorBody}");
            }

            var ms = new MemoryStream();
            await resp.Content.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        }

        private async Task<bool> LooksLikeFoAsync(Stream stream, CancellationToken ct)
        {
            long original = stream.Position;
            var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                int read = await stream.ReadAsync(buffer, ct);
                var head = Encoding.UTF8.GetString(buffer, 0, read).ToLowerInvariant();
                return head.Contains("<fo:root") || head.Contains("xmlns:fo") ||
                       head.Contains("w3.org/1999/xsl/format");
            }
            finally
            {
                stream.Position = original;
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task<(Stream stream, bool owned)> EnsureSeekableAsync(Stream input, CancellationToken ct)
        {
            if (input.CanSeek) return (input, false);

            string tmp = Path.GetTempFileName();
            var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite,
                                    FileShare.None, 81920, true);
            await input.CopyToAsync(fs, 81920, ct);
            fs.Position = 0;
            return (fs, true);
        }

        private void CleanupTemp(Stream stream)
        {
            if (stream is FileStream fs)
            {
                string path = fs.Name;
                fs.Dispose();
                try { File.Delete(path); } catch { /* Ignore cleanup errors */ }
            }
        }
    }
}