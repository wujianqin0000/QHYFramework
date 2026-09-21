namespace GameIntegration.Editor
{
    /// <summary>插件代码与宿主项目内容的唯一路径约定。</summary>
    public static class IntegrationProjectPaths
    {
        public const string PackageName = "com.wjq.qhy-framework";
        public const string PackageRoot = "Packages/" + PackageName;
        public const string PluginRoot = PackageRoot;
        public const string BootPrefabTemplate = PackageRoot + "/Runtime/Resources/Boot/BootUI.prefab";

        public const string ProjectRoot = "Assets/Game";
        public const string BootRoot = ProjectRoot + "/Boot";
        public const string BootPrefab = BootRoot + "/BootUI.prefab";
        public const string ConfigRoot = ProjectRoot + "/Config";
        public const string Settings = ConfigRoot + "/QHYFrameworkSettings.asset";
        public const string ContentRoot = ProjectRoot + "/Content";
        public const string CommonContent = ContentRoot + "/Common";
        public const string UIContent = ContentRoot + "/UI";
        public const string AudioContent = ContentRoot + "/Audio";
        public const string SceneContent = ContentRoot + "/Scenes";
        public const string MainScene = SceneContent + "/Main.unity";
        public const string GeneratedRoot = ProjectRoot + "/Generated";
        public const string GeneratedHotUpdate = GeneratedRoot + "/HotUpdate";
        public const string GeneratedAotMetadata = GeneratedRoot + "/AOTMetadata";
        public const string GeneratedQhyLinkRoot = GeneratedRoot + "/QHYLink";
        public const string GeneratedQhyLinkXml = GeneratedQhyLinkRoot + "/link.xml";
        public const string GeneratedDistributionRoot = GeneratedRoot + "/Distribution";
        public const string GeneratedDistributionResources = GeneratedDistributionRoot + "/Resources";
        public const string GeneratedDistributionConfig = GeneratedDistributionResources +
                                                           "/QHYDistributionConfig.json";
        public const string HotUpdateScripts = ProjectRoot + "/Scripts/HotUpdate";

        public const string BootScene = "Assets/Scenes/Boot.unity";
    }
}
