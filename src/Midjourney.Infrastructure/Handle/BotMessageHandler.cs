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
using Discord;
using Discord.WebSocket;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// Bot message event handler
    /// </summary>
    public abstract class BotMessageHandler
    {
        protected DiscordLoadBalancer discordLoadBalancer;
        protected DiscordHelper discordHelper;

        public BotMessageHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        {
            this.discordLoadBalancer = discordLoadBalancer;
            this.discordHelper = discordHelper;
        }

        public abstract void Handle(DiscordInstance instance, MessageType messageType, SocketMessage message);

        public virtual int Order() => 100;

        protected string GetMessageContent(SocketMessage message)
        {
            return message.Content;
        }

        protected string GetFullPrompt(SocketMessage message)
        {
            return ConvertUtils.GetFullPrompt(message.Content);
        }

        protected string GetMessageId(SocketMessage message)
        {
            return message.Id.ToString();
        }

        protected string GetInteractionName(SocketMessage message)
        {
            return message?.Interaction?.Name ?? string.Empty;
        }

        protected string GetReferenceMessageId(SocketMessage message)
        {
            return message?.Reference?.MessageId.ToString() ?? string.Empty;
        }

        protected EBotType? GetBotType(SocketMessage message)
        {
            var botId = message.Author?.Id.ToString();
            EBotType? botType = null;

            if (botId == Constants.NIJI_APPLICATION_ID)
            {
                botType = EBotType.NIJI_JOURNEY;
            }
            else if (botId == Constants.MJ_APPLICATION_ID)
            {
                botType = EBotType.MID_JOURNEY;
            }

            return botType;
        }

        protected void FindAndFinishImageTask(DiscordInstance instance, TaskAction action, string finalPrompt, SocketMessage message)
        {
            // Skip "Waiting to start" messages
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            // Check if the message has already been processed
            CacheHelper<string, bool>.TryAdd(message.Id.ToString(), false);
            if (CacheHelper<string, bool>.Get(message.Id.ToString()))
            {
                Log.Debug("BOT message has already been processed {@0}", message.Id);
                return;
            }

            if (string.IsNullOrWhiteSpace(finalPrompt))
                return;

            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();

            if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();

                // If the task is found through meta id but the full prompt is empty, update the full prompt
                if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                {
                    task.PromptFull = fullPrompt;
                }
            }

            // If the task still cannot be found, it might be a NIJI task
            var botType = GetBotType(message);

            // Prioritize matching with full prompt
            if (task == null)
            {
                if (!string.IsNullOrWhiteSpace(fullPrompt))
                {
                    task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && (c.BotType == botType || c.RealBotType == botType ) && c.PromptFull == fullPrompt)
                    .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }


            if (task == null)
            {
                var prompt = finalPrompt.FormatPrompt();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                        .FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && (c.BotType == botType || c.RealBotType == botType)
                        && !string.IsNullOrWhiteSpace(c.PromptEn)
                        && (c.PromptEn.FormatPrompt() == prompt || c.PromptEn.FormatPrompt().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPrompt())))
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
                else
                {
                    // If the final prompt is empty, it might be a redraw or blend task
                    task = instance
                        .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        (c.BotType == botType || c.RealBotType == botType) && c.Action == action)
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }

            // If the task still cannot be found, retain the prompt link for matching
            if (task == null)
            {
                var prompt = finalPrompt.FormatPromptParam();
                if (!string.IsNullOrWhiteSpace(prompt))
                {

                    task = instance
                            .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) && !string.IsNullOrWhiteSpace(c.PromptEn)
                            && (c.PromptEn.FormatPromptParam() == prompt || c.PromptEn.FormatPromptParam().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPromptParam())))
                            .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }

            // If it is a show job task
            if (task == null && action == TaskAction.SHOW)
            {
                task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                (c.BotType == botType || c.RealBotType == botType ) && c.Action == TaskAction.SHOW && c.JobId == messageHash).OrderBy(c => c.StartTime).FirstOrDefault();
            }

            if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
            {
                return;
            }

            task.MessageId = msgId;

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            task.ImageUrl = imageUrl;
            task.JobId = messageHash;

            FinishTask(task, message);

            task.Awake();
        }

        protected void FinishTask(TaskInfo task, SocketMessage message)
        {
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id.ToString());
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(task.ImageUrl));

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(c =>
                {
                    if (c is ButtonComponent btn)
                    {
                        return new CustomComponentModel
                        {
                            CustomId = btn.CustomId?.ToString() ?? string.Empty,
                            Emoji = btn.Emote?.Name ?? string.Empty,
                            Label = btn.Label ?? string.Empty,
                            Style = (int?)btn.Style ?? 0,
                            Type = (int?)btn.Type ?? 0,
                        };
                    }
                    return null;
                }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                task.Description = "Submit success";
            }
            if (string.IsNullOrWhiteSpace(task.FailReason))
            {
                task.FailReason = "";
            }
            if (string.IsNullOrWhiteSpace(task.State))
            {
                task.State = "";
            }

            task.Success();

            // Indicate that the message has been processed
            CacheHelper<string, bool>.AddOrUpdate(message.Id.ToString(), true);

            Log.Debug("Message processing completed by BOT {@0}", message.Id);
        }

        protected bool HasImage(SocketMessage message)
        {
            return message?.Attachments?.Count > 0;
        }

        protected string GetImageUrl(SocketMessage message)
        {
            if (message?.Attachments?.Count > 0)
            {
                return ReplaceCdnUrl(message.Attachments.FirstOrDefault()?.Url);
            }

            return default;
        }

        protected string ReplaceCdnUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return imageUrl;

            string cdn = discordHelper.GetCdn();
            if (imageUrl.StartsWith(cdn)) return imageUrl;

            return imageUrl.Replace(DiscordHelper.DISCORD_CDN_URL, cdn);
        }
    }
}