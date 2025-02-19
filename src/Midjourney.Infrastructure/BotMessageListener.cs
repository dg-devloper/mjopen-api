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
using Discord.Commands;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using RestSharp;
using Serilog;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using EventData = Midjourney.Infrastructure.Dto.EventData;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Bot message listener.
    /// </summary>
    public class BotMessageListener : IDisposable
    {
        private readonly ILogger _logger = Log.Logger;

        private readonly WebProxy _webProxy;
        private readonly DiscordHelper _discordHelper;
        private readonly ProxyProperties _properties;

        private DiscordInstance _discordInstance;
        private IEnumerable<BotMessageHandler> _botMessageHandlers;
        private IEnumerable<UserMessageHandler> _userMessageHandlers;

        public BotMessageListener(DiscordHelper discordHelper, WebProxy webProxy = null)
        {
            _properties = GlobalConfiguration.Setting;
            _webProxy = webProxy;
            _discordHelper = discordHelper;
        }

        public void Init(
            DiscordInstance instance,
            IEnumerable<BotMessageHandler> botMessageHandlers,
            IEnumerable<UserMessageHandler> userMessageHandlers)
        {
            _discordInstance = instance;
            _botMessageHandlers = botMessageHandlers;
            _userMessageHandlers = userMessageHandlers;
        }

        private DiscordAccount Account => _discordInstance?.Account;

        public async Task StartAsync()
        {
            // Bot TOKEN optional
            if (string.IsNullOrWhiteSpace(Account.BotToken))
            {
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                //// How much logging do you want to see?
                LogLevel = LogSeverity.Info,

                // If you or another service needs to do anything with messages
                // (eg. checking Reactions, checking the content of edited/deleted messages),
                // you must set the MessageCacheSize. You may adjust the number as needed.
                //MessageCacheSize = 50,

                RestClientProvider = _webProxy != null ? CustomRestClientProvider.Create(_webProxy, true)
                : DefaultRestClientProvider.Create(true),
                WebSocketProvider = DefaultWebSocketProvider.Create(_webProxy),

                // Read message permissions GatewayIntents.MessageContent
                // GatewayIntents.AllUnprivileged & ~(GatewayIntents.GuildScheduledEvents | GatewayIntents.GuildInvites) | GatewayIntents.MessageContent
                GatewayIntents = GatewayIntents.AllUnprivileged & ~(GatewayIntents.GuildScheduledEvents | GatewayIntents.GuildInvites) | GatewayIntents.MessageContent
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                // Again, log level:
                LogLevel = LogSeverity.Info,

                // There's a few more properties you can set,
                // for example, case-insensitive commands.
                CaseSensitiveCommands = false,
            });

            // Subscribe the logging handler to both the client and the CommandService.
            _client.Log += LogAction;
            _commands.Log += LogAction;

            await _client.LoginAsync(TokenType.Bot, Account.BotToken);
            await _client.StartAsync();

            // Centralize the logic for commands into a separate method.
            // Subscribe a handler to see if a message invokes a command.
            _client.MessageReceived += HandleCommandAsync;
            _client.MessageUpdated += MessageUpdatedAsync;
        }

        private DiscordSocketClient _client;

        // Keep the CommandService and DI container around for use with commands.
        // These two types require you install the Discord.Net.Commands package.
        private CommandService _commands;

        // Example of a logging handler. This can be re-used by addons
        // that ask for a Func<LogMessage, Task>.
        private Task LogAction(LogMessage message)
        {
            switch (message.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;

                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;

                case LogSeverity.Info:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;

                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
            }

            Log.Information($"{DateTime.Now,-19} [{message.Severity,8}] {message.Source}: {message.Message} {message.Exception}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Handle received messages
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private async Task HandleCommandAsync(SocketMessage arg)
        {
            try
            {
                var msg = arg as SocketUserMessage;
                if (msg == null)
                    return;

                _logger.Information($"BOT Received, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, mid: {msg?.InteractionMetadata?.Id}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content) && msg.Author.IsBot)
                {
                    foreach (var handler in _botMessageHandlers.OrderBy(h => h.Order()))
                    {
                        // Message lock processing
                        LocalLock.TryLock($"lock_{msg.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            handler.Handle(_discordInstance, MessageType.CREATE, msg);
                        });
                    }
                }
                // describe resubmit
                // MJ::Picread::Retry
                else if (msg.Embeds.Count > 0 && msg.Author.IsBot && msg.Components.Count > 0
                    && msg.Components.First().Components.Any(x => x.CustomId?.Contains("PicReader") == true))
                {
                    // Message lock processing
                    LocalLock.TryLock($"lock_{msg.Id}", TimeSpan.FromSeconds(10), () =>
                    {
                        var em = msg.Embeds.FirstOrDefault();
                        if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                        {
                            var handler = _botMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(BotDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, msg);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception handling bot message");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Handle updated messages
        /// </summary>
        /// <param name="before"></param>
        /// <param name="after"></param>
        /// <param name="channel"></param>
        /// <returns></returns>
        private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            try
            {
                var msg = after as IUserMessage;
                if (msg == null)
                    return;

                _logger.Information($"BOT Updated, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content)
                    && msg.Content.Contains("%")
                    && msg.Author.IsBot)
                {
                    foreach (var handler in _botMessageHandlers.OrderBy(h => h.Order()))
                    {
                        handler.Handle(_discordInstance, MessageType.UPDATE, after);
                    }
                }
                else if (msg.InteractionMetadata is ApplicationCommandInteractionMetadata metadata && metadata.Name == "describe")
                {
                    var handler = _botMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(BotDescribeSuccessHandler));
                    handler?.Handle(_discordInstance, MessageType.CREATE, after);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception handling bot updated message");
            }
        }

        /// <summary>
        /// Handle received user ws messages
        /// </summary>
        /// <param name="raw"></param>
        public void OnMessage(JsonElement raw)
        {
            try
            {
                _logger.Debug("User received message {@0}", raw.ToString());

                if (!raw.TryGetProperty("t", out JsonElement messageTypeElement))
                {
                    return;
                }

                var messageType = MessageTypeExtensions.Of(messageTypeElement.GetString());
                if (messageType == null || messageType == MessageType.DELETE)
                {
                    return;
                }

                if (!raw.TryGetProperty("d", out JsonElement data))
                {
                    return;
                }

                // Trigger CF human verification
                if (messageType == MessageType.INTERACTION_IFRAME_MODAL_CREATE)
                {
                    if (data.TryGetProperty("title", out var t))
                    {
                        if (t.GetString() == "Action required to continue")
                        {
                            _logger.Warning("CF verification {@0}, {@1}", Account.ChannelId, raw.ToString());

                            // Global lock
                            // Wait for manual or automatic processing
                            // Retry up to 3 times, process up to 5 minutes
                            LocalLock.TryLock($"cf_{Account.ChannelId}", TimeSpan.FromSeconds(10), () =>
                            {
                                try
                                {
                                    var custom_id = data.TryGetProperty("custom_id", out var c) ? c.GetString() : string.Empty;
                                    var application_id = data.TryGetProperty("application", out var a) && a.TryGetProperty("id", out var id) ? id.GetString() : string.Empty;
                                    if (!string.IsNullOrWhiteSpace(custom_id) && !string.IsNullOrWhiteSpace(application_id))
                                    {
                                        Account.Lock = true;

                                        // MJ::iframe::U3NmeM-lDTrmTCN_QY5n4DXvjrQRPGOZrQiLa-fT9y3siLA2AGjhj37IjzCqCtVzthUhGBj4KKqNSntQ
                                        var hash = custom_id.Split("::").LastOrDefault();
                                        var hashUrl = $"https://{application_id}.discordsays.com/captcha/api/c/{hash}/ack?hash=1";

                                        // Verification in progress, in lock mode
                                        Account.DisabledReason = "CF automatic verification in progress...";
                                        Account.CfHashUrl = hashUrl;
                                        Account.CfHashCreated = DateTime.Now;

                                        DbHelper.Instance.AccountStore.Update(Account);
                                        _discordInstance.ClearAccountCache(Account.Id);

                                        try
                                        {
                                            // Notify verification server
                                            if (!string.IsNullOrWhiteSpace(_properties.CaptchaNotifyHook) && !string.IsNullOrWhiteSpace(_properties.CaptchaServer))
                                            {
                                                // Use restsharp to notify, up to 3 times
                                                var notifyCount = 0;
                                                do
                                                {
                                                    if (notifyCount > 3)
                                                    {
                                                        break;
                                                    }

                                                    notifyCount++;
                                                    var notifyUrl = $"{_properties.CaptchaServer.Trim().TrimEnd('/')}/cf/verify";
                                                    var client = new RestClient();
                                                    var request = new RestRequest(notifyUrl, Method.Post);
                                                    request.AddHeader("Content-Type", "application/json");
                                                    var body = new CaptchaVerfyRequest
                                                    {
                                                        Url = hashUrl,
                                                        State = Account.ChannelId,
                                                        NotifyHook = _properties.CaptchaNotifyHook,
                                                        Secret = _properties.CaptchaNotifySecret
                                                    };
                                                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
                                                    request.AddJsonBody(json);
                                                    var response = client.Execute(request);
                                                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                                                    {
                                                        // Notified automatic verification server
                                                        _logger.Information("CF verification, notified server {@0}, {@1}", Account.ChannelId, hashUrl);

                                                        break;
                                                    }

                                                    Thread.Sleep(1000);
                                                } while (true);

                                                // Send email
                                                EmailJob.Instance.EmailSend(_properties.Smtp, $"CF automatic human verification-{Account.ChannelId}", hashUrl);
                                            }
                                            else
                                            {
                                                // Send hashUrl GET request, return {"hash":"OOUxejO94EQNxsCODRVPbg","token":"dXDm-gSb4Zlsx-PCkNVyhQ"}
                                                // Concatenate verification CF verification URL with hash and token

                                                WebProxy webProxy = null;
                                                var proxy = GlobalConfiguration.Setting.Proxy;
                                                if (!string.IsNullOrEmpty(proxy?.Host))
                                                {
                                                    webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                                                }
                                                var hch = new HttpClientHandler
                                                {
                                                    UseProxy = webProxy != null,
                                                    Proxy = webProxy
                                                };

                                                var httpClient = new HttpClient(hch);
                                                var response = httpClient.GetAsync(hashUrl).Result;
                                                var con = response.Content.ReadAsStringAsync().Result;
                                                if (!string.IsNullOrWhiteSpace(con))
                                                {
                                                    // Parse
                                                    var json = JsonSerializer.Deserialize<JsonElement>(con);
                                                    if (json.TryGetProperty("hash", out var h) && json.TryGetProperty("token", out var to))
                                                    {
                                                        var hashStr = h.GetString();
                                                        var token = to.GetString();

                                                        if (!string.IsNullOrWhiteSpace(hashStr) && !string.IsNullOrWhiteSpace(token))
                                                        {
                                                            // Send verification URL
                                                            // Concatenate verification CF verification URL with hash and token
                                                            // https://editor.midjourney.com/captcha/challenge/index.html?hash=OOUxejO94EQNxsCODRVPbg&token=dXDm-gSb4Zlsx-PCkNVyhQ

                                                            var url = $"https://editor.midjourney.com/captcha/challenge/index.html?hash={hashStr}&token={token}";

                                                            _logger.Information($"{Account.ChannelId}, CF human verification URL: {url}");

                                                            Account.CfUrl = url;

                                                            // Send email
                                                            EmailJob.Instance.EmailSend(_properties.Smtp, $"CF manual human verification-{Account.ChannelId}", url);
                                                        }
                                                    }
                                                }

                                                Account.DisabledReason = "CF manual verification...";

                                                DbHelper.Instance.AccountStore.Update(Account);
                                                _discordInstance.ClearAccountCache(Account.Id);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.Error(ex, "CF human verification processing failed {@0}", Account.ChannelId);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "CF human verification processing exception {@0}", Account.ChannelId);
                                }
                            });

                            return;
                        }
                    }
                }

                // Content
                var contentStr = string.Empty;
                if (data.TryGetProperty("content", out JsonElement content))
                {
                    contentStr = content.GetString();
                }

                // Author
                var authorName = string.Empty;
                var authId = string.Empty;
                if (data.TryGetProperty("author", out JsonElement author)
                    && author.TryGetProperty("username", out JsonElement username)
                    && author.TryGetProperty("id", out JsonElement uid))
                {
                    authorName = username.GetString();
                    authId = uid.GetString();
                }

                // Application ID is the bot ID
                var applicationId = string.Empty;
                if (data.TryGetProperty("application_id", out JsonElement application))
                {
                    applicationId = application.GetString();
                }

                // Interaction metadata id
                var metaId = string.Empty;
                var metaName = string.Empty;
                if (data.TryGetProperty("interaction_metadata", out JsonElement meta) && meta.TryGetProperty("id", out var mid))
                {
                    metaId = mid.GetString();

                    metaName = meta.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                }

                // Handle remix switch
                if (metaName == "prefer remix" && !string.IsNullOrWhiteSpace(contentStr))
                {
                    // MJ
                    if (authId == Constants.MJ_APPLICATION_ID)
                    {
                        if (contentStr.StartsWith("Remix mode turned off"))
                        {
                            foreach (var item in Account.Components)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 2;
                                    }
                                }
                            }
                        }
                        else if (contentStr.StartsWith("Remix mode turned on"))
                        {
                            foreach (var item in Account.Components)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 3;
                                    }
                                }
                            }
                        }
                    }
                    // NIJI
                    else if (authId == Constants.NIJI_APPLICATION_ID)
                    {
                        if (contentStr.StartsWith("Remix mode turned off"))
                        {
                            foreach (var item in Account.NijiComponents)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 2;
                                    }
                                }
                            }
                        }
                        else if (contentStr.StartsWith("Remix mode turned on"))
                        {
                            foreach (var item in Account.NijiComponents)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 3;
                                    }
                                }
                            }
                        }
                    }

                    DbHelper.Instance.AccountStore.Update("Components,NijiComponents", Account);
                    _discordInstance.ClearAccountCache(Account.Id);

                    return;
                }
                // Sync settings and remix
                else if (metaName == "settings")
                {
                    // settings command
                    var eventDataMsg = data.Deserialize<EventData>();
                    if (eventDataMsg != null && eventDataMsg.InteractionMetadata?.Name == "settings" && eventDataMsg.Components?.Count > 0)
                    {
                        if (applicationId == Constants.NIJI_APPLICATION_ID)
                        {
                            Account.NijiComponents = eventDataMsg.Components;
                            DbHelper.Instance.AccountStore.Update("NijiComponents", Account);
                            _discordInstance.ClearAccountCache(Account.Id);
                        }
                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                        {
                            Account.Components = eventDataMsg.Components;
                            DbHelper.Instance.AccountStore.Update("Components", Account);
                            _discordInstance.ClearAccountCache(Account.Id);
                        }
                    }
                }
                // Switch fast and relax
                else if (metaName == "fast" || metaName == "relax" || metaName == "turbo")
                {
                    // MJ
                    // Done! Your jobs now do not consume fast-hours, but might take a little longer. You can always switch back with /fast
                    if (metaName == "fast" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 3;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 3;
                                }
                            }
                        }
                    }
                    else if (metaName == "turbo" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                    }
                    else if (metaName == "relax" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                    }

                    DbHelper.Instance.AccountStore.Update("Components,NijiComponents", Account);
                    _discordInstance.ClearAccountCache(Account.Id);

                    return;
                }

                // Private channel
                var isPrivareChannel = false;
                if (data.TryGetProperty("channel_id", out JsonElement channelIdElement))
                {
                    var cid = channelIdElement.GetString();
                    if (cid == Account.PrivateChannelId || cid == Account.NijiBotChannelId)
                    {
                        isPrivareChannel = true;
                    }

                    if (channelIdElement.GetString() == Account.ChannelId)
                    {
                        isPrivareChannel = false;
                    }

                    // All different
                    // If there is a channel id, but it is not the current channel id, ignore it
                    if (cid != Account.ChannelId
                        && cid != Account.PrivateChannelId
                        && cid != Account.NijiBotChannelId)
                    {
                        // If it is not a sub-channel id, ignore it
                        if (!Account.SubChannelValues.ContainsKey(cid))
                        {
                            return;
                        }
                    }
                }

                if (isPrivareChannel)
                {
                    // Private channel
                    if (messageType == MessageType.CREATE && data.TryGetProperty("id", out JsonElement subIdElement))
                    {
                        var id = subIdElement.GetString();

                        // Define regex pattern
                        // "**girl**\n**Job ID**: 6243686b-7ab1-4174-a9fe-527cca66a829\n**seed** 1259687673"
                        var pattern = @"\*\*Job ID\*\*:\s*(?<jobId>[a-fA-F0-9-]{36})\s*\*\*seed\*\*\s*(?<seed>\d+)";

                        // Create regex object
                        var regex = new Regex(pattern);

                        // Try to match input string
                        var match = regex.Match(contentStr);

                        if (match.Success)
                        {
                            // Extract Job ID and seed
                            var jobId = match.Groups["jobId"].Value;
                            var seed = match.Groups["seed"].Value;

                            if (!string.IsNullOrWhiteSpace(jobId) && !string.IsNullOrWhiteSpace(seed))
                            {
                                var task = _discordInstance.FindRunningTask(c => c.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default) == jobId).FirstOrDefault();
                                if (task != null)
                                {
                                    if (!task.MessageIds.Contains(id))
                                    {
                                        task.MessageIds.Add(id);
                                    }

                                    task.Seed = seed;
                                }
                            }
                        }
                        else
                        {
                            // Get the url property of the first object in the attachments array
                            // seed message processing
                            if (data.TryGetProperty("attachments", out JsonElement attachments) && attachments.ValueKind == JsonValueKind.Array)
                            {
                                if (attachments.EnumerateArray().Count() > 0)
                                {
                                    var item = attachments.EnumerateArray().First();

                                    if (item.ValueKind != JsonValueKind.Null
                                        && item.TryGetProperty("url", out JsonElement url)
                                        && url.ValueKind != JsonValueKind.Null)
                                    {
                                        var imgUrl = url.GetString();
                                        if (!string.IsNullOrWhiteSpace(imgUrl))
                                        {
                                            var hash = _discordHelper.GetMessageHash(imgUrl);
                                            if (!string.IsNullOrWhiteSpace(hash))
                                            {
                                                var task = _discordInstance.FindRunningTask(c => c.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default) == hash).FirstOrDefault();
                                                if (task != null)
                                                {
                                                    if (!task.MessageIds.Contains(id))
                                                    {
                                                        task.MessageIds.Add(id);
                                                    }
                                                    task.SeedMessageId = id;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    return;
                }

                // Task id
                // Task nonce
                if (data.TryGetProperty("id", out JsonElement idElement))
                {
                    var id = idElement.GetString();

                    _logger.Information($"User message, {messageType}, {Account.GetDisplay()} - id: {id}, mid: {metaId}, {authorName}, content: {contentStr}");

                    var isEm = data.TryGetProperty("embeds", out var em);
                    if ((messageType == MessageType.CREATE || messageType == MessageType.UPDATE) && isEm)
                    {

                        if (metaName == "info" && messageType == MessageType.UPDATE)
                        {

                            // info command
                            if (em.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement item in em.EnumerateArray())
                                {
                                    if (item.TryGetProperty("title", out var emtitle))
                                    {
                                        if (emtitle.GetString().Contains("Your info"))
                                        {
                                            if (item.TryGetProperty("description", out var description))
                                            {
                                                var dic = ParseDiscordData(description.GetString());
                                                foreach (var d in dic)
                                                {
                                                    if (d.Key == "Job Mode")
                                                    {
                                                        if (applicationId == Constants.NIJI_APPLICATION_ID)
                                                        {
                                                            Account.SetProperty($"Niji {d.Key}", d.Value);
                                                        }
                                                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                                                        {
                                                            Account.SetProperty(d.Key, d.Value);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Account.SetProperty(d.Key, d.Value);
                                                    }
                                                }

                                                var db = DbHelper.Instance.AccountStore;
                                                Account.InfoUpdated = DateTime.Now;

                                                db.Update("InfoUpdated,Properties", Account);
                                                _discordInstance?.ClearAccountCache(Account.Id);
                                            }
                                        }
                                    }

                                    if (emtitle.GetString().Contains("You are blocked."))
                                    {
                                        if (item.TryGetProperty("description", out var description))
                                        {
                                            _logger.Debug("Accont disabled, {0} , {0}", Account.Id, description.GetString());
                                            if (description.GetString().Contains("You have been blocked from accessing Midjourney."))
                                            {

                                                try
                                                {
                                                    Account.Enable = false;
                                                    Account.DisabledReason = description.GetString();
                                                    DbHelper.Instance.AccountStore.Update("Enable,DisabledReason", Account);
                                                    _discordInstance?.ClearAccountCache(Account.Id);

                                                    EmailJob.Instance.EmailSend(_properties.Smtp, $"MJ account disable notification-{Account.Id}",
                                                        $"{Account.Id}, {Account.DisabledReason}");
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.Error(ex, "Account blocked, Exception disabling account {@0}", Account.Id);
                                                }

                                                return;
                                            }
                                        }
                                    }

                                }
                            }

                            return;
                        }
                        else if (metaName == "settings" && data.TryGetProperty("components", out var components))
                        {
                            // settings command
                            var eventDataMsg = data.Deserialize<EventData>();
                            if (eventDataMsg != null && eventDataMsg.InteractionMetadata?.Name == "settings" && eventDataMsg.Components?.Count > 0)
                            {
                                if (applicationId == Constants.NIJI_APPLICATION_ID)
                                {
                                    Account.NijiComponents = eventDataMsg.Components;
                                    Account.NijiSettingsMessageId = id;

                                    DbHelper.Instance.AccountStore.Update("NijiComponents,NijiSettingsMessageId", Account);
                                    _discordInstance?.ClearAccountCache(Account.Id);
                                }
                                else if (applicationId == Constants.MJ_APPLICATION_ID)
                                {
                                    Account.Components = eventDataMsg.Components;
                                    Account.SettingsMessageId = id;

                                    DbHelper.Instance.AccountStore.Update("Components,SettingsMessageId", Account);
                                    _discordInstance?.ClearAccountCache(Account.Id);
                                }
                            }

                            return;
                        }

                        // em is a JSON array
                        if (em.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in em.EnumerateArray())
                            {
                                if (item.TryGetProperty("title", out var emTitle))
                                {
                                    // Determine if the account usage is exhausted
                                    var title = emTitle.GetString();

                                    // 16711680 error, 65280 success, 16776960 warning
                                    var color = item.TryGetProperty("color", out var colorEle) ? colorEle.GetInt32() : 0;

                                    // Description
                                    var desc = item.GetProperty("description").GetString();

                                    _logger.Information($"User embeds message, {messageType}, {Account.GetDisplay()} - id: {id}, mid: {metaId}, {authorName}, embeds: {title}, {color}, {desc}");

                                    // Invalid parameter, banned prompt, invalid prompt
                                    var errorTitles = new[] {
                                        "Invalid prompt", // Invalid prompt
                                        "Invalid parameter", // Invalid parameter
                                        "Banned prompt detected", // Banned prompt
                                        "Invalid link", // Invalid link
                                        "Request cancelled due to output filters",
                                        "Queue full", // Queue full
                                    };

                                    // Skipped titles
                                    var continueTitles = new[] { "Action needed to continue" };

                                    // fast usage exhausted
                                    if (title == "Credits exhausted")
                                    {
                                        // Your processing logic
                                        _logger.Information($"Account {Account.GetDisplay()} usage exhausted");

                                        var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                        if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                        {
                                            task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                        }

                                        if (task != null)
                                        {
                                            task.Fail("Account usage exhausted");
                                        }


                                        // Mark fast mode exhausted
                                        Account.FastExhausted = true;

                                        // Automatically set to relax if fast is exhausted
                                        if (Account.FastExhausted == true && Account.EnableAutoSetRelax == true)
                                        {
                                            Account.AllowModes = new List<GenerationSpeedMode>() { GenerationSpeedMode.RELAX };

                                            if (Account.CoreSize > 3)
                                            {
                                                Account.CoreSize = 3;
                                            }
                                        }

                                        DbHelper.Instance.AccountStore.Update("AllowModes,FastExhausted,CoreSize", Account);
                                        _discordInstance?.ClearAccountCache(Account.Id);

                                        // If auto switch to relax mode is enabled
                                        if (Account.EnableFastToRelax == true)
                                        {
                                            // Switch to relax mode
                                            // Lock switch to relax mode
                                            // Execute switch relax command
                                            // If not currently in relax mode, switch to relax mode, lock switch
                                            if (Account.MjFastModeOn || Account.NijiFastModeOn)
                                            {
                                                _ = AsyncLocalLock.TryLockAsync($"relax:{Account.ChannelId}", TimeSpan.FromSeconds(5), async () =>
                                                {
                                                    try
                                                    {
                                                        Thread.Sleep(2500);
                                                        await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);

                                                        Thread.Sleep(2500);
                                                        await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.Error(ex, "Exception switching to relax mode {@0}", Account.ChannelId);
                                                    }
                                                });
                                            }
                                        }
                                        else
                                        {
                                            // Your processing logic
                                            _logger.Warning($"Account {Account.GetDisplay()} usage exhausted, auto disable account");

                                            // Disable account after 5s
                                            Task.Run(() =>
                                            {
                                                try
                                                {
                                                    Thread.Sleep(5 * 1000);

                                                    // Save
                                                    Account.Enable = false;
                                                    Account.DisabledReason = "Account usage exhausted";

                                                    DbHelper.Instance.AccountStore.Update(Account);
                                                    _discordInstance?.ClearAccountCache(Account.Id);
                                                    _discordInstance?.Dispose();


                                                    // Send email
                                                    EmailJob.Instance.EmailSend(_properties.Smtp, $"MJ account disable notification-{Account.ChannelId}",
                                                        $"{Account.ChannelId}, {Account.DisabledReason}");
                                                }
                                                catch (Exception ex)
                                                {
                                                    Log.Error(ex, "Account usage exhausted, exception disabling account {@0}", Account.ChannelId);
                                                }
                                            });
                                        }

                                        return;
                                    }
                                    // Temporarily banned/subscription cancelled/subscription expired/subscription paused
                                    else if (title == "Pending mod message"
                                        || title == "Blocked"
                                        || title == "Plan Cancelled"
                                        || title == "Subscription required"
                                        || title == "Subscription paused")
                                    {
                                        // Your processing logic
                                        _logger.Warning($"Account {Account.GetDisplay()} {title}, auto disable account");

                                        var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                        if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                        {
                                            task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                        }

                                        if (task != null)
                                        {
                                            task.Fail(title);
                                        }

                                        // Disable account after 5s
                                        Task.Run(() =>
                                        {
                                            try
                                            {
                                                Thread.Sleep(5 * 1000);

                                                // Save
                                                Account.Enable = false;
                                                Account.DisabledReason = $"{title}, {desc}";

                                                DbHelper.Instance.AccountStore.Update(Account);

                                                _discordInstance?.ClearAccountCache(Account.Id);
                                                _discordInstance?.Dispose();

                                                // Send email
                                                EmailJob.Instance.EmailSend(_properties.Smtp, $"MJ account disable notification-{Account.ChannelId}",
                                                    $"{Account.ChannelId}, {Account.DisabledReason}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, "{@0}, exception disabling account {@1}", title, Account.ChannelId);
                                            }
                                        });

                                        return;
                                    }
                                    // Running tasks full (usually more than 3)
                                    else if (title == "Job queued")
                                    {
                                        if (data.TryGetProperty("nonce", out JsonElement noneEle))
                                        {
                                            var nonce = noneEle.GetString();
                                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                                            {
                                                // Set task id corresponding to none
                                                var task = _discordInstance.GetRunningTaskByNonce(nonce);
                                                if (task != null)
                                                {
                                                    if (messageType == MessageType.CREATE)
                                                    {
                                                        // No need to assign
                                                        //task.MessageId = id;

                                                        task.Description = $"{title}, {desc}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // Temporarily skipped business processing
                                    else if (continueTitles.Contains(title))
                                    {
                                        _logger.Warning("Skipped embeds {@0}, {@1}", Account.ChannelId, data.ToString());
                                    }
                                    // Other error messages
                                    else if (errorTitles.Contains(title)
                                        || color == 16711680
                                        || title.Contains("Invalid")
                                        || title.Contains("error")
                                        || title.Contains("denied"))
                                    {

                                        if (data.TryGetProperty("nonce", out JsonElement noneEle))
                                        {
                                            var nonce = noneEle.GetString();
                                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                                            {
                                                // Set task id corresponding to none
                                                var task = _discordInstance.GetRunningTaskByNonce(nonce);
                                                if (task != null)
                                                {
                                                    // User needs to agree to Tos
                                                    if (title.Contains("Tos not accepted"))
                                                    {
                                                        try
                                                        {
                                                            var tosData = data.Deserialize<EventData>();
                                                            var customId = tosData?.Components?.SelectMany(x => x.Components)
                                                                .Where(x => x.Label == "Accept ToS")
                                                                .FirstOrDefault()?.CustomId;

                                                            if (!string.IsNullOrWhiteSpace(customId))
                                                            {
                                                                var nonce2 = SnowFlake.NextId();
                                                                var tosRes = _discordInstance.ActionAsync(id, customId, tosData.Flags, nonce2, task)
                                                                    .ConfigureAwait(false).GetAwaiter().GetResult();

                                                                if (tosRes?.Code == ReturnCode.SUCCESS)
                                                                {
                                                                    _logger.Information("Successfully processed Tos {@0}", Account.ChannelId);
                                                                    return;
                                                                }
                                                                else
                                                                {
                                                                    _logger.Information("Failed to process Tos {@0}, {@1}", Account.ChannelId, tosRes);
                                                                }
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _logger.Error(ex, "Exception processing Tos {@0}", Account.ChannelId);
                                                        }
                                                    }

                                                    var error = $"{title}, {desc}";

                                                    task.MessageId = id;
                                                    task.Description = error;

                                                    if (!task.MessageIds.Contains(id))
                                                    {
                                                        task.MessageIds.Add(id);
                                                    }

                                                    task.Fail(error);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // If meta is show
                                            // Indicates that the show task failed
                                            if (metaName == "show" && !string.IsNullOrWhiteSpace(desc))
                                            {
                                                // Set task id corresponding to none
                                                var task = _discordInstance.GetRunningTasks().Where(c => c.Action == TaskAction.SHOW && desc.Contains(c.JobId)).FirstOrDefault();
                                                if (task != null)
                                                {
                                                    if (messageType == MessageType.CREATE)
                                                    {
                                                        var error = $"{title}, {desc}";

                                                        task.MessageId = id;
                                                        task.Description = error;

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        task.Fail(error);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // If no none is obtained, try to use mid to get task
                                                var task = _discordInstance.GetRunningTasks()
                                                    .Where(c => c.MessageId == metaId || c.MessageIds.Contains(metaId) || c.InteractionMetadataId == metaId)
                                                    .FirstOrDefault();
                                                if (task != null)
                                                {
                                                    var error = $"{title}, {desc}";
                                                    task.Fail(error);
                                                }
                                                else
                                                {
                                                    // If no none is obtained
                                                    _logger.Error("Unknown embeds error {@0}, {@1}", Account.ChannelId, data.ToString());
                                                }
                                            }
                                        }
                                    }
                                    // Unknown message
                                    else
                                    {
                                        if (data.TryGetProperty("nonce", out JsonElement noneEle))
                                        {
                                            var nonce = noneEle.GetString();
                                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                                            {
                                                // Set task id corresponding to none
                                                var task = _discordInstance.GetRunningTaskByNonce(nonce);
                                                if (task != null)
                                                {
                                                    if (messageType == MessageType.CREATE)
                                                    {
                                                        task.MessageId = id;
                                                        task.Description = $"{title}, {desc}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        _logger.Warning($"Unknown message: {title}, {desc}, {Account.ChannelId}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }


                    if (data.TryGetProperty("nonce", out JsonElement noneElement))
                    {
                        var nonce = noneElement.GetString();

                        _logger.Debug($"User message, {messageType}, id: {id}, nonce: {nonce}");

                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                        {
                            // Set task id corresponding to none
                            var task = _discordInstance.GetRunningTaskByNonce(nonce);
                            if (task != null && task.Status != TaskStatus.SUCCESS && task.Status != TaskStatus.FAILURE)
                            {
                                if (isPrivareChannel)
                                {
                                    // Private channel
                                }
                                else
                                {
                                    // Drawing channel

                                    // MJ interaction success
                                    if (messageType == MessageType.INTERACTION_SUCCESS)
                                    {
                                        task.InteractionMetadataId = id;
                                    }
                                    // MJ partial redraw complete
                                    else if (messageType == MessageType.INTERACTION_IFRAME_MODAL_CREATE
                                        && data.TryGetProperty("custom_id", out var custom_id))
                                    {
                                        task.SetProperty(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, custom_id.GetString());

                                        //task.MessageId = id;

                                        if (!task.MessageIds.Contains(id))
                                        {
                                            task.MessageIds.Add(id);
                                        }
                                    }
                                    else
                                    {
                                        //task.MessageId = id;

                                        if (!task.MessageIds.Contains(id))
                                        {
                                            task.MessageIds.Add(id);
                                        }
                                    }

                                    // Only CREATE will set message id
                                    if (messageType == MessageType.CREATE)
                                    {
                                        task.MessageId = id;

                                        // Set prompt full text
                                        if (!string.IsNullOrWhiteSpace(contentStr) && contentStr.Contains("(Waiting to start)"))
                                        {
                                            if (string.IsNullOrWhiteSpace(task.PromptFull))
                                            {
                                                task.PromptFull = ConvertUtils.GetFullPrompt(contentStr);
                                            }
                                        }
                                    }

                                    // If the task is remix auto submit task
                                    if (task.RemixAutoSubmit
                                        && task.RemixModaling == true
                                        && messageType == MessageType.INTERACTION_SUCCESS)
                                    {
                                        task.RemixModalMessageId = id;
                                    }
                                }
                            }
                        }
                    }
                }

                var eventData = data.Deserialize<EventData>();

                // If the message type is CREATE
                // Then process the message confirmation event again to ensure high availability of the message
                if (messageType == MessageType.CREATE)
                {
                    Thread.Sleep(50);

                    if (eventData != null &&
                        (eventData.ChannelId == Account.ChannelId || Account.SubChannelValues.ContainsKey(eventData.ChannelId)))
                    {
                        foreach (var messageHandler in _userMessageHandlers.OrderBy(h => h.Order()))
                        {
                            // Processed
                            if (eventData.GetProperty<bool?>(Constants.MJ_MESSAGE_HANDLED, default) == true)
                            {
                                return;
                            }

                            // Message lock processing
                            LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                            {
                                messageHandler.Handle(_discordInstance, messageType.Value, eventData);
                            });
                        }
                    }
                }
                // describe resubmit
                // MJ::Picread::Retry
                else if (eventData.Embeds.Count > 0 && eventData.Author?.Bot == true && eventData.Components.Count > 0
                    && eventData.Components.First().Components.Any(x => x.CustomId?.Contains("PicReader") == true))
                {
                    // Message lock processing
                    LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                    {
                        var em = eventData.Embeds.FirstOrDefault();
                        if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        }
                    });
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(eventData.Content)
                          && eventData.Content.Contains("%")
                          && eventData.Author?.Bot == true)
                    {
                        // Message lock processing
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserStartAndProgressHandler));
                            handler?.Handle(_discordInstance, MessageType.UPDATE, eventData);
                        });
                    }
                    else if (eventData.InteractionMetadata?.Name == "describe")
                    {
                        // Message lock processing
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        });
                    }
                    else if (eventData.InteractionMetadata?.Name == "shorten"
                        // shorten show details -> PromptAnalyzerExtended
                        || eventData.Embeds?.FirstOrDefault()?.Footer?.Text.Contains("Click on a button to imagine one of the shortened prompts") == true)
                    {
                        // Message lock processing
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserShortenSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception handling user message {@0}", raw.ToString());
            }
        }

        private static Dictionary<string, string> ParseDiscordData(string input)
        {
            var data = new Dictionary<string, string>();

            foreach (var line in input.Split('\n'))
            {
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Replace("**", "").Trim();
                    var value = parts[1].Trim();
                    data[key] = value;
                }
            }

            return data;
        }

        public void Dispose()
        {
            // Unsubscribe from events
            if (_client != null)
            {
                _client.Log -= LogAction;
                _client.MessageReceived -= HandleCommandAsync;
                _client.MessageUpdated -= MessageUpdatedAsync;

                // Dispose the Discord client
                _client.Dispose();
            }

            // Dispose the command service
            if (_commands != null)
            {
                _commands.Log -= LogAction;
            }
        }
    }
}