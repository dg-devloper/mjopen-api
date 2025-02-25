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
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Models;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;
using System.Diagnostics;
using System.Text;
using ILogger = Serilog.ILogger;

namespace Midjourney.API
{
    /// <summary>
    /// Discord account initializer for initializing Discord account instances.
    /// </summary>
    public class DiscordAccountInitializer : IHostedService
    {
        private readonly ITaskService _taskService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly DiscordAccountHelper _discordAccountHelper;
        private readonly ProxyProperties _properties;
        private readonly DiscordHelper _discordHelper;

        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        private readonly IMemoryCache _memoryCache;
        private Timer _timer;

        public DiscordAccountInitializer(
            DiscordLoadBalancer discordLoadBalancer,
            DiscordAccountHelper discordAccountHelper,
            IConfiguration configuration,
            IOptions<ProxyProperties> options,
            ITaskService taskService,
            IMemoryCache memoryCache,
            DiscordHelper discordHelper)
        {
            // Configure global cache
            GlobalConfiguration.MemoryCache = memoryCache;

            _discordLoadBalancer = discordLoadBalancer;
            _discordAccountHelper = discordAccountHelper;
            _properties = options.Value;
            _taskService = taskService;
            _configuration = configuration;
            _logger = Log.Logger;
            _memoryCache = memoryCache;
            _discordHelper = discordHelper;
        }

