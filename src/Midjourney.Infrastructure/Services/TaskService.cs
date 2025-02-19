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
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// Task service implementation class, handles specific operations of tasks
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ITaskStoreService _taskStoreService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;

        public TaskService(ITaskStoreService taskStoreService, DiscordLoadBalancer discordLoadBalancer, IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
        }

        /// <summary>
        /// Get domain cache
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetDomainCache()
        {
            return _memoryCache.GetOrCreate("domains", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
                var list = DbHelper.Instance.DomainStore.GetAll().Where(c => c.Enable);

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            });
        }

        /// <summary>
        /// Clear domain cache
        /// </summary>
        public void ClearDomainCache()
        {
            _memoryCache.Remove("domains");
        }

        /// <summary>
        /// Get banned words cache
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetBannedWordsCache()
        {
            return _memoryCache.GetOrCreate("bannedWords", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
                var list = DbHelper.Instance.BannedWordStore.GetAll().Where(c => c.Enable);

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            });
        }

        /// <summary>
        /// Clear banned words cache
        /// </summary>
        public void ClearBannedWordsCache()
        {
            _memoryCache.Remove("bannedWords");
        }

        /// <summary>
        /// Validate banned words
        /// </summary>
        /// <param name="promptEn"></param>
        /// <exception cref="BannedPromptException"></exception>
        public void CheckBanned(string promptEn)
        {
            var finalPromptEn = promptEn.ToLower(CultureInfo.InvariantCulture);

            var dic = GetBannedWordsCache();
            foreach (var item in dic)
            {
                foreach (string word in item.Value)
                {
                    var regex = new Regex($"\\b{Regex.Escape(word)}\\b", RegexOptions.IgnoreCase);
                    var match = regex.Match(finalPromptEn);
                    if (match.Success)
                    {
                        int index = finalPromptEn.IndexOf(word, StringComparison.OrdinalIgnoreCase);

                        throw new BannedPromptException(promptEn.Substring(index, word.Length));
                    }
                }
            }
        }

        /// <summary>
        /// Submit imagine task.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrls"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitImagine(TaskInfo info, List<DataUrl> dataUrls)
        {
            // Check if vertical domain is enabled
            var domainIds = new List<string>();
            var isDomain = GlobalConfiguration.Setting.IsVerticalDomain;
            if (isDomain)
            {
                // Split Promat into individual words
                // Use ',' ' ' '.' '-' as delimiters
                // And filter out empty strings
                var prompts = info.Prompt.Split(new char[] { ',', ' ', '.', '-' })
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c?.Trim()?.ToLower())
                    .Distinct().ToList();

                var domains = GetDomainCache();
                foreach (var prompt in prompts)
                {
                    foreach (var domain in domains)
                    {
                        if (domain.Value.Contains(prompt) || domain.Value.Contains($"{prompt}s"))
                        {
                            domainIds.Add(domain.Key);
                        }
                    }
                }

                // If no domain is found, do not use domain account
                if (domainIds.Count == 0)
                {
                    isDomain = false;
                }
            }

            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                isNewTask: true,
                botType: info.RealBotType ?? info.BotType,
                isDomain: isDomain,
                domainIds: domainIds);

            if (instance == null || !instance.Account.IsAcceptNewTask)
            {
                if (isDomain && domainIds.Count > 0)
                {
                    // If no suitable domain account is found, try again without domain
                    instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                        isNewTask: true,
                        botType: info.RealBotType ?? info.BotType,
                        isDomain: false);
                }
            }

            if (instance == null || !instance.Account.IsAcceptNewTask)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            return instance.SubmitTaskAsync(info, async () =>
            {
                var imageUrls = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{info.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(uploadResult.Code, uploadResult.Description);
                    }

                    if (uploadResult.Description.StartsWith("http"))
                    {
                        imageUrls.Add(uploadResult.Description);
                    }
                    else
                    {
                        var finalFileName = uploadResult.Description;
                        var sendImageResult = await instance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                        if (sendImageResult.Code != ReturnCode.SUCCESS)
                        {
                            return Message.Of(sendImageResult.Code, sendImageResult.Description);
                        }
                        imageUrls.Add(sendImageResult.Description);
                    }
                }
                if (imageUrls.Any())
                {
                    info.Prompt = string.Join(" ", imageUrls) + " " + info.Prompt;
                    info.PromptEn = string.Join(" ", imageUrls) + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                    _taskStoreService.Save(info);
                }
                return await instance.ImagineAsync(info, info.PromptEn,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
            });
        }

        /// <summary>
        /// Submit show task
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        public SubmitResultVO ShowImagine(TaskInfo info)
        {
            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                botType:info.RealBotType ?? info.BotType);

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            return instance.SubmitTaskAsync(info, async () =>
            {
                return await instance.ShowAsync(info.JobId,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),info.RealBotType ?? info.BotType);
            });
        }

        public SubmitResultVO SubmitUpscale(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Account unavailable: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.UpscaleAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        public SubmitResultVO SubmitVariation(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Account unavailable: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.VariationAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        /// <summary>
        /// Submit reroll task.
        /// </summary>
        /// <param name="task"></param>
        /// <param name="targetMessageId"></param>
        /// <param name="targetMessageHash"></param>
        /// <param name="messageFlags"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitReroll(TaskInfo task, string targetMessageId, string targetMessageHash, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Account unavailable: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.RerollAsync(targetMessageId, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        /// <summary>
        /// Submit Describe task
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitDescribe(TaskInfo task, DataUrl dataUrl)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                describe: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return Message.Of(uploadResult.Code, uploadResult.Description);
                }

                var link = "";
                if (uploadResult.Description.StartsWith("http"))
                {
                    link = uploadResult.Description;
                }
                else
                {
                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await discordInstance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(sendImageResult.Code, sendImageResult.Description);
                    }
                    link = sendImageResult.Description;
                }

                //var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                //var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                //if (uploadResult.Code != ReturnCode.SUCCESS)
                //{
                //    return Message.Of(uploadResult.Code, uploadResult.Description);
                //}
                //var finalFileName = uploadResult.Description;
                //return await discordInstance.DescribeAsync(finalFileName, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                //  task.RealBotType ?? task.BotType);

                return await discordInstance.DescribeByLinkAsync(link, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                   task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// Upload a longer prompt, mj can return a set of brief prompts
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public SubmitResultVO ShortenAsync(TaskInfo task)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                shorten: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                return await discordInstance.ShortenAsync(task, task.PromptEn, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// Submit blend task
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrls"></param>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitBlend(TaskInfo task, List<DataUrl> dataUrls, BlendDimensions dimensions)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                blend: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }
            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var finalFileNames = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";

                    var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl, useDiscordUpload: true);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(uploadResult.Code, uploadResult.Description);
                    }

                    finalFileNames.Add(uploadResult.Description);
                }
                return await discordInstance.BlendAsync(finalFileNames, dimensions,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// Perform action
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitAction(TaskInfo task, SubmitActionDTO submitAction)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);
            if (discordInstance == null)
            {
                // If the main instance is not found, look for sub-instance
                var ids = new List<string>();
                var list = _discordLoadBalancer.GetAliveInstances().ToList();
                foreach (var item in list)
                {
                    if (item.Account.SubChannelValues.ContainsKey(task.SubInstanceId ?? task.InstanceId))
                    {
                        ids.Add(item.ChannelId);
                    }
                }

                // Filter available accounts through sub-channels
                if (ids.Count > 0)
                {
                    discordInstance = _discordLoadBalancer.ChooseInstance(accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType, ids: ids);

                    if (discordInstance != null)
                    {
                        // If found, mark the sub-channel information of the current task
                        task.SubInstanceId = task.SubInstanceId ?? task.InstanceId;
                    }
                }
            }

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }

            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);

            var targetTask = _taskStoreService.Get(submitAction.TaskId)!;
            var messageFlags = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

            task.BotType = targetTask.BotType;
            task.RealBotType = targetTask.RealBotType;

            task.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());
            task.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, submitAction.CustomId);

            // Set the task's prompt information = parent task's prompt information
            task.Prompt = targetTask.Prompt;

            // Use the final prompt of the last task as the prompt for variation
            // Remove speed mode parameters
            task.PromptEn = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default)?.Replace("--fast", "")?.Replace("--relax", "")?.Replace("--turbo", "")?.Trim();

            // But if the parent task is a blend task, the prompt may be empty
            if (string.IsNullOrWhiteSpace(task.PromptEn))
            {
                task.PromptEn = targetTask.PromptEn;
            }

            // Click like
            if (submitAction.CustomId.Contains("MJ::BOOKMARK"))
            {
                var res = discordInstance.ActionAsync(messageId ?? targetTask.MessageId,
                    submitAction.CustomId, messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                // No need to save the task here
                if (res.Code == ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "Success", task.ParentId);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.VALIDATION_ERROR, res.Description, task.ParentId);
                }
            }

            // If it is a Modal task, return directly
            if (submitAction.CustomId.StartsWith("MJ::CustomZoom::")
                || submitAction.CustomId.StartsWith("MJ::Inpaint::"))
            {
                // If it is an inpaint task, set the task status to modal
                if (task.Action == TaskAction.INPAINT)
                {
                    task.Status = TaskStatus.MODAL;
                    task.Prompt = "";
                    task.PromptEn = "";
                }

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                _taskStoreService.Save(task);

                // Status code is 21
                // Inpaint and custom zoom always have remix set to true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }
            // describe all regenerate drawing
            else if (submitAction.CustomId?.Contains("MJ::Job::PicReader::all") == true)
            {
                var prompts = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                var ids = new List<string>();
                var count = prompts.Length >= 4 ? 4 : prompts.Length;
                for (int i = 0; i < count; i++)
                {
                    var prompt = prompts[i].Substring(prompts[i].IndexOf(' ')).Trim();

                    var subTask = new TaskInfo()
                    {
                        Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomUtils.RandomNumbers(3)}",
                        SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        State = $"{task.State}::{i + 1}",
                        ParentId = targetTask.Id,
                        Action = task.Action,
                        BotType = task.BotType,
                        RealBotType = task.RealBotType,
                        InstanceId = task.InstanceId,
                        Prompt = prompt,
                        PromptEn = prompt,
                        Status = TaskStatus.NOT_START,
                        Mode = task.Mode,
                        RemixAutoSubmit = true,
                        SubInstanceId = task.SubInstanceId,
                    };

                    subTask.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());

                    var nonce = SnowFlake.NextId();
                    subTask.Nonce = nonce;
                    subTask.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
                    subTask.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, $"MJ::Job::PicReader::{i + 1}");

                    subTask.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    _taskStoreService.Save(subTask);

                    var res = SubmitModal(subTask, new SubmitModalDTO()
                    {
                        NotifyHook = submitAction.NotifyHook,
                        TaskId = subTask.Id,
                        Prompt = subTask.PromptEn,
                        State = subTask.State
                    });
                    ids.Add(subTask.Id);

                    Thread.Sleep(200);

                    if (res.Code != ReturnCode.SUCCESS && res.Code != ReturnCode.EXISTED && res.Code != ReturnCode.IN_QUEUE)
                    {
                        return SubmitResultVO.Of(ReturnCode.SUCCESS, "Success", string.Join(",", ids));
                    }
                }

                return SubmitResultVO.Of(ReturnCode.SUCCESS, "Success", string.Join(",", ids));
            }
            // If it is a PicReader task, return directly
            // Image to text -> text to image
            else if (submitAction.CustomId?.StartsWith("MJ::Job::PicReader::") == true)
            {
                var index = int.Parse(submitAction.CustomId.Split("::").LastOrDefault().Trim());
                var pre = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray()[index - 1].Trim();
                var prompt = pre.Substring(pre.IndexOf(' ')).Trim();

                task.Status = TaskStatus.MODAL;
                task.Prompt = prompt;
                task.PromptEn = prompt;

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                _taskStoreService.Save(task);

                // Status code is 21
                // Inpaint and custom zoom always have remix set to true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }
            // prompt shorten -> text to image
            else if (submitAction.CustomId.StartsWith("MJ::Job::PromptAnalyzer::"))
            {
                var index = int.Parse(submitAction.CustomId.Split("::").LastOrDefault().Trim());
                var si = targetTask.Description.IndexOf("Shortened prompts");
                if (si >= 0)
                {
                    var pre = targetTask.Description.Substring(si).Trim().Split('\n')
                     .Where(c => !string.IsNullOrWhiteSpace(c)).ToArray()[index].Trim();

                    var prompt = pre.Substring(pre.IndexOf(' ')).Trim();

                    task.Status = TaskStatus.MODAL;
                    task.Prompt = prompt;
                    task.PromptEn = prompt;

                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    // If remix auto-submit is enabled
                    if (discordInstance.Account.RemixAutoSubmit)
                    {
                        task.RemixAutoSubmit = true;
                        _taskStoreService.Save(task);

                        return SubmitModal(task, new SubmitModalDTO()
                        {
                            TaskId = task.Id,
                            NotifyHook = submitAction.NotifyHook,
                            Prompt = targetTask.PromptEn,
                            State = submitAction.State
                        });
                    }
                    else
                    {
                        _taskStoreService.Save(task);

                        // Status code is 21
                        // Inpaint and custom zoom always have remix set to true
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }
                else
                {
                    return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Shortened prompts not found");
                }
            }
            // REMIX handling
            else if (task.Action == TaskAction.PAN || task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL)
            {
                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                if (discordInstance.Account.RemixAutoSubmit)
                {
                    // If remix auto-submit is enabled
                    // And remix mode is enabled
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && discordInstance.Account.NijiRemixOn))
                    {
                        task.RemixAutoSubmit = true;

                        _taskStoreService.Save(task);

                        return SubmitModal(task, new SubmitModalDTO()
                        {
                            TaskId = task.Id,
                            NotifyHook = submitAction.NotifyHook,
                            Prompt = targetTask.PromptEn,
                            State = submitAction.State
                        });
                    }
                }
                else
                {
                    // Remix auto-submit is not enabled
                    // And remix mode is enabled
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && discordInstance.Account.NijiRemixOn))
                    {
                        // If it is a REMIX task, set the task status to modal
                        task.Status = TaskStatus.MODAL;
                        _taskStoreService.Save(task);

                        // Status code is 21
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }
            }

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                return await discordInstance.ActionAsync(
                    messageId ?? targetTask.MessageId,
                    submitAction.CustomId,
                    messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task);
            });
        }

        /// <summary>
        /// Perform Modal
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitModal(TaskInfo task, SubmitModalDTO submitAction, DataUrl dataUrl = null)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);
            if (discordInstance == null)
            {
                // If the main instance is not found, look for sub-instance
                var ids = new List<string>();
                var list = _discordLoadBalancer.GetAliveInstances().ToList();
                foreach (var item in list)
                {
                    if (item.Account.SubChannelValues.ContainsKey(task.SubInstanceId ?? task.InstanceId))
                    {
                        ids.Add(item.ChannelId);
                    }
                }

                // Filter available accounts through sub-channels
                if (ids.Count > 0)
                {
                    discordInstance = _discordLoadBalancer.ChooseInstance(accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType, ids: ids);

                    if (discordInstance != null)
                    {
                        // If found, mark the sub-channel information of the current task
                        task.SubInstanceId = task.SubInstanceId ?? task.InstanceId;
                    }
                }
            }

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }

            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);
                var messageFlags = task.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
                var messageId = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
                var nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default);

                // Window confirmation
                task = discordInstance.GetRunningTask(task.Id);
                task.RemixModaling = true;
                var res = await discordInstance.ActionAsync(messageId, customId, messageFlags, nonce, task);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    return res;
                }

                // Wait to get messageId and interaction message id
                // Maximum timeout 5min
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    // Wait 2.5s
                    Thread.Sleep(2500);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId))
                    {
                        if (sw.ElapsedMilliseconds > 300000)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "Timeout, message ID not found");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId));

                // Wait 1.2s
                Thread.Sleep(1200);

                task.RemixModaling = false;

                // Custom zoom
                if (customId.StartsWith("MJ::CustomZoom::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.ZoomAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce);
                }
                // Inpaint
                else if (customId.StartsWith("MJ::Inpaint::"))
                {
                    var ifarmeCustomId = task.GetProperty<string>(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, default);
                    return await discordInstance.InpaintAsync(task, ifarmeCustomId, task.PromptEn, submitAction.MaskBase64);
                }
                // Image to text -> text to image
                else if (customId.StartsWith("MJ::Job::PicReader::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.PicReaderAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                // prompt shorten -> text to image
                else if (customId.StartsWith("MJ::Job::PromptAnalyzer::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    // MJ::ImagineModal::1265485889606516808
                    customId = $"MJ::ImagineModal::{messageId}";
                    var modal = "MJ::ImagineModal::new_prompt";

                    return await discordInstance.RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                        customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                // Remix mode
                else if (task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL || task.Action == TaskAction.PAN)
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    var action = task.Action;

                    TaskInfo parentTask = null;
                    if (!string.IsNullOrWhiteSpace(task.ParentId))
                    {
                        parentTask = _taskStoreService.Get(task.ParentId);
                        if (parentTask == null)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "Parent task not found");
                        }
                    }

                    var prevCustomId = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, default);
                    var prevModal = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_MODAL, default);

                    var modal = "MJ::RemixModal::new_prompt";
                    if (action == TaskAction.REROLL)
                    {
                        // If it is the first submission, use the interaction messageId
                        if (string.IsNullOrWhiteSpace(prevCustomId))
                        {
                            // MJ::ImagineModal::1265485889606516808
                            customId = $"MJ::ImagineModal::{messageId}";
                            modal = "MJ::ImagineModal::new_prompt";
                        }
                        else
                        {
                            modal = prevModal;

                            if (prevModal.Contains("::PanModal"))
                            {
                                // If it is pan, pan is processed based on the customId of the enlarged image
                                var cus = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default);
                                if (string.IsNullOrWhiteSpace(cus))
                                {
                                    return Message.Of(ReturnCode.VALIDATION_ERROR, "Target image U operation not found");
                                }

                                // MJ::JOB::upsample::3::10f78893-eddb-468f-a0fb-55643a94e3b4
                                var arr = cus.Split("::");
                                var hash = arr[4];
                                var i = arr[3];

                                var prevArr = prevCustomId.Split("::");
                                var convertedString = $"MJ::PanModal::{prevArr[2]}::{hash}::{i}";
                                customId = convertedString;

                                // When performing U, record the customId of the target image's U operation
                                task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                            }
                            else
                            {
                                customId = prevCustomId;
                            }

                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                        }
                    }
                    else if (action == TaskAction.VARIATION)
                    {
                        var suffix = "0";

                        // If high variability is enabled globally, use high variability
                        if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
                        {
                            if (discordInstance.Account.Buttons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }
                        else
                        {
                            if (discordInstance.Account.NijiButtons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }

                        // Low variability
                        if (customId.Contains("low_variation"))
                        {
                            suffix = "0";
                        }
                        // If it is high variability
                        else if (customId.Contains("high_variation"))
                        {
                            suffix = "1";
                        }

                        var parts = customId.Split("::");
                        var convertedString = $"MJ::RemixModal::{parts[4]}::{parts[3]}::{suffix}";
                        customId = convertedString;

                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                    }
                    else if (action == TaskAction.PAN)
                    {
                        modal = "MJ::PanModal::prompt";

                        // MJ::JOB::pan_left::1::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::SOLO
                        // to
                        // MJ::PanModal::left::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::1

                        var parts = customId.Split("::");
                        var convertedString = $"MJ::PanModal::{parts[2].Split('_')[1]}::{parts[4]}::{parts[3]}";
                        customId = convertedString;

                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);

                        // When performing U, record the customId of the target image's U operation
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                    }
                    else
                    {
                        return Message.Failure("Unknown operation");
                    }

                    return await discordInstance.RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                        customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                else
                {
                    // Not supported
                    return Message.Of(ReturnCode.NOT_FOUND, "Operation not supported");
                }
            });
        }

        /// <summary>
        /// Get image seed
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitSeed(TaskInfo task)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "No available account instance");
            }

            // Please configure private chat channel
            var privateChannelId = string.Empty;

            if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
            {
                privateChannelId = discordInstance.Account.PrivateChannelId;
            }
            else
            {
                privateChannelId = discordInstance.Account.NijiBotChannelId;
            }

            if (string.IsNullOrWhiteSpace(privateChannelId))
            {
                return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Please configure private chat channel");
            }

            try
            {
                discordInstance.AddRunningTask(task);

                var hash = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);

                var nonce = SnowFlake.NextId();
                task.Nonce = nonce;
                task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                // /show job_id
                // https://discord.com/api/v9/interactions
                var res = await discordInstance.SeedAsync(hash, nonce, task.RealBotType ?? task.BotType);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, res.Description);
                }

                // Wait to get seed messageId
                // Maximum timeout 5min
                var sw = new Stopwatch();
                sw.Start();

                do
                {
                    Thread.Sleep(50);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.SeedMessageId))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Timeout, seed messageId not found");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.SeedMessageId));

                // Add reaction
                // https://discord.com/api/v9/channels/1256495659683676190/messages/1260598192333127701/reactions/✉️/@me?location=Message&type=0
                var url = $"https://discord.com/api/v9/channels/{privateChannelId}/messages/{task.SeedMessageId}/reactions/%E2%9C%89%EF%B8%8F/%40me?location=Message&type=0";
                var msgRes = await discordInstance.SeedMessagesAsync(url);
                if (msgRes.Code != ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, res.Description);
                }

                sw.Start();
                do
                {
                    Thread.Sleep(50);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.Seed))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "Timeout, seed not found");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.Seed));

                // Save task
                _taskStoreService.Save(task);
            }
            finally
            {
                discordInstance.RemoveRunningTask(task);
            }

            return SubmitResultVO.Of(ReturnCode.SUCCESS, "Success", task.Seed);
        }

        /// <summary>
        /// Perform info setting operation
        /// </summary>
        /// <returns></returns>
        public async Task InfoSetting(string id)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Account instance not found");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("No available account instance");
            }

            if (discordInstance.Account.EnableMj == true)
            {
                var res3 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                if (res3.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res3.Description);
                }
                Thread.Sleep(2500);

                var res0 = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                if (res0.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res0.Description);
                }
                Thread.Sleep(2500);
            }

            if (discordInstance.Account.EnableNiji == true)
            {
                var res2 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res2.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res2.Description);
                }
                Thread.Sleep(2500);

                var res = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res.Description);
                }
                Thread.Sleep(2500);
            }
        }

        /// <summary>
        /// Change version
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public async Task AccountChangeVersion(string id, string version)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Account instance not found");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("No available account instance");
            }

            var accsount = discordInstance.Account;

            var nonce = SnowFlake.NextId();
            accsount.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
            var res = await discordInstance.SettingSelectAsync(nonce, version);
            if (res.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res.Description);
            }

            Thread.Sleep(2000);

            await InfoSetting(id);
        }

        /// <summary>
        /// Perform action
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task AccountAction(string id, string customId, EBotType botType)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Account instance not found");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("No available account instance");
            }

            var accsount = discordInstance.Account;

            var nonce = SnowFlake.NextId();
            accsount.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
            var res = await discordInstance.SettingButtonAsync(nonce, customId, botType);
            if (res.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res.Description);
            }

            Thread.Sleep(2000);

            await InfoSetting(id);
        }

        /// <summary>
        /// MJ Plus data migration
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        public async Task MjPlusMigration(MjPlusMigrationDto dto)
        {
            var key = "mjplus";
            var islock = AsyncLocalLock.IsLockAvailable(key);
            if (!islock)
            {
                throw new LogicException("Migration task in progress...");
            }

            _ = Task.Run(async () =>
            {
                var isLock = await AsyncLocalLock.TryLockAsync("mjplus", TimeSpan.FromMilliseconds(3), async () =>
                {
                    try
                    {
                        // Account migration
                        if (true)
                        {
                            var ids = DbHelper.Instance.AccountStore.GetAllIds().ToHashSet<string>();

                            var path = "/mj/account/query";
                            var pageNumber = 0;
                            var pageSize = 100;
                            var isLastPage = false;
                            var sort = 0;

                            while (!isLastPage)
                            {
                                var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
                                var responseObject = JObject.Parse(responseContent);
                                var contentArray = (JArray)responseObject["content"];

                                if (contentArray.Count <= 0)
                                {
                                    break;
                                }

                                foreach (var item in contentArray)
                                {
                                    // Deserialize basic JSON
                                    var json = item.ToString();
                                    var accountJson = JsonConvert.DeserializeObject<dynamic>(json);

                                    // Create
                                    // Create DiscordAccount instance
                                    var acc = new DiscordAccount
                                    {
                                        Sponsor = "by mjplus",
                                        DayDrawLimit = -1, // Default value -1

                                        ChannelId = accountJson.channelId,
                                        GuildId = accountJson.guildId,
                                        PrivateChannelId = accountJson.mjBotChannelId,
                                        NijiBotChannelId = accountJson.nijiBotChannelId,
                                        UserToken = accountJson.userToken,
                                        BotToken = null,
                                        UserAgent = accountJson.userAgent,
                                        Enable = accountJson.enable,
                                        EnableMj = true,
                                        EnableNiji = true,
                                        CoreSize = accountJson.coreSize ?? 3, // Default value 3
                                        Interval = 1.2m, // Default value 1.2
                                        AfterIntervalMin = 1.2m, // Default value 1.2
                                        AfterIntervalMax = 1.2m, // Default value 1.2
                                        QueueSize = accountJson.queueSize ?? 10, // Default value 10
                                        MaxQueueSize = 100, // Default value 100
                                        TimeoutMinutes = accountJson.timeoutMinutes ?? 5, // Default value 5
                                        Remark = accountJson.remark,

                                        DateCreated = DateTimeOffset.FromUnixTimeMilliseconds((long)accountJson.dateCreated).DateTime,
                                        Weight = 1, // Assume weight comes from properties
                                        WorkTime = null,
                                        FishingTime = null,
                                        Sort = ++sort,
                                        RemixAutoSubmit = accountJson.remixAutoSubmit,
                                        Mode = Enum.TryParse<GenerationSpeedMode>((string)accountJson.mode, out var mode) ? mode : (GenerationSpeedMode?)null,
                                        AllowModes = new List<GenerationSpeedMode>(),
                                        Components = new List<Component>(),
                                        IsBlend = true, // Default true
                                        IsDescribe = true, // Default true
                                        IsVerticalDomain = false, // Default false
                                        IsShorten = true,
                                        VerticalDomainIds = new List<string>(),
                                        SubChannels = new List<string>(),
                                        SubChannelValues = new Dictionary<string, string>(),

                                        Id = accountJson.id,
                                    };

                                    if (!ids.Contains(acc.Id))
                                    {
                                        DbHelper.Instance.AccountStore.Add(acc);
                                        ids.Add(acc.Id);
                                    }
                                }

                                isLastPage = (bool)responseObject["last"];
                                pageNumber++;

                                Log.Information($"Account migration progress, page {pageNumber}, {pageSize} items per page, completed");
                            }

                            Log.Information("Account migration completed");
                        }

                        // Task migration
                        if (true)
                        {
                            var accounts = DbHelper.Instance.AccountStore.GetAll();

                            var ids = DbHelper.Instance.TaskStore.GetAllIds().ToHashSet<string>();

                            var path = "/mj/task-admin/query";
                            var pageNumber = 0;
                            var pageSize = 100;
                            var isLastPage = false;

                            while (!isLastPage)
                            {
                                var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
                                var responseObject = JObject.Parse(responseContent);
                                var contentArray = (JArray)responseObject["content"];

                                if (contentArray.Count <= 0)
                                {
                                    break;
                                }

                                foreach (var item in contentArray)
                                {
                                    // Deserialize basic JSON
                                    var json = item.ToString();
                                    var jsonObject = JsonConvert.DeserializeObject<dynamic>(json);

                                    string aid = jsonObject.properties?.discordInstanceId;
                                    var acc = accounts.FirstOrDefault(x => x.Id == aid);

                                    // Create TaskInfo instance
                                    var taskInfo = new TaskInfo
                                    {
                                        FinishTime = jsonObject.finishTime,
                                        PromptEn = jsonObject.promptEn,
                                        Description = jsonObject.description,
                                        SubmitTime = jsonObject.submitTime,
                                        ImageUrl = jsonObject.imageUrl,
                                        Action = Enum.TryParse<TaskAction>((string)jsonObject.action, out var action) ? action : (TaskAction?)null,
                                        Progress = jsonObject.progress,
                                        StartTime = jsonObject.startTime,
                                        FailReason = jsonObject.failReason,
                                        Id = jsonObject.id,
                                        State = jsonObject.state,
                                        Prompt = jsonObject.prompt,
                                        Status = Enum.TryParse<TaskStatus>((string)jsonObject.status, out var status) ? status : (TaskStatus?)null,
                                        Nonce = jsonObject.properties?.nonce,
                                        MessageId = jsonObject.properties?.messageId,
                                        BotType = Enum.TryParse<EBotType>((string)jsonObject.properties?.botType, out var botType) ? botType : EBotType.MID_JOURNEY,
                                        InstanceId = acc?.ChannelId,
                                        Buttons = JsonConvert.DeserializeObject<List<CustomComponentModel>>(JsonConvert.SerializeObject(jsonObject.buttons)),
                                        Properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(jsonObject.properties)),
                                    };

                                    aid = taskInfo.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
                                    if (!string.IsNullOrWhiteSpace(aid))
                                    {
                                        acc = accounts.FirstOrDefault(x => x.Id == aid);
                                        if (acc != null)
                                        {
                                            taskInfo.InstanceId = acc.ChannelId;
                                        }
                                    }

                                    if (!ids.Contains(taskInfo.Id))
                                    {
                                        DbHelper.Instance.TaskStore.Add(taskInfo);
                                        ids.Add(taskInfo.Id);
                                    }
                                }

                                isLastPage = (bool)responseObject["last"];
                                pageNumber++;

                                Log.Information($"Task migration progress, page {pageNumber}, {pageSize} items per page, completed");
                            }

                            Log.Information("Task migration completed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "mjplus migration execution exception");
                    }
                });

                if (!islock)
                {
                    Log.Warning("Migration task in progress...");
                }
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Get paginated data
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="path"></param>
        /// <param name="pageSize"></param>
        /// <param name="pageNumber"></param>
        /// <returns></returns>
        private static async Task<string> MjPlusPageData(MjPlusMigrationDto dto, string path, int pageSize, int pageNumber)
        {
            var options = new RestClientOptions(dto.Host)
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest(path, Method.Post);
            request.AddHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(dto.ApiSecret))
            {
                request.AddHeader("mj-api-secret", dto.ApiSecret);
            }
            var body = new JObject
            {
                ["pageSize"] = pageSize,
                ["pageNumber"] = pageNumber
            }.ToString();

            request.AddStringBody(body, DataFormat.Json);
            var response = await client.ExecuteAsync(request);
            return response.Content;
        }
    }
}