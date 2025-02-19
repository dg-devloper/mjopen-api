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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Storage;
using Serilog;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// Task class, representing basic information of a task.
    /// </summary>
    [BsonCollection("task")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    public class TaskInfo : DomainObject
    {
        public TaskInfo()
        {
        }

        /// <summary>
        /// Parent ID
        /// </summary>
        public string ParentId { get; set; }

        /// <summary>
        /// Bot type, mj (default) or niji
        /// MID_JOURNEY | Enum value: NIJI_JOURNEY
        /// </summary>
        public EBotType BotType { get; set; }

        /// <summary>
        /// Actual bot type, mj (default) or niji
        /// When niji->mj is enabled, mj bot is recorded here
        /// </summary>
        public EBotType? RealBotType { get; set; }

        /// <summary>
        /// User ID for the drawing
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Customer ID, untuk menandai user di dalam sistem MJCORE
        /// </summary>
        public string CustomerId { get; set; }

        /// <summary>
        /// Whitelist user (not affected by rate limits)
        /// </summary>
        public bool IsWhite { get; set; } = false;

        /// <summary>
        /// Unique ID of the submitted task.
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// ID returned after successfully interacting with MJ.
        /// </summary>
        public string InteractionMetadataId { get; set; }

        /// <summary>
        /// Final message ID from MJ (Nonce -> MessageId)
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Remix modal message ID
        /// </summary>
        public string RemixModalMessageId { get; set; }

        /// <summary>
        /// Indicates whether it is a Remix auto-submit task
        /// </summary>
        public bool RemixAutoSubmit { get; set; }

        /// <summary>
        /// Whether the Remix modal is currently active
        /// </summary>
        public bool RemixModaling { get; set; }

        /// <summary>
        /// Account instance ID = channel ID
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Sub-channel ID
        /// </summary>
        public string SubInstanceId { get; set; }

        /// <summary>
        /// List of message IDs (creation -> progress -> completion)
        /// </summary>
        public List<string> MessageIds { get; set; } = new List<string>();

        /// <summary>
        /// Task type
        /// </summary>
        public TaskAction? Action { get; set; }

        /// <summary>
        /// Task status
        /// </summary>
        public TaskStatus? Status { get; set; }

        /// <summary>
        /// Prompt
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// Prompt (English)
        /// </summary>
        public string PromptEn { get; set; }

        /// <summary>
        /// Prompt (full prompt returned by MJ)
        /// </summary>
        public string PromptFull { get; set; }

        /// <summary>
        /// Task description
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Custom parameters
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// Submission time
        /// </summary>
        public long? SubmitTime { get; set; }

        /// <summary>
        /// Start time
        /// </summary>
        public long? StartTime { get; set; }

        /// <summary>
        /// Finish time
        /// </summary>
        public long? FinishTime { get; set; }

        /// <summary>
        /// Image URL
        /// </summary>
        public string ImageUrl { get; set; }

        /// <summary>
        /// Thumbnail URL
        /// </summary>
        public string ThumbnailUrl { get; set; }

        /// <summary>
        /// Task progress
        /// </summary>
        public string Progress { get; set; }

        /// <summary>
        /// Reason for failure
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// Buttons
        /// </summary>
        public List<CustomComponentModel> Buttons { get; set; } = new List<CustomComponentModel>();

        /// <summary>
        /// Display information of the task
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, object> Displays
        {
            get
            {
                var dic = new Dictionary<string, object>();

                // Status
                dic["status"] = Status.ToString();

                // Convert to visual time
                dic["submitTime"] = SubmitTime?.ToDateTimeString();
                dic["startTime"] = StartTime?.ToDateTimeString();
                dic["finishTime"] = FinishTime?.ToDateTimeString();

                // Action
                dic["action"] = Action.ToString();

                // Discord instance ID
                dic["discordInstanceId"] = Properties.ContainsKey("discordInstanceId") ? Properties["discordInstanceId"] : "";

                return dic;
            }
        }

        /// <summary>
        /// Task seed
        /// </summary>
        public string Seed { get; set; }

        /// <summary>
        /// Seed message ID
        /// </summary>
        public string SeedMessageId { get; set; }

        /// <summary>
        /// Client IP address for the drawing
        /// </summary>
        public string ClientIp { get; set; }

        /// <summary>
        /// Image ID / image hash
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// Whether this is a replicate task
        /// </summary>
        public bool IsReplicate { get; set; }

        /// <summary>
        /// Face source image
        /// </summary>
        public string ReplicateSource { get; set; }

        /// <summary>
        /// Target image or video
        /// </summary>
        public string ReplicateTarget { get; set; }

        /// <summary>
        /// Speed mode specified by the current drawing client
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// Account filter
        /// </summary>
        public AccountFilter AccountFilter { get; set; }

        /// <summary>
        /// Start the task.
        /// </summary>
        public void Start()
        {
            StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUBMITTED;
            Progress = "0%";
        }

        /// <summary>
        /// Task succeeded.
        /// </summary>
        public void Success()
        {
            try
            {
                // Save image
                StorageHelper.DownloadFile(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save image {@0}", ImageUrl);
            }

            // Adjust image ACTION
            // If it is show
            if (Action == TaskAction.SHOW)
            {
                // Adjust based on buttons
                if (Buttons.Count > 0)
                {
                    // U1
                    if (Buttons.Any(x => x.CustomId?.Contains("MJ::JOB::upsample::1") == true))
                    {
                        Action = TaskAction.IMAGINE;
                    }
                    // Local redraw means upscale
                    else if (Buttons.Any(x => x.CustomId?.Contains("MJ::Inpaint::") == true))
                    {
                        Action = TaskAction.UPSCALE;
                    }
                    // MJ::Job::PicReader
                    else if (Buttons.Any(x => x.CustomId?.Contains("MJ::Job::PicReader") == true))
                    {
                        Action = TaskAction.DESCRIBE;
                    }
                }
            }

            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUCCESS;
            Progress = "100%";
        }

        /// <summary>
        /// Task failed.
        /// </summary>
        /// <param name="reason">Reason for failure.</param>
        public void Fail(string reason)
        {
            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.FAILURE;
            FailReason = reason;
            Progress = "";

            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (reason.Contains("Banned prompt detected", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("Image denied", StringComparison.OrdinalIgnoreCase))
                {
                    // Trigger prompt ban
                    var band = GlobalConfiguration.Setting?.BannedLimiting;
                    var cache = GlobalConfiguration.MemoryCache;

                    // Record cumulative trigger count
                    if (band?.Enable == true && cache != null)
                    {
                        if (!string.IsNullOrWhiteSpace(UserId))
                        {
                            // user band
                            var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{UserId}";
                            cache.TryGetValue(bandKey, out int limit);
                            limit++;
                            cache.Set(bandKey, limit, TimeSpan.FromDays(1));
                        }

                        if (true)
                        {
                            // ip band
                            var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{ClientIp}";
                            cache.TryGetValue(bandKey, out int limit);
                            limit++;
                            cache.Set(bandKey, limit, TimeSpan.FromDays(1));
                        }
                    }
                }
            }
        }
    }
}