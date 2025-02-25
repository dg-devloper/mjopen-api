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

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Proxy configuration property class.
    /// </summary>
    public class ProxyProperties : DomainObject
    {
        /// <summary>
        /// MongoDB default connection string.
        /// </summary>
        public string MongoDefaultConnectionString { get; set; }

        /// <summary>
        /// MongoDB default database.
        /// </summary>
        public string MongoDefaultDatabase { get; set; }

        /// <summary>
        /// Whether to use it or not.
        /// </summary>
        [BsonIgnore]
        public bool IsMongo { get; set; } 

        /// <summary>
        /// Whether to enable local data auto migration to MongoDB.
        /// </summary>
        public bool IsMongoAutoMigrate { get; set; }

        /// <summary>
        /// Store maximum data.
        /// </summary>
        public int MaxCount { get; set; } = 500000;

        /// <summary>
        /// Discord account selection rules.
        /// </summary>
        public AccountChooseRule AccountChooseRule { get; set; } = AccountChooseRule.BestWaitIdle;

        /// <summary>
        /// Discord single account configuration.
        /// </summary>
        [BsonIgnore]
        public DiscordAccountConfig Discord { get; set; } = new DiscordAccountConfig();

        /// <summary>
        /// Discord account pool configuration.
        /// </summary>
        [BsonIgnore]
        public List<DiscordAccountConfig> Accounts { get; set; } = new List<DiscordAccountConfig>();

        /// <summary>
        /// Proxy configuration.
        /// </summary>
        public ProxyConfig Proxy { get; set; } = new ProxyConfig();

        /// <summary>
        /// Reverse proxy configuration.
        /// </summary>
        public NgDiscordConfig NgDiscord { get; set; } = new NgDiscordConfig();

        /// <summary>
        /// Baidu translation configuration.
        /// </summary>
        public BaiduTranslateConfig BaiduTranslate { get; set; } = new BaiduTranslateConfig();

        /// <summary>
        /// OpenAI configuration.
        /// </summary>
        public OpenaiConfig Openai { get; set; } = new OpenaiConfig();

        /// <summary>
        /// Method of translating Chinese prompt.
        /// </summary>
        public TranslateWay TranslateWay { get; set; } = TranslateWay.NULL;

        /// <summary>
        /// Task status change callback address.
        /// </summary>
        public string NotifyHook { get; set; }

        /// <summary>
        /// Notification callback thread pool size.
        /// </summary>
        public int NotifyPoolSize { get; set; } = 10;

        /// <summary>
        /// Email sending configuration.
        /// </summary>
        public SmtpConfig Smtp { get; set; }

        /// <summary>
        /// CF verification server address.
        /// </summary>
        public string CaptchaServer { get; set; }

        /// <summary>
        /// CF verification notification address (callback after successful verification).
        /// </summary>
        public string CaptchaNotifyHook { get; set; }

        /// <summary>
        /// CF verification notification callback secret to prevent tampering.
        /// </summary>
        public string CaptchaNotifySecret { get; set; }

        /// <summary>
        /// Image storage method.
        /// </summary>
        public ImageStorageType ImageStorageType { get; set; } = ImageStorageType.NONE;

        /// <summary>
        /// Alibaba Cloud storage configuration.
        /// </summary>
        public AliyunOssOptions AliyunOss { get; set; } = new AliyunOssOptions();

        /// <summary>
        /// Tencent Cloud storage configuration.
        /// </summary>
        public TencentCosOptions TencentCos { get; set; } = new TencentCosOptions();

        /// <summary>
        /// Cloudflare R2 storage configuration.
        /// </summary>
        public CloudflareR2Options CloudflareR2 { get; set; } = new CloudflareR2Options();

        /// <summary>
        /// Face swap configuration.
        /// </summary>
        public ReplicateOptions Replicate { get; set; } = new ReplicateOptions();

        /// <summary>
        /// Local storage configuration.
        /// </summary>
        public LocalStorageOptions LocalStorage { get; set; } = new LocalStorageOptions();
    }

    /// <summary>
    /// Local storage configuration.
    /// </summary>
    public class LocalStorageOptions
    {
        /// <summary>
        /// Acceleration domain, can be used for image acceleration or checks.
        /// </summary>
        public string CustomCdn { get; set; }
    }

    /// <summary>
    /// Cloudflare R2 storage configuration.
    /// </summary>
    public class CloudflareR2Options
    {
        /// <summary>
        /// 
        /// </summary>
        public string AccountId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string AccessKey { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Bucket
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Acceleration domain, can be used for image acceleration or checks.
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// Default image style.
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// Default thumbnail image style.
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// Video snapshot.
        /// https://cloud.tencent.com/document/product/436/55671
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// Storage class of the object
        ///// en: https://intl.cloud.tencent.com/document/product/436/30925
        ///// zh: https://cloud.tencent.com/document/product/436/33417
        ///// </summary>
        //public string StorageClass { get; set; }

        /// <summary>
        /// Default link validity time.
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
    }

    /// <summary>
    /// Tencent Cloud storage configuration.
    /// </summary>
    public class TencentCosOptions
    {
        /// <summary>
        /// Tencent Cloud Account APPID
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// Cloud API Secret Id
        /// </summary>
        public string SecretId { get; set; }

        /// <summary>
        /// Cloud API Secret Key
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Bucket region ap-guangzhou ap-hongkong
        /// en: https://intl.cloud.tencent.com/document/product/436/6224
        /// zh: https://cloud.tencent.com/document/product/436/6224
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Bucket, format: BucketName-APPID
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// Acceleration domain, can be used for image acceleration or checks.
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// Default image style.
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// Default thumbnail image style.
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// Video snapshot.
        /// https://cloud.tencent.com/document/product/436/55671
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// Storage class of the object
        ///// en: https://intl.cloud.tencent.com/document/product/436/30925
        ///// zh: https://cloud.tencent.com/document/product/436/33417
        ///// </summary>
        //public string StorageClass { get; set; }

        /// <summary>
        /// Default link validity time.
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
    }

    /// <summary>
    /// Email sending configuration.
    /// </summary>
    public class SmtpConfig
    {
        /// <summary>
        /// SMTP server info.
        /// smtp.mxhichina.com
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// SMTP port, typically 587 or 465 depending on the server.
        /// </summary>
        public int Port { get; set; } = 465;

        /// <summary>
        /// Set according to your SMTP server requirements.
        /// </summary>
        public bool EnableSsl { get; set; } = true;

        /// <summary>
        /// Sender nickname.
        /// system
        /// </summary>
        public string FromName { get; set; }

        /// <summary>
        /// Sender email address.
        /// system@trueai.org
        /// </summary>
        public string FromEmail { get; set; }

        /// <summary>
        /// Your email password or app-specific password.
        /// </summary>
        public string FromPassword { get; set; }

        /// <summary>
        /// Recipients.
        /// </summary>
        public string To { get; set; }
    }

    /// <summary>
    /// Discord account configuration.
    /// </summary>
    public class DiscordAccountConfig
    {
        /// <summary>
        /// Server ID
        /// </summary>
        public string GuildId { get; set; }

        /// <summary>
        /// Channel ID.
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// MJ direct message channel ID, used to receive seed value.
        /// </summary>
        public string PrivateChannelId { get; set; }

        /// <summary>
        /// Niji direct message channel ID, used to receive seed value.
        /// </summary>
        public string NijiBotChannelId { get; set; }

        /// <summary>
        /// User token.
        /// </summary>
        public string UserToken { get; set; }

        /// <summary>
        /// Bot token
        ///
        /// 1. 创建应用
        /// https://discord.com/developers/applications
        ///
        /// 2. 设置应用权限（确保拥有读取内容权限）
        /// [Bot] 设置 -> 全部开启
        ///
        /// 3. 添加应用到频道服务器
        /// https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot
        ///
        /// 4. 复制或重置 Bot Token
        /// </summary>
        public string BotToken { get; set; }

        /// <summary>
        /// User UserAgent.
        /// </summary>
        public string UserAgent { get; set; } = Constants.DEFAULT_DISCORD_USER_AGENT;

        /// <summary>
        /// Whether it's available.
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// Enable Midjourney drawing.
        /// </summary>
        public bool EnableMj { get; set; } = true;

        /// <summary>
        /// Enable Niji drawing.
        /// </summary>
        public bool EnableNiji { get; set; } = true;

        /// <summary>
        /// Enable fast mode and automatically switch to slow mode after usage.
        /// </summary>
        public bool EnableFastToRelax { get; set; }

        /// <summary>
        /// 启用时，当有快速时长时，自动切换到快速模式
        /// </summary>
        public bool EnableRelaxToFast { get; set; }

        /// <summary>
        /// 自动设置慢速
        /// 启用后，当快速用完时，如果允许生成速度模式是 FAST 或 TURBO，则自动清空原有模式，并设置为 RELAX 模式。
        /// </summary>
        public bool? EnableAutoSetRelax { get; set; }

        /// <summary>
        /// Concurrency.
        /// </summary>
        public int CoreSize { get; set; } = 3;

        /// <summary>
        /// Waiting queue length.
        /// </summary>
        public int QueueSize { get; set; } = 10;

        /// <summary>
        /// Maximum waiting queue length.
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Task timeout (minutes).
        /// </summary>
        public int TimeoutMinutes { get; set; } = 5;

        /// <summary>
        /// Specify generation speed mode (--fast, --relax, or --turbo).
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// Allowed speed modes (invalid modes will remove keywords).
        /// </summary>
        public List<GenerationSpeedMode> AllowModes { get; set; } = new List<GenerationSpeedMode>();

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
        /// Daily max drawing limit (0 means no limit).
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

        /// <summary>
        /// Enable vertical domain.
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
        /// Remark.
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// Sponsor (rich text).
        /// </summary>
        public string Sponsor { get; set; }

        /// <summary>
        /// Whether it's a sponsor.
        /// </summary>
        public bool IsSponsor { get; set; }

        /// <summary>
        /// Sort.
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// Task execution interval (seconds, default: 1.2s).
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
        /// Work time.
        /// </summary>
        public string WorkTime { get; set; }

        /// <summary>
        /// Slacking off time (accept changed tasks only).
        /// </summary>
        public string FishingTime { get; set; }

        /// <summary>
        /// Permanent invitation link of the current channel.
        /// </summary>
        public string PermanentInvitationLink { get; set; }

        /// <summary>
        /// Weight.
        /// </summary>
        public int Weight { get; set; }

        /// <summary>
        /// Remix auto submit.
        /// </summary>
        public bool RemixAutoSubmit { get; set; }
    }

    /// <summary>
    /// Baidu translation configuration.
    /// </summary>
    public class BaiduTranslateConfig
    {
        /// <summary>
        /// App ID for Baidu translation.
        /// </summary>
        public string Appid { get; set; }

        /// <summary>
        /// App secret for Baidu translation.
        /// </summary>
        public string AppSecret { get; set; }
    }

    /// <summary>
    /// OpenAI configuration.
    /// </summary>
    public class OpenaiConfig
    {
        /// <summary>
        /// Custom GPT API URL.
        /// </summary>
        public string GptApiUrl { get; set; }

        /// <summary>
        /// GPT API key.
        /// </summary>
        public string GptApiKey { get; set; }

        /// <summary>
        /// Timeout.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Used model.
        /// </summary>
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// Maximum token count for the returned result.
        /// </summary>
        public int MaxTokens { get; set; } = 2048;

        /// <summary>
        /// Similarity, range 0-2.
        /// </summary>
        public double Temperature { get; set; } = 0;
    }

    /// <summary>
    /// Proxy configuration.
    /// </summary>
    public class ProxyConfig
    {
        /// <summary>
        /// Proxy host.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Proxy port.
        /// </summary>
        public int? Port { get; set; }
    }

    /// <summary>
    /// Reverse proxy configuration.
    /// </summary>
    public class NgDiscordConfig
    {
        /// <summary>
        /// https://discord.com 反代.
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// https://cdn.discordapp.com 反代.
        /// </summary>
        public string Cdn { get; set; }

        /// <summary>
        /// wss://gateway.discord.gg 反代.
        /// </summary>
        public string Wss { get; set; }

        /// <summary>
        /// wss://gateway-us-east1-b.discord.gg 反代.
        /// </summary>
        public string ResumeWss { get; set; }

        /// <summary>
        /// https://discord-attachments-uploads-prd.storage.googleapis.com 反代.
        /// </summary>
        public string UploadServer { get; set; }

        ///// <summary>
        ///// 自动下载图片并保存到本地
        ///// </summary>
        //public bool? SaveToLocal { get; set; }

        ///// <summary>
        ///// 自定义 CDN 加速地址
        ///// </summary>
        //public string CustomCdn { get; set; }
    }

    /// <summary>
    /// Alibaba Cloud storage configuration.
    /// <see cref="https://help.aliyun.com/document_detail/31947.html"/>
    /// </summary>
    public class AliyunOssOptions
    {
        ///// <summary>
        ///// 是否可用
        ///// </summary>
        //public bool Enable { get; set; }

        ///// <summary>
        ///// 启动本地图片自动迁移，待定
        ///// </summary>
        //public bool EnableAutoMigrate { get; set; }

        /// <summary>
        /// The storage bucket used to store objects.
        /// </summary>
        public string BucketName { get; set; }

        ///// <summary>
        ///// 地域表示 OSS 的数据中心所在物理位置。
        ///// </summary>
        //public string Region { get; set; }

        /// <summary>
        /// AccessKeyId is used to identify the user, and AccessKeySecret must be kept confidential.
        /// </summary>
        public string AccessKeyId { get; set; }

        /// <summary>
        /// AccessKeyId is used to identify the user, and AccessKeySecret must be kept confidential.
        /// </summary>
        public string AccessKeySecret { get; set; }

        /// <summary>
        /// Endpoint indicates the external domain for OSS.
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// Alibaba Cloud acceleration domain, can be used for image acceleration or checks.
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// Alibaba Cloud OSS default image style.
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// Alibaba Cloud OSS default thumbnail image style x-oss-process=style/w320
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// Alibaba Cloud OSS video snapshot.
        /// x-oss-process=video/snapshot,t_6000,f_jpg,w_400,m_fast
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// 开启自动迁移本地文件到阿里云支持
        ///// </summary>
        //public bool IsAutoMigrationLocalFile { get; set; }

        /// <summary>
        /// Default link validity time.
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
    }

    /// <summary>
    /// 基于 replicate 平台进行换脸等业务
    /// https://replicate.com/omniedgeio/face-swap
    /// https://replicate.com/codeplugtech/face-swap
    /// https://github.com/tzktz/face-swap?tab=readme-ov-file
    ///
    /// 其他参考：
    /// https://huggingface.co/spaces/tonyassi/video-face-swap
    /// https://huggingface.co/spaces/felixrosberg/face-swap
    /// https://felixrosberg-face-swap.hf.space/
    ///
    /// Picsi.Ai
    /// https://www.picsi.ai/faceswap
    /// </summary>
    public class ReplicateOptions
    {
        /// <summary>
        /// REPLICATE_API_TOKEN
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Enable face swap.
        /// </summary>
        public bool EnableFaceSwap { get; set; }

        /// <summary>
        /// Face swap version.
        /// 默认（$0.002/次）：https://replicate.com/codeplugtech/face-swap -> 278a81e7ebb22db98bcba54de985d22cc1abeead2754eb1f2af717247be69b34
        /// 快速（$0.019/次）：https://replicate.com/omniedgeio/face-swap -> d28faa318942bf3f1cbed9714def03594f99b3c69b2eb279c39fc60993cee9ac
        /// </summary>
        public string FaceSwapVersion { get; set; } = "278a81e7ebb22db98bcba54de985d22cc1abeead2754eb1f2af717247be69b34";

        /// <summary>
        /// Face swap concurrency.
        /// </summary>
        public int FaceSwapCoreSize { get; set; } = 3;

        /// <summary>
        /// Face swap waiting queue length.
        /// </summary>
        public int FaceSwapQueueSize { get; set; } = 10;

        /// <summary>
        /// Face swap task timeout.
        /// </summary>
        public int FaceSwapTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// Enable video face swap.
        /// </summary>
        public bool EnableVideoFaceSwap { get; set; }

        /// <summary>
        /// Video face swap model version.
        /// https://replicate.com/xrunda/hello
        /// </summary>
        public string VideoFaceSwapVersion { get; set; } = "104b4a39315349db50880757bc8c1c996c5309e3aa11286b0a3c84dab81fd440";

        /// <summary>
        /// Video face swap concurrency.
        /// </summary>
        public int VideoFaceSwapCoreSize { get; set; } = 3;

        /// <summary>
        /// Video face swap queue length.
        /// </summary>
        public int VideoFaceSwapQueueSize { get; set; } = 10;

        /// <summary>
        /// Video face swap task timeout.
        /// </summary>
        public int VideoFaceSwapTimeoutMinutes { get; set; } = 30;

        /// <summary>
        /// Max file size limit.
        /// </summary>
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Webhook notification.
        /// </summary>
        public string Webhook { get; set; }

        /// <summary>
        /// Webhook events filter.
        /// start：预测开始时立即
        /// output：每次预测都会产生一个输出（请注意，预测可以产生多个输出）
        /// logs：每次日志输出都是由预测生成的
        /// completed：当预测达到终止状态（成功/取消/失败）时
        /// </summary>
        public string[] WebhookEventsFilter { get; set; } = [];
    }
}