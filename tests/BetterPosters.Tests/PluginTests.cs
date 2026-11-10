using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.BetterPosters.Configuration;
using Jellyfin.Plugin.BetterPosters.Domain;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Xunit;

namespace Jellyfin.Plugin.BetterPosters.Tests;

public sealed class PluginTests
{
    [Fact]
    public void Plugin_HasDistinctGuidAndEmbeddedConfigResource()
    {
        using var tempDir = new TempDirectory();
        var plugin = new Plugin(new StubApplicationPaths(tempDir.Path), new StubXmlSerializer());

        Assert.Equal(Guid.Parse("4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19"), plugin.Id);
        Assert.NotEqual(Guid.Parse("862b7dbc-9485-41f2-a9d9-fc9ca66a6a51"), plugin.Id);
        Assert.NotEqual(Guid.Parse("b1ea6fb2-e42a-4632-841f-82ffba8307db"), plugin.Id);
        Assert.Equal("Better Posters", plugin.Name);

        var page = Assert.Single(plugin.GetPages());
        Assert.Equal("Better Posters", page.Name);
        Assert.Equal("Jellyfin.Plugin.BetterPosters.Configuration.configPage.html", page.EmbeddedResourcePath);

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
        Assert.NotNull(stream);
    }

    [Fact]
    public void UpdateConfiguration_InvalidSettings_RejectsAndPreservesExistingSafeConfiguration()
    {
        using var tempDir = new TempDirectory();
        var serializer = new StubXmlSerializer();
        var plugin = new Plugin(new StubApplicationPaths(tempDir.Path), serializer);

        var validConfig = new PluginConfiguration
        {
            Language = PosterLanguage.Japanese,
            RatingSource = PosterRatingSource.Imdb
        };
        plugin.UpdateConfiguration(validConfig);
        Assert.Equal(PosterLanguage.Japanese, plugin.Configuration.Language);

        var invalidConfig = new PluginConfiguration
        {
            Language = PosterLanguage.German,
            MaxConcurrency = 999
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => plugin.UpdateConfiguration(invalidConfig));
        Assert.Equal(PosterLanguage.Japanese, plugin.Configuration.Language);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failure in temp directory.
            }
        }
    }

    private sealed class StubApplicationPaths : IApplicationPaths
    {
        public StubApplicationPaths(string root)
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
            Directory.CreateDirectory(PluginsPath);
            Directory.CreateDirectory(PluginConfigurationsPath);
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

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string markerName, bool recursive)
        {
        }
    }

    private sealed class StubXmlSerializer : IXmlSerializer
    {
        private object? _lastSaved;

        public object DeserializeFromBytes(Type type, byte[] buffer) => Activator.CreateInstance(type)!;
        public object DeserializeFromFile(Type type, string file) => _lastSaved ?? Activator.CreateInstance(type)!;
        public object DeserializeFromStream(Type type, Stream stream) => Activator.CreateInstance(type)!;
        public void SerializeToFile(object obj, string file) => _lastSaved = obj;
        public void SerializeToStream(object obj, Stream stream) => _lastSaved = obj;
    }
}
