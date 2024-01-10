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
using static Steamworks.InventoryItem;
using Rust;
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
        private const string _PluginVersion = "0.0.14";
        private const string _PluginDescription = "Official Plugin for RustAnalytics.com";
        private const string _DownloadLink = "INSERT_LINK_HERE";

        // Plugin References
        [PluginReference]
        Plugin RustAnalyticsPlaytimeTracker;

        // Misc Variables
        private static RustAnalytics _pluginInstance;
        private SaveInfo _saveInfo = SaveInfo.Create(World.SaveFolderName + $"/player.blueprints.{Rust.Protocol.persistance}.db");
        private readonly Hash<ulong, Action<ClientPerformanceReport>> _clientPerformanceReports = new();
        private readonly CustomYieldInstruction _waitWhileYieldInstruction = new WaitWhile(() => BasePlayer.activePlayerList.Count == 0);
        private readonly YieldInstruction _waitYieldInstruction = new WaitForSeconds(60f);
        private readonly Hash<string, string> _cachedData = new();

        // Coroutines
        private IEnumerator webhookCoroutine;
        private IEnumerator clientDataCoroutine;

        // Harmony Variables
        private HInstance _harmonyInstance;
        private string HarmonyId => $"com.{_PluginAuthor}.{_PluginName}";

        // Rust Item Variables
        private Hash<string, string> rocketAmmoTypes = new Hash<string, string>
        {
            {"ROCKET_BASIC", "Rocket"},
            {"ROCKET_SMOKE", "Smoke Rocket"},
            {"ROCKET_HV", "High Velocity Rocket"},
            {"ROCKET_FIRE", "Incendiary Rocket"},
            {"ROCKET_HEATSEEKER", "Homing Missile"}
        };

        private Hash<string, string> containerTypes = new Hash<string, string>
        {
            {"dropbox.deployed", "Dropbox"},
            {"bbq.deployed", "Barbeque"},
            {"composter", "Composter"},
            {"bathtub.planter.deployed", "Bathtub Planter Box"},
            {"furnace", "Furnace"},
            {"hitchtrough.deployed", "Hitch & Trough"},
            {"planter.large.deployed", "Large Planter"},
            {"planter.small.deployed", "Small Planter"},
            {"legacy_furnace", "Legacy Furnace"},
            {"box.wooden.large", "Large Wooden Box"},
            {"woodbox_deployed", "Wooden Box"},
            {"locker.deployed", "Locker"},
            {"mailbox.deployed", "Mailbox"},
            {"mixingtable.deployed", "Mixing Table"},
            {"furnace.large", "Large Furnace"},
            {"hobobarrel.deployed", "Hobo Barrel"},
            {"railroadplanter.deployed", "Railroad Planter"},
            {"repairbench_deployed", "Repair Bench"},
            {"researchtable_deployed", "Research Table"},
            {"tunalight.deployed", "Tuna Can Light"},
            {"stocking_large_deployed", "SUPER Stocking"},
            {"stocking_small_deployed", "Small Stocking"},
            {"weaponrack_tall.deployed", "Weapon Rank (Tall)"},
            {"weaponrack_horizontal.deployed", "Weapon Rank (Horizontal)"},
            {"weaponrack_stand.deployed", "Weapon Rank (Stand)"},
            {"weaponrack_wide.deployed", "Weapon Rank (Wide)"},
            {"refinery_small_deployed", "Small Oil Refinery"},
            {"torchholder.deployed", "Torch Holder"},
            {"vendingmachine.deployed", "Vending Machine"},
            {"fridge.deployed", "Fridge"},
            {"campfire", "Campfire"},
            {"skull_fire_pit", "Skull Fire Pit"},
            {"workbench1.deployed", "Workbench (Level 1)"},
            {"workbench2.deployed", "Workbench (Level 2)"},
            {"workbench3.deployed", "Workbench (Level 3)"},
            {"small_stash_deployed", "Small Stash"},
            {"fireplace.deployed", "Fireplace"},
            {"storage_barrel_c", "Storage Barrel (Horizontal)"},
            {"storage_barrel_b", "Storage Barrel (Vertical)"},
            {"cupboard.tool.deployed", "Tool Cupboard"},
            {"electricfurnace.deployed", "Electric Furnace"},
            {"cursedcauldron.deployed", "Cursed Cauldron"},
            {"coffinstorage", "Coffin"},
            {"guntrap.deployed", "Shotgun Trap"},
            {"flameturret.deployed", "Flame Turret"}
        };

        private Hash<string, string> buildingTiers = new Hash<string, string>
        {
            {"twigs", "0"},
            {"wood", "1"},
            {"stone", "2"},
            {"metal", "3"},
            {"toptier", "4"}
        };

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
            //ShowSplashScreen();
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

        public void StartGlobalTimers()
        {
            // Call some functions before starting the timers so that they are immediately being collected.
            CreateServerData();

            ConsoleLog("Starting 10 minute timer...");
            timer.Every(600f, () =>
            {
                CreateServerData();
            });

            ConsoleLog("Starting 60 second timer...");
            timer.Every(60f, () =>
            {

            });

            // Start the getPlayerClientDataCoroutine
            ServerMgr.Instance.StartCoroutine(clientDataCoroutine = GetPlayerClientDataCoroutine());
        }

        #region HelperFunctions

        private void ClearCachedData()
        {
            _cachedData.Clear();
            _cachedData["api_key"] = Configuration.General.APIToken;
        }

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

        private string GetGridFromPosition(Vector3 position)
        {
            Vector2 roundedPos = new Vector2(World.Size / 2 + position.x, World.Size / 2 - position.z);

            string grid = $"{ConvertXCoordinateToLetter((int)(roundedPos.x / 150))}{(int)(roundedPos.y / 150)}";

            return grid;
        }

        private static string ConvertXCoordinateToLetter(int num)
        {
            int num2 = Mathf.FloorToInt((float)(num / 26));
            int num3 = num % 26;
            string text = string.Empty;
            if (num2 > 0)
            {
                for (int i = 0; i < num2; i++)
                {
                    text += Convert.ToChar(65 + i);
                }
            }
            return text + Convert.ToChar(65 + num3);
        }

        #endregion

        #region CachedDataHandling

        private Hash<string, string> GetServerData()
        {
            uint worldSize = World.Size / 1000;
            double usedMemory = Math.Round((Performance.current.memoryUsageSystem * 1f) / 1024, 2);
            double maxMemory = Math.Round((UnityEngine.SystemInfo.systemMemorySize * 1f) / 1024, 2);
            double networkIn = Math.Round((Network.Net.sv.GetStat(null, Network.BaseNetwork.StatTypeLong.BytesReceived_LastSecond) * 1f) / 1024, 2);
            double networkOut = Math.Round((Network.Net.sv.GetStat(null, Network.BaseNetwork.StatTypeLong.BytesSent_LastSecond) * 1f) / 1024, 2);

            ClearCachedData();
            _cachedData["entities"] = $"{BaseNetworkable.serverEntities.Count}";
            _cachedData["world_seed"] = $"{World.Seed}";
            _cachedData["world_name"] = $"{World.Name}";
            _cachedData["players_online"] = $"{BasePlayer.activePlayerList.Count}";
            _cachedData["players_max"] = $"{ConVar.Server.maxplayers}";
            _cachedData["in_game_time"] = $"{TOD_Sky.Instance.Cycle.DateTime}";
            _cachedData["server_fps"] = $"{Performance.report.frameRate}";
            _cachedData["map_size"] = $"{worldSize}";
            _cachedData["protocol"] = $"{Rust.Protocol.network}";
            _cachedData["used_memory"] = $"{usedMemory}";
            _cachedData["max_memory"] = $"{maxMemory}";
            _cachedData["network_in"] = $"{networkIn}";
            _cachedData["network_out"] = $"{networkOut}";
            _cachedData["last_wiped"] = $"{SaveRestore.SaveCreatedTime}";
            _cachedData["blueprint_last_wiped"] = $"{_saveInfo.CreationTime}";

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

            return _cachedData;
        }

        private Hash<string, string> GetPlayerConnectionData(BasePlayer player, string type)
        {
            ClearCachedData();
            _cachedData["username"] = player.displayName;
            _cachedData["steam_id"] = player.UserIDString;
            _cachedData["ip_address"] = GetPlayerIPAddress(player);
            _cachedData["type"] = type;

            // Playtime Tracker
            if (!RustAnalyticsPlaytimeTracker)
            {
                ConsoleError($"RustMetricsPlaytimeTracker is not loaded, but you have tracking enabled. Download from here: {_DownloadLink}");
                _cachedData["online_seconds"] = "1";
                _cachedData["afk_seconds"] = "1";
            }
            else
            {
                _cachedData["online_seconds"] = GetPlayerOnlineTime(player);
                _cachedData["afk_seconds"] = GetPlayerAFKTime(player);
            }

            return _cachedData;
        }

        private Hash<string, string> GetPlayerClientData(ClientPerformanceReport clientPerformanceReport)
        {
            ClearCachedData();
            _cachedData["steam_id"] = clientPerformanceReport.user_id;
            _cachedData["frame_rate"] = clientPerformanceReport.fps.ToString();
            _cachedData["ping"] = clientPerformanceReport.ping.ToString();

            return _cachedData;
        }

        private Hash<string, string> GetPlayerBannedData(string name, string id, string address, string reason)
        {
            ClearCachedData();
            _cachedData["username"] = name;
            _cachedData["steam_id"] = id;
            _cachedData["ip_address"] = address;
            _cachedData["reason"] = reason;

            return _cachedData;
        }

        private Hash<string, string> GetPlayerUnbannedData(string id)
        {
            ClearCachedData();
            _cachedData["steam_id"] = id;

            return _cachedData;
        }

        private Hash<string, string> GetPlayerGatherData(string itemName, string amount, BasePlayer player)
        {
            ClearCachedData();
            _cachedData["username"] = player.displayName;
            _cachedData["steam_id"] = player.UserIDString;
            _cachedData["resource"] = itemName;
            _cachedData["amount"] = amount;

            return _cachedData;
        }

        private Hash<string, string> GetWeaponFireData(BasePlayer player, string bullet, string weapon)
        {
            ClearCachedData();
            _cachedData["username"] = player.displayName;
            _cachedData["steam_id"] = player.UserIDString;
            _cachedData["bullet"] = bullet;
            _cachedData["weapon"] = weapon;
            _cachedData["amount"] = "1";

            return _cachedData;
        }

        private Hash<string, string> GetDestroyedContainersData(BasePlayer player, string owner, string type, string weapon, string grid, string x, string y)
        {
            ClearCachedData();
            _cachedData["username"] = player.displayName;
            _cachedData["steam_id"] = player.UserIDString;
            _cachedData["owner"] = owner;
            _cachedData["type"] = type;
            _cachedData["weapon"] = weapon;
            _cachedData["grid"] = grid;
            _cachedData["x"] = x;
            _cachedData["y"] = y;

            return _cachedData;
        }

        private Hash<string, string> GetDestroyedContainersData(BasePlayer player, string owner, string type, string tier, string weapon, string grid)
        {
            ClearCachedData();
            _cachedData["username"] = player.displayName;
            _cachedData["steam_id"] = player.UserIDString;
            _cachedData["owner"] = owner;
            _cachedData["type"] = type;
            _cachedData["tier"] = tier;
            _cachedData["weapon"] = weapon;
            _cachedData["grid"] = grid;

            return _cachedData;
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

        #region PlayerConnections
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

        #endregion

        #region PlayerBans
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

        private void OnUserUnbanned(string name, string id)
    {
        _Debug("------------------------------");
        _Debug("Method: OnUserUnbanned");
        _Debug($"ID: {id}");

        DestroyPlayerBannedData(id);
    }

        #endregion

        #region OnPlayerGather
        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            _Debug("------------------------------");
            _Debug("Method: OnDispenserBonus");

            var itemName = item.info.displayName.english;
            var amount = item.amount.ToString();

            _Debug($"Resource: {itemName}");
            _Debug($"Amount: {amount}");

            CreatePlayerGatherData(itemName, amount, player);
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            _Debug("------------------------------");
            _Debug("Method: OnDispenserGather");

            var itemName = item.info.displayName.english;
            var amount = item.amount.ToString();

            _Debug($"Resource: {itemName}");
            _Debug($"Amount: {amount}");

            CreatePlayerGatherData(itemName, amount, player);
        }

        private void OnCollectiblePickup(Item item, BasePlayer player)
    {
        _Debug("------------------------------");
        _Debug("Method: OnCollectiblePickup");

        var itemName = item.info.displayName.english;
        var amount = item.amount.ToString();

        _Debug($"Resource: {itemName}");
        _Debug($"Amount: {amount}");

        CreatePlayerGatherData(itemName, amount, player);
    }

        #endregion

        /*
        #region WeaponFire
        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile itemProjectile, object projectiles)
        {
            _Debug("------------------------------");
            _Debug("Method: OnWeaponFired");

            // Define Some Variables
            string bullet = "Not Found";
            string weapon = "Not Found";

            // Define the weapon
            if (player.GetActiveItem() != null)
            {
                weapon = player.GetActiveItem().info.displayName.english;
            }

            // Try and get the bullet information
            try
            {
                bullet = projectile.primaryMagazine.ammoType.displayName.english;
            }
            catch (Exception e)
            {
                ConsoleWarn("Can Not Get Bullet Information: " + e.StackTrace);
                if (projectile == null)
                {
                    ConsoleWarn("Projectile is null");
                }
                else if (projectile.primaryMagazine == null)
                {
                    ConsoleWarn("Projectile Primary Magazine is null");
                }
                else if (projectile.primaryMagazine.ammoType == null)
                {
                    ConsoleWarn("Projectile Primary Magazine Ammo Type is null");
                }
                return;
            }

            _Debug($"Player: {player.displayName}");
            _Debug($"Steam ID: {player.UserIDString}");
            _Debug($"Weapon: {weapon}");
            _Debug($"Bullet: {bullet}");

            CreateWeaponFireData(player, weapon, bullet);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            _Debug("------------------------------");
            _Debug("Method: OnExplosiveThrown");
            _Debug($"Player: {player.displayName}");
            _Debug($"Steam ID: {player.UserIDString}");

            string explosive = player.GetActiveItem().info.displayName.english;
            _Debug($"Explosive: {explosive}");
            CreateWeaponFireData(player, explosive, explosive);
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            _Debug("------------------------------");
            _Debug("Method: OnRocketLaunched");
            _Debug($"Player: {player.displayName}");
            _Debug($"Steam ID: {player.UserIDString}");

            // Define some variables
            string rocketName = player.GetActiveItem().info.displayName.english;
            string ammo = rocketAmmoTypes.ContainsKey(entity.ShortPrefabName.ToUpper()) ? rocketAmmoTypes[entity.ShortPrefabName.ToUpper()] : entity.ShortPrefabName.ToUpper();

            _Debug($"Rocket Launcher: {rocketName}");
            _Debug($"Rocket Ammo: {ammo}");

            CreateWeaponFireData(player, rocketName, ammo);
        }

        #endregion
        */

        #region OnEntityDeath (DestroyedContainer, DestroyedBuilding, AnimalKill, PlayerKills)

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo hitInfo)
        {
            _Debug("------------------------------");
            _Debug("Method: OnEntityDeath");
            string weapon = "Weapon Not Found";

            // Check if the last attacker was a BasePlayer
            if(entity.lastAttacker is BasePlayer && entity.lastAttacker != null)
            {
                BasePlayer player = (BasePlayer)entity.lastAttacker;
                _Debug($"Attacking Player: {player.displayName}");
                _Debug($"Attacking Player ID: {player.UserIDString}");
                // Check if the entity is a Storage Container (DestroyedContainer)
                CheckIfEntityIsStorage(player, entity, weapon);

                // Check if the entity is a Building Block (DestroyedBuilding)
                CheckIfEntityIsBuilding(player, entity, weapon);
            }
        }

        private void CheckIfEntityIsStorage(BasePlayer player, BaseCombatEntity entity, string weapon)
        {
            if (entity is StorageContainer)
            {
                // Get the storage container
                StorageContainer container = (StorageContainer)entity;
                string containerName = containerTypes.ContainsKey(container.ShortPrefabName) ? containerTypes[container.ShortPrefabName] : container.ShortPrefabName;
                _Debug($"Container (Short Prefab Name): {container.ShortPrefabName}");
                _Debug($"Container (Proper Name): {containerName}");

                // Get the player weapon
                try
                {
                    weapon = player.GetActiveItem().info.displayName.english;
                    _Debug($"Weapon: {weapon}");
                }
                catch
                {
                    ConsoleWarn("Can Not Get Player Weapon");
                }

                // Get the container coordinates and grid
                string x = container.transform.position.x.ToString();
                string y = container.transform.position.y.ToString();
                string grid = GetGridFromPosition(container.transform.position);

                _Debug($"X Coordinate: {x}");
                _Debug($"Y Coordinate: {y}");
                _Debug($"Grid: {grid}");

                // Get the container Owner
                string containerOwner = BasePlayer.FindAwakeOrSleeping(entity.OwnerID.ToString()).displayName;
                _Debug($"Container Owner: {containerOwner}");

                // Create the Destroyed Container Data
                CreateDestroyedContainerData(player, containerOwner, containerName, weapon, grid, x, y);
            }
        }

        private void CheckIfEntityIsBuilding(BasePlayer player, BaseCombatEntity entity, string weapon)
        {
            if (entity is BuildingBlock)
            {
                // Get the destroyed building block
                BuildingBlock destroyedBuilding = (BuildingBlock)entity;
                string buildingName = destroyedBuilding.blockDefinition.info.name.english;
                string tierName = destroyedBuilding.currentGrade.gradeBase.name;
                string tierIntStr = buildingTiers.ContainsKey(tierName) ? buildingTiers[tierName] : tierName;

                _Debug($"Building Name: {buildingName}");
                _Debug($"Building Tier: {tierName}");
                _Debug($"Building Tier (IntStr): {tierIntStr}");

                // Get the player weapon
                try
                {
                    weapon = player.GetActiveItem().info.displayName.english;
                    _Debug($"Weapon: {weapon}");
                }
                catch
                {
                    ConsoleWarn("Can Not Get Player Weapon");
                }

                // Get the building block grid
                string grid = GetGridFromPosition(destroyedBuilding.transform.position);
                _Debug($"Grid: {grid}");

                // Get the building Owner
                string buildingOwner = BasePlayer.FindAwakeOrSleeping(entity.OwnerID.ToString()).displayName;
                _Debug($"Building Owner: {buildingOwner}");

                // Create the Destroyed Container Data
                CreateDestroyedBuildingData(player, buildingOwner, buildingName, tierIntStr, weapon, grid);
            }
        }

        #endregion

        #endregion Hooks

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
            _Debug("Method: CreatePlayerData");
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

        private void DestroyPlayerBannedData(string id)
        {
            var data = GetPlayerUnbannedData(id);

            webhookCoroutine = WebhookSend(data, Configuration.API.PlayerBanDataRoute.Destroy);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreatePlayerGatherData(string itemName, string amount, BasePlayer player)
        {
            var data = GetPlayerGatherData(itemName, amount, player);

            webhookCoroutine = WebhookSend(data, Configuration.API.GatheringRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreateWeaponFireData(BasePlayer player, string bullet, string weapon)
        {
            var data = GetWeaponFireData(player, bullet, weapon);

            webhookCoroutine = WebhookSend(data, Configuration.API.WeaponFireRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreateDestroyedContainerData(BasePlayer player, string owner, string type, string weapon, string grid, string x, string y)
        {
            var data = GetDestroyedContainersData(player, owner, type, weapon, grid, x, y);

            webhookCoroutine = WebhookSend(data, Configuration.API.DestroyedContainersRoute.Create);
            ServerMgr.Instance.StartCoroutine(webhookCoroutine);
        }

        private void CreateDestroyedBuildingData(BasePlayer player, string owner, string type, string tier, string weapon, string grid)
        {
            var data = GetDestroyedContainersData(player, owner, type, tier, weapon, grid);

            webhookCoroutine = WebhookSend(data, Configuration.API.DestroyedBuildingRoute.Create);
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

                    [JsonProperty(PropertyName = "Destroy")]
                    public string Destroy { get; set; }
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
                        Create = "http://localhost:8000/api/v1/server/players/bans/create",
                        Destroy = "http://localhost:8000/api/v1/server/players/bans/destroy"
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

        private IEnumerator GetPlayerClientDataCoroutine()
        {
            _Debug("------------------------------");
            _Debug("Method: GetPlayerClientDataCoroutine");

            while (true)
            {
                _Debug("Wait while server is empty");
                yield return _waitWhileYieldInstruction;

                _Debug("Looping through all players");
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    _Debug($"Player Name: {player.displayName}");

                    GetPlayerPerformance(player, CreatePlayerData);
                    yield return null;
                }

                _Debug("Waiting 60 seconds before next check of all players");
                yield return _waitYieldInstruction;
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

        private IEnumerator WebhookSend(Hash<string, string> data, string webhook)
        {
            // Create New Form Data
            WWWForm formData = new WWWForm();

            foreach (KeyValuePair<string, string> entry in data)
            {
                formData.AddField(entry.Key, entry.Value);
            }

            // Define the request
            using (UnityWebRequest request = UnityWebRequest.Post(webhook, formData))
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

        #region SplashScreen

        private void ShowSplashScreen()
        {
            ConsoleLog("+-+-+-+-+-+-+-+-+-+-+-+-+-+");
            ConsoleLog("|R|u|s|t|A|n|a|l|y|t|i|c|s|");
            ConsoleLog("+-+-+-+-+-+-+-+-+-+-+-+-+-+");
            ConsoleLog("   |C|r|e|a|t|e|d| |B|y|   ");
            ConsoleLog(" +-+-+-+-+-+-+-+-+-+-+-+-+ ");
            ConsoleLog(" |B|i|p|p|y|M|i|e|s|t|e|r| ");
            ConsoleLog(" +-+-+-+-+-+-+-+-+-+-+-+-+ ");
        }

        #endregion
    }

}