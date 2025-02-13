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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.StandardTable;
using Midjourney.Infrastructure.Storage;
using MongoDB.Driver;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// Admin API
    /// Used for querying and managing accounts
    /// </summary>
    [ApiController]
    [Route("mj/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ITaskService _taskService;

        // Whether the user is anonymous
        private readonly bool _isAnonymous;

        private readonly DiscordLoadBalancer _loadBalancer;
        private readonly DiscordAccountInitializer _discordAccountInitializer;
        private readonly ProxyProperties _properties;
        private readonly WorkContext _workContext;

        public AdminController(
            ITaskService taskService,
            DiscordLoadBalancer loadBalancer,
            DiscordAccountInitializer discordAccountInitializer,
            IMemoryCache memoryCache,
            WorkContext workContext,
            IHttpContextAccessor context)
        {
            _memoryCache = memoryCache;
            _loadBalancer = loadBalancer;
            _taskService = taskService;
            _discordAccountInitializer = discordAccountInitializer;
            _workContext = workContext;

            // If not an admin and in demo mode, then the user is anonymous
            var user = workContext.GetUser();

            _isAnonymous = user?.Role != EUserRole.ADMIN;
            _properties = GlobalConfiguration.Setting;

            // Regular users cannot log in to the admin backend, except in demo mode
            // If the current user is a regular user
            // and not an anonymous controller
            if (user?.Role != EUserRole.ADMIN)
            {
            var endpoint = context.HttpContext.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
            if (!allowAnonymous && GlobalConfiguration.IsDemoMode != true)
            {
                // If the user is a regular user and not an anonymous controller, return 401
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.HttpContext.Response.WriteAsync("Forbidden: User is not admin.");
                return;
            }
            }
        }

        /// <summary>
        /// Restart
        /// </summary>
        /// <returns></returns>
        [HttpPost("restart")]
        public Result Restart()
        {
            try
            {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            // Use dotnet command to start DLL
            var fileName = "dotnet";
            var arguments = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                UseShellExecute = true
            };
            Process.Start(processStartInfo);

            // Exit the current application
            Environment.Exit(0);

            return Result.Ok("Application is restarting...");
            }
            catch (Exception ex)
            {
            Log.Error(ex, "System auto-restart exception");

            return Result.Fail("Restart failed, please restart manually");
            }
        }

        /// <summary>
        /// Register User
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("register")]
        [AllowAnonymous]
        public Result Register([FromBody] RegisterDto registerDto)
        {
            if (registerDto == null || string.IsNullOrWhiteSpace(registerDto.Email))
            {
                throw new LogicException("Invalid email length");
            }

            // Validate length
            if (registerDto.Email.Length < 5 || registerDto.Email.Length > 50)
            {
                throw new LogicException("Invalid email length");
            }

            var mail = registerDto.Email.Trim();

            // Validate email format
            var isMatch = Regex.IsMatch(mail, @"^[\w-]+(\.[\w-]+)*@[\w-]+(\.[\w-]+)+$");
            if (!isMatch)
            {
                throw new LogicException("Invalid email format");
            }

            // Check if registration is open
            // If email service is not configured, registration is not allowed
            if (GlobalConfiguration.Setting.EnableRegister != true
                || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword))
            {
                throw new LogicException("Registration is closed");
            }

            // Each IP can only register one account per day
            var ip = _workContext.GetIp();
            var key = $"register:{ip}";
            if (_memoryCache.TryGetValue(key, out _))
            {
                throw new LogicException("Registration too frequent");
            }

            // Check if user already exists
            var user = DbHelper.Instance.UserStore.Single(u => u.Email == mail);
            if (user != null)
            {
                throw new LogicException("User already exists");
            }

            user = new User
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = EUserRole.USER,
                Status = EUserStatus.NORMAL,
                DayDrawLimit = GlobalConfiguration.Setting.RegisterUserDefaultDayLimit,
                Email = mail,
                RegisterIp = ip,
                RegisterTime = DateTime.Now,
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Name = mail.Split('@').FirstOrDefault()
            };
            DbHelper.Instance.UserStore.Add(user);

            // Send email
            EmailJob.Instance.EmailSend(GlobalConfiguration.Setting.Smtp,
                $"Midjourney Proxy Registration Notification", $"Your login password is: {user.Token}",
                user.Email);

            // Set cache
            _memoryCache.Set(key, true, TimeSpan.FromDays(1));

            return Result.Ok();
        }

        /// <summary>
        /// Admin login
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] string token)
        {
            // If guest mode is not enabled, anonymous login is not allowed
            if (GlobalConfiguration.IsDemoMode != true && string.IsNullOrWhiteSpace(token))
            {
                throw new LogicException("Login prohibited");
            }

            // If in DEMO mode and no token is provided, return an empty token
            if (GlobalConfiguration.IsDemoMode == true && string.IsNullOrWhiteSpace(token))
            {
                return Ok(new
                {
                    code = 1,
                    apiSecret = "",
                });
            }

            // If guest mode is enabled
            //if (string.IsNullOrWhiteSpace(token) && GlobalConfiguration.Setting.EnableGuest)
            //{
            //    return Ok(new
            //    {
            //        code = 1,
            //        apiSecret = "",
            //    });
            //}

            var user = DbHelper.Instance.UserStore.Single(u => u.Token == token);
            if (user == null)
            {
                throw new LogicException("User token is incorrect");
            }

            if (user.Status == EUserStatus.DISABLED)
            {
                throw new LogicException("User has been disabled");
            }

            // Non-demo mode, regular users and guests cannot log in to the admin backend
            if (user.Role != EUserRole.ADMIN && GlobalConfiguration.IsDemoMode != true)
            {
                throw new LogicException("User does not have permission");
            }

            // Update last login time
            user.LastLoginTime = DateTime.Now;
            user.LastLoginIp = _workContext.GetIp();

            DbHelper.Instance.UserStore.Update(user);

            return Ok(new
            {
                code = 1,
                apiSecret = user.Token,
            });
        }

        /// <summary>
        /// Admin logout
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("logout")]
        public ActionResult Logout()
        {
            return Ok();
        }

        /// <summary>
        /// CF verification notification (allow anonymous)
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("account-cf-notify")]
        public ActionResult Validate([FromBody] CaptchaVerfyRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.State) && !string.IsNullOrWhiteSpace(request.Url))
            {
                var item = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == request.State);

                if (item != null && item.Lock)
                {
                    var secret = GlobalConfiguration.Setting.CaptchaNotifySecret;
                    if (string.IsNullOrWhiteSpace(secret) || secret == request.Secret)
                    {
                        // Valid for 10 minutes
                        if (item.CfHashCreated != null && (DateTime.Now - item.CfHashCreated.Value).TotalMinutes > 10)
                        {
                            if (request.Success)
                            {
                                request.Success = false;
                                request.Message = "CF verification expired, more than 10 minutes";
                            }

                            Log.Warning("CF verification expired, more than 10 minutes {@0}, time: {@1}", request, item.CfHashCreated);
                        }

                        if (request.Success)
                        {
                            item.Lock = false;
                            item.CfHashUrl = null;
                            item.CfHashCreated = null;
                            item.CfUrl = null;
                            item.DisabledReason = null;
                        }
                        else
                        {
                            // Update verification failure reason
                            item.DisabledReason = request.Message;
                        }

                        // Update account information
                        DbHelper.Instance.AccountStore.Update(item);

                        // Clear cache
                        var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                        inc?.ClearAccountCache(item.Id);

                        if (!request.Success)
                        {
                            // Send email
                            EmailJob.Instance.EmailSend(_properties.Smtp, $"CF automatic human verification failed-{item.ChannelId}", $"CF automatic human verification failed-{item.ChannelId}, please verify manually");
                        }
                    }
                    else
                    {
                        // Signature error
                        Log.Warning("Verification notification signature verification failed {@0}", request);

                        return Ok();
                    }
                }
            }

            return Ok();
        }

        /// <summary>
        /// Current user information
        /// </summary>
        /// <returns></returns>
        [HttpGet("current")]
        [AllowAnonymous]
        public ActionResult Current()
        {
            var user = _workContext.GetUser();

            var token = user?.Token;
            var name = user?.Name ?? "Guest";

            // If guest is not enabled, and not logged in, and demo mode is not enabled, return 403
            if (GlobalConfiguration.Setting.EnableGuest != true && user == null && GlobalConfiguration.IsDemoMode != true)
            {
                return StatusCode(403);
            }

            return Ok(new
            {
                id = name,
                userid = name,
                name = name,
                apiSecret = token,
                version = GlobalConfiguration.Version,
                active = true,
                imagePrefix = "",
                avatar = "",
                email = "",
                signature = "",
                title = "",
                group = "",
                tags = new[]
                {
                    new { key = "role",label = user?.Role?.GetDescription() ?? "Guest" },
                },
                notifyCount = 0,
                unreadCount = 0,
                country = "",
                access = "",
                geographic = new
                {
                    province = new { label = "", key = "" },
                    city = new { label = "", key = "" }
                },
                address = "",
                phone = ""
            });
        }

        /// <summary>
        /// Get logs
        /// </summary>
        /// <param name="tail"></param>
        /// <returns></returns>
        [HttpGet("probe")]
        public IActionResult GetLogs([FromQuery] int tail = 1000)
        {
            // Demo mode 100 lines
            if (_isAnonymous)
            {
                tail = 100;
            }

            // Project directory, not AppContext.BaseDirectory
            var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"logs/log{DateTime.Now:yyyyMMdd}.txt");

            if (!System.IO.File.Exists(logFilePath))
            {
                return NotFound("Log file not found.");
            }

            try
            {
                using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var streamReader = new StreamReader(fileStream))
                {
                    var logLines = streamReader.ReadToEnd().Split(Environment.NewLine).Reverse().Take(tail).Reverse().ToArray();
                    return Ok(string.Join("\n", logLines));
                }
            }
            catch (IOException ex)
            {
                return StatusCode(500, $"Error reading log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Get account information by account ID
        /// Specify ID to get account
        /// </summary>
        /// <param name="id">Account ID</param>
        /// <returns>Discord account information</returns>
        [HttpGet("account/{id}")]
        public ActionResult<DiscordAccount> Fetch(string id)
        {
            //var instance = _loadBalancer.GetDiscordInstance(id);
            //return instance == null ? (ActionResult<DiscordAccount>)NotFound() : Ok(instance.Account);

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("Account does not exist");
            }

            if (_isAnonymous)
            {
                // Token encryption
                item.UserToken = item.UserToken?.Substring(0, 4) + "****" + item.UserToken?.Substring(item.UserToken.Length - 4);
                item.BotToken = item.BotToken?.Substring(0, 4) + "****" + item.BotToken?.Substring(item.BotToken.Length - 4);
            }

            return Ok(item);
        }

        /// <summary>
        /// Execute info and setting
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("account-sync/{id}")]
        public async Task<Result> SyncAccount(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            await _taskService.InfoSetting(id);
            return Result.Ok();
        }

        /// <summary>
        /// Get CF human verification link
        /// </summary>
        /// <param name="id"></param>
        /// <param name="refresh">Whether to get a new link</param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpGet("account-cf/{id}")]
        public async Task<Result<DiscordAccount>> CfUrlValidate(string id, [FromQuery] bool refresh = false)
        {
            if (_isAnonymous)
            {
                throw new LogicException("Demo mode, operation prohibited");
            }

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("Account does not exist");
            }

            if (!item.Lock || string.IsNullOrWhiteSpace(item.CfHashUrl))
            {
                throw new LogicException("CF verification link does not exist");
            }

            // Send hashUrl GET request, return {"hash":"OOUxejO94EQNxsCODRVPbg","token":"dXDm-gSb4Zlsx-PCkNVyhQ"}
            // Concatenate verification CF verification URL with hash and token

            if (refresh)
            {
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
                var hashUrl = item.CfHashUrl;
                var response = await httpClient.GetAsync(hashUrl);
                var con = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(con))
                {
                    // Parse
                    var json = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(con);
                    if (json.TryGetProperty("hash", out var h) && json.TryGetProperty("token", out var to))
                    {
                        var hashStr = h.GetString();
                        var token = to.GetString();

                        if (!string.IsNullOrWhiteSpace(hashStr) && !string.IsNullOrWhiteSpace(token))
                        {
                            // Concatenate verification CF verification URL with hash and token
                            // https://editor.midjourney.com/captcha/challenge/index.html?hash=OOUxejO94EQNxsCODRVPbg&token=dXDm-gSb4Zlsx-PCkNVyhQ

                            var url = $"https://editor.midjourney.com/captcha/challenge/index.html?hash={hashStr}&token={token}";

                            item.CfUrl = url;

                            // Update account information
                            DbHelper.Instance.AccountStore.Update(item);

                            // Clear cache
                            var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                            inc?.ClearAccountCache(item.Id);
                        }
                    }
                    else
                    {
                        throw new LogicException("Failed to generate link");
                    }
                }
            }

            return Result.Ok(item);
        }

        /// <summary>
        /// CF verification marked as completed
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("account-cf/{id}")]
        public Result CfUrlValidateOK(string id)
        {
            if (_isAnonymous)
            {
                throw new LogicException("Demo mode, operation prohibited");
            }

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("Account does not exist");
            }

            //if (!item.Lock)
            //{
            //    throw new LogicException("No need for CF verification");
            //}

            item.Lock = false;
            item.CfHashUrl = null;
            item.CfHashCreated = null;
            item.CfUrl = null;
            item.DisabledReason = null;

            // Update account information
            DbHelper.Instance.AccountStore.Update(item);

            // Clear cache
            var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
            inc?.ClearAccountCache(item.Id);

            return Result.Ok();
        }

        /// <summary>
        /// Change version
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        [HttpPost("account-change-version/{id}")]
        public async Task<Result> AccountChangeVersion(string id, [FromQuery] string version)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            await _taskService.AccountChangeVersion(id, version);
            return Result.Ok();
        }

        /// <summary>
        /// Execute action
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        [HttpPost("account-action/{id}")]
        public async Task<Result> AccountAction(string id, [FromQuery] string customId, [FromQuery] EBotType botType)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            await _taskService.AccountAction(id, customId, botType);
            return Result.Ok();
        }

        /// <summary>
        /// Add account
        /// </summary>
        /// <param name="accountConfig"></param>
        /// <returns></returns>
        [HttpPost("account")]
        public Result AccountAdd([FromBody] DiscordAccountConfig accountConfig)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("Sponsorship feature not enabled, operation prohibited");
            }

            // The same user can sponsor up to 10 accounts per day
            var limitKey = $"{DateTime.Now:yyyyMMdd}:sponsor:{user.Id}";
            var sponsorCount = 0;
            if (setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                if (_memoryCache.TryGetValue(limitKey, out sponsorCount) && sponsorCount > 10)
                {
                    Result.Fail("You can sponsor up to 10 accounts per day");
                }
            }

            var model = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == accountConfig.ChannelId);

            if (model != null)
            {
                throw new LogicException("Channel already exists");
            }

            var account = DiscordAccount.Create(accountConfig);

            // Sponsor account
            if (account.IsSponsor)
            {
                account.SponsorUserId = user.Id;

                // Options prohibited for sponsors
                if (user.Role != EUserRole.ADMIN)
                {
                    account.Sort = 0;
                    account.SubChannels.Clear();
                    account.WorkTime = null;
                    account.FishingTime = null;
                }

                // Sponsor parameter validation
                account.SponsorValidate();
            }

            DbHelper.Instance.AccountStore.Add(account);

            // Execute in background
            _ = _discordAccountInitializer.StartCheckAccount(account);

            // Update cache
            if (setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                sponsorCount++;

                _memoryCache.Set(limitKey, sponsorCount, TimeSpan.FromDays(1));
            }

            return Result.Ok();
        }

        /// <summary>
        /// Edit account
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPut("account/{id}")]
        public async Task<Result> AccountEdit([FromBody] DiscordAccount param)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("Sponsorship feature not enabled, operation prohibited");
            }

            //if (_isAnonymous)
            //{
            //    return Result.Fail("Demo mode, operation prohibited");
            //}

            var model = DbHelper.Instance.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("Account does not exist");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("No permission to operate");
            }

            // Options prohibited for sponsors
            if (user.Role != EUserRole.ADMIN)
            {
                param.Sort = model.Sort;
                param.SubChannels = model.SubChannels;
                param.WorkTime = model.WorkTime;
                param.FishingTime = model.WorkTime;

                // Sponsor parameter validation
                param.SponsorValidate();
            }

            model.NijiBotChannelId = param.NijiBotChannelId;
            model.PrivateChannelId = param.PrivateChannelId;
            model.RemixAutoSubmit = param.RemixAutoSubmit;
            model.TimeoutMinutes = param.TimeoutMinutes;
            model.Weight = param.Weight;
            model.Remark = param.Remark;
            model.Sponsor = param.Sponsor;
            model.Sort = param.Sort;
            model.PermanentInvitationLink = param.PermanentInvitationLink;
            model.IsVerticalDomain = param.IsVerticalDomain;
            model.VerticalDomainIds = param.VerticalDomainIds;
            model.SubChannels = param.SubChannels;
            model.IsBlend = param.IsBlend;
            model.EnableMj = param.EnableMj;
            model.EnableNiji = param.EnableNiji;
            model.IsDescribe = param.IsDescribe;
            model.IsShorten = param.IsShorten;
            model.DayDrawLimit = param.DayDrawLimit;

            // Initialize sub-channels
            model.InitSubChannels();

            await _discordAccountInitializer.UpdateAccount(model);

            return Result.Ok();
        }

        /// <summary>
        /// Update account and reconnect
        /// </summary>
        /// <param name="id"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPut("account-reconnect/{id}")]
        public async Task<Result> AccountReconnect(string id, [FromBody] DiscordAccount param)
        {
            if (id != param.Id)
            {
                throw new LogicException("Parameter error");
            }

            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("Sponsorship feature not enabled, operation prohibited");
            }

            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Account does not exist");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("No permission to operate");
            }

            // Cannot modify channel ID
            if (param.GuildId != model.GuildId || param.ChannelId != model.ChannelId)
            {
                return Result.Fail("Modification of channel ID and server ID is prohibited");
            }

            await _discordAccountInitializer.ReconnectAccount(param);

            return Result.Ok();
        }

        /// <summary>
        /// Delete account
        /// </summary>
        /// <returns></returns>
        [HttpDelete("account/{id}")]
        public Result AccountDelete(string id)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("Sponsorship feature not enabled, operation prohibited");
            }

            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Account does not exist");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("No permission to operate");
            }

            //if (_isAnonymous)
            //{
            //    return Result.Fail("Demo mode, operation prohibited");
            //}

            _discordAccountInitializer.DeleteAccount(id);

            return Result.Ok();
        }

        /// <summary>
        /// Get all account information (only return enabled accounts)
        /// </summary>
        /// <returns>All Discord account information</returns>
        [HttpGet("accounts")]
        public ActionResult<List<DiscordAccount>> List()
        {
            var user = _workContext.GetUser();

            var list = DbHelper.Instance.AccountStore.GetAll().Where(c => c.Enable == true)
                .ToList()
                .OrderBy(c => c.Sort).ThenBy(c => c.DateCreated).ToList();

            foreach (var item in list)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                item.RunningCount = inc?.GetRunningFutures().Count ?? 0;
                item.QueueCount = inc?.GetQueueTasks().Count ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token encryption
                    item.UserToken = item.UserToken?.Substring(0, item.UserToken.Length / 5) + "****";
                    item.BotToken = item.BotToken?.Substring(0, item.BotToken.Length / 5) + "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    if (item.SubChannels.Count > 0)
                    {
                        // Encrypt
                        item.SubChannels = item.SubChannels.Select(c => "****").ToList();
                    }
                }
            }

            return Ok(list);
        }

        /// <summary>
        /// Paginate account information
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("accounts")]
        public ActionResult<StandardTableResult<DiscordAccount>> Accounts([FromBody] StandardTableParam<DiscordAccount> request)
        {
            var user = _workContext.GetUser();

            var page = request.Pagination;
            if (page.PageSize > 100)
            {
                page.PageSize = 100;
            }

            // Demo mode 100 lines
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("Demo mode, operation prohibited");
                }
            }

            var sort = request.Sort;
            var param = request.Search;

            var list = new List<DiscordAccount>();
            var count = 0;

            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<DiscordAccount>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                count = query.Count();
                list = query
                    .OrderByIf(nameof(DiscordAccount.GuildId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.GuildId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.ChannelId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.ChannelId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Enable).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Enable, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Remark).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Remark, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Sponsor).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Sponsor, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.DateCreated).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.DateCreated, sort.Reverse)
                    .OrderByIf(string.IsNullOrWhiteSpace(sort.Predicate), c => c.Sort, false)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.AccountStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                count = query.Count();
                list = query
                    .OrderByIf(nameof(DiscordAccount.GuildId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.GuildId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.ChannelId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.ChannelId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Enable).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Enable, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Remark).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Remark, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Sponsor).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Sponsor, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.DateCreated).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.DateCreated, sort.Reverse)
                    .OrderByIf(string.IsNullOrWhiteSpace(sort.Predicate), c => c.Sort, false)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();
            }

            foreach (var item in list)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                item.RunningCount = inc?.GetRunningFutures().Count ?? 0;
                item.QueueCount = inc?.GetQueueTasks().Count ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token encryption
                    item.UserToken = item.UserToken?.Substring(0, item.UserToken.Length / 5) + "****";
                    item.BotToken = item.BotToken?.Substring(0, item.BotToken.Length / 5) + "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    if (item.SubChannels.Count > 0)
                    {
                        // Encrypt
                        item.SubChannels = item.SubChannels.Select(c => "****").ToList();
                    }
                }
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// Get all task information
        /// </summary>
        /// <returns>All task information</returns>
        [HttpPost("tasks")]
        public ActionResult<StandardTableResult<TaskInfo>> Tasks([FromBody] StandardTableParam<TaskInfo> request)
        {
            var page = request.Pagination;
            if (page.PageSize > 100)
            {
                page.PageSize = 100;
            }

            // Demo mode 100 lines
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("Demo mode, operation prohibited");
                }
            }

            var param = request.Search;

            // Use native query here because the query conditions are more complex
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<TaskInfo>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.UserId), c => c.UserId == param.UserId || c.State == param.UserId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                    .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Description.Contains(param.Description) || c.Prompt.Contains(param.Description) || c.PromptEn.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
            else
            {
                var query = LiteDBHelper.TaskStore.GetCollection().Query()
                .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                .WhereIf(!string.IsNullOrWhiteSpace(param.UserId), c => c.UserId == param.UserId || c.State == param.UserId)
                .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Description.Contains(param.Description) || c.Prompt.Contains(param.Description) || c.PromptEn.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
        }

        /// <summary>
        /// Delete task
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("task/{id}")]
        public Result TaskDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            var queueTask = _loadBalancer.GetQueueTasks().FirstOrDefault(t => t.Id == id);
            if (queueTask != null)
            {
                queueTask.Fail("Delete task");

                Thread.Sleep(1000);
            }

            var task = DbHelper.Instance.TaskStore.Get(id);
            if (task != null)
            {
                var ins = _loadBalancer.GetDiscordInstance(task.InstanceId);
                if (ins != null)
                {
                    var model = ins.FindRunningTask(c => c.Id == id).FirstOrDefault();
                    if (model != null)
                    {
                        model.Fail("Delete task");

                        Thread.Sleep(1000);
                    }
                }

                DbHelper.Instance.TaskStore.Delete(id);
            }

            return Result.Ok();
        }

        /// <summary>
        /// User list
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("users")]
        public ActionResult<StandardTableResult<User>> Users([FromBody] StandardTableParam<User> request)
        {
            var page = request.Pagination;

            // Demo mode 100 lines
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("Demo mode, operation prohibited");
                }
            }

            var param = request.Search;

            var count = 0;
            var list = new List<User>();
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<User>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                    .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status);

                count = query.Count();
                list = query
                    .OrderByDescending(c => c.UpdateTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.UserStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                    .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status);

                count = query.Count();
                list = query
                   .OrderByDescending(c => c.UpdateTime)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Limit(page.PageSize)
                   .ToList();
            }

            if (_isAnonymous)
            {
                // Mask user information
                foreach (var item in list)
                {
                    item.Name = "***";
                    item.Email = "***";
                    item.Phone = "***";
                    item.Token = "***";
                }
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// Add or edit user
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("user")]
        public Result UserAddOrEdit([FromBody] User user)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            var oldToken = user?.Token;

            if (string.IsNullOrWhiteSpace(user.Id))
            {
                user.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.UserStore.Get(user.Id);
                if (model == null)
                {
                    throw new LogicException("User does not exist");
                }

                oldToken = model?.Token;

                user.LastLoginIp = model.LastLoginIp;
                user.LastLoginTime = model.LastLoginTime;
                user.RegisterIp = model.RegisterIp;
                user.RegisterTime = model.RegisterTime;
                user.CreateTime = model.CreateTime;
            }

            // Parameter validation
            // Token cannot be empty
            if (string.IsNullOrWhiteSpace(user.Token))
            {
                throw new LogicException("Token cannot be empty");
            }

            // Check for duplicate token
            var tokenUser = DbHelper.Instance.UserStore.Single(c => c.Id != user.Id && c.Token == user.Token);
            if (tokenUser != null)
            {
                throw new LogicException("Duplicate token");
            }

            // Username cannot be empty
            if (string.IsNullOrWhiteSpace(user.Name))
            {
                throw new LogicException("Username cannot be empty");
            }

            // Role
            if (user.Role == null)
            {
                user.Role = EUserRole.USER;
            }

            // Status
            if (user.Status == null)
            {
                user.Status = EUserStatus.NORMAL;
            }

            user.UpdateTime = DateTime.Now;

            DbHelper.Instance.UserStore.Save(user);

            // Clear cache
            var key = $"USER_{oldToken}";
            _memoryCache.Remove(key);

            return Result.Ok();
        }

        /// <summary>
        /// Delete user
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("user/{id}")]
        public Result UserDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            var model = DbHelper.Instance.UserStore.Get(id);
            if (model == null)
            {
                throw new LogicException("User does not exist");
            }
            if (model.Id == Constants.ADMIN_USER_ID)
            {
                throw new LogicException("Cannot delete admin account");
            }
            if (model.Id == Constants.DEFAULT_USER_ID)
            {
                throw new LogicException("Cannot delete default account");
            }

            // Clear cache
            var key = $"USER_{model.Token}";
            _memoryCache.Remove(key);

            DbHelper.Instance.UserStore.Delete(id);

            return Result.Ok();
        }

        /// <summary>
        /// Get all enabled domain tags
        /// </summary>
        /// <returns></returns>
        [HttpGet("domain-tags")]
        public Result<List<SelectOption>> DomainTags()
        {
            var data = DbHelper.Instance.DomainStore.GetAll()
                .Select(c => new SelectOption()
                {
                    Value = c.Id,
                    Label = c.Name,
                    Disabled = !c.Enable
                }).ToList();

            return Result.Ok(data);
        }

        /// <summary>
        /// Domain tag management
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("domain-tags")]
        public ActionResult<StandardTableResult<DomainTag>> Domains([FromBody] StandardTableParam<DomainTag> request)
        {
            var page = request.Pagination;

            var firstKeyword = request.Search.Keywords?.FirstOrDefault();
            var param = request.Search;

            var count = 0;
            var list = new List<DomainTag>();
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<DomainTag>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                    .OrderBy(c => c.Sort)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.DomainStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                   .OrderBy(c => c.Sort)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Limit(page.PageSize)
                   .ToList();
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// Add or edit domain tag
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("domain-tag")]
        public Result DomainAddOrEdit([FromBody] DomainTag domain)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (string.IsNullOrWhiteSpace(domain.Id))
            {
                domain.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.DomainStore.Get(domain.Id);
                if (model == null)
                {
                    throw new LogicException("Domain tag does not exist");
                }

                domain.CreateTime = model.CreateTime;
            }

            domain.UpdateTime = DateTime.Now;

            domain.Keywords = domain.Keywords.Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLower())
                .Distinct()
                .ToList();

            DbHelper.Instance.DomainStore.Save(domain);

            _taskService.ClearDomainCache();

            return Result.Ok();
        }

        /// <summary>
        /// Delete domain tag
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("domain-tag/{id}")]
        public Result DomainDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            var model = DbHelper.Instance.DomainStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Domain tag does not exist");
            }

            if (model.Id == Constants.DEFAULT_DOMAIN_ID)
            {
                throw new LogicException("Cannot delete default domain tag");
            }

            if (model.Id == Constants.DEFAULT_DOMAIN_FULL_ID)
            {
                throw new LogicException("Cannot delete default domain tag");
            }

            DbHelper.Instance.DomainStore.Delete(id);

            _taskService.ClearDomainCache();

            return Result.Ok();
        }

        /// <summary>
        /// Banned words
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("banned-words")]
        public ActionResult<StandardTableResult<BannedWord>> BannedWords([FromBody] StandardTableParam<BannedWord> request)
        {
            var page = request.Pagination;

            var firstKeyword = request.Search.Keywords?.FirstOrDefault();
            var param = request.Search;

            var count = 0;
            var list = new List<BannedWord>();

            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<BannedWord>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                   .OrderBy(c => c.Sort)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Take(page.PageSize)
                   .ToList();
            }
            else
            {
                var query = LiteDBHelper.BannedWordStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                    .OrderBy(c => c.Sort)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// Add or edit banned word
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("banned-word")]
        public Result BannedWordAddOrEdit([FromBody] BannedWord param)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (string.IsNullOrWhiteSpace(param.Id))
            {
                param.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.BannedWordStore.Get(param.Id);
                if (model == null)
                {
                    throw new LogicException("Banned word does not exist");
                }

                model.CreateTime = model.CreateTime;
            }

            param.UpdateTime = DateTime.Now;

            param.Keywords = param.Keywords.Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLower())
                .Distinct()
                .ToList();

            DbHelper.Instance.BannedWordStore.Save(param);

            _taskService.ClearBannedWordsCache();

            return Result.Ok();
        }

        /// <summary>
        /// Delete banned word
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("banned-word/{id}")]
        public Result BannedWordDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            var model = DbHelper.Instance.BannedWordStore.Get(id);
            if (model == null)
            {
                throw new LogicException("Banned word does not exist");
            }

            if (model.Id == Constants.DEFAULT_BANNED_WORD_ID)
            {
                throw new LogicException("Cannot delete default banned word");
            }

            DbHelper.Instance.BannedWordStore.Delete(id);

            _taskService.ClearBannedWordsCache();

            return Result.Ok();
        }

        /// <summary>
        /// Get system configuration
        /// </summary>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpGet("setting")]
        public Result<Setting> GetSetting()
        {
            var model = LiteDBHelper.SettingStore.Get(Constants.DEFAULT_SETTING_ID);
            if (model == null)
            {
                throw new LogicException("System configuration error, please restart the service");
            }

            model.IsMongo = GlobalConfiguration.Setting.IsMongo;

            // Demo mode, some configurations are not visible
            if (_isAnonymous)
            {
                if (model.Smtp != null)
                {
                    model.Smtp.FromPassword = "****";
                    model.Smtp.FromEmail = "****";
                    model.Smtp.To = "****";
                }

                if (model.BaiduTranslate != null)
                {
                    model.BaiduTranslate.Appid = "****";
                    model.BaiduTranslate.AppSecret = "****";
                }

                if (model.Openai != null)
                {
                    model.Openai.GptApiUrl = "****";
                    model.Openai.GptApiKey = "****";
                }

                if (!string.IsNullOrWhiteSpace(model.MongoDefaultConnectionString))
                {
                    model.MongoDefaultConnectionString = "****";
                }

                if (model.AliyunOss != null)
                {
                    model.AliyunOss.AccessKeyId = "****";
                    model.AliyunOss.AccessKeySecret = "****";
                }

                if (model.Replicate != null)
                {
                    model.Replicate.Token = "****";
                }

                if (model.TencentCos != null)
                {
                    model.TencentCos.SecretId = "****";
                    model.TencentCos.SecretKey = "****";
                }

                if (model.CloudflareR2 != null)
                {
                    model.CloudflareR2.AccessKey = "****";
                    model.CloudflareR2.SecretKey = "****";
                }

                model.CaptchaNotifySecret = "****";
            }

            return Result.Ok(model);
        }

        /// <summary>
        /// Edit system configuration
        /// </summary>
        /// <param name="setting"></param>
        /// <returns></returns>
        [HttpPost("setting")]
        public Result SettingEdit([FromBody] Setting setting)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            setting.Id = Constants.DEFAULT_SETTING_ID;

            LiteDBHelper.SettingStore.Update(setting);

            GlobalConfiguration.Setting = setting;

            // Storage service
            StorageHelper.Configure();

            // Home page cache
            _memoryCache.Remove("HOME");
            var now = DateTime.Now.ToString("yyyyMMdd");
            var key = $"{now}_home";
            _memoryCache.Remove(key);

            return Result.Ok();
        }

        /// <summary>
        /// MJ Plus data migration (migrate account data and task data)
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("mjplus-migration")]
        public async Task<Result> MjPlusMigration([FromBody] MjPlusMigrationDto dto)
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            await _taskService.MjPlusMigration(dto);

            return Result.Ok();
        }

        /// <summary>
        /// Verify if MongoDB is connected properly
        /// </summary>
        /// <returns></returns>
        [HttpPost("verify-mongo")]
        public Result ValidateMongo()
        {
            if (_isAnonymous)
            {
                return Result.Fail("Demo mode, operation prohibited");
            }

            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultConnectionString)
                || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultDatabase))
            {
                return Result.Fail("MongoDB configuration error, please save the configuration and then verify");
            }

            var success = MongoHelper.Verify();

            return success ? Result.Ok() : Result.Fail("Connection failed");
        }
    }
}