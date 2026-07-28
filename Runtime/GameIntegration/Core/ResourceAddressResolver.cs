using System;

namespace GameIntegration
{
    public sealed class ResourceAddressResolver : IResourceAddressResolver
    {
        public string Resolve(string originalName, string ownerBundle, Type assetType)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                throw new ArgumentException("资源名不能为空。", nameof(originalName));

            var address = originalName.Trim().Replace('\\', '/');
            const string resourcesPrefix = "Resources/";
            if (address.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
                address = address.Substring(resourcesPrefix.Length);

            var extensionIndex = address.LastIndexOf('.');
            var slashIndex = address.LastIndexOf('/');
            if (extensionIndex > slashIndex)
                address = address.Substring(0, extensionIndex);

            return address;
        }
    }
}
