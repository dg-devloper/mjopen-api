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
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using UAParser;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Class for handling Discord WebSocket connections, providing startup and message listening functionality
    /// https://discord.com/developers/docs/topics/gateway-events
    /// </summary>
    public class WebSocketManager : IDisposable
    {
        /// <summary>
        /// Maximum retry attempts for new connections
        /// </summary>
        private const int CONNECT_RETRY_LIMIT = 5;

        /// <summary>
        /// Reconnect error code
        /// </summary>
        public const int CLOSE_CODE_RECONNECT = 2001;

        /// <summary>
        /// Exception error code (create a new connection)
        /// </summary>
        public const int CLOSE_CODE_EXCEPTION = 1011;

        private readonly ILogger _logger;
        private readonly DiscordHelper _discordHelper;
        private readonly BotMessageListener _botListener;
        private readonly WebProxy _webProxy;
        private readonly DiscordInstance _discordInstance;

        /// <summary>
        /// Indicates whether resources have been disposed
        /// </summary>
        private bool _isDispose = false;

        /// <summary>
        /// Compressed messages
        /// </summary>
        private MemoryStream _compressed;

        /// <summary>
        /// Decompressor
        /// </summary>
        private DeflateStream _decompressor;

        /// <summary>
        /// wss
        /// </summary>
        public ClientWebSocket WebSocket { get; private set; }

        /// <summary>
        /// wss heartbeat process
        /// </summary>
        private Task _heartbeatTask;

        /// <summary>
        /// wss last message received time
        /// </summary>
        private long _lastMessageTime;

        /// <summary>
        /// wss heartbeat acknowledgment received
        /// </summary>
        private bool _heartbeatAck = true;

        /// <summary>
        /// wss heartbeat interval
        /// </summary>
        private long _heartbeatInterval = 41250;

        /// <summary>
        /// wss last session ID received by the client
        /// </summary>
        private string _sessionId;

        /// <summary>
        /// wss last sequence number received by the client
        /// </summary>
        private int? _sequence;

        /// <summary>
        /// wss gateway resume URL
        /// </summary>
        private string _resumeGatewayUrl;

        /// <summary>
        /// wss receive message and heartbeat token
        /// </summary>
        private CancellationTokenSource _receiveTokenSource;

        /// <summary>
        /// wss receive message process
        /// </summary>
        private Task _receiveTask;

        /// <summary>
        /// wss heartbeat queue
        /// </summary>
        private readonly ConcurrentQueue<long> _heartbeatTimes = new ConcurrentQueue<long>();

        /// <summary>
        /// wss latency
        /// </summary>
        private int Latency { get; set; }

        /// <summary>
        /// wss running status
        /// </summary>
        public bool Running { get; private set; }

        /// <summary>
        /// Message queue
        /// </summary>
        private readonly ConcurrentQueue<JsonElement> _messageQueue = new ConcurrentQueue<JsonElement>();

        private readonly Task _messageQueueTask;

        private readonly IMemoryCache _memoryCache;

        public WebSocketManager(
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            DiscordInstance discordInstance,
            IMemoryCache memoryCache)
        {
            _botListener = userMessageListener;
            _discordHelper = discordHelper;
            _webProxy = webProxy;
            _discordInstance = discordInstance;
            _memoryCache = memoryCache;

            _logger = Log.Logger;

            _messageQueueTask = new Task(MessageQueueDoWork, TaskCreationOptions.LongRunning);
            _messageQueueTask.Start();
        }

        private DiscordAccount Account => _discordInstance?.Account;

        /// <summary>
        /// Asynchronously start the WebSocket connection
        /// </summary>
        /// <param name="reconnect"></param>
        /// <returns></returns>
        public async Task<bool> StartAsync(bool reconnect = false)
        {
            try
            {
                // If resources have been disposed or the account is disabled, do not process further
                if (_isDispose || Account?.Enable != true)
                {
                    _logger.Warning("User is disabled or resources have been disposed {@0},{@1}", Account.ChannelId, _isDispose);
                    return false;
                }

                var isLock = await AsyncLocalLock.TryLockAsync($"contact_{Account.Id}", TimeSpan.FromMinutes(1), async () =>
                {
                    // Close existing connection and cancel related tasks
                    CloseSocket(reconnect);

                    // Reset token
                    _receiveTokenSource = new CancellationTokenSource();

                    WebSocket = new ClientWebSocket();

                    if (_webProxy != null)
                    {
                        WebSocket.Options.Proxy = _webProxy;
                    }

                    WebSocket.Options.SetRequestHeader("User-Agent", Account.UserAgent);
                    WebSocket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
                    WebSocket.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
                    WebSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
                    WebSocket.Options.SetRequestHeader("Pragma", "no-cache");
                    WebSocket.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");

                    // Get gateway address
                    var gatewayUrl = GetGatewayServer(reconnect ? _resumeGatewayUrl : null) + "/?encoding=json&v=9&compress=zlib-stream";

                    // Reconnect
                    if (reconnect && !string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                    {
                        // Resume
                        await WebSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                        // Attempt to resume session
                        await ResumeSessionAsync();
                    }
                    else
                    {
                        await WebSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                        // New connection, send identify message
                        await SendIdentifyMessageAsync();
                    }

                    _receiveTask = ReceiveMessagesAsync(_receiveTokenSource.Token);

                    _logger.Information("User WebSocket connection established {@0}", Account.ChannelId);

                });

                if (!isLock)
                {
                    _logger.Information($"Processing canceled, lock not acquired, reconnect: {reconnect}, {Account.ChannelId}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "User WebSocket connection exception {@0}", Account.ChannelId);

                HandleFailure(CLOSE_CODE_EXCEPTION, "User WebSocket connection exception");
            }

            return false;
        }

        /// <summary>
        /// Get gateway
        /// </summary>
        /// <param name="resumeGatewayUrl"></param>
        /// <returns></returns>
        private string GetGatewayServer(string resumeGatewayUrl = null)
        {
            return !string.IsNullOrWhiteSpace(resumeGatewayUrl) ? resumeGatewayUrl : _discordHelper.GetWss();
        }

        /// <summary>
        /// Perform resume or identify
        /// </summary>
        private async Task DoResumeOrIdentify()
        {
            if (!string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
            {
                await ResumeSessionAsync();
            }
            else
            {
                await SendIdentifyMessageAsync();
            }
        }

        /// <summary>
        /// Send identify message
        /// </summary>
        /// <returns></returns>
        private async Task SendIdentifyMessageAsync()
        {
            var authData = CreateAuthData();
            var identifyMessage = new { op = 2, d = authData };
            await SendMessageAsync(identifyMessage);

            _logger.Information("User sent IDENTIFY message {@0}", Account.ChannelId);
        }

        /// <summary>
        /// Resume connection
        /// </summary>
        /// <returns></returns>
        private async Task ResumeSessionAsync()
        {
            var resumeMessage = new
            {
                op = 6, // RESUME operation code
                d = new
                {
                    token = Account.UserToken,
                    session_id = _sessionId,
                    seq = _sequence,
                }
            };

            await SendMessageAsync(resumeMessage);

            _logger.Information("User sent RESUME message {@0}", Account.ChannelId);
        }

        /// <summary>
        /// Send message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task SendMessageAsync(object message)
        {
            if (WebSocket.State != WebSocketState.Open)
            {
                _logger.Warning("User WebSocket is closed, cannot send message {@0}", Account.ChannelId);
                return;
            }

            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await WebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        /// Receive messages
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (WebSocket == null)
                {
                    return;
                }

                while (WebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    var buffer = new byte[1024 * 4];

                    using (var ms = new MemoryStream())
                    {
                        try
                        {
                            do
                            {
                                //result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                                //ms.Write(buffer, 0, result.Count);

                                // Use Task.WhenAny to wait for ReceiveAsync or cancel task
                                var receiveTask = WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                                var completedTask = await Task.WhenAny(receiveTask, Task.Delay(-1, cancellationToken));

                                if (completedTask == receiveTask)
                                {
                                    result = receiveTask.Result;
                                    ms.Write(buffer, 0, result.Count);
                                }
                                else
                                {
                                    // Task canceled
                                    _logger.Information("Receive message task canceled {@0}", Account.ChannelId);
                                    return;
                                }

                            } while (!result.EndOfMessage && !cancellationToken.IsCancellationRequested);

                            ms.Seek(0, SeekOrigin.Begin);
                            if (result.MessageType == WebSocketMessageType.Binary)
                            {
                                buffer = ms.ToArray();
                                await HandleBinaryMessageAsync(buffer);
                            }
                            else if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var message = Encoding.UTF8.GetString(ms.ToArray());
                                HandleMessage(message);
                            }
                            else if (result.MessageType == WebSocketMessageType.Close)
                            {
                                _logger.Warning("User WebSocket connection closed {@0}", Account.ChannelId);

                                await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                                HandleFailure((int)result.CloseStatus, result.CloseStatusDescription);
                            }
                            else
                            {
                                _logger.Warning("User received unknown message {@0}", Account.ChannelId);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Do not reconnect
                            //HandleFailure(CLOSE_CODE_EXCEPTION, "User exception while receiving message");

                            _logger.Error(ex, "User exception while receiving ws message {@0}", Account.ChannelId);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Task canceled
                _logger.Information("Receive message task canceled {@0}", Account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Receive message processing exception {@0}", Account.ChannelId);
            }
        }

        /// <summary>
        /// Handle byte type messages
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private async Task HandleBinaryMessageAsync(byte[] buffer)
        {
            using (var decompressed = new MemoryStream())
            {
                if (_compressed == null)
                    _compressed = new MemoryStream();
                if (_decompressor == null)
                    _decompressor = new DeflateStream(_compressed, CompressionMode.Decompress);

                if (buffer[0] == 0x78)
                {
                    _compressed.Write(buffer, 2, buffer.Length - 2);
                    _compressed.SetLength(buffer.Length - 2);
                }
                else
                {
                    _compressed.Write(buffer, 0, buffer.Length);
                    _compressed.SetLength(buffer.Length);
                }

                _compressed.Position = 0;
                await _decompressor.CopyToAsync(decompressed);
                _compressed.Position = 0;
                decompressed.Position = 0;

                using (var reader = new StreamReader(decompressed, Encoding.UTF8))
                {
                    var messageContent = await reader.ReadToEndAsync();
                    HandleMessage(messageContent);
                }
            }
        }

        /// <summary>
        /// Handle message
        /// </summary>
        /// <param name="message"></param>
        private void HandleMessage(string message)
        {
            // Do not wait for message processing to complete, return immediately
            _ = Task.Run(async () =>
            {
                try
                {
                    var data = JsonDocument.Parse(message).RootElement;
                    var opCode = data.GetProperty("op").GetInt32();
                    var seq = data.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number ? (int?)seqElement.GetInt32() : null;
                    var type = data.TryGetProperty("t", out var typeElement) ? typeElement.GetString() : null;

                    await ProcessMessageAsync((GatewayOpCode)opCode, seq, type, data);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to process received WebSocket message {@0}", Account.ChannelId);
                }
            });
        }

        /// <summary>
        /// Perform heartbeat
        /// </summary>
        /// <param name="intervalMillis"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task RunHeartbeatAsync(int intervalMillis, CancellationToken cancelToken)
        {
            // Generate a random number between 0.9 and 1.0
            var r = new Random();
            var v = 1 - r.NextDouble() / 10;
            var delayInterval = (int)(intervalMillis * v);

            //int delayInterval = (int)(intervalMillis * 0.9);

            try
            {
                _logger.Information("Heartbeat Started {@0}", Account.ChannelId);

                while (!cancelToken.IsCancellationRequested)
                {
                    int now = Environment.TickCount;

                    if (_heartbeatTimes.Count != 0 && (now - _lastMessageTime) > intervalMillis)
                    {
                        if (WebSocket.State == WebSocketState.Open)
                        {
                            HandleFailure(CLOSE_CODE_RECONNECT, "Server did not respond to the last heartbeat, reconnecting");
                            return;
                        }
                    }

                    _heartbeatTimes.Enqueue(now);
                    try
                    {
                        await SendHeartbeatAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Heartbeat Errored {@0}", Account.ChannelId);
                    }

                    int delay = Math.Max(0, delayInterval - Latency);
                    await Task.Delay(delay, cancelToken).ConfigureAwait(false);
                }

                _logger.Information("Heartbeat Stopped {@0}", Account.ChannelId);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Heartbeat Canceled {@0}", Account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Heartbeat Errored {@0}", Account.ChannelId);
            }
        }

        /// <summary>
        /// Send heartbeat
        /// </summary>
        /// <returns></returns>
        private async Task SendHeartbeatAsync()
        {
            if (!_heartbeatAck)
            {
                _logger.Warning("User did not receive heartbeat ACK, attempting to reconnect... {@0}", Account.ChannelId);
                TryReconnect();
                return;
            }

            var heartbeatMessage = new { op = 1, d = _sequence };

            await SendMessageAsync(heartbeatMessage);
            _logger.Information("User sent HEARTBEAT message {@0}", Account.ChannelId);

            _heartbeatAck = false;
        }

        private async Task ProcessMessageAsync(GatewayOpCode opCode, int? seq, string type, JsonElement payload)
        {
            if (seq != null)
            {
                _sequence = seq.Value;
            }

            _lastMessageTime = Environment.TickCount;

            try
            {
                switch (opCode)
                {
                    case GatewayOpCode.Hello:
                        {
                            _logger.Information("Received Hello {@0}", Account.ChannelId);
                            _heartbeatInterval = payload.GetProperty("d").GetProperty("heartbeat_interval").GetInt64();

                            // Attempt to release the previous heartbeat task
                            if (_heartbeatTask != null && !_heartbeatTask.IsCompleted)
                            {
                                try
                                {
                                    _receiveTokenSource?.Cancel();

                                    await _heartbeatTask;
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "Failed to cancel heartbeat task");
                                }

                                _heartbeatTask = null;
                            }

                            //// Send identify message first
                            //await DoResumeOrIdentify();

                            // Then handle heartbeat
                            _heartbeatAck = true;
                            _heartbeatTimes.Clear();
                            Latency = 0;
                            _heartbeatTask = RunHeartbeatAsync((int)_heartbeatInterval, _receiveTokenSource.Token);
                        }
                        break;

                    case GatewayOpCode.Heartbeat:
                        {
                            _logger.Information("Received Heartbeat {@0}", Account.ChannelId);

                            // Immediately send heartbeat
                            var heartbeatMessage = new { op = 1, d = _sequence };
                            await SendMessageAsync(heartbeatMessage);

                            _logger.Information("Received Heartbeat message sent {@0}", Account.ChannelId);
                        }
                        break;

                    case GatewayOpCode.HeartbeatAck:
                        {
                            _logger.Information("Received HeartbeatAck {@0}", Account.ChannelId);

                            if (_heartbeatTimes.TryDequeue(out long time))
                            {
                                Latency = (int)(Environment.TickCount - time);
                                _heartbeatAck = true;
                            }
                        }
                        break;

                    case GatewayOpCode.InvalidSession:
                        {
                            _logger.Warning("Received InvalidSession {@0}", Account.ChannelId);
                            _logger.Warning("Failed to resume previous session {@0}", Account.ChannelId);

                            _sessionId = null;
                            _sequence = null;
                            _resumeGatewayUrl = null;

                            HandleFailure(CLOSE_CODE_EXCEPTION, "Invalid authorization, creating a new connection");
                        }
                        break;

                    case GatewayOpCode.Reconnect:
                        {
                            _logger.Warning("Received Reconnect {@0}", Account.ChannelId);

                            HandleFailure(CLOSE_CODE_RECONNECT, "Received reconnect request, will automatically reconnect");
                        }
                        break;

                    case GatewayOpCode.Resume:
                        {
                            _logger.Information("Resume {@0}", Account.ChannelId);

                            OnSocketSuccess();
                        }
                        break;

                    case GatewayOpCode.Dispatch:
                        {
                            _logger.Information("Received Dispatch {@0}, {@1}", type, Account.ChannelId);
                            HandleDispatch(payload);
                        }
                        break;

                    default:
                        _logger.Warning("Unknown OpCode ({@0}) {@1}", opCode, Account.ChannelId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling {opCode}{(type != null ? $" ({type})" : "")}, {Account.ChannelId}");
            }
        }



        /// <summary>
        /// Handle received message
        /// </summary>
        /// <param name="data"></param>
        private void HandleDispatch(JsonElement data)
        {
            if (data.TryGetProperty("t", out var t) && t.GetString() == "READY")
            {
                _sessionId = data.GetProperty("d").GetProperty("session_id").GetString();
                _resumeGatewayUrl = data.GetProperty("d").GetProperty("resume_gateway_url").GetString() + "/?encoding=json&v=9&compress=zlib-stream";

                OnSocketSuccess();
            }
            else if (data.TryGetProperty("t", out var resumed) && resumed.GetString() == "RESUMED")
            {
                OnSocketSuccess();
            }
            else
            {
                _messageQueue.Enqueue(data);
            }
        }

        private void MessageQueueDoWork()
        {
            while (true)
            {
                while (_messageQueue.TryDequeue(out var message))
                {
                    try
                    {
                        _botListener.OnMessage(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Exception occurred while processing message queue {@0}", Account.ChannelId);
                    }
                }

                Thread.Sleep(10);
            }
        }


        /// <summary>
        /// Create authorization information
        /// </summary>
        /// <returns></returns>
        private JsonElement CreateAuthData()
        {
            var uaParser = Parser.GetDefault();
            var agent = uaParser.Parse(Account.UserAgent);
            var connectionProperties = new
            {
                browser = agent.UA.Family,
                browser_user_agent = Account.UserAgent,
                browser_version = agent.UA.Major + "." + agent.UA.Minor,
                client_build_number = 222963,
                client_event_source = (string)null,
                device = agent.Device.Model,
                os = agent.OS.Family,
                referer = "https://www.midjourney.com",
                referring_domain = "www.midjourney.com",
                release_channel = "stable",
                system_locale = "zh-CN"
            };

            var presence = new
            {
                activities = Array.Empty<object>(),
                afk = false,
                since = 0,
                status = "online"
            };

            var clientState = new
            {
                api_code_version = 0,
                guild_versions = new { },
                highest_last_message_id = "0",
                private_channels_version = "0",
                read_state_version = 0,
                user_guild_settings_version = -1,
                user_settings_version = -1
            };

            var authData = new
            {
                capabilities = 16381,
                client_state = clientState,
                compress = false,
                presence = presence,
                properties = connectionProperties,
                token = Account.UserToken
            };

            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(authData));
        }

        /// <summary>
        /// Handle error
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        private void HandleFailure(int code, string reason)
        {
            _logger.Error("User WebSocket connection failed, code {0}: {1}, {2}", code, reason, Account.ChannelId);

            if (!Running)
            {
                NotifyWss(code, reason);
            }

            Running = false;

            if (code >= 4000)
            {
                _logger.Warning("User cannot reconnect, closed by {0}({1}) {2}, attempting new connection... ", code, reason, Account.ChannelId);
                TryNewConnect();
            }
            else if (code == 2001)
            {
                _logger.Warning("User closed by {0}({1}), attempting to reconnect... {2}", code, reason, Account.ChannelId);
                TryReconnect();
            }
            else
            {
                _logger.Warning("User closed by {0}({1}), attempting new connection... {2}", code, reason, Account.ChannelId);
                TryNewConnect();
            }
        }

        /// <summary>
        /// Reconnect
        /// </summary>
        private void TryReconnect()
        {
            try
            {
                if (_isDispose)
                {
                    return;
                }

                var success = StartAsync(true).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!success)
                {
                    _logger.Warning("User reconnect failed {@0}, attempting new connection", Account.ChannelId);

                    Thread.Sleep(1000);
                    TryNewConnect();
                }
            }
            catch (Exception e)
            {
                _logger.Warning(e, "User reconnect exception {@0}, attempting new connection", Account.ChannelId);

                Thread.Sleep(1000);
                TryNewConnect();
            }
        }

        /// <summary>
        /// New connection
        /// </summary>
        private void TryNewConnect()
        {
            if (_isDispose)
            {
                return;
            }

            var isLock = LocalLock.TryLock("TryNewConnect", TimeSpan.FromSeconds(3), () =>
            {
                for (int i = 1; i <= CONNECT_RETRY_LIMIT; i++)
                {
                    try
                    {
                        // If the number of failures exceeds the limit within 5 minutes, disable the account
                        var ncKey = $"TryNewConnect_{Account.ChannelId}";
                        _memoryCache.TryGetValue(ncKey, out int count);
                        if (count > CONNECT_RETRY_LIMIT)
                        {
                            _logger.Warning("Number of new connection failures exceeds the limit, disabling account");
                            DisableAccount("Number of new connection failures exceeds the limit, disabling account");
                            return;
                        }
                        _memoryCache.Set(ncKey, count + 1, TimeSpan.FromMinutes(5));

                        var success = StartAsync(false).ConfigureAwait(false).GetAwaiter().GetResult();
                        if (success)
                        {
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Warning(e, "User new connection failed, attempt {@0}, {@1}", i, Account.ChannelId);

                        Thread.Sleep(5000);
                    }
                }

                if (WebSocket == null || WebSocket.State != WebSocketState.Open)
                {
                    _logger.Error("Unable to reconnect, automatically disabling account");

                    DisableAccount("Unable to reconnect, automatically disabling account");
                }
            });

            if (!isLock)
            {
                _logger.Warning("New connection job is already in progress, duplicate execution is prohibited");
            }
        }

        /// <summary>
        /// Stop and disable account
        /// </summary>
        public void DisableAccount(string msg)
        {
            try
            {
                // Save
                Account.Enable = false;
                Account.DisabledReason = msg;

                DbHelper.Instance.AccountStore.Update(Account);

                _discordInstance?.ClearAccountCache(Account.Id);
                _discordInstance?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to disable account {@0}", Account.ChannelId);
            }
            finally
            {
                // Email notification
                var smtp = GlobalConfiguration.Setting?.Smtp;
                EmailJob.Instance.EmailSend(smtp, $"MJ account disable notification-{Account.ChannelId}",
                    $"{Account.ChannelId}, {Account.DisabledReason}");
            }
        }

        /// <summary>
        /// Write info message
        /// </summary>
        /// <param name="msg"></param>
        private void LogInfo(string msg)
        {
            _logger.Information(msg + ", {@ChannelId}", Account.ChannelId);
        }

        /// <summary>
        /// If open, close wss
        /// </summary>
        private void CloseSocket(bool reconnect = false)
        {
            try
            {
                try
                {
                    if (_receiveTokenSource != null)
                    {
                        LogInfo("Force cancel message token");
                        _receiveTokenSource?.Cancel();
                        _receiveTokenSource?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    if (_receiveTask != null)
                    {
                        LogInfo("Force release message task");
                        _receiveTask?.Wait(1000);
                        _receiveTask?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    if (_heartbeatTask != null)
                    {
                        LogInfo("Force release heartbeat task");
                        _heartbeatTask?.Wait(1000);
                        _heartbeatTask?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    // Force close
                    if (WebSocket != null && WebSocket.State != WebSocketState.Closed)
                    {
                        LogInfo("Force close wss close");

                        if (reconnect)
                        {
                            // Reconnect using 4000 to disconnect
                            var status = (WebSocketCloseStatus)4000;
                            var closeTask = Task.Run(() => WebSocket.CloseOutputAsync(status, "", new CancellationToken()));
                            if (!closeTask.Wait(5000))
                            {
                                _logger.Warning("WebSocket close operation timed out {@0}", Account.ChannelId);

                                // If the close operation times out, force abort the connection
                                WebSocket?.Abort();
                            }
                        }
                        else
                        {
                            var closeTask = Task.Run(() => WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Force close", CancellationToken.None));
                            if (!closeTask.Wait(5000))
                            {
                                _logger.Warning("WebSocket close operation timed out {@0}", Account.ChannelId);

                                // If the close operation times out, force abort the connection
                                WebSocket?.Abort();
                            }
                        }
                    }
                }
                catch
                {
                }

                // Force close
                try
                {
                    if (WebSocket != null && (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived))
                    {
                        LogInfo("Force close wss open");

                        WebSocket.Abort();
                        WebSocket.Dispose();
                    }
                }
                catch
                {
                }
            }
            catch
            {
                // do
            }
            finally
            {
                WebSocket = null;
                _receiveTokenSource = null;
                _receiveTask = null;
                _heartbeatTask = null;

                LogInfo("WebSocket resources have been released");
            }

            // No delay needed for now
            //// Delay to ensure all resources are properly released
            //Thread.Sleep(1000);
        }

        /// <summary>
        /// Notify error or success
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        private void NotifyWss(int code, string reason)
        {
            if (!Account.Lock)
            {
                Account.DisabledReason = reason;
            }

            // Save
            DbHelper.Instance.AccountStore.Update("Enable,DisabledReason", Account);
            _discordInstance?.ClearAccountCache(Account.Id);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            try
            {
                _isDispose = true;

                CloseSocket();

                _messageQueue?.Clear();
                _messageQueueTask?.Dispose();
            }
            catch
            {
            }

            try
            {
                WebSocket?.Dispose();
                _botListener?.Dispose();

                // Lock does not need to be released
                //_stateLock?.Dispose();
            }
            catch
            {

            }
        }

        /// <summary>
        /// Connection successful
        /// </summary>
        private void OnSocketSuccess()
        {
            Running = true;
            _discordInstance.DefaultSessionId = _sessionId;

            NotifyWss(ReturnCode.SUCCESS, "");
        }
    }

    internal enum GatewayOpCode : byte
    {
        Dispatch = 0,
        Heartbeat = 1,
        Identify = 2,
        PresenceUpdate = 3,
        VoiceStateUpdate = 4,
        VoiceServerPing = 5,
        Resume = 6,
        Reconnect = 7,
        RequestGuildMembers = 8,
        InvalidSession = 9,
        Hello = 10,
        HeartbeatAck = 11,
        GuildSync = 12
    }
}