namespace Terminal.Plugins.Sdk;

/// <summary>
/// Marker contract for terminal plugins. Intentionally minimal for now — the plugin
/// host, discovery and lifecycle land in Phase 5 (T-501). Kept here so the layering
/// (Core / UI / Plugins.Sdk / AI) is established from T-001 onward.
/// </summary>
public interface ITerminalPlugin
{
    /// <summary>Human-readable plugin name.</summary>
    string Name { get; }

    /// <summary>Semantic version of the plugin.</summary>
    string Version { get; }
}
