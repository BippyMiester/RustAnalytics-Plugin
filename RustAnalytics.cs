using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Facepunch;
using Facepunch.Extend;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;
using UnityEngine.Networking;
using System.Net.Sockets;
//Reference: 0Harmony
#if CARBON
    using HarmonyLib;
    using HInstance = HarmonyLib.Harmony;
#else
using Harmony;
    using HInstance = Harmony.HarmonyInstance;
#endif

namespace Oxide.Plugins
{
    [Info(_PluginName, _PluginAuthor, _PluginVersion)]
    [Description(_PluginDescription)]
    public class RustAnalytics : RustPlugin
    {

        // Plugin Metadata
        private const string _PluginName = "RustAnalytics";
        private const string _PluginAuthor = "BippyMiester";
        private const string _PluginVersion = "0.0.12";
        private const string _PluginDescription = "Official Plugin for RustAnalytics.com";
        private const string _DownloadLink = "INSERT_LINK_HERE";

        // Plugin References
        [PluginReference]
        Plugin RustAnalyticsPlaytimeTracker;

        // Permision Constants
        // public const string permCanSeeCSFVote = "easyvotepro.canseecsfvote";

        #region ChangeLog
        /*
         * 0.0.1
         * 
         * 
         */
        #endregion


        // Misc Variables
        private static RustAnalytics _pluginInstance;
        private SaveInfo _saveInfo = SaveInfo.Create(World.SaveFolderName + $"/player.blueprints.{Rust.Protocol.persistance}.db");
        private readonly Hash<ulong, Action<ClientPerformanceReport>> _clientPerformanceReports = new();

        // Coroutines
        private IEnumerator webhookCoroutine;
        private IEnumerator clientDataCoroutine;

        // Harmony Variables
        private HInstance _harmonyInstance;
        private string HarmonyId => $"com.{_PluginAuthor}.{_PluginName}";

        private void Init()
        {
            ConsoleLog($"{_PluginName} has been initialized...");
            LoadMessages();
            RegisterAllPermissions();
        }

        private void RegisterAllPermissions()
        {
            // permission.RegisterPermission(permCanSeeCSFVote, this);
        }

        private void OnServerInitialized()
        {
            _pluginInstance = this;
            PatchHarmony();
            StartGlobalTimers();
        }

        private void Loaded()
        {

        }

        private void Unload()
        {
            // Stop all coroutines
            if (webhookCoroutine != null) ServerMgr.Instance.StopCoroutine(webhookCoroutine);
            if (clientDataCoroutine != null) ServerMgr.Instance.StopCoroutine(clientDataCoroutine);

            UnpatchHarmony();
            _pluginInstance = null;
        }

        #region HelperFunctions
        
        private void GetPlayerPerformance(BasePlayer player, Action<ClientPerformanceReport> callback)
        {
            _clientPerformanceReports[player.userID] = callback;
            player.ClientRPCPlayer(null, player, "GetPerformanceReport", "lookup");
        }

        private string GetPlayerIPAddress(BasePlayer player)
        {
            String _ipPattern = @":{1}[0-9]{1}\d*";

            String Address = Regex.Replace(player.net.connection.ipaddress, _ipPattern, string.Empty);

            return Address;
        }

        private string GetPlayerOnlineTime(BasePlayer player)
        {
            string seconds = Convert.ToString(RustAnalyticsPlaytimeTracker.Call("GetPlayTime", player.UserIDString));

            if (seconds == null)
            {
                return "1";
            }
            return seconds;
        }

        private string GetPlayerAFKTime(BasePlayer player)
        {
            string seconds = Convert.ToString(RustAnalyticsPlaytimeTracker.Call("GetAFKTime", player.UserIDString));

            if (seconds == null)
            {
                return "1";
            }
            return seconds;
        }

