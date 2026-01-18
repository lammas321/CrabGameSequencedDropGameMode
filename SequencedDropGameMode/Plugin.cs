using BepInEx;
using BepInEx.IL2CPP;
using HarmonyLib;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace SequencedDropGameMode
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("lammas123.CustomGameModes")]
    [BepInDependency("lammas123.CrabDevKit")]
    public sealed class SequencedDropGameMode : BasePlugin
    {
        public override void Load()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            CustomGameModes.Api.RegisterCustomGameMode(new CustomGameModeSequencedDrop());

            Log.LogInfo($"Initialized [{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION}]");
        }
    }
}