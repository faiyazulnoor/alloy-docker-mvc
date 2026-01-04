using SolrNet.Attributes;

namespace alloy_docker_mvc.Models
{
    public class SolrDocument
    {
        [SolrUniqueKey("id")]
        public string Id { get; set; }

        [SolrField("content")]
        public string Content { get; set; }

        [SolrField("doctype")]
        public string DocType { get; set; }

        [SolrField("title")]
        public string Title { get; set; }

        [SolrField("url")]
        public string Url { get; set; }

        [SolrField("modified")]
        public DateTime? Modified { get; set; }

        [SolrField("created")]
        public DateTime? Created { get; set; }

        [SolrField("file_size")]
        public long? FileSize { get; set; }
    }
}