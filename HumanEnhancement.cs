using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlayerRoles;
using Exiled.API.Features;
using Exiled.API.Enums;
using PlayerStatsSystem;
using MEC;
using Exiled.API.Features.Roles;

namespace SCPTeammateHUD
{
    public class SCPTeammateHUD : Plugin<Config>
    {
        public override string Name => "SCPTeammateHUD";
        public override string Author => "YourName";
        public override Version Version => new Version(5, 0, 0);
        public override PluginPriority Priority => PluginPriority.Low;

        private const float UpdateInterval = 0.7f;
        private CoroutineHandle _updateCoroutine;
        private Dictionary<Player, string> _lastHudCache = new Dictionary<Player, string>();
        private Dictionary<Player, DateTime> _lastUpdateTime = new Dictionary<Player, DateTime>();

        // 自定义HUD参数
        private const float HUD_DURATION = 2.0f; // 显示持续时间
        private const string HUD_FORMAT = "<voffset=1em><align=left><size=50%>{0}</size></align></voffset>"; // 左上角显示

        public override void OnEnabled()
        {
            // 注册事件监听
            Exiled.Events.Handlers.Player.ChangingRole += OnPlayerChangingRole;
            Exiled.Events.Handlers.Player.Died += OnPlayerDied;
            Exiled.Events.Handlers.Player.Left += OnPlayerLeft;

            _updateCoroutine = Timing.RunCoroutine(UpdateHUD());
            Log.Info("SCP队友状态显示器已启用");
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            // 取消事件监听
            Exiled.Events.Handlers.Player.ChangingRole -= OnPlayerChangingRole;
            Exiled.Events.Handlers.Player.Died -= OnPlayerDied;
            Exiled.Events.Handlers.Player.Left -= OnPlayerLeft;

            Timing.KillCoroutines(_updateCoroutine);

            // 清除所有玩家的HUD
            foreach (var player in Player.List)
            {
                ClearPlayerHUD(player);
            }

            _lastHudCache.Clear();
            _lastUpdateTime.Clear();

            Log.Info("SCP队友状态显示器已禁用");
            base.OnDisabled();
        }

        // 玩家角色变更事件
        private void OnPlayerChangingRole(Exiled.Events.EventArgs.Player.ChangingRoleEventArgs ev)
        {
            // 清除旧角色的HUD
            ClearPlayerHUD(ev.Player);

            // 如果是SCP角色，强制更新HUD
            if (ev.NewRole.GetTeam() == Team.SCPs)
            {
                Timing.CallDelayed(0.5f, () => UpdatePlayerHUD(ev.Player, true));
            }
        }

        // 玩家死亡事件
        private void OnPlayerDied(Exiled.Events.EventArgs.Player.DiedEventArgs ev)
        {
            ClearPlayerHUD(ev.Player);
        }

        // 玩家离开服务器事件
        private void OnPlayerLeft(Exiled.Events.EventArgs.Player.LeftEventArgs ev)
        {
            ClearPlayerHUD(ev.Player);
            if (_lastHudCache.ContainsKey(ev.Player))
            {
                _lastHudCache.Remove(ev.Player);
            }
            if (_lastUpdateTime.ContainsKey(ev.Player))
            {
                _lastUpdateTime.Remove(ev.Player);
            }
        }

        // 清除玩家的HUD
        private void ClearPlayerHUD(Player player)
        {
            player.ShowHint("", 0.1f); // 立即清除提示
            if (_lastHudCache.ContainsKey(player))
            {
                _lastHudCache[player] = "";
            }
        }

        private IEnumerator<float> UpdateHUD()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(UpdateInterval);

