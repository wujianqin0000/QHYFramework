using System;
using YooAsset;

namespace GameIntegration
{
    public enum StartupState
    {
        Idle,
        CheckingClientUpdate,
        ClientUpdateAvailable,
        DownloadingClient,
        VerifyingClient,
        InstallingClient,
        Initializing,
        CheckingVersion,
        LoadingManifest,
        WaitingForDownloadConfirmation,
        Downloading,
        ClearingCache,
        LoadingMetadata,
        LoadingHotUpdateAssemblies,
        LoadingStartupScene,
        Completed,
        Failed
    }

    public readonly struct StartupProgress
    {
        public readonly StartupState State;
        public readonly float Progress;
        public readonly int CurrentFiles;
        public readonly int TotalFiles;
        public readonly long CurrentBytes;
        public readonly long TotalBytes;
        public readonly string Message;

        public StartupProgress(StartupState state, float progress, int currentFiles, int totalFiles,
            long currentBytes, long totalBytes, string message)
        {
            State = state;
            Progress = progress;
            CurrentFiles = currentFiles;
            TotalFiles = totalFiles;
            CurrentBytes = currentBytes;
            TotalBytes = totalBytes;
            Message = message ?? string.Empty;
        }
    }

    public interface IResourceAddressResolver
    {
        string Resolve(string originalName, string ownerBundle, Type assetType);
    }

    public sealed class StartupException : Exception
    {
        public StartupState Stage { get; }

        public StartupException(StartupState stage, string message) : base(message)
        {
            Stage = stage;
        }
    }

    internal static class YooOperation
    {
        public static void EnsureSucceeded(AsyncOperationBase operation, StartupState stage, string action)
        {
            if (operation.Status != EOperationStatus.Succeeded)
                throw new StartupException(stage, $"{action}失败：{operation.Error}");
        }

        public static void EnsureSucceeded(HandleBase handle, StartupState stage, string action)
        {
            if (handle.Status != EOperationStatus.Succeeded)
                throw new StartupException(stage, $"{action}失败：{handle.Error}");
        }
    }
}
