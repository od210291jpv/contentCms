namespace ContentCms.API.Controllers
{
    public partial class ContentController
    {
        public class PagedResult<T>
        {
            public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
            public int TotalCount { get; set; }
            public int Page { get; set; }
            public int PageSize { get; set; }
            public int TotalPages { get; set; }
        }
    }
}
