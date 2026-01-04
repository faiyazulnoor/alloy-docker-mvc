using EPiServer.PlugIn;
using EPiServer.Scheduler;
using EPiServer.Core;
using EPiServer.Web;
using EPiServer.Framework.Blobs;
using System.Text.Json;
using alloy_docker_mvc.Models;
using alloy_docker_mvc.Services;
using EPiServer.Web.Routing;

namespace alloy_docker_mvc.ScheduledJobs
{
    [ScheduledPlugIn(
        DisplayName = "Index FO and PDF Documents to Solr",
        Description = "Searches for FO and PDF files in specified media folder and indexes them to Solr",
        SortIndex = 100)]
    public class SolrDocumentIndexingJob : ScheduledJobBase
    {
        private readonly IContentRepository _contentRepository;
        private readonly SolrIndexService _solrIndexService;
        private readonly SmartContentExtractor _contentExtractor;
        private readonly IConfiguration _configuration;
        private readonly IContentLoader _contentLoader;
        private readonly IUrlResolver _urlResolver;
        private bool _stopSignaled;

        public SolrDocumentIndexingJob(
            IContentRepository contentRepository,
            IConfiguration configuration,
            SolrIndexService solrIndexService,
            SmartContentExtractor contentExtractor,
            IContentLoader contentLoader,
            IUrlResolver urlResolver)
        {
            _contentRepository = contentRepository;
            _configuration = configuration;
            _solrIndexService = solrIndexService;
            _contentExtractor = contentExtractor;
            _contentLoader = contentLoader;
            _urlResolver = urlResolver;

            IsStoppable = true;
        }

        public override string Execute()
        {
            var statusMessage = new System.Text.StringBuilder();
            int processedCount = 0;
            int errorCount = 0;

            try
            {
                OnStatusChanged($"Starting document indexing job at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                string mediaFolderPath = _configuration["DocumentIndexing:MediaFolderPath"] ?? "Documents";

                OnStatusChanged($"Searching for documents in folder: {mediaFolderPath}");

                var mediaFolder = FindMediaFolder(mediaFolderPath);
                if (mediaFolder == null)
                {
                    return $"Error: Media folder '{mediaFolderPath}' not found. Please create this folder in Global Assets.";
                }

                var documents = GetDocumentsRecursively(mediaFolder.ContentLink);

                OnStatusChanged($"Found {documents.Count} FO/PDF documents to process");

                if (documents.Count == 0)
                {
                    return $"No FO or PDF files found in folder '{mediaFolderPath}'";
                }

                foreach (var document in documents)
                {
                    if (_stopSignaled)
                    {
                        return $"Job stopped by user. Processed: {processedCount}, Errors: {errorCount}";
                    }

                    try
                    {
                        ProcessDocumentAsync(document).Wait();
                        processedCount++;
                        OnStatusChanged($"✓ Processed: {document.Name} ({processedCount}/{documents.Count})");
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        var errorMsg = $"✗ Error processing {document.Name}: {ex.Message}";
                        OnStatusChanged(errorMsg);
                        statusMessage.AppendLine(errorMsg);
                    }
                }

                OnStatusChanged("Optimizing Solr index...");
                _solrIndexService.OptimizeAsync().Wait();

                string result = $"✓ Job completed! Processed: {processedCount}, Errors: {errorCount}";
                if (statusMessage.Length > 0)
                {
                    result += Environment.NewLine + Environment.NewLine + "Errors:" + Environment.NewLine + statusMessage.ToString();
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"✗ Job failed: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
            }
        }

        private ContentFolder FindMediaFolder(string folderPath)
        {
            try
            {
                var siteDefinition = SiteDefinition.Current;
                if (siteDefinition?.GlobalAssetsRoot == null)
                {
                    OnStatusChanged("Warning: GlobalAssetsRoot not found");
                    return null;
                }

                var globalAssetsRoot = _contentLoader.Get<ContentFolder>(siteDefinition.GlobalAssetsRoot);

                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    return globalAssetsRoot;
                }

                var pathParts = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                ContentReference currentRef = globalAssetsRoot.ContentLink;

                foreach (var part in pathParts)
                {
                    var children = _contentLoader.GetChildren<ContentFolder>(currentRef);
                    var folder = children.FirstOrDefault(f =>
                        f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));

                    if (folder == null)
                    {
                        OnStatusChanged($"Folder '{part}' not found in path");
                        return null;
                    }

                    currentRef = folder.ContentLink;
                }

                return _contentLoader.Get<ContentFolder>(currentRef);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error finding media folder: {ex.Message}");
                return null;
            }
        }

        private List<MediaData> GetDocumentsRecursively(ContentReference folderRef)
        {
            var documents = new List<MediaData>();
            var allowedExtensions = new[] { ".fo", ".pdf" };

            void TraverseFolder(ContentReference reference)
            {
                try
                {
                    var children = _contentLoader.GetChildren<IContent>(reference);

                    foreach (var child in children)
                    {
                        if (_stopSignaled) break;

                        if (child is ContentFolder folder)
                        {
                            TraverseFolder(folder.ContentLink);
                        }
                        else if (child is MediaData media)
                        {
                            var extension = Path.GetExtension(media.Name)?.ToLowerInvariant();
                            if (allowedExtensions.Contains(extension))
                            {
                                documents.Add(media);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnStatusChanged($"Error traversing folder: {ex.Message}");
                }
            }

            TraverseFolder(folderRef);
            return documents;
        }

        private async Task ProcessDocumentAsync(MediaData document)
        {
            var blob = document.BinaryData;
            if (blob == null)
            {
                throw new Exception("Document has no binary data");
            }

            using var stream = blob.OpenRead();

            string json = await _contentExtractor.ExtractContentAsync(stream);

            var parsed = JsonSerializer.Deserialize<DocumentExtract>(json);

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.content))
            {
                throw new Exception("No content extracted from document");
            }

            string docType = Path.GetExtension(document.Name)?.ToLowerInvariant() == ".fo"
                ? "application/xslfo"
                : "application/pdf";

            var url = _urlResolver.GetUrl(document.ContentLink);

            // Get file size - Blob doesn't have Length property directly
            long fileSize = 0;
            try
            {
                using (var sizeStream = blob.OpenRead())
                {
                    fileSize = sizeStream.Length;
                }
            }
            catch
            {
                // If we can't get the size, just use 0
                fileSize = 0;
            }

            var solrDoc = new SolrDocument
            {
                Id = document.ContentGuid.ToString(),
                Title = document.Name,
                DocType = docType,
                Content = parsed.content,
                Url = url,
                Modified = document.Changed,
                Created = document.Created,
                FileSize = fileSize
            };

            await _solrIndexService.AddOrUpdateDocumentAsync(solrDoc);
        }

        public override void Stop()
        {
            _stopSignaled = true;
            OnStatusChanged("Stop signal received. Finishing current document...");
            base.Stop();
        }
        
    }
}