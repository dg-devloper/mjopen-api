// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using LiteDB;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// Discord account class.
    /// </summary>
    [BsonCollection("account")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    public class DiscordAccount : DomainObject
    {
        public DiscordAccount()
        {
        }

        /// <summary>
        /// Channel ID = ID.
        /// </summary>
        [Display(Name = "频道ID")]
        public string ChannelId { get; set; }

        /// <summary>
        /// Server ID.
        /// </summary>
        [Display(Name = "服务器ID")]
        public string GuildId { get; set; }

        /// <summary>
        /// Mj private channel ID (to receive seed values).
        /// </summary>
        [Display(Name = "私信频道ID")]
        public string PrivateChannelId { get; set; }

        /// <summary>
        /// Niji private channel ID (to receive seed values).
        /// </summary>
        public string NijiBotChannelId { get; set; }

        /// <summary>
        /// User token.
        /// </summary>
        [Display(Name = "用户Token")]
        public string UserToken { get; set; }

        /// <summary>
        /// Bot token.
        /// </summary>
        [Display(Name = "机器人Token")]
        public string BotToken { get; set; }

        /// <summary>
        /// User agent.
        /// </summary>
        [Display(Name = "用户UserAgent")]
        public string UserAgent { get; set; } = Constants.DEFAULT_DISCORD_USER_AGENT;

        /// <summary>
        /// Whether enabled.
        /// </summary>
        public bool? Enable { get; set; }

        /// <summary>
        /// Enable Midjourney drawing.
        /// </summary>
        public bool? EnableMj { get; set; }

        /// <summary>
        /// Enable Niji drawing.
        /// </summary>
        public bool? EnableNiji { get; set; }

        /// <summary>
        /// Enable fast mode to relax mode.
        /// </summary>
        public bool? EnableFastToRelax { get; set; }

        /// <summary>
        /// Enable relax mode to fast mode.
        /// </summary>
        public bool? EnableRelaxToFast { get; set; }

        /// <summary>
        /// Indicates whether fast mode is exhausted.
        /// </summary>
        public bool FastExhausted { get; set; }

        /// <summary>
        /// Whether locked (temporarily locked, possibly triggered human verification).
        /// </summary>
        public bool Lock { get; set; }

        /// <summary>
        /// Disabled reason.
        /// </summary>
        public string DisabledReason { get; set; }

        /// <summary>
        /// Permanent invitation link for the current channel.
        /// </summary>
        public string PermanentInvitationLink { get; set; }

        /// <summary>
        /// Human verification hash URL creation time.
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? CfHashCreated { get; set; }

        /// <summary>
        /// Human verification hash URL.
        /// </summary>
        public string CfHashUrl { get; set; }

        /// <summary>
        /// Human verification URL.
        /// </summary>
        public string CfUrl { get; set; }

        /// <summary>
        /// Whether sponsor.
        /// </summary>
        public bool IsSponsor { get; set; }

        /// <summary>
        /// Sponsor user ID.
        /// </summary>
        public string SponsorUserId { get; set; }

        /// <summary>
        /// Concurrency count.
        /// </summary>
        [Display(Name = "并发数")]
        public int CoreSize { get; set; } = 3;

        /// <summary>
        /// Task execution interval time (seconds, default: 1.2s).
        /// </summary>
        public decimal Interval { get; set; } = 1.2m;

        /// <summary>
        /// Minimum interval time after task execution (seconds, default: 1.2s).
        /// </summary>
        public decimal AfterIntervalMin { get; set; } = 1.2m;

        /// <summary>
        /// Maximum interval time after task execution (seconds, default: 1.2s).
        /// </summary>
        public decimal AfterIntervalMax { get; set; } = 1.2m;

        /// <summary>
        /// Queue size.
        /// </summary>
        [Display(Name = "等待队列长度")]
        public int QueueSize { get; set; } = 10;

        /// <summary>
        /// Maximum queue size.
        /// </summary>
        [Display(Name = "等待最大队列长度")]
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Timeout in minutes.
        /// </summary>
        [Display(Name = "任务超时时间（分钟）")]
        public int TimeoutMinutes { get; set; } = 5;

        /// <summary>
        /// Note.
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// Sponsor (rich text).
        /// </summary>
        public string Sponsor { get; set; }

        /// <summary>
        /// Creation date.
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime DateCreated { get; set; } = DateTime.Now;

        /// <summary>
        /// Mj info update time.
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? InfoUpdated { get; set; }

        /// <summary>
        /// Weight.
        /// </summary>
        public int Weight { get; set; }

        /// <summary>
        /// Work time (no tasks accepted outside these hours).
        /// </summary>
        public string WorkTime { get; set; }

        /// <summary>
        /// Fishing time (accepts only ongoing tasks, no new ones).
        /// </summary>
        public string FishingTime { get; set; }

        /// <summary>
        /// Indicates whether to accept new tasks.
        /// 1. Within work hours.
        /// 2. Not within fishing hours.
        /// 3. Does not exceed the maximum task limit.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool IsAcceptNewTask
        {
            get
            {
                // If both work time and fishing time are empty
                if (string.IsNullOrWhiteSpace(WorkTime) && string.IsNullOrWhiteSpace(FishingTime))
                {
                    if (DayDrawLimit <= -1 || DayDrawCount < DayDrawLimit)
                    {
                        return true;
                    }
                }

                // If within work hours and not within fishing hours
                if (DateTime.Now.IsInWorkTime(WorkTime) && !DateTime.Now.IsInFishTime(FishingTime))
                {
                    if (DayDrawLimit <= -1 || DayDrawCount < DayDrawLimit)
                    {
                        return true;
                    }
                }

                // Indicates not accepting new tasks
                return false;
            }
        }

        /// <summary>
        /// Sort.
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// Remix auto-submit.
        /// </summary>
        public bool RemixAutoSubmit { get; set; }

        /// <summary>
        /// Specify generation speed mode --fast, --relax, or --turbo parameter at the end.
        /// </summary>
        [Display(Name = "生成速度模式 fast | relax | turbo")]
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// Allowed speed modes (if an unsupported speed mode appears, the keyword will be automatically cleared).
        /// </summary>
        public List<GenerationSpeedMode> AllowModes { get; set; } = new List<GenerationSpeedMode>();

        /// <summary>
        /// Auto set to relax mode
        /// When enabled, if fast mode is exhausted and the allowed generation speed mode is FAST or TURBO, the original mode will be automatically cleared and set to RELAX mode.
        /// </summary>
        public bool? EnableAutoSetRelax { get; set; }

        /// <summary>
        /// MJ component list.
        /// </summary>
        public List<Component> Components { get; set; } = new List<Component>();

        /// <summary>
        /// MJ settings message ID.
        /// </summary>
        public string SettingsMessageId { get; set; }

        /// <summary>
        /// NIJI component list.
        /// </summary>
        public List<Component> NijiComponents { get; set; } = new List<Component>();

        /// <summary>
        /// NIJI settings message ID.
        /// </summary>
        public string NijiSettingsMessageId { get; set; }

        /// <summary>
        /// Enable Blend feature.
        /// </summary>
        public bool IsBlend { get; set; } = true;

        /// <summary>
        /// Enable Describe feature.
        /// </summary>
        public bool IsDescribe { get; set; } = true;

        /// <summary>
        /// Enable Shorten feature.
        /// </summary>
        public bool IsShorten { get; set; } = true;

        /// <summary>
        /// Daily drawing limit, default -1 for no limit.
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

        /// <summary>
        /// Daily drawing count (refreshes every 5 minutes).
        /// </summary>
        public int DayDrawCount { get; set; } = 0;

        /// <summary>
        /// Whether to enable vertical domain.
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// Vertical domain IDs.
        /// </summary>
        public List<string> VerticalDomainIds { get; set; } = new List<string>();

        /// <summary>
        /// Sub-channel list.
        /// </summary>
        public List<string> SubChannels { get; set; } = new List<string>();

        /// <summary>
        /// Sub-channel IDs calculated from SubChannels.
        /// key: channel ID, value: server ID
        /// </summary>
        public Dictionary<string, string> SubChannelValues { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Number of running tasks.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public int RunningCount { get; set; }

        /// <summary>
        /// Number of tasks in queue.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public int QueueCount { get; set; }

        /// <summary>
        /// Whether WSS is running.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool Running { get; set; }

        /// <summary>
        /// Mj buttons.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public List<CustomComponentModel> Buttons => Components.Where(c => c.Id != 1).SelectMany(x => x.Components)
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.CustomId?.ToString() ?? string.Empty,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty,
                    Style = c.Style ?? 0,
                    Type = (int?)c.Type ?? 0,
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// Whether Mj remix mode is enabled.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool MjRemixOn => Buttons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// Whether Mj fast mode is enabled.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool MjFastModeOn =>
            Buttons.Any(x => (x.Label == "Fast mode" || x.Label == "Turbo mode") && x.Style == 3);

        /// <summary>
        /// Niji buttons.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public List<CustomComponentModel> NijiButtons => NijiComponents.SelectMany(x => x.Components)
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.CustomId?.ToString() ?? string.Empty,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty,
                    Style = c.Style ?? 0,
                    Type = (int?)c.Type ?? 0,
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// Whether Niji remix mode is enabled.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public bool NijiRemixOn => NijiButtons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// Whether Niji fast mode is enabled.
        /// </summary>
        public bool NijiFastModeOn =>
            NijiButtons.Any(x => (x.Label == "Fast mode" || x.Label == "Turbo mode") && x.Style == 3);

        /// <summary>
        /// Mj dropdown.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public List<CustomComponentModel> VersionSelector => Components.Where(c => c.Id == 1)
            .FirstOrDefault()?.Components?.FirstOrDefault()?.Options
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.Value,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// Default dropdown value.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public string Version => Components.Where(c => c.Id == 1)
            .FirstOrDefault()?.Components?.FirstOrDefault()?.Options
            .Where(c => c.Default == true).FirstOrDefault()?.Value;

        /// <summary>
        /// Display information.
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public Dictionary<string, object> Displays
        {
            get
            {
                var dic = new Dictionary<string, object>();

                // Standard (Active monthly, renews next on <t:1722649226>)"
                var plan = Properties.ContainsKey("Subscription") ? Properties["Subscription"].ToString() : "";

                // Regular expression to capture subscribePlan, billedWay, and timestamp
                var pattern = @"([A-Za-z\s]+) \(([A-Za-z\s]+), renews next on <t:(\d+)\>\)";
                var match = Regex.Match(plan, pattern);
                if (match.Success)
                {
                    string subscribePlan = match.Groups[1].Value;
                    string billedWay = match.Groups[2].Value;
                    string timestamp = match.Groups[3].Value;

                    dic["subscribePlan"] = subscribePlan.Trim();
                    dic["billedWay"] = billedWay.Trim();
                    dic["renewDate"] = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                }

                dic["mode"] = Properties.ContainsKey("Job Mode") ? Properties["Job Mode"] : "";
                dic["nijiMode"] = Properties.ContainsKey("Niji Job Mode") ? Properties["Niji Job Mode"] : "";

                return dic;
            }
        }

        /// <summary>
        /// Fast time remaining.
        /// </summary>
        public object FastTimeRemaining => Properties.ContainsKey("Fast Time Remaining") ? Properties["Fast Time Remaining"] : "";

        /// <summary>
        /// Relaxed usage.
        /// </summary>
        public object RelaxedUsage => Properties.ContainsKey("Relaxed Usage") ? Properties["Relaxed Usage"] : "";

        /// <summary>
        /// Turbo usage.
        /// </summary>
        public object TurboUsage => Properties.ContainsKey("Turbo Usage") ? Properties["Turbo Usage"] : "";

        /// <summary>
        /// Fast usage.
        /// </summary>
        public object FastUsage => Properties.ContainsKey("Fast Usage") ? Properties["Fast Usage"] : "";

        /// <summary>
        /// Lifetime usage.
        /// </summary>
        public object LifetimeUsage => Properties.ContainsKey("Lifetime Usage") ? Properties["Lifetime Usage"] : "";

        /// <summary>
        /// Get display name.
        /// </summary>
        /// <returns>Channel ID.</returns>
        public string GetDisplay()
        {
            return ChannelId;
        }

        /// <summary>
        /// Create Discord account.
        /// </summary>
        /// <param name="configAccount"></param>
        /// <returns></returns>
        public static DiscordAccount Create(DiscordAccountConfig configAccount)
        {
            if (configAccount.Interval < 1.2m)
            {
                configAccount.Interval = 1.2m;
            }

            return new DiscordAccount
            {
                Id = Guid.NewGuid().ToString(),
                ChannelId = configAccount.ChannelId,

                UserAgent = string.IsNullOrEmpty(configAccount.UserAgent) ? Constants.DEFAULT_DISCORD_USER_AGENT : configAccount.UserAgent,
                GuildId = configAccount.GuildId,
                UserToken = configAccount.UserToken,
                Enable = configAccount.Enable,
                CoreSize = configAccount.CoreSize,
                QueueSize = configAccount.QueueSize,
                BotToken = configAccount.BotToken,
                TimeoutMinutes = configAccount.TimeoutMinutes,
                PrivateChannelId = configAccount.PrivateChannelId,
                NijiBotChannelId = configAccount.NijiBotChannelId,
                MaxQueueSize = configAccount.MaxQueueSize,
                Mode = configAccount.Mode,
                AllowModes = configAccount.AllowModes,
                Weight = configAccount.Weight,
                Remark = configAccount.Remark,
                RemixAutoSubmit = configAccount.RemixAutoSubmit,
                Sponsor = configAccount.Sponsor,
                IsSponsor = configAccount.IsSponsor,
                Sort = configAccount.Sort,
                Interval = configAccount.Interval,

                AfterIntervalMax = configAccount.AfterIntervalMax,
                AfterIntervalMin = configAccount.AfterIntervalMin,
                WorkTime = configAccount.WorkTime,
                FishingTime = configAccount.FishingTime,
                PermanentInvitationLink = configAccount.PermanentInvitationLink,

                SubChannels = configAccount.SubChannels,
                IsBlend = configAccount.IsBlend,
                VerticalDomainIds = configAccount.VerticalDomainIds,
                IsVerticalDomain = configAccount.IsVerticalDomain,
                IsDescribe = configAccount.IsDescribe,
                IsShorten = configAccount.IsShorten,
                DayDrawLimit = configAccount.DayDrawLimit,
                EnableMj = configAccount.EnableMj,
                EnableNiji = configAccount.EnableNiji,
                EnableFastToRelax = configAccount.EnableFastToRelax,
                EnableRelaxToFast = configAccount.EnableRelaxToFast,
                EnableAutoSetRelax = configAccount.EnableAutoSetRelax,
            };
        }

        /// <summary>
        /// Initialize sub-channels.
        /// </summary>
        public void InitSubChannels()
        {
            // Pre-start validation
            if (SubChannels.Count > 0)
            {
                // https://discord.com/channels/1256526716130693201/1256526716130693204
                // https://discord.com/channels/{guid}/{id}
                // {guid} and {id} are both pure numbers

                var dic = new Dictionary<string, string>();
                foreach (var item in SubChannels)
                {
                    if (string.IsNullOrWhiteSpace(item) || !item.Contains("https://discord.com/channels"))
                    {
                        continue;
                    }

                    // {id} as key, {guid} as value
                    var fir = item.Split(',').Where(c => c.Contains("https://discord.com/channels")).FirstOrDefault();
                    if (fir == null)
                    {
                        continue;
                    }

                    var arr = fir.Split('/').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                    if (arr.Length < 5)
                    {
                        continue;
                    }

                    var guid = arr[3];
                    var id = arr[4];

                    dic[id] = guid;
                }

                SubChannelValues = dic;
            }
            else
            {
                SubChannels.Clear();
                SubChannelValues.Clear();
            }
        }

        /// <summary>
        /// Sponsor account validation.
        /// </summary>
        public void SponsorValidate()
        {
            if (DayDrawLimit > 0 && DayDrawLimit < 10)
            {
                DayDrawLimit = 10;
            }

            if (CoreSize <= 0)
            {
                CoreSize = 1;
            }

            if (QueueSize <= 0)
            {
                QueueSize = 1;
            }

            if (MaxQueueSize <= 0)
            {
                MaxQueueSize = 1;
            }

            if (TimeoutMinutes < 5)
            {
                TimeoutMinutes = 5;
            }

            if (TimeoutMinutes > 30)
            {
                TimeoutMinutes = 30;
            }

            if (Interval > 180)
            {
                Interval = 180;
            }

            if (AfterIntervalMin > 180)
            {
                AfterIntervalMin = 180;
            }

            if (AfterIntervalMax > 180)
            {
                AfterIntervalMax = 180;
            }

            if (EnableMj != true)
            {
                EnableMj = true;
            }

            if (Sponsor?.Length > 1000)
            {
                Sponsor = Sponsor.Substring(0, 1000);
            }
        }
    }
}