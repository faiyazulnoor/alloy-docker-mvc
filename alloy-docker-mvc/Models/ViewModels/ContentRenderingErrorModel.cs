namespace alloy_docker_mvc.Models.ViewModels
{
    public class ContentRenderingErrorModel
    {
        public ContentRenderingErrorModel(IContentData contentData, Exception exception)
        {
            if (contentData is IContent content)
            {
                ContentName = content.Name;
            }
            else
            {
                ContentName = string.Empty;
            }

            ContentTypeName = contentData.GetOriginalType().Name;

            Exception = exception;
        }

        public string ContentName { get; set; }

        public string ContentTypeName { get; set; }

        public Exception Exception { get; set; }
    }
}
