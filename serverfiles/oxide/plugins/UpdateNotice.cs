using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Update Notice", "Psystec", "1.0.5", ResourceId = 2837)]
    [Description("Notifies you when a RUST update is released.")]
    //A huge thanks to LaserHydra for his involvement in the development of this plugin! Go check out his resources at: https://oxidemod.org/resources/authors/laserhydra.53411/
    public class UpdateNotice : RustPlugin
    {

        #region Fields

        private const string AdminPermission = "updatenotice.admin";

        private const string ApiUrl = "http://www.psystec.co.za/api/UpdateInfo";

        private Configuration _configuration;

        private int _serverBuildId = 0, _clientBuildId = 0, _stagingBuildId = 0;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Only Notify Admins")]
            public bool OnlyNotifyAdmins { get; set; } = false;

            [JsonProperty("Enable Staging Notifications")]
            public bool EnableStaging { get; set; } = false;

            [JsonProperty("Checking Interval (in Seconds)")]
            public int CheckingInterval { get; set; } = 45;

            [JsonProperty("GUI Removal Delay (in Seconds)")]
            public int GuiRemovalDelay { get; set; } = 300;
        }

        protected override void LoadDefaultConfig()
        {
            _configuration = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _configuration = Config.ReadObject<Configuration>();
        }

        protected override void SaveConfig() => Config.WriteObject(_configuration);

        #endregion Configuration

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ServerUpdated"] = "Server Update Released!",
                ["ClientUpdated"] = "Client Update Released. Go Update Your Client!",
                ["StagingUpdated"] = "Staging Update Released!",
                ["FailedToCheckUpdates"] = "Failed to check for RUST updates, if this keeps happening please contact the developer."
            }, this);
        }

        #endregion Localization

        #region Hooks

        private void Init()
        {
            LoadConfig();
            permission.RegisterPermission(AdminPermission, this);
        }

        private void Loaded() => timer.Every(_configuration.CheckingInterval, CompareBuilds);

        #endregion Hooks

        #region Build Comparison

        private void CompareBuilds()
        {
            webrequest.Enqueue(ApiUrl, null, (code, response) =>
            {
                if (code != 200)
                {
                    if (code == 0) return;

                    PrintWarning(Lang("FailedToCheckUpdates") + "\nError Code: " + code + " | Message: " + response);
                    return;
                }

                var updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(response);

                if (_serverBuildId == 0 || _clientBuildId == 0 || _stagingBuildId == 0)
                {
                    _serverBuildId = updateInfo.ServerVersion;
                    _clientBuildId = updateInfo.ClientVersion;
                    _stagingBuildId = updateInfo.StagingVersion;
                }
                else
                {
                    bool serverUpdated = _serverBuildId != updateInfo.ServerVersion;
                    bool clientUpdated = _clientBuildId != updateInfo.ClientVersion;
                    bool stagingUpdated = _stagingBuildId != updateInfo.StagingVersion;

                    if (!serverUpdated && !clientUpdated && !stagingUpdated)
                        return;

                    if (serverUpdated)
                    {
                        _serverBuildId = updateInfo.ServerVersion;
                        DrawGuiForAll(Lang("ServerUpdated"));
                    }
                    if (clientUpdated)
                    {
                        _clientBuildId = updateInfo.ClientVersion;
                        DrawGuiForAll(Lang("ClientUpdated"));
                    }
                    if (stagingUpdated)
                    {
                        _stagingBuildId = updateInfo.StagingVersion;
                        if (_configuration.EnableStaging) DrawGuiForAll(Lang("StagingUpdated"));
                    }
                }
            }, this);
        }


        public class UpdateInfo
        {
            public int ServerVersion { get; set; }
            public int ClientVersion { get; set; }
            public int StagingVersion { get; set; }
        }

        #endregion Build Comparison

        #region Gui Handling

        private void RemoveGuiAfterDelay(int delay) => timer.Once(delay, RemoveGuiForAll);

        private void RemoveGuiForAll()
        {
            BasePlayer.activePlayerList.ForEach(RemoveGui);
            GuiTracker = 0;
            y = 0.98;
        }

        private void RemoveGui(BasePlayer player)
        {
            for (int i = 0; i <= GuiTracker; i++)
            {
                CuiHelper.DestroyUi(player, "UpdateNotice" + i.ToString());
            }
        }

        private void DrawGuiForAll(string message)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (_configuration.OnlyNotifyAdmins && !HasPermission(player, AdminPermission))
                    continue;

                AddGui(player, message);
            }

            RemoveGuiAfterDelay(_configuration.GuiRemovalDelay);
        }

        double y = 0.98;
        int GuiTracker = 0;
        private void AddGui(BasePlayer player, string message)
        {
            y = y - 0.025;
            GuiTracker++;
            var container = new CuiElementContainer();

            var panel = container.Add(new CuiPanel
            {
                Image =
                {
                    Color = "0.1 0.1 0.1 0.5"
                },
                RectTransform =
                {
                    AnchorMin = "0.012 " + y.ToString(), // left down
			        AnchorMax = "0.25 " + (y + 0.02).ToString() // right up
		        },
                CursorEnabled = false
            }, "Hud", "UpdateNotice" + GuiTracker.ToString());
            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = message,
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.0 8.0 0.0 1.0"
                },
                RectTransform =
                {
                    AnchorMin = "0.00 0.00",
                    AnchorMax = "1.00 1.00"
                }
            }, panel);
            CuiHelper.AddUi(player, container);
            y = y - 0.005;
        }

        #endregion Procedures

        #region Helpers

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        private bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.userID.ToString(), perm);

        #endregion Helpers
    }
}