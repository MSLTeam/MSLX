using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Services.ResourceServices;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Resources;
using Xunit;

namespace MSLX.Tests;

public class ModDictionaryAndSearchTests
{
    private class FakeResourceProvider : IResourceProvider
    {
        public ResourceProviderType ProviderType => ResourceProviderType.CurseForge;
        public List<Resource> ItemsToReturn { get; set; } = new();

        public Task<ResourceSearchResult> SearchAsync(ResourceSearchFilter filter)
        {
            return Task.FromResult(new ResourceSearchResult
            {
                Items = new List<Resource>(ItemsToReturn),
                TotalCount = ItemsToReturn.Count
            });
        }

        public Task<Resource> GetResourceAsync(string id, bool useMirror = true) => throw new System.NotImplementedException();
        public Task<IEnumerable<ResourceVersion>> GetVersionsAsync(string id, string? gameVersion = null, string? loader = null, bool useMirror = true) => throw new System.NotImplementedException();
    }

    [Theory]
    [InlineData("机械动力", "create")]
    [InlineData("机械动力模组", "create")]
    [InlineData("create", "create")]
    public void TranslateChineseQueryToEnglish_TranslatesProperly(string input, string expected)
    {
        var dictService = new ModDictionaryService(NullLogger<ModDictionaryService>.Instance);
        var actual = dictService.TranslateChineseQueryToEnglish(input);
        Assert.Equal(expected, actual, ignoreCase: true);
    }

    [Fact]
    public async Task SearchAsync_RanksExactTitleMatchAboveUnrelatedPopularMod_WhenSearchingChinese()
    {
        var dictService = new ModDictionaryService(NullLogger<ModDictionaryService>.Instance);

        // Fake CurseForge returning Xaero's Minimap (more downloads, mentions create in summary) and Create
        var fakeProvider = new FakeResourceProvider
        {
            ItemsToReturn = new List<Resource>
            {
                new Resource
                {
                    Id = "xaero-minimap",
                    Name = "Xaero's Minimap",
                    DownloadCount = 250_000_000,
                    Provider = ResourceProviderType.CurseForge,
                    Summary = "Creates a minimap in the corner of your screen."
                },
                new Resource
                {
                    Id = "create",
                    Name = "Create",
                    DownloadCount = 200_000_000,
                    Provider = ResourceProviderType.CurseForge,
                    Summary = "Welcome to Create, a mod offering a variety of tools and blocks for Automation."
                }
            }
        };

        var service = new UnifiedResourceService(new[] { fakeProvider }, dictService);
        var result = await service.SearchAsync(new ResourceSearchFilter
        {
            Query = "机械动力",
            Limit = 20,
            Offset = 0
        });

        var list = new List<Resource>(result.Items);
        Assert.Equal(2, list.Count);
        // Create should be translated and ranked #1 because its title matches "机械动力"
        Assert.Equal("机械动力", list[0].Name);
        Assert.Equal("create", list[0].Id);
        Assert.Equal("xaero-minimap", list[1].Id);
    }

    [Fact]
    public async Task SearchAsync_RanksExactTitleMatchAboveUnrelatedPopularMod_WhenSearchingEnglish()
    {
        var dictService = new ModDictionaryService(NullLogger<ModDictionaryService>.Instance);

        var fakeProvider = new FakeResourceProvider
        {
            ItemsToReturn = new List<Resource>
            {
                new Resource
                {
                    Id = "xaero-minimap",
                    Name = "Xaero's Minimap",
                    DownloadCount = 250_000_000,
                    Provider = ResourceProviderType.CurseForge,
                    Summary = "Creates a minimap in the corner of your screen."
                },
                new Resource
                {
                    Id = "create",
                    Name = "Create",
                    DownloadCount = 200_000_000,
                    Provider = ResourceProviderType.CurseForge,
                    Summary = "Welcome to Create, a mod offering a variety of tools and blocks for Automation."
                }
            }
        };

        var service = new UnifiedResourceService(new[] { fakeProvider }, dictService);
        var result = await service.SearchAsync(new ResourceSearchFilter
        {
            Query = "create",
            Limit = 20,
            Offset = 0
        });

        var list = new List<Resource>(result.Items);
        Assert.Equal(2, list.Count);
        // Create should be ranked #1 because its original title matches "create"
        Assert.Equal("create", list[0].Id);
        Assert.Equal("xaero-minimap", list[1].Id);
    }
}
