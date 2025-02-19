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
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Storage;
using Midjourney.Infrastructure.Util;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using ILogger = Serilog.ILogger;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord instance
    /// Implements the IDiscordInstance interface, responsible for handling Discord-related task management and execution.
    /// </summary>
    public class DiscordInstance
    {

        private readonly object _lockAccount = new object();

        private readonly ILogger _logger = Log.Logger;

        private readonly ITaskStoreService _taskStoreService;
        private readonly INotifyService _notifyService;

        private readonly List<TaskInfo> _runningTasks = [];
        private readonly ConcurrentDictionary<string, Task> _taskFutureMap = [];
        private readonly AsyncParallelLock _semaphoreSlimLock;

        private readonly Task _longTask;
        private readonly Task _longTaskCache;

        private readonly CancellationTokenSource _longToken;
        private readonly ManualResetEvent _mre; // Signal

        private readonly HttpClient _httpClient;
        private readonly DiscordHelper _discordHelper;
        private readonly Dictionary<string, string> _paramsMap;

        private readonly string _discordInteractionUrl;
        private readonly string _discordAttachmentUrl;
        private readonly string _discordMessageUrl;
        private readonly IMemoryCache _cache;
        private readonly ITaskService _taskService;

        /// <summary>
        /// Current queue tasks
        /// </summary>
        private ConcurrentQueue<(TaskInfo, Func<Task<Message>>)> _queueTasks = [];

        private DiscordAccount _account;

        public DiscordInstance(
            IMemoryCache memoryCache,
            DiscordAccount account,
            ITaskStoreService taskStoreService,
            INotifyService notifyService,
            DiscordHelper discordHelper,
            Dictionary<string, string> paramsMap,
            IWebProxy webProxy,
            ITaskService taskService)
        {
            _logger = Log.Logger;

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            _httpClient = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            _taskService = taskService;
            _cache = memoryCache;
            _paramsMap = paramsMap;
            _discordHelper = discordHelper;

            _account = account;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;

            // Minimum 1, maximum 12
            _semaphoreSlimLock = new AsyncParallelLock(Math.Max(1, Math.Min(account.CoreSize, 12)));

            // Initialize signal
            _mre = new ManualResetEvent(false);

            var discordServer = _discordHelper.GetServer();

            _discordInteractionUrl = $"{discordServer}/api/v9/interactions";
            _discordAttachmentUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/attachments";
            _discordMessageUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/messages";

            // Background task
            // Background task cancellation token
            _longToken = new CancellationTokenSource();
            _longTask = new Task(Running, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTask.Start();

            _longTaskCache = new Task(RuningCache, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTaskCache.Start();
        }

        /// <summary>
        /// Default session ID.
        /// </summary>
        public string DefaultSessionId { get; set; } = "f1a313a09ce079ce252459dc70231f30";

        /// <summary>
        /// Get instance ID.
        /// </summary>
        /// <returns>Instance ID</returns>
        public string ChannelId => Account.ChannelId;

        public BotMessageListener BotMessageListener { get; set; }

        public WebSocketManager WebSocketManager { get; set; }

        /// <summary>
        /// Get Discord account information.
        /// </summary>
        /// <returns>Discord account</returns>
        public DiscordAccount Account
        {
            get
            {
                try
                {
                    lock (_lockAccount)
                    {
                        if (!string.IsNullOrWhiteSpace(_account?.Id))
                        {
                            _account = _cache.GetOrCreate($"account:{_account.Id}", (c) =>
                            {
                                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(2));

                                // Must exist in the database
                                var acc = DbHelper.Instance.AccountStore.Get(_account.Id);
                                if (acc != null)
                                {
                                    return acc;
                                }

                                // If the account is deleted
                                IsInit = false;

                                return _account;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to get account. {@0}", _account?.Id ?? "unknown");
                }

                return _account;
            }
        }

        /// <summary>
        /// Clear account cache
        /// </summary>
        /// <param name="id"></param>
        public void ClearAccountCache(string id)
        {
            _cache.Remove($"account:{id}");
        }

        /// <summary>
        /// Whether initialization is complete
        /// </summary>
        public bool IsInit { get; set; }

        /// <summary>
        /// Determine if the instance is alive
        /// </summary>
        /// <returns>Whether alive</returns>
        public bool IsAlive => IsInit && Account != null
             && Account.Enable != null && Account.Enable == true
             && WebSocketManager != null
             && WebSocketManager.Running == true
             && Account.Lock == false;

        /// <summary>
        /// Get the list of running tasks.
        /// </summary>
        /// <returns>List of running tasks</returns>
        public List<TaskInfo> GetRunningTasks() => _runningTasks;

        /// <summary>
        /// Get the list of tasks in the queue.
        /// </summary>
        /// <returns>List of tasks in the queue</returns>
        public List<TaskInfo> GetQueueTasks() => new List<TaskInfo>(_queueTasks.Select(c => c.Item1) ?? []);

        /// <summary>
        /// Whether there is an idle queue, i.e., whether the queue is full, whether new tasks can be added
        /// </summary>
        public bool IsIdleQueue
        {
            get
            {
                if (_queueTasks.Count <= 0)
                {
                    return true;
                }

                if (Account.MaxQueueSize <= 0)
                {
                    return true;
                }

                return _queueTasks.Count < Account.MaxQueueSize;
            }
        }

        /// <summary>
        /// Background service execution task
        /// </summary>
        private void Running()
        {
            while (true)
            {
                try
                {
                    if (_longToken.Token.IsCancellationRequested)
                    {
                        // Clean up resources (if needed)
                        break;
                    }
                }
                catch
                {

                }

                try
                {
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // Clean up resources (if needed)
                    //    break;
                    //}

                    // Wait for signal notification
                    _mre.WaitOne();

                    // Determine if there are still resources available
                    while (!_semaphoreSlimLock.IsLockAvailable())
                    {
                        //if (_longToken.Token.IsCancellationRequested)
                        //{
                        //    // Clean up resources (if needed)
                        //    break;
                        //}

                        // Wait
                        Thread.Sleep(100);
                    }

                    //// Allow simultaneous execution of N semaphore tasks
                    //while (_queueTasks.TryDequeue(out var info))
                    //{
                    //    // Determine if there are still resources available
                    //    while (!_semaphoreSlimLock.TryWait(100))
                    //    {
                    //        // Wait
                    //        Thread.Sleep(100);
                    //    }

                    //    _taskFutureMap[info.Item1.Id] = ExecuteTaskAsync(info.Item1, info.Item2);
                    //}

                    // Allow simultaneous execution of N semaphore tasks
                    //while (true)
                    //{
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // Clean up resources (if needed)
                    //    break;
                    //}

                    while (_queueTasks.TryPeek(out var info))
                    {
                        // Determine if there are still resources available
                        if (_semaphoreSlimLock.IsLockAvailable())
                        {
                            var preSleep = Account.Interval;
                            if (preSleep <= 1.2m)
                            {
                                preSleep = 1.2m;
                            }

                            // Interval before submitting the task
                            // Whether to wait for a while before submitting the next job after one job is completed
                            Thread.Sleep((int)(preSleep * 1000));

                            // Remove the task from the queue and start execution
                            if (_queueTasks.TryDequeue(out info))
                            {
                                _taskFutureMap[info.Item1.Id] = ExecuteTaskAsync(info.Item1, info.Item2);

                                // Calculate the interval after execution
                                var min = Account.AfterIntervalMin;
                                var max = Account.AfterIntervalMax;

                                // Calculate random number between min and max
                                var afterInterval = 1200;
                                if (max > min && min >= 1.2m)
                                {
                                    afterInterval = new Random().Next((int)(min * 1000), (int)(max * 1000));
                                }
                                else if (max == min && min >= 1.2m)
                                {
                                    afterInterval = (int)(min * 1000);
                                }

                                // If it is a picture-to-text operation
                                if (info.Item1.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default)?.Contains("PicReader") == true)
                                {
                                    // Batch task operation submission interval 1.2s + 6.8s
                                    Thread.Sleep(afterInterval + 6800);
                                }
                                else
                                {
                                    // Queue submission interval
                                    Thread.Sleep(afterInterval);
                                }
                            }
                        }
                        else
                        {
                            // If there are no available resources, wait
                            Thread.Sleep(100);
                        }
                    }

                    //else
                    //{
                    //    // Queue is empty, exit loop
                    //    break;
                    //}
                    //}

                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // Clean up resources (if needed)
                    //    break;
                    //}

                    //// Wait
                    //Thread.Sleep(100);



                    // Reset signal
                    _mre.Reset();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Background job execution exception {Account?.ChannelId}");

                    // Stop for 1 minute
                    Thread.Sleep(1000 * 60);
                }
            }
        }

        /// <summary>
        /// Cache processing
        /// </summary>
        private void RuningCache()
        {
            while (true)
            {
                if (_longToken.Token.IsCancellationRequested)
                {
                    // Clean up resources (if needed)
                    break;
                }

                try
                {
                    // Convert current time to Unix timestamp
                    // Unix timestamp at 0:00 today
                    var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                    var count = (int)DbHelper.Instance.TaskStore.Count(c => c.SubmitTime >= now && c.InstanceId == Account.ChannelId);

                    if (Account.DayDrawCount != count)
                    {
                        Account.DayDrawCount = count;

                        DbHelper.Instance.AccountStore.Update("DayDrawCount", Account);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "RuningCache exception");
                }

                // Execute every 2 minutes
                Thread.Sleep(60 * 1000 * 2);
            }
        }

        /// <summary>
        /// Exit task and save and notify.
        /// </summary>
        /// <param name="task">Task information</param>
        public void ExitTask(TaskInfo task)
        {
            _taskFutureMap.TryRemove(task.Id, out _);
            SaveAndNotify(task);

            // Determine if the specified task exists in the _queueTasks queue, if so, remove it
            //if (_queueTasks.Any(c => c.Item1.Id == task.Id))
            //{
            //    _queueTasks = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>(_queueTasks.Where(c => c.Item1.Id != task.Id));
            //}

            // Determine if the specified task exists in the _queueTasks queue, if so, remove it
            // Use thread-safe way to remove
            if (_queueTasks.Any(c => c.Item1.Id == task.Id))
            {
                // Remove the specified task from the _queueTasks queue
                var tempQueue = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>();

                // Add elements that do not need to be removed to the temporary queue
                while (_queueTasks.TryDequeue(out var item))
                {
                    if (item.Item1.Id != task.Id)
                    {
                        tempQueue.Enqueue(item);
                    }
                }

                // Swap queue references
                _queueTasks = tempQueue;
            }
        }

        /// <summary>
        /// Get the running task Future map.
        /// </summary>
        /// <returns>Task Future map</returns>
        public Dictionary<string, Task> GetRunningFutures() => new Dictionary<string, Task>(_taskFutureMap);

        /// <summary>
        /// Submit task.
        /// </summary>
        /// <param name="info">Task information</param>
        /// <param name="discordSubmit">Delegate for submitting tasks to Discord</param>
        /// <returns>Task submission result</returns>
        public SubmitResultVO SubmitTaskAsync(TaskInfo info, Func<Task<Message>> discordSubmit)
        {
            // Number of tasks before submission
            var currentWaitNumbers = _queueTasks.Count;
            if (Account.MaxQueueSize > 0 && currentWaitNumbers >= Account.MaxQueueSize)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "Submission failed, queue is full, please try again later")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
            }

            info.InstanceId = ChannelId;
            _taskStoreService.Save(info);

            try
            {
                _queueTasks.Enqueue((info, discordSubmit));

                // Notify the background service of new tasks
                _mre.Set();

                //// When the number of running tasks is not full, recalculate the number of tasks in the queue
                //if (_runningTasks.Count < _account.CoreSize)
                //{
                //    // Wait 10ms to check
                //    Thread.Sleep(10);
                //}

                //currentWaitNumbers = _queueTasks.Count;

                if (currentWaitNumbers == 0)
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "Submission successful", info.Id)
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.IN_QUEUE, $"In queue, there are {currentWaitNumbers} tasks ahead", info.Id)
                        .SetProperty("numberOfQueues", currentWaitNumbers)
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "submit task error");

                _taskStoreService.Delete(info.Id);

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "Submission failed, system error")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
            }
        }

        /// <summary>
        /// Asynchronously execute task.
        /// </summary>
        /// <param name="info">Task information</param>
        /// <param name="discordSubmit">Delegate for submitting tasks to Discord</param>
        /// <returns>Asynchronous task</returns>
        private async Task ExecuteTaskAsync(TaskInfo info, Func<Task<Message>> discordSubmit)
        {
            try
            {
                await _semaphoreSlimLock.LockAsync();

                _runningTasks.Add(info);

                // Determine if the current instance is available
                if (!IsAlive)
                {
                    info.Fail("Instance unavailable");
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                // banned judgment
                // banned will cause inaccurate calculation of the number of running tasks, temporarily not handled
                //if (!info.IsWhite)
                //{
                //    if (!string.IsNullOrWhiteSpace(info.UserId))
                //    {
                //        var lockKey = $"banned:lock:{info.UserId}";
                //        if (_cache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                //        {
                //            info.Fail("Account has been temporarily blocked, please do not use illegal words to draw");
                //            SaveAndNotify(info);
                //            _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                //            return;
                //        }
                //    }

                //    if (true)
                //    {
                //        var lockKey = $"banned:lock:{info.ClientIp}";
                //        if (_cache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                //        {
                //            info.Fail("Account has been temporarily blocked, please do not use illegal words to draw");
                //            SaveAndNotify(info);
                //            _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                //            return;
                //        }
                //    }
                //}

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";
                SaveAndNotify(info);

                var result = await discordSubmit();

                // Determine if the current instance is available
                if (!IsAlive)
                {
                    info.Fail("Instance unavailable");
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                if (result.Code != ReturnCode.SUCCESS)
                {
                    info.Fail(result.Description);
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task finished, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";

                await Task.Delay(500);

                SaveAndNotify(info);

                // Timeout handling
                var timeoutMin = Account.TimeoutMinutes;
                var sw = new Stopwatch();
                sw.Start();

                while (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                {
                    SaveAndNotify(info);

                    // Every 500ms
                    await Task.Delay(500);

                    if (sw.ElapsedMilliseconds > timeoutMin * 60 * 1000)
                    {
                        info.Fail($"Execution timeout {timeoutMin} minutes");
                        SaveAndNotify(info);
                        return;
                    }
                }

                // Do not randomize, directly read the message
                // Automatically read the message after the task is completed
                // Randomly 3 times, if hit, read the message
                //if (new Random().Next(0, 3) == 0)
                //{
                try
                {
                    // Read the message only if successful
                    if (info.Status == TaskStatus.SUCCESS)
                    {
                        var res = await ReadMessageAsync(info.MessageId);
                        if (res.Code == ReturnCode.SUCCESS)
                        {
                            _logger.Debug("Automatic message reading succeeded {@0} - {@1}", info.InstanceId, info.Id);
                        }
                        else
                        {
                            _logger.Warning("Automatic message reading failed {@0} - {@1}", info.InstanceId, info.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Automatic message reading exception {@0} - {@1}", info.InstanceId, info.Id);
                }
                //}

                SaveAndNotify(info);

                _logger.Debug("[{AccountDisplay}] task finished, id: {TaskId}, status: {TaskStatus}", Account.GetDisplay(), info.Id, info.Status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{AccountDisplay}] task execute error, id: {TaskId}", Account.GetDisplay(), info.Id);
                info.Fail("[Internal Server Error] " + ex.Message);

                SaveAndNotify(info);
            }
            finally
            {
                _runningTasks.Remove(info);
                _taskFutureMap.TryRemove(info.Id, out _);

                _semaphoreSlimLock.Unlock();

                SaveAndNotify(info);
            }
        }

        public void AddRunningTask(TaskInfo task)
        {
            _runningTasks.Add(task);
        }

        public void RemoveRunningTask(TaskInfo task)
        {
            _runningTasks.Remove(task);
        }

        /// <summary>
        /// Save and notify task status changes.
        /// </summary>
        /// <param name="task">Task information</param>
        private void SaveAndNotify(TaskInfo task)
        {
            try
            {
                _taskStoreService.Save(task);
                _notifyService.NotifyTaskChange(task);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Job notification execution exception {@0}", task.Id);
            }
        }

        /// <summary>
        /// Find running tasks that meet the conditions.
        /// </summary>
        /// <param name="condition">Condition</param>
        /// <returns>List of running tasks that meet the conditions</returns>
        public IEnumerable<TaskInfo> FindRunningTask(Func<TaskInfo, bool> condition)
        {
            return GetRunningTasks().Where(condition);
        }

        /// <summary>
        /// Get the running task by ID.
        /// </summary>
        /// <param name="id">Task ID</param>
        /// <returns>Task information</returns>
        public TaskInfo GetRunningTask(string id)
        {
            return GetRunningTasks().FirstOrDefault(t => id == t.Id);
        }

        /// <summary>
        /// Get historical tasks by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public TaskInfo GetTask(string id)
        {
            return _taskStoreService.Get(id);
        }

        /// <summary>
        /// Get the running task by nonce.
        /// </summary>
        /// <param name="nonce">Nonce</param>
        /// <returns>Task information</returns>
        public TaskInfo GetRunningTaskByNonce(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                return null;
            }

            return FindRunningTask(c => c.Nonce == nonce).FirstOrDefault();
        }

        /// <summary>
        /// Get the running task by message ID.
        /// </summary>
        /// <param name="messageId">Message ID</param>
        /// <returns>Task information</returns>
        public TaskInfo GetRunningTaskByMessageId(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return FindRunningTask(c => c.MessageId == messageId).FirstOrDefault();
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Clear cache
                ClearAccountCache(Account?.Id);

                BotMessageListener?.Dispose();
                WebSocketManager?.Dispose();

                _mre.Set();

                // Task cancellation
                _longToken.Cancel();

                // Stop background task
                _mre.Set(); // Release wait, prevent deadlock

                // Clean up background task
                if (_longTask != null && !_longTask.IsCompleted)
                {
                    try
                    {
                        _longTask.Wait();
                    }
                    catch
                    {
                        // Ignore exceptions from logging task
                    }
                }

                // Release unfinished tasks
                foreach (var runningTask in _runningTasks)
                {
                    runningTask.Fail("Forced cancellation"); // Cancel task (assuming TaskInfo has Cancel method)
                }

                // Clean up task queue
                while (_queueTasks.TryDequeue(out var taskInfo))
                {
                    taskInfo.Item1.Fail("Forced cancellation"); // Cancel task (assuming TaskInfo has Cancel method)
                }

                // Release semaphore
                //_semaphoreSlimLock?.Dispose();

                // Release signal
                _mre?.Dispose();

                // Release task map
                foreach (var task in _taskFutureMap.Values)
                {
                    if (!task.IsCompleted)
                    {
                        try
                        {
                            task.Wait(); // Wait for task to complete
                        }
                        catch
                        {
                            // Ignore exceptions from tasks
                        }
                    }
                }

                // Clean up resources
                _taskFutureMap.Clear();
                _runningTasks.Clear();
            }
            catch
            {
            }
        }


        /// <summary>
        /// Drawing
        /// </summary>
        /// <param name="info"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public async Task<Message> ImagineAsync(TaskInfo info, string prompt, string nonce)
        {
            prompt = GetPrompt(prompt, info);

            var json = (info.RealBotType ?? info.BotType) == EBotType.MID_JOURNEY ? _paramsMap["imagine"] : _paramsMap["imagineniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);

            JObject paramsJson = JObject.Parse(paramsStr);
            paramsJson["data"]["options"][0]["value"] = prompt;

            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        /// <summary>
        /// Upscale
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["upscale"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Variation
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["variation"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Perform action
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public async Task<Message> ActionAsync(
            string messageId,
            string customId,
            int messageFlags,
            string nonce,
            TaskInfo info)
        {
            var botType = info.RealBotType ?? info.BotType;

            string guid = null;
            string channelId = null;
            if (!string.IsNullOrWhiteSpace(info.SubInstanceId))
            {
                if (Account.SubChannelValues.ContainsKey(info.SubInstanceId))
                {
                    guid = Account.SubChannelValues[info.SubInstanceId];
                    channelId = info.SubInstanceId;
                }
            }

            var paramsStr = ReplaceInteractionParams(_paramsMap["action"], nonce, botType,
                guid, channelId)
                .Replace("$message_id", messageId);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            obj["data"]["custom_id"] = customId;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Image seed value
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SeedAsync(string jobId, string nonce, EBotType botType)
        {
            // Private channel
            var json = botType == EBotType.MID_JOURNEY ? _paramsMap["seed"] : _paramsMap["seedniji"];
            var paramsStr = json
              .Replace("$channel_id", botType == EBotType.MID_JOURNEY ? Account.PrivateChannelId : Account.NijiBotChannelId)
              .Replace("$session_id", DefaultSessionId)
              .Replace("$nonce", nonce)
              .Replace("$job_id", jobId);

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Image seed value message
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<Message> SeedMessagesAsync(string url)
        {
            try
            {
                // Decode
                url = System.Web.HttpUtility.UrlDecode(url);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };

                request.Headers.UserAgent.ParseAdd(Account.UserAgent);

                // Set request Authorization to UserToken, no Bearer prefix needed
                request.Headers.Add("Authorization", Account.UserToken);

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return Message.Success();
                }

                _logger.Error("Seed Http request execution failed {@0}, {@1}, {@2}", url, response.StatusCode, response.Content);

                return Message.Of((int)response.StatusCode, "Request failed");
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "Seed Http request execution exception {@0}", url);

                return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "Unknown error");
            }
        }

        /// <summary>
        /// Custom zoom
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public async Task<Message> ZoomAsync(TaskInfo info, string messageId, string customId, string prompt, string nonce)
        {
            customId = customId.Replace("MJ::CustomZoom::", "MJ::OutpaintCustomZoomModal::");
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["zoom"], nonce, info.RealBotType ?? info.BotType)
                .Replace("$message_id", messageId);
            //.Replace("$prompt", prompt);

            var obj = JObject.Parse(paramsStr);

            obj["data"]["custom_id"] = customId;
            obj["data"]["components"][0]["components"][0]["value"] = prompt;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Picture-to-text - Generate picture
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> PicReaderAsync(TaskInfo info, string messageId, string customId, string prompt, string nonce, EBotType botType)
        {
            var index = customId.Split("::").LastOrDefault();
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["picreader"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index);

            var obj = JObject.Parse(paramsStr);
            obj["data"]["components"][0]["components"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Remix operation
        /// </summary>
        /// <param name="action"></param>
        /// <param name="messageId"></param>
        /// <param name="modal"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RemixAsync(TaskInfo info, TaskAction action, string messageId, string modal, string customId, string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["remix"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$custom_id", customId)
                .Replace("$modal", modal);

            var obj = JObject.Parse(paramsStr);
            obj["data"]["components"][0]["components"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Perform info operation
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> InfoAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.MID_JOURNEY ? _paramsMap["info"] : _paramsMap["infoniji"];

            var paramsStr = ReplaceInteractionParams(content, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Perform settings button operation
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="custom_id"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingButtonAsync(string nonce, string custom_id, EBotType botType)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingbutton"], nonce)
                .Replace("$custom_id", custom_id);

            if (botType == EBotType.NIJI_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.NIJI_APPLICATION_ID)
                    .Replace("$message_id", Account.NijiSettingsMessageId);
            }
            else if (botType == EBotType.MID_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.MJ_APPLICATION_ID)
                    .Replace("$message_id", Account.SettingsMessageId);
            }

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// MJ perform settings select operation
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public async Task<Message> SettingSelectAsync(string nonce, string values)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingselect"], nonce)
              .Replace("$message_id", Account.SettingsMessageId)
              .Replace("$values", values);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Perform setting operation
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.NIJI_JOURNEY ? _paramsMap["settingniji"] : _paramsMap["setting"];

            var paramsStr = ReplaceInteractionParams(content, nonce);

            //var obj = JObject.Parse(paramsStr);
            //paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Display task information based on job id
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ShowAsync(string jobId, string nonce, EBotType botType)
        {
            var content = botType == EBotType.MID_JOURNEY ? _paramsMap["show"] : _paramsMap["showniji"];

            var paramsStr = ReplaceInteractionParams(content, nonce)
                .Replace("$value", jobId);

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Get prompt formatting
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public string GetPrompt(string prompt, TaskInfo info)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // If niji to mj is enabled
            if (info.RealBotType == EBotType.MID_JOURNEY && info.BotType == EBotType.NIJI_JOURNEY)
            {
                if (!prompt.Contains("--niji"))
                {
                    prompt += " --niji";
                }
            }

            // Replace 2 spaces with 1 space
            // Replace " -- " with " "
            prompt = prompt.Replace(" -- ", " ")
                .Replace("  ", " ").Replace("  ", " ").Replace("  ", " ").Trim();

            // Task specified speed mode
            if (info != null && info.Mode != null)
            {
                // Remove possible parameters from prompt
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                // If the task specifies a speed mode
                if (info.Mode != null)
                {
                    switch (info.Mode.Value)
                    {
                        case GenerationSpeedMode.RELAX:
                            prompt += " --relax";
                            break;

                        case GenerationSpeedMode.FAST:
                            prompt += " --fast";
                            break;

                        case GenerationSpeedMode.TURBO:
                            prompt += " --turbo";
                            break;

                        default:
                            break;
                    }
                }
            }

            // Allow speed mode
            if (Account.AllowModes?.Count > 0)
            {
                // Calculate disallowed speed modes and delete related parameters
                var notAllowModes = new List<string>();
                if (!Account.AllowModes.Contains(GenerationSpeedMode.RELAX))
                {
                    notAllowModes.Add("--relax");
                }
                if (!Account.AllowModes.Contains(GenerationSpeedMode.FAST))
                {
                    notAllowModes.Add("--fast");
                }
                if (!Account.AllowModes.Contains(GenerationSpeedMode.TURBO))
                {
                    notAllowModes.Add("--turbo");
                }

                // Remove possible parameters from prompt
                foreach (var mode in notAllowModes)
                {
                    prompt = prompt.Replace(mode, "");
                }
            }

            // Specify generation speed mode
            if (Account.Mode != null)
            {
                // Remove possible parameters from prompt
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                switch (Account.Mode.Value)
                {
                    case GenerationSpeedMode.RELAX:
                        prompt += " --relax";
                        break;

                    case GenerationSpeedMode.FAST:
                        prompt += " --fast";
                        break;

                    case GenerationSpeedMode.TURBO:
                        prompt += " --turbo";
                        break;

                    default:
                        break;
                }
            }

            // If fast mode is exhausted, specify slow mode
            if (Account.FastExhausted)
            {
                // Remove possible parameters from prompt
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                prompt += " --relax";
            }

            //// Handle escape characters such as quotes
            //return prompt.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");

            prompt = FormatUrls(prompt).ConfigureAwait(false).GetAwaiter().GetResult();

            return prompt;
        }

        /// <summary>
        /// Convert URLs in the prompt to official URLs
        /// The same URL is valid for 1 hour cache
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public async Task<string> FormatUrls(string prompt)
        {
            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableConvertOfficialLink)
            {
                return prompt;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // Use regex to extract all URLs
            var urls = Regex.Matches(prompt, @"(https?|ftp|file)://[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]")
                .Select(c => c.Value).Distinct().ToList();

            if (urls?.Count > 0)
            {
                var urlDic = new Dictionary<string, string>();
                foreach (var url in urls)
                {
                    try
                    {
                        // URL cache is valid for 24 hours by default
                        var okUrl = await _cache.GetOrCreateAsync($"tmp:{url}", async entry =>
                        {
                            entry.AbsoluteExpiration = DateTimeOffset.Now.AddHours(24);

                            var ff = new FileFetchHelper();
                            var res = await ff.FetchFileAsync(url);
                            if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                            {
                                return res.Url;
                            }
                            else if (res.Success && res.FileBytes.Length > 0)
                            {
                                // Upload to Discord server
                                var uploadResult = await UploadAsync(res.FileName, new DataUrl(res.ContentType, res.FileBytes));
                                if (uploadResult.Code != ReturnCode.SUCCESS)
                                {
                                    throw new LogicException(uploadResult.Code, uploadResult.Description);
                                }

                                if (uploadResult.Description.StartsWith("http"))
                                {
                                    return uploadResult.Description;
                                }
                                else
                                {
                                    var finalFileName = uploadResult.Description;
                                    var sendImageResult = await SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                                    {
                                        throw new LogicException(sendImageResult.Code, sendImageResult.Description);
                                    }

                                    return sendImageResult.Description;
                                }
                            }

                            throw new LogicException($"Failed to parse link {url}, {res?.Msg}");
                        });

                        urlDic[url] = okUrl;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to parse URL {0}", url);
                    }
                }

                // Replace URL
                foreach (var item in urlDic)
                {
                    prompt = prompt.Replace(item.Key, item.Value);
                }
            }

            return prompt;
        }

        /// <summary>
        /// Inpainting
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <returns></returns>
        public async Task<Message> InpaintAsync(TaskInfo info, string customId, string prompt, string maskBase64)
        {
            try
            {
                prompt = GetPrompt(prompt, info);

                customId = customId?.Replace("MJ::iframe::", "");

                // mask.replace(/^data:.+?;base64,/, ''),
                maskBase64 = maskBase64?.Replace("data:image/png;base64,", "");

                var obj = new
                {
                    customId = customId,
                    //full_prompt = null,
                    mask = maskBase64,
                    prompt = prompt,
                    userId = "0",
                    username = "0",
                };
                var paramsStr = Newtonsoft.Json.JsonConvert.SerializeObject(obj);

                // NIJI is also this link
                var response = await PostJsonAsync("https://936929561302675456.discordsays.com/inpaint/api/submit-job",
                    paramsStr);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return Message.Success();
                }

                return Message.Of((int)response.StatusCode, "Submission failed");
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "Inpainting request execution exception {@0}", info);

                return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "Unknown error");
            }
        }

        /// <summary>
        /// Reroll
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["reroll"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Describe
        /// </summary>
        /// <param name="finalFileName"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> DescribeAsync(string finalFileName, string nonce, EBotType botType)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);

            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["describeniji"] : _paramsMap["describe"];
            string paramsStr = ReplaceInteractionParams(json, nonce)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Describe
        /// </summary>
        /// <param name="link"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> DescribeByLinkAsync(string link, string nonce, EBotType botType)
        {
            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["describenijilink"] : _paramsMap["describelink"];
            string paramsStr = ReplaceInteractionParams(json, nonce)
                .Replace("$link", link);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Upload a longer prompt, mj can return a set of brief prompts
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ShortenAsync(TaskInfo info, string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt, info);

            var json = botType == EBotType.MID_JOURNEY || prompt.Contains("--niji") ? _paramsMap["shorten"] : _paramsMap["shortenniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);


            var obj = JObject.Parse(paramsStr);
            obj["data"]["options"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Blend
        /// </summary>
        /// <param name="finalFileNames"></param>
        /// <param name="dimensions"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce, EBotType botType)
        {
            var json = botType == EBotType.MID_JOURNEY || GlobalConfiguration.Setting.EnableConvertNijiToMj ? _paramsMap["blend"] : _paramsMap["blendniji"];

            string paramsStr = ReplaceInteractionParams(json, nonce);
            JObject paramsJson = JObject.Parse(paramsStr);
            JArray options = (JArray)paramsJson["data"]["options"];
            JArray attachments = (JArray)paramsJson["data"]["attachments"];
            for (int i = 0; i < finalFileNames.Count; i++)
            {
                string finalFileName = finalFileNames[i];
                string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
                JObject attachment = new JObject
                {
                    ["id"] = i.ToString(),
                    ["filename"] = fileName,
                    ["uploaded_filename"] = finalFileName
                };
                attachments.Add(attachment);
                JObject option = new JObject
                {
                    ["type"] = 11,
                    ["name"] = $"image{i + 1}",
                    ["value"] = i
                };
                options.Add(option);
            }
            options.Add(new JObject
            {
                ["type"] = 3,
                ["name"] = "dimensions",
                ["value"] = $"--ar {dimensions.GetValue()}"
            });
            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce,
            string guid = null, string channelId = null)
        {
            return paramsStr.Replace("$guild_id", guid ?? Account.GuildId)
                .Replace("$channel_id", channelId ?? Account.ChannelId)
                .Replace("$session_id", DefaultSessionId)
                .Replace("$nonce", nonce);
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce, EBotType botType,
            string guid = null, string channelId = null)
        {
            var str = ReplaceInteractionParams(paramsStr, nonce, guid, channelId);

            if (botType == EBotType.MID_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.MJ_APPLICATION_ID);
            }
            else if (botType == EBotType.NIJI_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.NIJI_APPLICATION_ID);
            }


            return str;
        }

        public async Task<Message> UploadAsync(string fileName, DataUrl dataUrl, bool useDiscordUpload = false)
        {
            // Enable conversion to cloud link
            if (GlobalConfiguration.Setting.EnableConvertAliyunLink && !useDiscordUpload)
            {
                try
                {
                    //var oss = new AliyunOssStorageService();

                    var localPath = $"attachments/{DateTime.Now:yyyyMMdd}/{fileName}";

                    var mt = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(localPath));
                    if (string.IsNullOrWhiteSpace(mt))
                    {
                        mt = "image/png";
                    }

                    var stream = new MemoryStream(dataUrl.Data);
                    var res = StorageHelper.Instance.SaveAsync(stream, localPath, dataUrl.MimeType ?? mt);
                    if (string.IsNullOrWhiteSpace(res?.Url))
                    {
                        throw new Exception("Failed to upload image to acceleration site");
                    }

                    //// Replace URL
                    //var customCdn = oss.Options.CustomCdn;
                    //if (string.IsNullOrWhiteSpace(customCdn))
                    //{
                    //    customCdn = oss.Options.Endpoint;
                    //}

                    //var url = $"{customCdn?.Trim()?.Trim('/')}/{res.Key}";

                    var url = res.Url;

                    return Message.Success(url);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to upload image to acceleration site");

                    return Message.Of(ReturnCode.FAILURE, "Failed to upload image to acceleration site");
                }
            }
            else
            {
                try
                {
                    JObject fileObj = new JObject
                    {
                        ["filename"] = fileName,
                        ["file_size"] = dataUrl.Data.Length,
                        ["id"] = "0"
                    };
                    JObject paramsJson = new JObject
                    {
                        ["files"] = new JArray { fileObj }
                    };
                    HttpResponseMessage response = await PostJsonAsync(_discordAttachmentUrl, paramsJson.ToString());
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        _logger.Error("Failed to upload image to discord, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                        return Message.Of(ReturnCode.VALIDATION_ERROR, "Failed to upload image to discord");
                    }
                    JArray array = JObject.Parse(await response.Content.ReadAsStringAsync())["attachments"] as JArray;
                    if (array == null || array.Count == 0)
                    {
                        return Message.Of(ReturnCode.VALIDATION_ERROR, "Failed to upload image to discord");
                    }
                    string uploadUrl = array[0]["upload_url"].ToString();
                    string uploadFilename = array[0]["upload_filename"].ToString();

                    await PutFileAsync(uploadUrl, dataUrl);

                    return Message.Success(uploadFilename);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to upload image to discord");

                    return Message.Of(ReturnCode.FAILURE, "Failed to upload image to discord");
                }
            }
        }

        public async Task<Message> SendImageMessageAsync(string content, string finalFileName)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
            string paramsStr = _paramsMap["message"]
                .Replace("$content", content)
                .Replace("$channel_id", Account.ChannelId)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            HttpResponseMessage response = await PostJsonAsync(_discordMessageUrl, paramsStr);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("Failed to send image message to discord, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return Message.Of(ReturnCode.VALIDATION_ERROR, "Failed to send image message to discord");
            }
            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync());
            JArray attachments = result["attachments"] as JArray;
            if (attachments != null && attachments.Count > 0)
            {
                return Message.Success(attachments[0]["url"].ToString());
            }
            return Message.Failure("Failed to send image message to discord: Image does not exist");
        }

        /// <summary>
        /// Automatically read the last message from discord (mark as read)
        /// </summary>
        /// <param name="lastMessageId"></param>
        /// <returns></returns>
        public async Task<Message> ReadMessageAsync(string lastMessageId)
        {
            if (string.IsNullOrWhiteSpace(lastMessageId))
            {
                return Message.Of(ReturnCode.VALIDATION_ERROR, "lastMessageId cannot be empty");
            }

            var paramsStr = @"{""token"":null,""last_viewed"":3496}";
            var url = $"{_discordMessageUrl}/{lastMessageId}/ack";

            HttpResponseMessage response = await PostJsonAsync(url, paramsStr);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("Failed to automatically read discord message, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return Message.Of(ReturnCode.VALIDATION_ERROR, "Failed to automatically read discord message");
            }
            return Message.Success();
        }

        private async Task PutFileAsync(string uploadUrl, DataUrl dataUrl)
        {
            uploadUrl = _discordHelper.GetDiscordUploadUrl(uploadUrl);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(dataUrl.Data)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(dataUrl.MimeType);
            request.Content.Headers.ContentLength = dataUrl.Data.Length;
            request.Headers.UserAgent.ParseAdd(Account.UserAgent);
            await _httpClient.SendAsync(request);
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string url, string paramsStr)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(paramsStr, Encoding.UTF8, "application/json")
            };

            request.Headers.UserAgent.ParseAdd(Account.UserAgent);

            // Set request Authorization to UserToken, no Bearer prefix needed
            request.Headers.Add("Authorization", Account.UserToken);

            return await _httpClient.SendAsync(request);
        }

        private async Task<Message> PostJsonAndCheckStatusAsync(string paramsStr)
        {
            // If TooManyRequests request fails, retry up to 3 times
            var count = 5;

            // Processed message ids
            var messageIds = new List<string>();
            do
            {
                HttpResponseMessage response = null;
                try
                {
                    response = await PostJsonAsync(_discordInteractionUrl, paramsStr);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        return Message.Success();
                    }
                    else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        count--;
                        if (count > 0)
                        {
                            // Wait 3~6 seconds
                            var random = new Random();
                            var seconds = random.Next(3, 6);
                            await Task.Delay(seconds * 1000);

                            _logger.Warning("Http request execution frequent, waiting to retry {@0}, {@1}, {@2}", paramsStr, response.StatusCode, response.Content);
                            continue;
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        count--;

                        if (count > 0)
                        {
                            // Wait 3~6 seconds
                            var random = new Random();
                            var seconds = random.Next(3, 6);
                            await Task.Delay(seconds * 1000);

                            // When it is NotFound
                            // It may be caused by message id confusion
                            if (paramsStr.Contains("message_id") && paramsStr.Contains("nonce"))
                            {
                                var obj = JObject.Parse(paramsStr);
                                if (obj.ContainsKey("message_id") && obj.ContainsKey("nonce"))
                                {
                                    var nonce = obj["nonce"].ToString();
                                    var message_id = obj["message_id"].ToString();
                                    if (!string.IsNullOrEmpty(nonce) && !string.IsNullOrWhiteSpace(message_id))
                                    {
                                        messageIds.Add(message_id);

                                        var t = GetRunningTaskByNonce(nonce);
                                        if (t != null && !string.IsNullOrWhiteSpace(t.ParentId))
                                        {
                                            var p = GetTask(t.ParentId);
                                            if (p != null)
                                            {
                                                var newMessageId = p.MessageIds.Where(c => !messageIds.Contains(c)).FirstOrDefault();
                                                if (!string.IsNullOrWhiteSpace(newMessageId))
                                                {
                                                    obj["message_id"] = newMessageId;

                                                    var oldStr = paramsStr;
                                                    paramsStr = obj.ToString();

                                                    _logger.Warning("Http may be message confusion, waiting to retry {@0}, {@1}, {@2}, {@3}", oldStr, paramsStr, response.StatusCode, response.Content);
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    _logger.Error("Http request execution failed {@0}, {@1}, {@2}", paramsStr, response.StatusCode, response.Content);

                    var error = $"{response.StatusCode}: {paramsStr.Substring(0, Math.Min(paramsStr.Length, 1000))}";

                    // If it is 403, directly disable the account
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.Error("Http request has no operation permission, disable account {@0}", paramsStr);

                        Account.Enable = false;
                        Account.DisabledReason = "Http request has no operation permission, disable account";
                        DbHelper.Instance.AccountStore.Update(Account);
                        ClearAccountCache(Account.Id);

                        return Message.Of(ReturnCode.FAILURE, "Request failed, disable account");
                    }

                    return Message.Of((int)response.StatusCode, error);
                }
                catch (HttpRequestException e)
                {
                    _logger.Error(e, "Http request execution exception {@0}", paramsStr);

                    return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "Unknown error");
                }
            } while (true);
        }

        /// <summary>
        /// Globally switch to fast mode
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> FastAsync(string nonce, EBotType botType)
        {
            if (botType == EBotType.NIJI_JOURNEY && Account.EnableNiji != true)
            {
                return Message.Success("Ignore submission, niji not enabled");
            }

            if (botType == EBotType.MID_JOURNEY && Account.EnableMj != true)
            {
                return Message.Success("Ignore submission, mj not enabled");
            }

            var json = botType == EBotType.MID_JOURNEY ? _paramsMap["fast"] : _paramsMap["fastniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Globally switch to relax mode
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RelaxAsync(string nonce, EBotType botType)
        {
            if (botType == EBotType.NIJI_JOURNEY && Account.EnableNiji != true)
            {
                return Message.Success("Ignore submission, niji not enabled");
            }

            if (botType == EBotType.MID_JOURNEY && Account.EnableMj != true)
            {
                return Message.Success("Ignore submission, mj not enabled");
            }

            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["relax"] : _paramsMap["relaxniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Globally switch fast mode check
        /// </summary>
        /// <returns></returns>
        public async Task RelaxToFastValidate()
        {
            try
            {
                // When fast is exhausted
                // And fast mode is enabled to switch to relax mode
                if (Account != null && Account.FastExhausted && Account.EnableRelaxToFast == true)
                {
                    // Check if the account has fast time every 6~12 hours and at startup
                    await RandomSyncInfo();

                    // Check if the info check time is within 5 minutes
                    if (Account.InfoUpdated != null && Account.InfoUpdated.Value.AddMinutes(5) >= DateTime.Now)
                    {
                        _logger.Information("Automatically switch to fast mode, validate {@0}", Account.ChannelId);

                        // Extract fastime
                        // If after checking, fast exceeds 1 hour, mark as fast not exhausted
                        var fastTime = Account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();
                        if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out var ftime) && ftime >= 1)
                        {
                            _logger.Information("Automatically switch to fast mode, start {@0}", Account.ChannelId);

                            // Mark as fast not exhausted
                            Account.FastExhausted = false;
                            DbHelper.Instance.AccountStore.Update("FastExhausted", Account);

                            // If automatic switching to fast is enabled, automatically switch to fast
                            try
                            {
                                if (Account.EnableRelaxToFast == true)
                                {
                                    Thread.Sleep(2500);
                                    await FastAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);

                                    Thread.Sleep(2500);
                                    await FastAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Automatically switch to fast mode, execution exception {@0}", Account.ChannelId);
                            }

                            ClearAccountCache(Account.Id);

                            _logger.Information("Automatically switch to fast mode, execution completed {@0}", Account.ChannelId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fast mode switch to relax mode, check execution exception");
            }
        }

        /// <summary>
        /// Randomly sync account information every 6-12 hours
        /// </summary>
        /// <returns></returns>
        public async Task RandomSyncInfo()
        {
            // Every 6~12 hours
            if (Account.InfoUpdated == null || Account.InfoUpdated.Value.AddMinutes(5) < DateTime.Now)
            {
                var key = $"fast_exhausted_{Account.ChannelId}";
                await _cache.GetOrCreateAsync(key, async c =>
                {
                    try
                    {
                        _logger.Information("Randomly sync account information start {@0}", Account.ChannelId);

                        // Randomly 6~12 hours
                        var random = new Random();
                        var minutes = random.Next(360, 600);
                        c.SetAbsoluteExpiration(TimeSpan.FromMinutes(minutes));

                        await _taskService.InfoSetting(Account.Id);

                        _logger.Information("Randomly sync account information completed {@0}", Account.ChannelId);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Randomly sync account information exception {@0}", Account.ChannelId);
                    }

                    return false;
                });
            }
        }
    }
}