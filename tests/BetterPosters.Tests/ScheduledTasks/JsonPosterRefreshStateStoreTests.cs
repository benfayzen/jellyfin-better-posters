using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BetterPosters.Http;
using Jellyfin.Plugin.BetterPosters.ScheduledTasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests.ScheduledTasks;

public sealed class JsonPosterRefreshStateStoreTests
{
    [Fact]
    public async Task FlushAsync_PersistsAndReloadsFingerprintsAcrossInstances()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "bp-statestore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var paths = new StubPaths(tempRoot);
            var itemId = Guid.NewGuid();
            const string expectedFingerprint = "bp:v1:movie:tt1375666:poster:tag=auto:lang=en:rs=avg";

            var store1 = new JsonPosterRefreshStateStore(paths, NullLogger<JsonPosterRefreshStateStore>.Instance);
            store1.RecordFingerprint(itemId, expectedFingerprint);
            await store1.FlushAsync(CancellationToken.None);

            var store2 = new JsonPosterRefreshStateStore(paths, NullLogger<JsonPosterRefreshStateStore>.Instance);
            var found = store2.TryGetRecordedFingerprint(itemId, out var loadedFingerprint);

            Assert.True(found);
            Assert.Equal(expectedFingerprint, loadedFingerprint);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void PluginServiceRegistrator_RegistersExpectedPluginServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var registrator = new PluginServiceRegistrator();
        registrator.RegisterServices(services, applicationHost: null!);

        Assert.Contains(services, d => d.ServiceType == typeof(IBtttrClient));
        Assert.Contains(services, d => d.ServiceType == typeof(IRemoteImageProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IScheduledTask));
        Assert.Contains(services, d => d.ServiceType == typeof(IPosterRefreshService));
    }

    private sealed class StubPaths : IApplicationPaths
    {
        public StubPaths(string root)
        {
            ProgramDataPath = root;
            WebPath = root;
            ProgramSystemPath = root;
            DataPath = root;
            ImageCachePath = root;
            PluginsPath = Path.Combine(root, "plugins");
            PluginConfigurationsPath = Path.Combine(root, "plugins", "configurations");
            LogDirectoryPath = root;
            ConfigurationDirectoryPath = root;
            SystemConfigurationFilePath = Path.Combine(root, "system.xml");
            CachePath = root;
            TempDirectory = root;
            VirtualDataPath = root;
            TrickplayPath = root;
            BackupPath = root;
        }

        public string ProgramDataPath { get; }
        public string WebPath { get; }
        public string ProgramSystemPath { get; }
        public string DataPath { get; }
        public string ImageCachePath { get; }
        public string PluginsPath { get; }
        public string PluginConfigurationsPath { get; }
        public string LogDirectoryPath { get; }
        public string ConfigurationDirectoryPath { get; }
        public string SystemConfigurationFilePath { get; }
        public string CachePath { get; }
        public string TempDirectory { get; }
        public string VirtualDataPath { get; }
        public string TrickplayPath { get; }
        public string BackupPath { get; }
        public void MakeSanityCheckOrThrow() { }
        public void CreateAndCheckMarker(string path, string markerName, bool recursive) { }
    }
}
