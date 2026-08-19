using System.Collections.Generic;

namespace MSLX.SDK.Models.Resources
{
    public enum DependencyType
    {
        Required = 0,
        Optional = 1,
        Incompatible = 2,
        Embedded = 3
    }

    public enum DependencyMatchStatus
    {
        ExactMatch = 0,
        MultipleMatches = 1,
        NotFound = 2,
        Embedded = 3,
        AlreadyInstalled = 4
    }

    public class ResourceDependency
    {
        public string ProjectId { get; set; }
        public string VersionId { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string IconUrl { get; set; }
        public DependencyType Type { get; set; }
        public ResourceProviderType Provider { get; set; }
        
        // 解析状态
        public DependencyMatchStatus MatchStatus { get; set; }
        public string StatusMessage { get; set; }
        
        // 自动推荐匹配的版本
        public ResourceVersion SelectedVersion { get; set; }
        
        // 供下拉选择的候选项
        public List<ResourceVersion> CandidateVersions { get; set; } = new List<ResourceVersion>();
    }
}