        private Dictionary<string, string> GetServerData()
        {
            Dictionary<string, string> data = new Dictionary<string, string>();

            var worldSize = World.Size / 1000;
            var usedMemory = Math.Round((Performance.current.memoryUsageSystem * 1f) / 1024, 2);
            var maxMemory = Math.Round((UnityEngine.SystemInfo.systemMemorySize * 1f) / 1024, 2);
            var networkIn = Math.Round((Network.Net.sv.GetStat(null, Network.BaseNetwork.StatTypeLong.BytesReceived_LastSecond) * 1f) / 1024, 2);
            var networkOut = Math.Round((Network.Net.sv.GetStat(null, Network.BaseNetwork.StatTypeLong.BytesSent_LastSecond) * 1f) / 1024, 2);

            data["api_key"] = Configuration.General.APIToken;
            data["entities"] = $"{BaseNetworkable.serverEntities.Count}";
            data["world_seed"] = $"{World.Seed}";
            data["world_name"] = $"{World.Name}";
            data["players_online"] = $"{BasePlayer.activePlayerList.Count}";
            data["players_max"] = $"{ConVar.Server.maxplayers}";
            data["in_game_time"] = $"{TOD_Sky.Instance.Cycle.DateTime}";
            data["server_fps"] = $"{Performance.report.frameRate}";
            data["map_size"] = $"{worldSize}";
            data["protocol"] = $"{Rust.Protocol.network}";
            data["used_memory"] = $"{usedMemory}";
            data["max_memory"] = $"{maxMemory}";
            data["network_in"] = $"{networkIn}";
            data["network_out"] = $"{networkOut}";
            data["last_wiped"] = $"{SaveRestore.SaveCreatedTime}";
            data["blueprint_last_wiped"] = $"{_saveInfo.CreationTime}";

            _Debug($"Server Entities: {BaseNetworkable.serverEntities.Count}");
            _Debug($"World Seed: {World.Seed}");
            _Debug($"World Name: {World.Name}");
            _Debug($"Players Online: {BasePlayer.activePlayerList.Count}");
            _Debug($"Max Players Online: {ConVar.Server.maxplayers}");
            _Debug($"In-Game Time: {TOD_Sky.Instance.Cycle.DateTime}");
            _Debug($"Server FPS: {Performance.report.frameRate}");
            _Debug($"Map Size: {worldSize} km");
            _Debug($"Rust Protocol: {Rust.Protocol.network}");
            _Debug($"Used Memory: {usedMemory} Gb");
            _Debug($"Max Memory: {maxMemory} Gb");
            _Debug($"Network In: {networkIn} kb/s");
            _Debug($"Network Out: {networkOut} kb/s");
            _Debug($"Last Wiped: {SaveRestore.SaveCreatedTime}");
            _Debug($"Blueprint Wipe: {_saveInfo.CreationTime.ToString()}");

            return data;
        }

        private Dictionary<string, string> GetPlayerConnectionData(BasePlayer player, string type)
        {
            Dictionary<string, string> data = new Dictionary<string, string>();

            data["api_key"] = Configuration.General.APIToken;
            data["username"] = player.displayName;
            data["steam_id"] = player.UserIDString;
            data["ip_address"] = GetPlayerIPAddress(player);
            data["type"] = type;

            // Playtime Tracker
            if (!RustAnalyticsPlaytimeTracker)
            {
                ConsoleError($"RustMetricsPlaytimeTracker is not loaded, but you have tracking enabled. Download from here: {_DownloadLink}");
                data["online_seconds"] = "1";
                data["afk_seconds"] = "1";
            }
            else
            {
                data["online_seconds"] = GetPlayerOnlineTime(player);
                data["afk_seconds"] = GetPlayerAFKTime(player);
            }

            return data;
        }

        private Dictionary<string, string> GetPlayerClientData(ClientPerformanceReport clientPerformanceReport)
        {
            Dictionary<string, string> data = new Dictionary<string, string>();

            data["api_key"] = Configuration.General.APIToken;
            data["steam_id"] = clientPerformanceReport.user_id;
            data["frame_rate"] = clientPerformanceReport.fps.ToString();
            data["ping"] = clientPerformanceReport.ping.ToString();

            return data;
        }

        private Dictionary<string, string> GetPlayerBannedData(string name, string id, string address, string reason)
        {
            Dictionary<string, string> data = new Dictionary<string, string>();

            data["api_key"] = Configuration.General.APIToken;
            data["username"] = name;
            data["steam_id"] = id;
            data["ip_address"] = address;
            data["reason"] = reason;

            return data;
        }