        /// <summary>
        /// Start the service and initialize Discord account instances.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Initialize environment variables
            var proxy = GlobalConfiguration.Setting?.Proxy;
            if (!string.IsNullOrEmpty(proxy.Host))
            {
                Environment.SetEnvironmentVariable("http_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("http_proxyPort", proxy.Port.ToString());
                Environment.SetEnvironmentVariable("https_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("https_proxyPort", proxy.Port.ToString());
            }

            // Check if MongoDB is enabled
            if (GlobalConfiguration.Setting.IsMongo)
            {
                // Migrate account user domain banded
                try
                {
                    // If liteAccountIds data does not exist in mongoAccountIds, migrate to MongoDB
                    // Account migration
                    var liteAccountIds = LiteDBHelper.AccountStore.GetAllIds();
                    var mongoAccountDb = new MongoDBRepository<DiscordAccount>();
                    var mongoAccountIds = mongoAccountDb.GetAllIds();
                    var accountIds = liteAccountIds.Except(mongoAccountIds).ToList();
                    if (accountIds.Count > 0)
                    {
                        var liteAccounts = LiteDBHelper.AccountStore.GetAll();
                        foreach (var id in accountIds)
                        {
                            var model = liteAccounts.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                mongoAccountDb.Add(model);
                            }
                        }
                    }

                    // User migration
                    var liteUserIds = LiteDBHelper.UserStore.GetAllIds();
                    var mongoUserDb = new MongoDBRepository<User>();
                    var mongoUserIds = mongoUserDb.GetAllIds();
                    var userIds = liteUserIds.Except(mongoUserIds).ToList();
                    if (userIds.Count > 0)
                    {
                        var liteUsers = LiteDBHelper.UserStore.GetAll();
                        foreach (var id in userIds)
                        {
                            var model = liteUsers.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                mongoUserDb.Add(model);
                            }
                        }
                    }

                    // Domain migration
                    var liteDomainIds = LiteDBHelper.DomainStore.GetAllIds();
                    var mongoDomainDb = new MongoDBRepository<DomainTag>();
                    var mongoDomainIds = mongoDomainDb.GetAllIds();
                    var domainIds = liteDomainIds.Except(mongoDomainIds).ToList();
                    if (domainIds.Count > 0)
                    {
                        var liteDomains = LiteDBHelper.DomainStore.GetAll();
                        foreach (var id in domainIds)
                        {
                            var model = liteDomains.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                mongoDomainDb.Add(model);
                            }
                        }
                    }

                    // Banded migration
                    var liteBannedIds = LiteDBHelper.BannedWordStore.GetAllIds();
                    var mongoBannedDb = new MongoDBRepository<BannedWord>();
                    var mongoBannedIds = mongoBannedDb.GetAllIds();
                    var bannedIds = liteBannedIds.Except(mongoBannedIds).ToList();
                    if (bannedIds.Count > 0)
                    {
                        var liteBanneds = LiteDBHelper.BannedWordStore.GetAll();
                        foreach (var id in bannedIds)
                        {
                            var model = liteBanneds.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                mongoBannedDb.Add(model);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Local storage migration to MongoDB failed, reset to local database");

                    // If an exception occurs, reset to local storage
                    GlobalConfiguration.Setting.MongoDefaultConnectionString = "";

                    LiteDBHelper.SettingStore.Save(GlobalConfiguration.Setting);
                }
            }


            // Initialize administrator user
            // Check if super admin exists
            var admin = DbHelper.Instance.UserStore.Get(Constants.ADMIN_USER_ID);
            if (admin == null)
            {
                admin = new User
                {
                    Id = Constants.ADMIN_USER_ID,
                    Name = Constants.ADMIN_USER_ID,
                    Token = _configuration["AdminToken"],
                    Role = EUserRole.ADMIN,
                    Status = EUserStatus.NORMAL,
                    IsWhite = true
                };

                if (string.IsNullOrWhiteSpace(admin.Token))
                {
                    admin.Token = "admin";
                }

                DbHelper.Instance.UserStore.Add(admin);
            }

            // Initialize ordinary user
            var user = DbHelper.Instance.UserStore.Get(Constants.DEFAULT_USER_ID);
            var userToken = _configuration["UserToken"];
            if (user == null && !string.IsNullOrWhiteSpace(userToken))
            {
                user = new User
                {
                    Id = Constants.DEFAULT_USER_ID,
                    Name = Constants.DEFAULT_USER_ID,
                    Token = userToken,
                    Role = EUserRole.USER,
                    Status = EUserStatus.NORMAL,
                    IsWhite = true
                };
                DbHelper.Instance.UserStore.Add(user);
            }

            // Initialize domain tags
            var defaultDomain = DbHelper.Instance.DomainStore.Get(Constants.DEFAULT_DOMAIN_ID);
            if (defaultDomain == null)
            {
                defaultDomain = new DomainTag
                {
                    Id = Constants.DEFAULT_DOMAIN_ID,
                    Name = "Default Tag",
                    Description = "",
                    Sort = 0,
                    Enable = true,
                    Keywords = WordsUtils.GetWords()
                };
                DbHelper.Instance.DomainStore.Add(defaultDomain);
            }

            // Default full tag
            var fullDomain = DbHelper.Instance.DomainStore.Get(Constants.DEFAULT_DOMAIN_FULL_ID);
            if (fullDomain == null)
            {
                fullDomain = new DomainTag
                {
                    Id = Constants.DEFAULT_DOMAIN_FULL_ID,
                    Name = "Default Full Tag",
                    Description = "",
                    Sort = 0,
                    Enable = true,
                    Keywords = WordsUtils.GetWordsFull()
                };
                DbHelper.Instance.DomainStore.Add(fullDomain);
            }

            // Prohibited word
            var bannedWord = DbHelper.Instance.BannedWordStore.Get(Constants.DEFAULT_BANNED_WORD_ID);
            if (bannedWord == null)
            {
                bannedWord = new BannedWord
                {
                    Id = Constants.DEFAULT_BANNED_WORD_ID,
                    Name = "Default Prohibited Word",
                    Description = "",
                    Sort = 0,
                    Enable = true,
                    Keywords = BannedPromptUtils.GetStrings()
                };
                DbHelper.Instance.BannedWordStore.Add(bannedWord);
            }

            _ = Task.Run(() =>
            {
                if (GlobalConfiguration.Setting.IsMongo)
                {
                    // Index
                    MongoIndexInit();

                    // Auto migrate task data
                    if (GlobalConfiguration.Setting.IsMongoAutoMigrate)
                    {
                        MongoAutoMigrate();
                    }

                    var oss = GlobalConfiguration.Setting.AliyunOss;

                    //if (oss?.Enable == true && oss?.IsAutoMigrationLocalFile == true)
                    //{
                    //    AutoMigrationLocalFileToOss();
                    //}
                }
            });

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            await Task.CompletedTask;
        }

        /// <summary>
        /// Initialize MongoDB indexes and maximum data limits
        /// </summary>
        public void MongoIndexInit()
        {
            try
            {
                if (!GlobalConfiguration.Setting.IsMongo)
                {
                    return;
                }

                LocalLock.TryLock("MongoIndexInit", TimeSpan.FromSeconds(10), () =>
                {
                    // Cannot be fixed size because data cannot be modified
                    //var database = MongoHelper.Instance;
                    //var collectionName = "task";
                    //var collectionExists = database.ListCollectionNames().ToList().Contains(collectionName);
                    //if (!collectionExists)
                    //{
                    //    var options = new CreateCollectionOptions
                    //    {
                    //        Capped = true,
                    //        MaxSize = 1024L * 1024L * 1024L * 1024L,  // 1 TB collection size, actually unlimited
                    //        MaxDocuments = 1000000
                    //    };
                    //    database.CreateCollection("task", options);
                    //}

                    var coll = MongoHelper.GetCollection<TaskInfo>();

                    var index1 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Descending(c => c.SubmitTime));
                    coll.Indexes.CreateOne(index1);

                    var index2 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.PromptEn));
                    coll.Indexes.CreateOne(index2);

                    var index3 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Descending(c => c.Prompt));
                    coll.Indexes.CreateOne(index3);

