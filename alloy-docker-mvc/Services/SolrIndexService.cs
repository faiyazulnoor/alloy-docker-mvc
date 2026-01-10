using alloy_docker_mvc.Models;
using SolrNet;

namespace Services
{
    public class SolrIndexService
    {
        private readonly ISolrOperations<SolrDocument> _solr;

        public SolrIndexService(ISolrOperations<SolrDocument> solr)
        {
            _solr = solr;
        }

        public async Task AddOrUpdateDocumentAsync(SolrDocument doc)
        {
            await _solr.AddAsync(doc);
            await _solr.CommitAsync();
        }

        public async Task AddOrUpdateDocumentsAsync(IEnumerable<SolrDocument> docs)
        {
            await _solr.AddRangeAsync(docs);
            await _solr.CommitAsync();
        }

        public async Task DeleteDocumentAsync(string id)
        {
            await _solr.DeleteAsync(id);
            await _solr.CommitAsync();
        }

        public async Task<ICollection<SolrDocument>> SearchAsync(string query, int rows = 10)
        {
            var results = await _solr.QueryAsync(query, new SolrNet.Commands.Parameters.QueryOptions
            {
                Rows = rows
            });
            return results;
        }

        public async Task OptimizeAsync()
        {
            await _solr.OptimizeAsync();
        }

        public async Task DeleteAllAsync()
        {
            await _solr.DeleteAsync(SolrQuery.All);
            await _solr.CommitAsync();
        }
    }
}