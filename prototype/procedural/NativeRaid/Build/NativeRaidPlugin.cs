using BepInEx;

namespace Contour4.NativeRaid;

[BepInPlugin(PluginId, "Contour-4 Procedural", "0.1.0")]
[BepInDependency("REPOLib", "4.5.1")]
public sealed class NativeRaidPlugin : BaseUnityPlugin
{
    public const string PluginId = "spacejourney.contour4.procedural";
    public const string LevelName = NativeRaidFeature.LevelName;
    private NativeRaidFeature feature;

    private void Awake() => feature = NativeRaidFeature.Attach(Config, Logger, PluginId);

    private void OnDestroy() => feature?.Dispose();
}