                    var index4 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.InstanceId));
                    coll.Indexes.CreateOne(index4);

                    var index5 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.Status));
                    coll.Indexes.CreateOne(index5);

                    var index6 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.Action));
                    coll.Indexes.CreateOne(index6);

                    var index7 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.Description));
                    coll.Indexes.CreateOne(index7);

                    var index8 = new CreateIndexModel<TaskInfo>(Builders<TaskInfo>.IndexKeys.Ascending(c => c.ImageUrl));
                    coll.Indexes.CreateOne(index8);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Initialize MongoDB indexes and maximum data limits exception");
            }
        }

        /// <summary>
        /// Auto migrate task data
        /// </summary>
        public void MongoAutoMigrate()
        {
            try
            {
                LocalLock.TryLock("MongoAutoMigrate", TimeSpan.FromSeconds(10), () =>
                {
                    // Check if the last one exists
                    var success = 0;
                    var last = LiteDBHelper.TaskStore.GetCollection().Query().OrderByDescending(c => c.SubmitTime).FirstOrDefault();
                    if (last != null)
                    {
                        var coll = MongoHelper.GetCollection<TaskInfo>();
                        var lastMongo = coll.Find(c => c.Id == last.Id).FirstOrDefault();
                        if (lastMongo == null)
                        {
                            // Migrate data
                            var taskIds = LiteDBHelper.TaskStore.GetCollection().Query().Select(c => c.Id).ToList();
                            foreach (var tid in taskIds)
                            {
                                var info = LiteDBHelper.TaskStore.Get(tid);
                                if (info != null)
                                {
                                    // Check if it exists
                                    var exist = coll.CountDocuments(c => c.Id == info.Id) > 0;
                                    if (!exist)
                                    {
                                        coll.InsertOne(info);
                                        success++;
                                    }
                                }
                            }

                            _logger.Information("MongoAutoMigrate success: {@0}", success);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MongoAutoMigrate error");
            }
        }

        ///// <summary>
        ///// Auto migrate local files to OSS
        ///// </summary>
        //public void AutoMigrationLocalFileToOss()
        //{
        //    try
        //    {
        //        LocalLock.TryLock("AutoMigrationLocalFileToOss", TimeSpan.FromSeconds(10), () =>
        //        {
        //            var oss = GlobalConfiguration.Setting.AliyunOss;
        //            var dis = GlobalConfiguration.Setting.NgDiscord;
        //            var coll = MongoHelper.GetCollection<TaskInfo>();

        //            var localCdn = dis.CustomCdn;
        //            var aliCdn = oss.CustomCdn;

        //            // And enable local domain
        //            // And MongoDB is enabled
        //            var isMongo = GlobalConfiguration.Setting.IsMongo;
        //            if (oss?.Enable == true && oss?.IsAutoMigrationLocalFile == true && !string.IsNullOrWhiteSpace(localCdn) && isMongo)
        //            {
        //                var localPath1 = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "attachments");
        //                var localPath2 = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "ephemeral-attachments");
        //                var paths = new List<string> { localPath1, localPath2 };
        //                var process = 0;
        //                foreach (var dir in paths)
        //                {
        //                    if (Directory.Exists(dir))
        //                    {
        //                        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
        //                        foreach (var fileFullPath in files)
        //                        {
        //                            try
        //                            {
        //                                var fileName = Path.GetFileName(fileFullPath);

        //                                var model = coll.Find(c => c.ImageUrl.StartsWith(localCdn) && c.ImageUrl.Contains(fileName)).FirstOrDefault();
        //                                if (model != null)
        //                                {
        //                                    // Create save path
        //                                    var uri = new Uri(model.ImageUrl);
        //                                    var localPath = uri.AbsolutePath.TrimStart('/');

        //                                    var stream = File.OpenRead(fileFullPath);
        //                                    var ossService = new AliyunOssStorageService();

        //                                    var mm = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(localPath));
        //                                    if (string.IsNullOrWhiteSpace(mm))
        //                                    {
        //                                        mm = "image/png";
        //                                    }

        //                                    var result = ossService.SaveAsync(stream, localPath, mm);

        //                                    // Replace url
        //                                    var url = $"{aliCdn?.Trim()?.Trim('/')}/{localPath}{uri?.Query}";

        //                                    if (model.Action != TaskAction.SWAP_VIDEO_FACE)
        //                                    {
        //                                        model.ImageUrl = url.ToStyle(oss.ImageStyle);
        //                                        model.ThumbnailUrl = url.ToStyle(oss.ThumbnailImageStyle);
        //                                    }
        //                                    else
        //                                    {
        //                                        model.ImageUrl = url;
        //                                        model.ThumbnailUrl = url.ToStyle(oss.VideoSnapshotStyle);
        //                                    }
        //                                    coll.ReplaceOne(c => c.Id == model.Id, model);

        //                                    stream.Close();

        //                                    // Delete
        //                                    File.Delete(fileFullPath);

        //                                    process++;
        //                                    Log.Information("File has been automatically migrated to Aliyun {@0}, {@1}", process, fileFullPath);
        //                                }
        //                            }
        //                            catch (Exception ex)
        //                            {
        //                                Log.Error(ex, "File has been automatically migrated to Aliyun exception {@0}", fileFullPath);
        //                            }
        //                        }
        //                    }

        //                    Log.Information("File has been automatically migrated to Aliyun completed {@0}", process);

        //                    // Second temporary fix, if the local database is Aliyun, but MongoDB is not Aliyun, then assign the local url to MongoDB
        //                    //var localDb = DbHelper.TaskStore;
        //                    //var localList = localDb.GetAll();
        //                    //foreach (var localItem in localList)
        //                    //{
        //                    //    if (localItem.ImageUrl?.StartsWith(aliCdn) == true)
        //                    //    {
        //                    //        var model = coll.Find(c => c.Id == localItem.Id).FirstOrDefault();
        //                    //        if (model != null && localItem.ImageUrl != model.ImageUrl)
        //                    //        {
        //                    //            model.ImageUrl = localItem.ImageUrl;
        //                    //            model.ThumbnailUrl = localItem.ThumbnailUrl;
        //                    //            coll.ReplaceOne(c => c.Id == model.Id, model);
        //                    //        }
        //                    //    }
        //                    //}
        //                }
        //            }
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.Error(ex, "AutoMigrationLocalFileToOss error");
        //    }
        //}

        private async void DoWork(object state)
        {
            _logger.Information("Routine check started");

            try
            {
                var isLock = await AsyncLocalLock.TryLockAsync("DoWork", TimeSpan.FromSeconds(10), async () =>
                {
                    try
                    {
                        // Default account in local configuration
                        var configAccounts = _properties.Accounts.ToList();
                        if (!string.IsNullOrEmpty(_properties.Discord?.ChannelId)
                        && !_properties.Discord.ChannelId.Contains("*"))
                        {
                            configAccounts.Add(_properties.Discord);
                        }

                        await Initialize(configAccounts.ToArray());

                        // Check and delete old documents
                        CheckAndDeleteOldDocuments();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Exception occurred during routine check");
                    }

                    _logger.Information("Routine check completed");
                });

                if (!isLock)
                {
                    _logger.Information("Routine check in progress, please try again later...");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Routine check execution exception");
            }
        }

        /// <summary>
        /// Check and delete old documents
        /// </summary>
        public static void CheckAndDeleteOldDocuments()
        {
            if (GlobalConfiguration.Setting.MaxCount <= 0)
            {
                return;
            }

            var maxCount = GlobalConfiguration.Setting.MaxCount;

            // If it exceeds x items, delete the earliest inserted data
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<TaskInfo>();
                var documentCount = coll.CountDocuments(Builders<TaskInfo>.Filter.Empty);
                if (documentCount > maxCount)
                {
                    var documentsToDelete = documentCount - maxCount;
                    var ids = coll.Find(c => true).SortBy(c => c.SubmitTime).Limit((int)documentsToDelete).Project(c => c.Id).ToList();
                    if (ids.Any())
                    {
                        coll.DeleteMany(c => ids.Contains(c.Id));
                    }
                }
            }
            else
            {
                var documentCount = LiteDBHelper.TaskStore.GetCollection().Query().Count();
                if (documentCount > maxCount)
                {
                    var documentsToDelete = documentCount - maxCount;
                    var ids = LiteDBHelper.TaskStore.GetCollection().Query().OrderBy(c => c.SubmitTime)
                        .Limit(documentsToDelete)
                        .ToList()
                        .Select(c => c.Id);

                    if (ids.Any())
                    {
                        LiteDBHelper.TaskStore.GetCollection().DeleteMany(c => ids.Contains(c.Id));
                    }
                }
            }
        }

        /// <summary>
        /// Initialize all accounts.
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(params DiscordAccountConfig[] appends)
        {
            var isLock = await AsyncLocalLock.TryLockAsync("initialize:all", TimeSpan.FromSeconds(10), async () =>
            {
                var db = DbHelper.Instance.AccountStore;
                var accounts = db.GetAll().OrderBy(c => c.Sort).ToList();

                // Add account from startup configuration to database
                var configAccounts = new List<DiscordAccountConfig>();
                if (appends?.Length > 0)
                {
                    configAccounts.AddRange(appends);
                }

                foreach (var configAccount in configAccounts)
                {
                    var account = accounts.FirstOrDefault(c => c.ChannelId == configAccount.ChannelId);
                    if (account == null)
                    {
                        account = DiscordAccount.Create(configAccount);
                        db.Add(account);

                        accounts.Add(account);
                    }
                }

                foreach (var account in accounts)
                {
                    try
                    {
                        await StartCheckAccount(account, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Account({@0}) init fail, disabled: {@1}", account.GetDisplay(), ex.Message);

                        account.Enable = false;
                        account.DisabledReason = "Initialization failed";

                        db.Update(account);
                    }
                }

                var enableInstanceIds = _discordLoadBalancer.GetAllInstances()
                .Where(instance => instance.IsAlive)
                .Select(instance => instance.ChannelId)
                .ToHashSet();

                _logger.Information("Current available accounts [{@0}] - {@1}", enableInstanceIds.Count, string.Join(", ", enableInstanceIds));
            });
            if (!isLock)
            {
                _logger.Warning("Initializing all accounts, please try again later...");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Check and start connection
        /// </summary>
        public async Task StartCheckAccount(DiscordAccount account, bool isValidateLock = true)
        {
            if (account == null || account.Enable != true)
            {
                return;
            }

            var isLock = await AsyncLocalLock.TryLockAsync($"initialize:{account.Id}", TimeSpan.FromSeconds(5), async () =>
            {
                var setting = GlobalConfiguration.Setting;

                var sw = new Stopwatch();
                var swAll = new Stopwatch();

                swAll.Start();
                sw.Start();

                var info = new StringBuilder();
                info.AppendLine($"{account.Id} initializing...");

                var db = DbHelper.Instance.AccountStore;
                DiscordInstance disInstance = null;

                try
                {
                    // Get value
                    account = db.Get(account.Id)!;
                    if (account.Enable != true)
                    {
                        return;
                    }

                    disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);

                    // Check if it is within working hours
                    var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                    var dayCount = (int)DbHelper.Instance.TaskStore.Count(c => c.InstanceId == account.ChannelId && c.SubmitTime >= now);

                    sw.Stop();
                    info.AppendLine($"{account.Id} initializing... Get task count time: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();

                    // Randomly delay token
                    if (setting.EnableAutoExtendToken)
                    {
                        await RandomSyncToken(account);
                        sw.Stop();
                        info.AppendLine($"{account.Id} initializing... Randomly delay token time: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();
                    }

                    // As long as it is within working hours, create an instance
                    if (DateTime.Now.IsInWorkTime(account.WorkTime))
                    {
                        if (disInstance == null)
                        {
                            // Initialize sub-channels
                            account.InitSubChannels();

                            // Fast duration validation
                            // If fastTime <= 0.1, mark as fast exhausted
                            var fastTime = account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();
                            if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out var ftime) && ftime <= 0.1)
                            {
                                account.FastExhausted = true;
                            }
                            else
                            {
                                account.FastExhausted = false;
                            }

                            // Automatically set to slow mode if fast exhausted
                            if (account.FastExhausted == true && account.EnableAutoSetRelax == true)
                            {
                                account.AllowModes = new List<GenerationSpeedMode>() { GenerationSpeedMode.RELAX };

                                if (account.CoreSize > 3)
                                {
                                    account.CoreSize = 3;
                                }
                            }

                            // Enable automatic retrieval of private message ID
                            if (setting.EnableAutoGetPrivateId)
                            {
                                try
                                {
                                    Thread.Sleep(500);
                                    var id = await _discordAccountHelper.GetBotPrivateId(account, EBotType.MID_JOURNEY);
                                    if (!string.IsNullOrWhiteSpace(id))
                                    {
                                        account.PrivateChannelId = id;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "Get MJ private chat channel ID exception {@0}", account.ChannelId);

                                    info.AppendLine($"{account.Id} initializing... Get MJ private chat channel ID exception");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id} initializing... Get MJ private chat channel ID time: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();

                                try
                                {
                                    Thread.Sleep(500);
                                    var id = await _discordAccountHelper.GetBotPrivateId(account, EBotType.NIJI_JOURNEY);
                                    if (!string.IsNullOrWhiteSpace(id))
                                    {
                                        account.NijiBotChannelId = id;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "Get NIJI private chat channel ID exception {@0}", account.ChannelId);

                                    info.AppendLine($"{account.Id} initializing... Get NIJI private chat channel ID exception");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id} initializing... Get NIJI private chat channel ID time: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();
                            }

                            account.DayDrawCount = dayCount;
                            db.Update("NijiBotChannelId,PrivateChannelId,AllowModes,SubChannels,SubChannelValues,FastExhausted,DayDrawCount", account);

                            // Clear cache
                            ClearAccountCache(account.Id);

                            // Enable automatic account verification
                            // Validate account before connecting
                            if (setting.EnableAutoVerifyAccount)
                            {
                                var success = await _discordAccountHelper.ValidateAccount(account);
                                if (!success)
                                {
                                    throw new Exception("Account unavailable");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id} initializing... Validate account time: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();
                            }

                            disInstance = await _discordAccountHelper.CreateDiscordInstance(account)!;
                            disInstance.IsInit = true;
                            _discordLoadBalancer.AddInstance(disInstance);

                            sw.Stop();
                            info.AppendLine($"{account.Id} initializing... Create instance time: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();

                            // Here should wait for initialization to complete, and get user information verification, set to available status after getting user successfully
                            // When starting multiple accounts, wait for a while before starting the next account
                            await Task.Delay(1000 * 5);


                            try
                            {
                                // Execute info setting operation after startup
                                await _taskService.InfoSetting(account.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Sync info exception {@0}", account.ChannelId);

                                info.AppendLine($"{account.Id} initializing... Sync info exception");
                            }

                            sw.Stop();
                            info.AppendLine($"{account.Id} initializing... Sync info time: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();

                        }

                        // Slow mode switch to fast mode check
                        if (account.EnableRelaxToFast == true)
                        {
                            await disInstance?.RelaxToFastValidate();
                            sw.Stop();
                            info.AppendLine($"{account.Id} initializing... Slow mode switch to fast mode check time: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }

                        // Enable automatic synchronization of information and settings
                        if (setting.EnableAutoSyncInfoSetting)
                        {
                            // Sync account information every 6~12 hours
                            await disInstance?.RandomSyncInfo();
                            sw.Stop();
                            info.AppendLine($"{account.Id} initializing... Random sync info time: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }
                    }
                    else
                    {
                        sw.Stop();
                        info.AppendLine($"{account.Id} initializing... Not working hours, do not create instance time: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();

                        // If it is not within working hours, release the instance if it exists
                        if (disInstance != null)
                        {
                            _discordLoadBalancer.RemoveInstance(disInstance);
                            disInstance.Dispose();
                        }

                        sw.Stop();
                        info.AppendLine($"{account.Id} initializing... Not working hours, release instance time: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    info.AppendLine($"{account.Id} initializing... Exception: {ex.Message} time: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();

                    _logger.Error(ex, "Account({@0}) init fail, disabled: {@1}", account.ChannelId, ex.Message);

                    account.Enable = false;
                    account.DisabledReason = ex.Message ?? "Initialization failed";

                    db.Update(account);

                    disInstance?.ClearAccountCache(account.Id);
                    disInstance = null;

                    // Clear cache
                    ClearAccountCache(account.Id);

                    sw.Stop();
                    info.AppendLine($"{account.Id} initializing... Exception, disable account time: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();
                }
                finally
                {
                    swAll.Stop();
                    info.AppendLine($"{account.Id} initialization completed, total time: {swAll.ElapsedMilliseconds}ms");

                    _logger.Information(info.ToString());
                }
            });

            // If the lock is not obtained, whether to throw an exception
            // If the lock is validated, but the lock is not obtained, throw an exception
            if (isValidateLock && !isLock)
            {
                throw new LogicException("Initializing, please try again later");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Randomly delay token for 60~600s
        /// </summary>
        /// <returns></returns>
        public async Task RandomSyncToken(DiscordAccount account)
        {
            var key = $"random_token_{account.ChannelId}";
            await _memoryCache.GetOrCreateAsync(key, async c =>
            {
                try
                {
                    _logger.Information("Randomly delay token {@0}", account.ChannelId);

                    // Randomly 60~600s
                    var random = new Random();
                    var sec = random.Next(60, 600);
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(sec));

                    var options = new RestClientOptions(_discordHelper.GetServer())
                    {
                        MaxTimeout = -1,
                        UserAgent = account.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
                    };
                    var client = new RestClient(options);
                    var request = new RestRequest("/api/v9/content-inventory/users/@me", Method.Get);
                    request.AddHeader("authorization", account.UserToken);

                    // base64 encoding
                    // "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiQ2hyb21lIiwiZGV2aWNlIjoiIiwic3lzdGVtX2xvY2FsZSI6InpoLUNOIiwiYnJvd3Nlcl91c2VyX2FnZW50IjoiTW96aWxsYS81LjAgKFdpbmRvd3MgTlQgMTAuMDsgV2luNjQ7IHg2NCkgQXBwbGVXZWJLaXQvNTM3LjM2IChLSFRNTCwgbGlrZSBHZWNrbykgQ2hyb21lLzEyOS4wLjAuMCBTYWZhcmkvNTM3LjM2IiwiYnJvd3Nlcl92ZXJzaW9uIjoiMTI5LjAuMC4wIiwib3NfdmVyc2lvbiI6IjEwIiwicmVmZXJyZXIiOiJodHRwczovL2Rpc2NvcmQuY29tLz9kaXNjb3JkdG9rZW49TVRJM056TXhOVEEyT1RFMU1UQXlNekUzTlEuR1k2U2RpLm9zdl81cVpOcl9xeVdxVDBtTW0tYkJ4RVRXQzgwQzVPbzU4WlJvIiwicmVmZXJyaW5nX2RvbWFpbiI6ImRpc2NvcmQuY29tIiwicmVmZXJyZXJfY3VycmVudCI6IiIsInJlZmVycmluZ19kb21haW5fY3VycmVudCI6IiIsInJlbGVhc2VfY2hhbm5lbCI6InN0YWJsZSIsImNsaWVudF9idWlsZF9udW1iZXIiOjM0Mjk2OCwiY2xpZW50X2V2ZW50X3NvdXJjZSI6bnVsbH0="

                    var str = "{\"os\":\"Windows\",\"browser\":\"Chrome\",\"device\":\"\",\"system_locale\":\"zh-CN\",\"browser_user_agent\":\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36\",\"browser_version\":\"129.0.0.0\",\"os_version\":\"10\",\"referrer\":\"https://discord.com/?discordtoken={@token}\",\"referring_domain\":\"discord.com\",\"referrer_current\":\"\",\"referring_domain_current\":\"\",\"release_channel\":\"stable\",\"client_build_number\":342968,\"client_event_source\":null}";

                    str = str.Replace("{@token}", account.UserToken);

                    // str to base64
                    var bs64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(str));

                    request.AddHeader("x-super-properties", bs64);
                    var response = await client.ExecuteAsync(request);

                    //{
                    //    "request_id": "62a56587a8964dfa9cbb81c234a9a962",
                    //    "entries": [],
                    //    "entries_hash": 0,
                    //    "expired_at": "2024-11-08T02:50:21.323000+00:00",
                    //    "refresh_stale_inbox_after_ms": 30000,
                    //    "refresh_token": "eyJjcmVhdGVkX2F0IjogIjIwMjQtMTEtMDhUMDI6Mzk6MjguNDY4MzcyKzAwOjAwIiwgImNvbnRlbnRfaGFzaCI6ICI0N0RFUXBqOEhCU2ErL1RJbVcrNUpDZXVRZVJrbTVOTXBKV1pHM2hTdUZVPSJ9",
                    //    "wait_ms_until_next_fetch": 652856
                    //}

                    var obj = JObject.Parse(response.Content);
                    if (obj.ContainsKey("refresh_token"))
                    {
                        var refreshToken = obj["refresh_token"].ToString();
                        if (!string.IsNullOrWhiteSpace(refreshToken))
                        {
                            _logger.Information("Randomly delay token success {@0}", account.ChannelId);
                            return true;
                        }
                    }

                    _logger.Information("Randomly delay token failed {@0}, {@1}", account.ChannelId, response.Content);

                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Randomly delay token exception {@0}", account.ChannelId);
                }

                return false;
            });
        }

        /// <summary>
        /// Update account information
        /// </summary>
        /// <param name="param"></param>
        public async Task UpdateAccount(DiscordAccount param)
        {
            var model = DbHelper.Instance.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("Account does not exist");
            }

            // Update must be locked because other processes will modify the account value, causing the value to be overwritten
            var isLock = await AsyncLocalLock.TryLockAsync($"initialize:{model.Id}", TimeSpan.FromSeconds(5), async () =>
            {
                model = DbHelper.Instance.AccountStore.Get(model.Id)!;

                // Channel ID and server ID are prohibited from modification
                //model.ChannelId = account.ChannelId;
                //model.GuildId = account.GuildId;

                // Automatically unlock when updating account reconnection
                model.Lock = false;
                model.CfHashCreated = null;
                model.CfHashUrl = null;
                model.CfUrl = null;

                // Validate Interval
                if (param.Interval < 1.2m)
                {
                    param.Interval = 1.2m;
                }

                // Validate WorkTime
                if (!string.IsNullOrEmpty(param.WorkTime))
                {
                    var ts = param.WorkTime.ToTimeSlots();
                    if (ts.Count == 0)
                    {
                        param.WorkTime = null;
                    }
                }

                // Validate FishingTime
                if (!string.IsNullOrEmpty(param.FishingTime))
                {
                    var ts = param.FishingTime.ToTimeSlots();
                    if (ts.Count == 0)
                    {
                        param.FishingTime = null;
                    }
                }

                model.EnableAutoSetRelax = param.EnableAutoSetRelax;
                model.EnableRelaxToFast = param.EnableRelaxToFast;
                model.EnableFastToRelax = param.EnableFastToRelax;
                model.IsBlend = param.IsBlend;
                model.IsDescribe = param.IsDescribe;
                model.IsShorten = param.IsShorten;
                model.DayDrawLimit = param.DayDrawLimit;
                model.IsVerticalDomain = param.IsVerticalDomain;
                model.VerticalDomainIds = param.VerticalDomainIds;
                model.SubChannels = param.SubChannels;

                model.PermanentInvitationLink = param.PermanentInvitationLink;
                model.FishingTime = param.FishingTime;
                model.EnableNiji = param.EnableNiji;
                model.EnableMj = param.EnableMj;
                model.AllowModes = param.AllowModes;
                model.WorkTime = param.WorkTime;
                model.Interval = param.Interval;
                model.AfterIntervalMin = param.AfterIntervalMin;
                model.AfterIntervalMax = param.AfterIntervalMax;
                model.Sort = param.Sort;
                model.Enable = param.Enable;
                model.PrivateChannelId = param.PrivateChannelId;
                model.NijiBotChannelId = param.NijiBotChannelId;
                model.UserAgent = param.UserAgent;
                model.RemixAutoSubmit = param.RemixAutoSubmit;
                model.CoreSize = param.CoreSize;
                model.QueueSize = param.QueueSize;
                model.MaxQueueSize = param.MaxQueueSize;
                model.TimeoutMinutes = param.TimeoutMinutes;
                model.Weight = param.Weight;
                model.Remark = param.Remark;
                model.BotToken = param.BotToken;
                model.UserToken = param.UserToken;
                model.Mode = param.Mode;
                model.Sponsor = param.Sponsor;

                DbHelper.Instance.AccountStore.Update(model);

                var disInstance = _discordLoadBalancer.GetDiscordInstance(model.ChannelId);
                disInstance?.ClearAccountCache(model.Id);

                // Clear cache
                ClearAccountCache(model.Id);

                await Task.CompletedTask;
            });
            if (!isLock)
            {
                throw new LogicException("Job in progress, please try again later");
            }
        }

        /// <summary>
        /// Clear account cache
        /// </summary>
        /// <param name="id"></param>
        public void ClearAccountCache(string id)
        {
            _memoryCache.Remove($"account:{id}");
        }


        /// <summary>
        /// Update and reconnect the account.
        /// </summary>
        /// <param name="account"></param>
        public async Task ReconnectAccount(DiscordAccount account)
        {
            try
            {
                // If it's running, then release.
                var disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                if (disInstance != null)
                {
                    _discordLoadBalancer.RemoveInstance(disInstance);
                    disInstance.Dispose();
                }
            }
            catch
            {
            }

            await UpdateAccount(account);

            // Execute asynchronously
            _ = StartCheckAccount(account);
        }

        /// <summary>
        /// Stop the connection and delete the account.
        /// </summary>
        /// <param name="id"></param>
        public void DeleteAccount(string id)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);

            if (model != null)
            {
                try
                {
                    var disInstance = _discordLoadBalancer.GetDiscordInstance(model.ChannelId);
                    if (disInstance != null)
                    {
                        _discordLoadBalancer.RemoveInstance(disInstance);
                        disInstance.Dispose();
                    }
                }
                catch
                {
                }

                DbHelper.Instance.AccountStore.Delete(id);
            }
        }

        /// <summary>
        /// Stop service.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Information("Routine check service stopped");

            _timer?.Change(Timeout.Infinite, 0);

            await Task.CompletedTask;
        }
    }
}