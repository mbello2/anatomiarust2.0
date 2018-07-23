using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Barrel Points", "redBDGR", "2.0.7")]
    [Description("Gives players extra rewards for destroying barrels")]
    class BarrelPoints : RustPlugin
    {
        [PluginReference] Plugin Economics; // http://oxidemod.org/plugins/economics.717/
        [PluginReference] Plugin ServerRewards; // http://oxidemod.org/plugins/serverrewards.1751/

        static Dictionary<string, object> _PermissionDic()
        {
            var x = new Dictionary<string, object>();
            x.Add("barrelpoints.default", 2.0);
            x.Add("barrelpoints.vip", 5.0);
            return x;
        }
        Dictionary<string, object> permissionList;
        Dictionary<string, int> playerInfo = new Dictionary<string, int>();
        List<uint> CrateCache = new List<uint>();

        private bool Changed;
        private bool LoadingQue;
        private bool useEconomy = true;
        private bool useServerRewards; // this is no disrespect to the author of ServerRewards, they are both amazing
        private bool resetBarrelsOnDeath = true;
        private bool sendNotificationMessage = true;
        private bool useCrates;
        private bool useBarrels = true;
        private int givePointsEvery = 1;

        private void Init() => LoadVariables();

        private void Loaded()
        {
            foreach (var entry in permissionList)
                permission.RegisterPermission(entry.Key, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                //chat
                ["Economy Notice (Barrel)"] = "You received ${0} for destroying a barrel!",
                ["RP Notice (Barrel)"] = "You received {0} RP for destorying a barrel!",

                ["Economy Notice (Crate)"] = "You received ${0} for looting a crate!",
                ["RP Notice (Crate)"] = "You received {0} RP for looting a crate!",
            }, this);

            if (useEconomy)
                if (!Economics)
                {
                    PrintError("Economics.cs was not found! Disabling the economics setting until you reload me");
                    useEconomy = false;
                }
            if (useServerRewards)
                if (!ServerRewards)
                {
                    PrintError("ServerRewards.cs was not found! Disabling the RP setting until you reload me!");
                    useServerRewards = false;
                }
        }

        private void OnPluginLoaded(Plugin name)
        {
            if (name.Name != "Economics" && name.Name != "ServerRewards") return;
            if (LoadingQue)
                return;
            Puts("A plugin dependency was detected as being loaded / reloaded... I will automatically reload myself incase of any changes in 3 seconds");
            LoadingQue = true;
            timer.Once(3f, () =>
            {
                rust.RunServerCommand("o.reload BarrelPoints");
                LoadingQue = false;
            });
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadVariables();
        }

        private void LoadVariables()
        {
            permissionList = (Dictionary<string, object>)GetConfig("Settings", "Permission List", _PermissionDic());
            useEconomy = Convert.ToBoolean(GetConfig("Settings", "Use Economics", true));
            useServerRewards = Convert.ToBoolean(GetConfig("Settings", "Use ServerRewards", false));
            sendNotificationMessage = Convert.ToBoolean(GetConfig("Settings", "Send Notification Message", true));
            givePointsEvery = Convert.ToInt32(GetConfig("Settings", "Give Points Every x Barrels", 1));
            resetBarrelsOnDeath = Convert.ToBoolean(GetConfig("Settings", "Reset Barrel Count on Death", true));
            useBarrels = Convert.ToBoolean(GetConfig("Settings", "Give Points For Barrels", true));
            useCrates = Convert.ToBoolean(GetConfig("Settings", "Give Points For Crates", false));

            if (!Changed) return;
            SaveConfig();
            Changed = false;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!useBarrels)
                return;
            if (entity.ShortPrefabName != "loot-barrel-1" && entity.ShortPrefabName != "loot-barrel-2" && entity.ShortPrefabName != "loot_barrel_1" && entity.ShortPrefabName != "loot_barrel_2" && entity.ShortPrefabName != "oil_barrel") return;
            if (info == null)
                return;
            if (!info.Initiator)
                return;
            if (!(info.Initiator is BasePlayer))
                return;
            BasePlayer player = info.InitiatorPlayer;
            if (player == null)
                return;
            if (!player.IsValid())
                return;
            string userPermission = GetPermissionName(player);
            if (userPermission == null) return;

            // Checking for number of barrels hit
            if (!playerInfo.ContainsKey(player.UserIDString))
                playerInfo.Add(player.UserIDString, 0);
            if (playerInfo[player.UserIDString] == givePointsEvery - 1)
            {
                // Section that gives the player their money
                if (useEconomy)
                {
                    Economics?.CallHook("Deposit", player.userID, Convert.ToDouble(permissionList[userPermission]));
                    if (sendNotificationMessage)
                        player.ChatMessage(string.Format(msg("Economy Notice (Barrel)", player.UserIDString), permissionList[userPermission].ToString()));
                }
                if (useServerRewards)
                {
                    ServerRewards?.Call("AddPoints", new object[] { player.userID, Convert.ToInt32(permissionList[userPermission]) });
                    if (sendNotificationMessage)
                        player.ChatMessage(string.Format(msg("RP Notice (Barrel)", player.UserIDString), permissionList[userPermission].ToString()));
                }
                playerInfo[player.UserIDString] = 0;
            }
            else
                playerInfo[player.UserIDString]++;
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null)
                return;
            if (!useCrates)
                return;
            if (entity.ShortPrefabName != "crate_mine" && entity.ShortPrefabName != "crate_normal" && entity.ShortPrefabName != "crate_normal_2" && entity.ShortPrefabName != "crate_normal_2_food" && entity.ShortPrefabName != "crate_normal_2_medical" && entity.ShortPrefabName != "crate_tools" && entity.ShortPrefabName != "heli_crate") return;
            if (CrateCache.Contains(entity.net.ID))
                CrateCache.Remove(entity.net.ID);
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!useCrates)
                return;
            if (entity.ShortPrefabName != "crate_mine" && entity.ShortPrefabName != "crate_normal" && entity.ShortPrefabName != "crate_normal_2" && entity.ShortPrefabName != "crate_normal_2_food" && entity.ShortPrefabName != "crate_normal_2_medical" && entity.ShortPrefabName != "crate_tools" && entity.ShortPrefabName != "heli_crate") return;
            if (CrateCache.Contains(entity.net.ID))
                return;
            CrateCache.Add(entity.net.ID);
            string userPermission = GetPermissionName(player);
            if (userPermission == null) return;
            if (useEconomy)
            {
                Economics?.CallHook("Deposit", player.userID, Convert.ToDouble(permissionList[userPermission]));
                if (sendNotificationMessage)
                    player.ChatMessage(string.Format(msg("Economy Notice (Crate)", player.UserIDString), permissionList[userPermission].ToString()));
            }
            if (!useServerRewards) return;
            ServerRewards?.Call("AddPoints", new object[] { player.userID, Convert.ToInt32(permissionList[userPermission]) });
            if (sendNotificationMessage)
                player.ChatMessage(string.Format(msg("RP Notice (Crate)", player.UserIDString), permissionList[userPermission].ToString()));
        }

        private void OnPlayerDie(BasePlayer player, HitInfo info)
        {
            if (!resetBarrelsOnDeath) return;
            if (playerInfo.ContainsKey(player.UserIDString))
                playerInfo[player.UserIDString] = 0;
        }

        private string GetPermissionName(BasePlayer player)
        {
            KeyValuePair<string, int> _perms = new KeyValuePair<string, int>(null, 0);
            Dictionary<string, int> perms = permissionList.Where(entry => permission.UserHasPermission(player.UserIDString, entry.Key)).ToDictionary(entry => entry.Key, entry => Convert.ToInt32(entry.Value));
            foreach (var entry in perms)
                if (Convert.ToInt32(entry.Value) > _perms.Value)
                    _perms = new KeyValuePair<string, int>(entry.Key, Convert.ToInt32(entry.Value));
            return _perms.Key;
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }
            return value;
        }

        private string msg(string key, string id = null) => lang.GetMessage(key, this, id);
    }
}