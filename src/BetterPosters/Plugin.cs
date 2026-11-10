using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.BetterPosters.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

[assembly: Guid(Jellyfin.Plugin.BetterPosters.Plugin.PluginGuidString)]

namespace Jellyfin.Plugin.BetterPosters;

/// <summary>
/// Entry point for the Better Posters Jellyfin 12 plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Brand-new, independent plugin GUID for Better Posters (Jellyfin 12).
    /// </summary>
    public const string PluginGuidString = "4c7a9d3e-6f12-4b88-9e41-2a5d8c0f7b19";

    /// <summary>
    /// Canonical display name of the plugin and remote image provider.
    /// </summary>
    public const string PluginName = "Better Posters";

    private static readonly Guid PluginGuid = Guid.Parse(PluginGuidString);

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the active singleton instance of the plugin when loaded in a Jellyfin host.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => PluginName;

    /// <inheritdoc />
    public override string Description => "Customizable primary posters for Movies and Series from btttr.cc (Jellyfin 12).";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is not PluginConfiguration typedConfig)
        {
            throw new ArgumentException($"Expected configuration of type {nameof(PluginConfiguration)}.", nameof(configuration));
        }

        // Validate before mutating or persisting so invalid settings never overwrite safe existing configuration.
        typedConfig.ValidateOrThrow();

        base.UpdateConfiguration(typedConfig);
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    typeof(Plugin).Namespace)
            }
        ];
    }
}
