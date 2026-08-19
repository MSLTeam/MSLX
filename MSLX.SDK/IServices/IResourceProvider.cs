using System.Collections.Generic;
using System.Threading.Tasks;
using MSLX.SDK.Models.Resources;

namespace MSLX.SDK.IServices
{
    public interface IResourceProvider
    {
        ResourceProviderType ProviderType { get; }
        
        Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter);
        
        Task<Resource> GetResourceAsync(string id);
        
        Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, string gameVersion = null, string loader = null);
    }
}
