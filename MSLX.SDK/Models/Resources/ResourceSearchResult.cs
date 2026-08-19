namespace MSLX.SDK.Models.Resources
{
    using System.Collections.Generic;

    public class ResourceSearchResult
    {
        public IEnumerable<Resource> Items { get; set; }
        public long TotalCount { get; set; }
    }
}