        public void StartGlobalTimers()
        {
            // Call some functions before starting the timers so that they are immediately being collected.
            CreateServerData();

            ConsoleLog("Starting 10 minute timer...");
            timer.Every(600f, () =>
            {
                _Debug("Global Repeating Timer");
                CreateServerData();
            });

            ConsoleLog("Starting 60 second timer...");
            timer.Every(60f, () =>
            {
                // Start the getPlayerClientDataCoroutine
                clientDataCoroutine = getPlayerClientDataCoroutine();
                ServerMgr.Instance.StartCoroutine(clientDataCoroutine);
            });
        }

        #endregion
        
        #region Harmony Helpers

        private void PatchHarmony()
        {
            _Debug("Patching Harmony");
            #if CARBON
                _harmonyInstance = new HInstance(HarmonyId);
            #else
                _harmonyInstance = HInstance.Create(HarmonyId);
            #endif
            _harmonyInstance.PatchAll();
            _Debug("Patching Complete");
        }

        private void UnpatchHarmony()
        {
            _Debug("Unpatching Harmony");
            _harmonyInstance.UnpatchAll(HarmonyId);
            _harmonyInstance = null;
            _Debug("Harmony patches are now back to normal");
        }

        #endregion

        #region Harmony Patches

        [HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PerformanceReport))]
        private static class PerformanceReportPatch
        {
            public static bool Prefix(BasePlayer __instance, BaseEntity.RPCMessage msg)
            {
                string str1 = msg.read.String();
                string message = msg.read.StringRaw();
                ClientPerformanceReport performanceReport = JsonConvert.DeserializeObject<ClientPerformanceReport>(message);
                if (performanceReport.user_id != __instance.UserIDString)
                {
                    DebugEx.Log(string.Format("Client performance report from {0} has incorrect user_id ({1})",
                        __instance, __instance.UserIDString));
                }
                else
                {
                    switch (str1)
                    {
                        case "json":
                        {
                            DebugEx.Log(message);
                            break;
                        }
                        case "legacy":
                        {
                            string str2 = (performanceReport.memory_managed_heap + "MB").PadRight(9);
                            string str3 = (performanceReport.memory_system + "MB").PadRight(9);
                            string str4 = (performanceReport.fps.ToString("0") + "FPS").PadRight(8);
                            string str5 = ((long)performanceReport.fps).FormatSeconds().PadRight(9);
                            string str6 = __instance.UserIDString.PadRight(20);
                            string str7 = performanceReport.streamer_mode.ToString().PadRight(7);
                            DebugEx.Log(str2 + str3 + str4 + str5 + str7 + str6 + __instance.displayName);
                            break;
                        }
                        case "none":
                        {
                            break;
                        }
                        case "rcon":
                        {
                            RCon.Broadcast(RCon.LogType.ClientPerf, message);
                            break;
                        }
                        case "lookup":
                        {
                            _pluginInstance._clientPerformanceReports[__instance.userID]?.Invoke(performanceReport);
                            break;
                        }
                        default:
                        {
                            Debug.LogError("Unknown PerformanceReport format '" + str1 + "'");
                            break;
                        }
                    }
                }
            
                return false;
            }
        }

        #endregion

        #region Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            _Debug("------------------------------");
            _Debug("Method: OnPlayerConnected");
            _Debug($"Player: {player.displayName}/{player.UserIDString}");

            CreatePlayerConnectionData(player, "connect");

            _Debug("OnPlayerConnected End");
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            _Debug("------------------------------");
            _Debug("Method: OnPlayerDisconnected");
            _Debug($"Player: {player.displayName}/{player.UserIDString}");

            CreatePlayerConnectionData(player, "quit");

            _Debug("OnPlayerDisconnected End");
        }

        void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {

            Puts("OnItemCraftFinished was called!");
        }

        private void OnNewSave(string filename)
        {
            _Debug("------------------------------");
            _Debug("Method: OnNewSave");
            ConsoleLog("New map data detected!");
        }

        private void OnUserBanned(string name, string id, string address, string reason)
        {
            _Debug("------------------------------");
            _Debug("Method: OnUserBanned");
            _Debug($"Name: {name}");
            _Debug($"ID: {id}");
            _Debug($"Address: {address}");
            _Debug($"Reason: {reason}");

            CreatePlayerBannedData(name, id, address, reason);
        }

        

        #endregion

        #region Database Methods

        public void CreatePlayerConnectionData(BasePlayer player, string type)
        {
            var data = GetPlayerConnectionData(player, type);

            webhookCoroutine = WebhookSend(data, Configuration.API.PlayersConnectionRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        public void CreateServerData()
        {
            var data = GetServerData();

            webhookCoroutine = WebhookSend(data, Configuration.API.ServerDataRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreatePlayerData(ClientPerformanceReport clientPerformanceReport)
        {
            _Debug("------------------------------");
            _Debug("Method: OnPlayerConnected");
            _Debug($"Player: {clientPerformanceReport.user_id} | Framerate: {clientPerformanceReport.fps} | Ping: {clientPerformanceReport.ping}");

            var data = GetPlayerClientData(clientPerformanceReport);

            webhookCoroutine = WebhookSend(data, Configuration.API.PlayersDataRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreatePlayerBannedData(string name, string id, string address, string reason)
        {
            var data = GetPlayerBannedData(name, id, address, reason);

            webhookCoroutine = WebhookSend(data, Configuration.API.PlayerBanDataRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        #endregion

        #region ChatCommands



        #endregion

        #region ConsoleHelpers

        public void ConsoleLog(object message)
        {
            Puts(message?.ToString());
        }

        public void ConsoleError(string message)
        {
            if (Convert.ToBoolean(Configuration.General.LogToFile))
                LogToFile(_PluginName, $"ERROR: {message}", this);

            Debug.LogError($"***********************************************");
            Debug.LogError($"************* RUSTANALYTICS ERROR *************");
            Debug.LogError($"***********************************************");
            Debug.LogError($"[{_PluginName}] ERROR: " + message);
            Debug.LogError($"***********************************************");

        }

        public void ConsoleWarn(string message)
        {
            if (Convert.ToBoolean(Configuration.General.LogToFile))
                LogToFile(_PluginName, $"WARNING: {message}", this);

            Debug.LogWarning($"[{_PluginName}] WARNING: " + message);
        }

        public void _Debug(string message, string arg = null)
        {
            if (Configuration.General.DebugModeEnabled)
            {
                if (Convert.ToBoolean(Configuration.General.LogToFile))
                    LogToFile(_PluginName, $"DEBUG: {message}", this);

                Puts($"DEBUG: {message}");
                if (arg != null)
                {
                    Puts($"DEBUG ARG: {arg}");
                }
            }
        }

        #endregion

        #region ConsoleCommands

        [ConsoleCommand("test")]
        private void TestConsoleCommand(ConsoleSystem.Arg arg = null)
        {
            _Debug("------------------------------");
            _Debug("Method: TestConsoleCommand");

            var data = GetServerData();
            webhookCoroutine = WebhookSend(data, Configuration.API.ServerDataRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
            ConsoleLog("Sent");
        }

        [ConsoleCommand("testapi")]
        private void TestApiConsoleCommand(ConsoleSystem.Arg arg = null)
        {
            _Debug("------------------------------");
            _Debug("Method: TestApiConsoleCommand");

            Dictionary<string, string> data = new Dictionary<string, string>();
            data["api_key"] = Configuration.General.APIToken;
            data["username"] = "BippyMiester";
            data["steam_id"] = "1234567891234567";
            data["ip_address"] = "127.0.0.1";
            data["type"] = "quit";

            // Playtime Tracker
            if (!RustAnalyticsPlaytimeTracker)
            {
                ConsoleError($"RustMetricsPlaytimeTracker is not loaded, but you have tracking enabled. Download from here: {_DownloadLink}");
                data["online_seconds"] = "1";
                data["afk_seconds"] = "1";
            }
            else
            {
                data["online_seconds"] = "12";
                data["afk_seconds"] = "12";
            }

            webhookCoroutine = WebhookSend(data, Configuration.API.PlayersConnectionRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
            ConsoleLog("Sent");
        }

        #endregion

        #region APIHooks



        #endregion

        #region Localization
        string _lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private void LoadMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to use this command!"
            }, this);
        }
        #endregion

        #region Config

        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "General Options")]
            public GeneralOptions General { get; set; }

            [JsonProperty(PropertyName = "Tracking Options")]
            public TrackingOptions Tracking { get; set; }

            [JsonProperty(PropertyName = "DO NOT CHANGE! ---- API INFORMATION --- DO NOT CHANGE!")]
            public APIOptions API { get; set; }

            public class GeneralOptions
            {
                [JsonProperty(PropertyName = "Debug Mode Enabled?")]
                public bool DebugModeEnabled { get; set; }

                [JsonProperty(PropertyName = "Log To File?")]
                public bool LogToFile { get; set; }

                [JsonProperty(PropertyName = "Discord Webhook Enabled?")]
                public bool DiscordWebhookEnabled { get; set; }

                [JsonProperty(PropertyName = "Discord Webhook")]
                public string DiscordWebhook { get; set; }

                [JsonProperty(PropertyName = "Server API Token")]
                public string APIToken { get; set; }
            }

            public class TrackingOptions
            {
                [JsonProperty(PropertyName = "Track Players Online Time?")]
                public bool TrackPlayerOnlineTime { get; set; }
            }

            public class APIOptions
            {
                [JsonProperty(PropertyName = "PlayerBanData")]
                public PlayerBanDataRoutes PlayerBanDataRoute { get; set; }

                public class PlayerBanDataRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "ServerData")]
                public ServerDataRoutes ServerDataRoute { get; set; }

                public class ServerDataRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "AnimalKills")]
                public AnimalKillsRoutes AnimalKillsRoute { get; set; }

                public class AnimalKillsRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Crafting")]
                public CraftingRoutes CraftingRoute { get; set; }

                public class CraftingRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Destroyed Buildings")]
                public DestroyedBuildingsRoutes DestroyedBuildingRoute { get; set; }

                public class DestroyedBuildingsRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Destroyed Containers")]
                public DestroyedContainersRoutes DestroyedContainersRoute { get; set; }

                public class DestroyedContainersRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Kills")]
                public KillsRoutes KillsRoute { get; set; }

                public class KillsRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Placed Deployables")]
                public PlacedDeployablesRoutes PlacedDeployablesRoute { get; set; }

                public class PlacedDeployablesRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Placed Structures")]
                public PlacedStructuresRoutes PlacedStructuresRoute { get; set; }

                public class PlacedStructuresRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Player Connections")]
                public PlayersConnectionsRoutes PlayersConnectionRoute { get; set; }

                public class PlayersConnectionsRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Player Data")]
                public PlayersDataRoutes PlayersDataRoute { get; set; }

                public class PlayersDataRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Deaths")]
                public DeathsRoutes DeathsRoute { get; set; }

                public class DeathsRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Gathering")]
                public GatheringRoutes GatheringRoute { get; set; }

                public class GatheringRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }

                [JsonProperty(PropertyName = "Weapon Fire")]
                public WeaponFireRoutes WeaponFireRoute { get; set; }

                public class WeaponFireRoutes
                {
                    [JsonProperty(PropertyName = "Create")]
                    public string Create { get; set; }
                }
            }

            public VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                General = new ConfigData.GeneralOptions
                {
                    DebugModeEnabled = true,
                    LogToFile = true,
                    DiscordWebhookEnabled = true,
                    DiscordWebhook = "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks",
                    APIToken = "7e0c91ce-c7c1-3304-8d40-9eab41cf29f6"
                },
                Tracking = new ConfigData.TrackingOptions
                {
                    TrackPlayerOnlineTime = true
                },
                API = new ConfigData.APIOptions
                {
                    PlayerBanDataRoute = new ConfigData.APIOptions.PlayerBanDataRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/players/bans/create"
                    },
                    ServerDataRoute = new ConfigData.APIOptions.ServerDataRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/data/create"
                    },
                    AnimalKillsRoute = new ConfigData.APIOptions.AnimalKillsRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/animalkills/create"
                    },
                    CraftingRoute = new ConfigData.APIOptions.CraftingRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/crafting/create"
                    },
                    DestroyedBuildingRoute = new ConfigData.APIOptions.DestroyedBuildingsRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/destroyedbuildings/create"
                    },
                    DestroyedContainersRoute = new ConfigData.APIOptions.DestroyedContainersRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/destroyedcontainers/create"
                    },
                    KillsRoute = new ConfigData.APIOptions.KillsRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/kills/create"
                    },
                    PlacedDeployablesRoute = new ConfigData.APIOptions.PlacedDeployablesRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/placeddeployables/create"
                    },
                    PlacedStructuresRoute = new ConfigData.APIOptions.PlacedStructuresRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/placedstructures/create"
                    },
                    PlayersConnectionRoute = new ConfigData.APIOptions.PlayersConnectionsRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/players/connection/create"
                    },
                    PlayersDataRoute = new ConfigData.APIOptions.PlayersDataRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/players/data/create"
                    },
                    DeathsRoute = new ConfigData.APIOptions.DeathsRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/deaths/create"
                    },
                    GatheringRoute = new ConfigData.APIOptions.GatheringRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/gathering/create"
                    },
                    WeaponFireRoute = new ConfigData.APIOptions.WeaponFireRoutes
                    {
                        Create = "http://localhost:8000/api/v1/server/weaponfire/create"
                    },
                },
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            ConsoleWarn("Config update detected! Updating config values...");

            if (Configuration.Version < new VersionNumber(0, 2, 0))
                Configuration = GetBaseConfig();

            Configuration.Version = Version;
            ConsoleWarn("Config update completed!");
        }

        #endregion

        #region Data

        protected internal static DynamicConfigFile DataFile = Interface.Oxide.DataFileSystem.GetDatafile(_PluginName);

        private void SaveDataFile(DynamicConfigFile data)
        {
            data.Save();
            _Debug("Data file has been updated.");
        }

        #endregion

        #region Coroutines

        private IEnumerator getPlayerClientDataCoroutine()
        {
            _Debug("------------------------------");
            _Debug("Method: getPlayerClientDataCoroutine");

            _Debug("Looping through all players");
            foreach (var player in BasePlayer.activePlayerList)
            {
                _Debug($"Player Name: {player.displayName}");

                GetPlayerPerformance(player, CreatePlayerData);

                yield return null;
            }
        }

        private IEnumerator DiscordSendMessage(string msg)
        {
            if (Configuration.General.DiscordWebhookEnabled)
            {
                // Check if the discord webhook is default or null/empty
                if (Configuration.General.DiscordWebhook != "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks" || !string.IsNullOrEmpty(Configuration.General.DiscordWebhook))
                {
                    // Grab the form data
                    WWWForm formData = new WWWForm();
                    string content = $"{msg}\n";
                    formData.AddField("content", content);

                    // Define the request
                    using (var request = UnityWebRequest.Post(Configuration.General.DiscordWebhook, formData))
                    {
                        // Execute the request
                        yield return request.SendWebRequest();
                        if ((request.isNetworkError || request.isHttpError) && request.error.Contains("Too Many Requests"))
                        {
                            Puts("Discord Webhook Rate Limit Exceeded... Waiting 30 seconds...");
                            yield return new WaitForSeconds(30f);
                        }
                    }
                }
                else
                {
                    ConsoleError("Discord Webhook Enabled, but the webhook isn't set correctly. Please check your Discord Webhook in the configuration file.");
                }

                ServerMgr.Instance.StopCoroutine(webhookCoroutine);
            }
        }

        private IEnumerator WebhookSend(Dictionary<string, string> data, string webhook)
        {
            // Create New Form Data
            WWWForm formData = new WWWForm();

            foreach (KeyValuePair<string, string> entry in data)
            {
                formData.AddField(entry.Key, entry.Value);
            }

            // Define the request
            using (var request = UnityWebRequest.Post(webhook, formData))
            {
                // Execute the request
                yield return request.SendWebRequest();
                if ((request.isNetworkError || request.isHttpError) && request.error.Contains("Too Many Requests"))
                {
                    Puts("Rate Limit Exceeded... Waiting 30 seconds...");
                    yield return new WaitForSeconds(30f);
                }
                else
                {
                    // Use ConsoleLog function to log the response
                    _Debug(request.downloadHandler.text);
                    bool APIKeyValid = request.downloadHandler.text.IndexOf("API Key Invalid", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (APIKeyValid)
                    {
                        ConsoleError("Server API Key Invalid. Check your API key and try again.");
                    }
                }
            }

            ServerMgr.Instance.StopCoroutine(webhookCoroutine);
        }

        #endregion
    }

}