                try
                {
                    // 获取所有存活的SCP玩家
                    var aliveSCPs = Player.Get(Team.SCPs).Where(p => p.IsAlive).ToList();

                    // 为每个SCP更新HUD
                    foreach (Player player in aliveSCPs)
                    {
                        UpdatePlayerHUD(player, false);
                    }

                    // 清除已死亡或离开玩家的缓存
                    var playersToRemove = _lastHudCache.Keys
                        .Where(p => !aliveSCPs.Contains(p) || !p.IsAlive)
                        .ToList();

                    foreach (var player in playersToRemove)
                    {
                        ClearPlayerHUD(player);
                        _lastHudCache.Remove(player);
                        _lastUpdateTime.Remove(player);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"更新HUD时出错: {e}");
                }
            }
        }

        // 更新单个玩家的HUD
        private void UpdatePlayerHUD(Player player, bool forceUpdate)
        {
            try
            {
                // 确保玩家是存活的SCP
                if (player.Role == null || player.Role.Team != Team.SCPs || !player.IsAlive)
                    return;

                // 获取所有存活的SCP队友（排除自己）
                var teammates = Player.Get(Team.SCPs)
                    .Where(p => p != player && p.IsAlive)
                    .ToList();

                // 如果没有队友，清除HUD
                if (teammates.Count == 0)
                {
                    if (_lastHudCache.ContainsKey(player) && !string.IsNullOrEmpty(_lastHudCache[player]))
                    {
                        ClearPlayerHUD(player);
                    }
                    return;
                }

                // 生成队友信息
                StringBuilder hudBuilder = new StringBuilder();

                foreach (var teammate in teammates)
                {
                    if (teammate.Role == null) continue;

                    string scpTag = GetSCPEmojiAndName(teammate.Role);
                    int health = (int)teammate.Health;
                    int humeShield = GetHumeShieldValue(teammate); // 获取休谟护盾值

                    // 添加白色昵称显示
                    hudBuilder.Append(
                        $"[<color=white>{teammate.Nickname}</color>] " +
                        $"{scpTag} | " +
                        $"<color=red>{health}HP</color>  " +
                        $"<color=#00FFFF>{humeShield}HS</color>\n"
                    );
                }

                // 移除最后一个多余的换行符
                if (hudBuilder.Length > 0)
                    hudBuilder.Length--;

                // 格式化HUD内容
                string newHUD = string.Format(HUD_FORMAT, hudBuilder.ToString());

                // 获取上次更新时间和最后HUD内容
                DateTime lastUpdate = DateTime.MinValue;
                string lastHUD = "";

                if (_lastUpdateTime.ContainsKey(player))
                    lastUpdate = _lastUpdateTime[player];
                if (_lastHudCache.ContainsKey(player))
                    lastHUD = _lastHudCache[player];

                // 检查是否需要更新
                bool needsUpdate = forceUpdate ||
                                  lastHUD != newHUD ||
                                  (DateTime.Now - lastUpdate).TotalSeconds > HUD_DURATION * 0.8;

                if (needsUpdate)
                {
                    // 显示提示
                    player.ShowHint(newHUD, HUD_DURATION);

                    // 更新缓存
                    _lastHudCache[player] = newHUD;
                    _lastUpdateTime[player] = DateTime.Now;

                    if (Config.Debug)
                    {
                        Log.Debug($"为玩家 {player.Nickname} 更新HUD:\n{newHUD}");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"更新玩家 {player?.Nickname ?? "null"} HUD时出错: {e}");
            }
        }

        // 获取SCP的休谟护盾值
        private int GetHumeShieldValue(Player player)
        {
            try
            {
                // 休谟护盾通常通过HumeShieldStat管理
                if (player.ReferenceHub.playerStats.GetModule<HumeShieldStat>() is HumeShieldStat humeShield)
                {
                    return (int)humeShield.CurValue;
                }
            }
            catch (Exception e)
            {
                Log.Error($"获取休谟护盾值时出错: {e}");
            }
            return 0;
        }

        // 获取带emoji的SCP名称
        private string GetSCPEmojiAndName(Role role)
        {
            if (role == null) return "❓ UNKNOWN";

            switch (role.Type)
            {
                case RoleTypeId.Scp049:
                    return "🧪 049"; // 试管emoji
                case RoleTypeId.Scp0492:
                    return "🧟 049-2"; // 僵尸emoji
                case RoleTypeId.Scp079:
                    return "💻 079"; // 电脑emoji
                case RoleTypeId.Scp096:
                    return "😢 096"; // 哭泣emoji
                case RoleTypeId.Scp106:
                    return "👴 106"; // 老人emoji
                case RoleTypeId.Scp173:
                    return "🗿 173"; // 雕像emo1ji
                case RoleTypeId.Scp939:
                    return "🐺 939"; // 狼emoji
                default:
                    return $"🔷 {role.Type.ToString().Replace("Scp", "")}"; // 蓝色方块emoji
            }
        }
    }

    public class Config : Exiled.API.Interfaces.IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;

        // 可选：添加配置项控制emoji显示
        public bool ShowEmojis { get; set; } = true;
    }
}