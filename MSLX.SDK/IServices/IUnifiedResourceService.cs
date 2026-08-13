using System.Collections.Generic;
using System.Threading.Tasks;
using MSLX.SDK.Models.Resources;

namespace MSLX.SDK.IServices
{
    public interface IUnifiedResourceService
    {
        Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter);
        
        Task<Resource> GetResourceAsync(string id, ResourceProviderType providerType);
        
        Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, ResourceProviderType providerType, string gameVersion = null, string loader = null);
    }
}
