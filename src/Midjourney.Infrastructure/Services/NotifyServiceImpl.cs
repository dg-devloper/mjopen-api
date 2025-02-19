﻿// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
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

using Microsoft.Extensions.Logging;
using Midjourney.Infrastructure.Data;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Serilog;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// Notification service implementation class.
    /// </summary>
    public class NotifyServiceImpl : INotifyService
    {
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() } // Add converter to serialize enums as strings
        };

        private readonly ILogger<NotifyServiceImpl> _logger;
        private readonly ConcurrentDictionary<string, string> _taskStatusMap = new ConcurrentDictionary<string, string>();
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly int _notifyPoolSize;

        public NotifyServiceImpl(ILogger<NotifyServiceImpl> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            _notifyPoolSize = GlobalConfiguration.Setting.NotifyPoolSize;

            var max = Math.Max(1, Math.Min(_notifyPoolSize, 64));

            _semaphoreSlim = new SemaphoreSlim(max, max);
        }

        public async Task NotifyTaskChange(TaskInfo task)
        {
            string notifyHook = task.GetProperty<string>(Constants.TASK_PROPERTY_NOTIFY_HOOK, default);
            
            if (string.IsNullOrWhiteSpace(notifyHook))
            {
                _logger.LogTrace("No notify hook, task: {0}({1})", task.Id, task.Status);
                return;
            }

            string taskId = task.Id;
            string statusStr = $"{task.Status}:{task.Progress}";
            _logger.LogTrace("Wait notify task change, task: {0}({1}), hook: {2}", taskId, statusStr, notifyHook);

            try
            {
                string paramsStr = JsonSerializer.Serialize(task, _jsonSerializerOptions);
                await Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteNotify(taskId, statusStr, notifyHook, paramsStr);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning("Notify task change error, task: {0}({1}), hook: {2}, msg: {3}", taskId, statusStr, notifyHook, e.Message);
                    }
                });
            }
            catch (JsonException e)
            {
                _logger.LogError(e, e.Message);
            }
            finally
            {
                // If the task is completed or failed, remove the task status
                if (task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    _taskStatusMap.TryRemove(taskId, out _);
                }
            }
        }

        private async Task ExecuteNotify(string taskId, string currentStatusStr, string notifyHook, string paramsStr)
        {
            await _semaphoreSlim.WaitAsync();

            try
            {
                if (_taskStatusMap.TryGetValue(taskId, out var existStatusStr))
                {
                    int compare = CompareStatusStr(currentStatusStr, existStatusStr);
                    if (compare <= 0)
                    {
                        // Ignore the message
                        _logger.LogDebug("Ignore this change, task: {0}({1})", taskId, currentStatusStr);
                        return;
                    }
                }

                _taskStatusMap[taskId] = currentStatusStr;
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            _logger.LogDebug("Push task change, task: {0}({1}), hook: {2}", taskId, currentStatusStr, notifyHook);

            HttpResponseMessage response = await PostJson(notifyHook, paramsStr);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Notify task change fail, task: {0}({1}), hook: {2}, code: {3}, msg: {4}", taskId, currentStatusStr, notifyHook, (int)response.StatusCode, response.Content.ReadAsStringAsync().Result);
            }
        }

        private async Task<HttpResponseMessage> PostJson(string notifyHook, string paramsJson)
        {
            var content = new StringContent(paramsJson, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(notifyHook, content);
        }

        private int CompareStatusStr(string statusStr1, string statusStr2)
        {
            if (statusStr1 == statusStr2)
            {
                return 0;
            }

            float o1 = ConvertOrder(statusStr1);
            float o2 = ConvertOrder(statusStr2);
            return o1.CompareTo(o2);
        }

        private float ConvertOrder(string statusStr)
        {
            var split = statusStr.Split(':');
            TaskStatus status = Enum.Parse<TaskStatus>(split[0]);
            if (status != TaskStatus.IN_PROGRESS || split.Length == 1)
            {
                return status.GetOrder();
            }

            string progress = split[1];
            if (progress.EndsWith("%"))
            {
                return status.GetOrder() + float.Parse(progress.TrimEnd('%')) / 100;
            }

            return status.GetOrder();
        }
    }
}