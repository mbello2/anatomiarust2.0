using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// TODO: Add component sorting to GUI
// TODO: Add command for player preferences
// TODO: Add options to enable/disable each container type (wear, bar, main)
// TODO: Fix players being able to teleport/move and exploit loot containers

namespace Oxide.Plugins
{
    [Info("Quick Sort", "Wulf/lukespragg", "1.2.1")]
    [Description("Adds a GUI that allows players to quickly sort items into containers")]
    public class QuickSort : CovalencePlugin
    {
        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty(PropertyName = "Default UI style (center, lite, right)")]
            public string DefaultUiStyle { get; set; } = "right";

            [JsonProperty(PropertyName = "Loot all delay in seconds (0 to disable)")]
            public int LootAllDelay { get; set; } = 0;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            string configPath = $"{Interface.Oxide.ConfigDirectory}{Path.DirectorySeparatorChar}{Name}.json";
            PrintWarning($"Could not load a valid configuration file, creating a new configuration file at {configPath}");
            config = new Configuration();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion Configuration

        #region Localization

        private new void LoadDefaultMessages()
        {
            // English
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Deposit"] = "Deposit",
                ["DepositAll"] = "All",
                ["DepositAmmo"] = "Ammo",
                ["DepositAttire"] = "Attire",
                ["DepositConstruction"] = "Construction",
                ["DepositExisting"] = "Existing",
                ["DepositFood"] = "Food",
                ["DepositItems"] = "Deployables",
                ["DepositMedical"] = "Medical",
                ["DepositResources"] = "Resources",
                ["DepositTools"] = "Tools",
                ["DepositTraps"] = "Traps",
                ["DepositWeapons"] = "Weapons",
                ["LootAll"] = "Loot All"
            }, this);
        }

        #endregion Localization

        #region Initialization

        private static readonly Dictionary<ulong, string> guiInfo = new Dictionary<ulong, string>();

        private const string permLootAll = "quicksort.lootall";
        private const string permUse = "quicksort.use";

        private void Init()
        {
            permission.RegisterPermission(permLootAll, this);
            permission.RegisterPermission(permUse, this);
        }

        #endregion Initialization

        #region Game Hooks

        private void OnLootPlayer(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permUse))
            {
                UserInterface(player);
            }
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (permission.UserHasPermission(player.UserIDString, permUse) && !(entity is VendingMachine) && !(entity is ShopFront))
            {
                UserInterface(player);
            }
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory.GetComponent<BasePlayer>();

            if (player != null)
            {
                DestroyUi(player);
            }
        }

        #endregion Game Hooks

        #region Console Commands

        [Command("quicksort")]
        private void SortCommand(IPlayer player, string command, string[] args)
        {
            if (player.HasPermission(permUse))
            {
                SortItems(player.Object as BasePlayer, args);
            }
        }

        [Command("quicksort.lootall")]
        private void LootAllCommand(IPlayer player, string command, string[] args)
        {
            if (player.HasPermission(permLootAll))
            {
                timer.Once(config.LootAllDelay, () => AutoLoot(player.Object as BasePlayer));
            }
        }

        [Command("quicksort.lootdelay")]
        private void LootDelayCommand(IPlayer player, string command, string[] args)
        {
            if (player.IsAdmin)
            {
                int x;
                if (int.TryParse(args[0], out x))
                {
                    config.LootAllDelay = x;
                    SaveConfig();
                }
            }
        }

        #endregion Console Commands

        #region Loot Handling

        private void AutoLoot(BasePlayer player)
        {
            ItemContainer container = GetLootedInventory(player);
            ItemContainer playerMain = player.inventory.containerMain;

            if (container != null && playerMain != null && container.playerOwner == null)
            {
                List<Item> itemsSelected = CloneItemList(container.itemList);
                itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));
                MoveItems(itemsSelected, playerMain);
            }
        }

        private void SortItems(BasePlayer player, string[] args)
        {
            ItemContainer container = GetLootedInventory(player);
            ItemContainer playerMain = player.inventory.containerMain;
            //ItemContainer playerWear = player.inventory.containerWear;
            //ItemContainer playerBelt = player.inventory.containerBelt;

            if (container != null && playerMain != null)
            {
                List<Item> itemsSelected;

                if (args.Length == 1)
                {
                    if (args[0].Equals("existing"))
                    {
                        itemsSelected = GetExistingItems(playerMain, container);
                    }
                    else
                    {
                        ItemCategory category = StringToItemCategory(args[0]);
                        itemsSelected = GetItemsOfType(playerMain, category);

                        //itemsSelected.AddRange(GetItemsOfType(playerWear, category));
                        //itemsSelected.AddRange(GetItemsOfType(playerBelt, category));
                    }
                }
                else
                {
                    itemsSelected = CloneItemList(playerMain.itemList);

                    //itemsSelected.AddRange(CloneItemList(playerWear.itemList));
                    //itemsSelected.AddRange(CloneItemList(playerBelt.itemList));
                }

                IEnumerable<Item> uselessItems = GetUselessItems(itemsSelected, container);

                foreach (Item item in uselessItems)
                {
                    itemsSelected.Remove(item);
                }

                itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));
                MoveItems(itemsSelected, container);
            }
        }

        #endregion Loot Handling

        #region Item Helpers

        private IEnumerable<Item> GetUselessItems(IEnumerable<Item> items, ItemContainer container)
        {
            BaseOven furnace = container.entityOwner?.GetComponent<BaseOven>();
            List<Item> uselessItems = new List<Item>();

            if (furnace != null)
            {
                foreach (Item item in items)
                {
                    ItemModCookable cookable = item.info.GetComponent<ItemModCookable>();

                    if (cookable == null || cookable.lowTemp > furnace.cookingTemperature || cookable.highTemp < furnace.cookingTemperature)
                    {
                        uselessItems.Add(item);
                    }
                }
            }

            return uselessItems;
        }

        private List<Item> CloneItemList(IEnumerable<Item> list)
        {
            List<Item> clone = new List<Item>();

            foreach (Item item in list)
            {
                clone.Add(item);
            }

            return clone;
        }

        private List<Item> GetExistingItems(ItemContainer primary, ItemContainer secondary)
        {
            List<Item> existingItems = new List<Item>();

            if (primary != null && secondary != null)
            {
                foreach (Item t in primary.itemList)
                {
                    foreach (Item t1 in secondary.itemList)
                    {
                        if (t.info.itemid != t1.info.itemid)
                        {
                            continue;
                        }

                        existingItems.Add(t);
                        break;
                    }
                }
            }

            return existingItems;
        }

        private List<Item> GetItemsOfType(ItemContainer container, ItemCategory category)
        {
            List<Item> items = new List<Item>();

            foreach (Item item in container.itemList)
            {
                if (item.info.category == category)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private ItemContainer GetLootedInventory(BasePlayer player)
        {
            PlayerLoot playerLoot = player.inventory.loot;
            return playerLoot != null && playerLoot.IsLooting() ? playerLoot.containers[0] : null;
        }

        private void MoveItems(IEnumerable<Item> items, ItemContainer to)
        {
            foreach (Item item in items)
            {
                item.MoveToContainer(to);
            }
        }

        private ItemCategory StringToItemCategory(string categoryName)
        {
            string[] categoryNames = Enum.GetNames(typeof(ItemCategory));

            for (int i = 0; i < categoryNames.Length; i++)
            {
                if (categoryName.ToLower().Equals(categoryNames[i].ToLower()))
                {
                    return (ItemCategory)i;
                }
            }

            return (ItemCategory)categoryNames.Length;
        }

        #endregion Item Helpers

        #region User Interface

        private void UserInterface(BasePlayer player)
        {
            DestroyUi(player);
            guiInfo[player.userID] = CuiHelper.GetGuid();
            player.inventory.loot.gameObject.AddComponent<UIDestroyer>();

            switch (config.DefaultUiStyle.ToLower())
            {
                case "center":
                    UiCenter(player);
                    break;

                case "lite":
                    UiLite(player);
                    break;

                case "right":
                    UiRight(player);
                    break;
            }
        }

        #region UI Center

        private void UiCenter(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.33" },
                RectTransform = { AnchorMin = "0.354 0.625", AnchorMax = "0.633 0.816" }
            }, "Hud.Menu", guiInfo[player.userID]);
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang("Deposit"), FontSize = 16, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.3 1" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort existing", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.3 0.8" },
                Text = { Text = Lang("DepositExisting"), FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.3 0.55" },
                Text = { Text = Lang("DepositAll"), FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panel);
            if (permission.UserHasPermission(player.UserIDString, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksort.lootall", Color = "0 0.7 0 0.5" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.3 0.3" },
                    Text = { Text = Lang("LootAll"), FontSize = 16, Align = TextAnchor.MiddleCenter }
                }, panel);
            }
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort weapon", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.8", AnchorMax = "0.63 0.94" },
                Text = { Text = Lang("DepositWeapons"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort ammunition", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.6", AnchorMax = "0.63 0.75" },
                Text = { Text = Lang("DepositAmmo"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort medical", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.41", AnchorMax = "0.63 0.555" },
                Text = { Text = Lang("DepositMedical"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort attire", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.235", AnchorMax = "0.63 0.368" },
                Text = { Text = Lang("DepositAttire"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort resources", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.05", AnchorMax = "0.63 0.19" },
                Text = { Text = Lang("DepositResources"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort construction", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.8", AnchorMax = "0.95 0.94" },
                Text = { Text = Lang("DepositConstruction"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort items", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.6", AnchorMax = "0.95 0.75" },
                Text = { Text = Lang("DepositItems"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort tool", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.41", AnchorMax = "0.95 0.555" },
                Text = { Text = Lang("DepositTools"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort food", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.235", AnchorMax = "0.95 0.368" },
                Text = { Text = Lang("DepositFood"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort traps", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.05", AnchorMax = "0.95 0.19" },
                Text = { Text = Lang("DepositTraps"), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Center

        #region UI Lite

        private void UiLite(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.0 0.0 0.0 0.0" },
                RectTransform = { AnchorMin = "0.677 0.769", AnchorMax = "0.963 0.96" }
            }, "Hud.Menu", guiInfo[player.userID]);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort existing", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "-0.88 -1.545", AnchorMax = "-0.63 -1.435" },
                Text = { Text = Lang("DepositExisting", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "-0.61 -1.545", AnchorMax = "-0.36 -1.435" },
                Text = { Text = Lang("DepositAll", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort.lootall", Color = "0 0.7 0 0.5" },
                RectTransform = { AnchorMin = "-0.34 -1.545", AnchorMax = "-0.13 -1.435" },
                Text = { Text = Lang("LootAll", player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Lite

        #region UI Right

        private void UiRight(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.33" },
                RectTransform = { AnchorMin = "0.677 0.769", AnchorMax = "0.963 0.96" }
            }, "Hud.Menu", guiInfo[player.userID]);
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang("Deposit", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.3 1" }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort existing", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.3 0.8" },
                Text = { Text = Lang("DepositExisting", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.3 0.55" },
                Text = { Text = Lang("DepositAll", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panel);
            if (permission.UserHasPermission(player.UserIDString, permLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksort.lootall", Color = "0 0.7 0 0.5" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.3 0.3" },
                    Text = { Text = Lang("LootAll", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter }
                }, panel);
            }
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort weapon", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.8", AnchorMax = "0.63 0.94" },
                Text = { Text = Lang("DepositWeapons", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort ammunition", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.6", AnchorMax = "0.63 0.75" },
                Text = { Text = Lang("DepositAmmo", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort medical", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.41", AnchorMax = "0.63 0.555" },
                Text = { Text = Lang("DepositMedical", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort attire", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.235", AnchorMax = "0.63 0.368" },
                Text = { Text = Lang("DepositAttire", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort resources", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.35 0.05", AnchorMax = "0.63 0.19" },
                Text = { Text = Lang("DepositResources", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort construction", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.8", AnchorMax = "0.95 0.94" },
                Text = { Text = Lang("DepositConstruction", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort items", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.6", AnchorMax = "0.95 0.75" },
                Text = { Text = Lang("DepositItems", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort tool", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.41", AnchorMax = "0.95 0.555" },
                Text = { Text = Lang("DepositTools", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort food", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.235", AnchorMax = "0.95 0.368" },
                Text = { Text = Lang("DepositFood", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksort traps", Color = "1 0.5 0 0.5" },
                RectTransform = { AnchorMin = "0.67 0.05", AnchorMax = "0.95 0.19" },
                Text = { Text = Lang("DepositTraps", player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, panel);

            CuiHelper.AddUi(player, elements);
        }

        #endregion UI Right

        #region Cleanup

        private static void DestroyUi(BasePlayer player)
        {
            string gui;
            if (guiInfo.TryGetValue(player.userID, out gui))
            {
                CuiHelper.DestroyUi(player, gui);
            }
        }

        private class UIDestroyer : MonoBehaviour
        {
            private void PlayerStoppedLooting(BasePlayer player)
            {
                DestroyUi(player);
                Destroy(this);
            }
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                DestroyUi(player);
            }
        }

        #endregion Cleanup

        #endregion User Interface

        #region Helpers

        private string Lang(string key, string id = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        #endregion Helpers
    }
}
