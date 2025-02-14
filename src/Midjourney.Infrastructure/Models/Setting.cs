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

using Midjourney.Infrastructure.Options;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// System configuration
    /// </summary>
    public class Setting : ProxyProperties
    {
        /// <summary>
        /// Globally enable vertical domain
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// Enable Swagger
        /// </summary>
        public bool EnableSwagger { get; set; }

        /// <summary>
        /// Banned limiting configuration
        /// </summary>
        public BannedLimitingOptions BannedLimiting { get; set; } = new();

        /// <summary>
        /// Rate limiting configuration
        /// </summary>
        public IpRateLimitingOptions IpRateLimiting { get; set; }

        /// <summary>
        /// Blacklist rate limiting configuration
        /// </summary>
        public IpBlackRateLimitingOptions IpBlackRateLimiting { get; set; }

        /// <summary>
        /// Enable registration
        /// </summary>
        public bool EnableRegister { get; set; }

        /// <summary>
        /// Daily drawing limit for newly registered users
        /// </summary>
        public int RegisterUserDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// Enable guest
        /// </summary>
        public bool EnableGuest { get; set; }

        /// <summary>
        /// Default daily drawing limit for guest users
        /// </summary>
        public int GuestDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// Home page notification
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// Enable auto-get private ID at startup
        /// </summary>
        public bool EnableAutoGetPrivateId { get; set; }

        /// <summary>
        /// Enable auto-verify account availability at startup
        /// </summary>
        public bool EnableAutoVerifyAccount { get; set; }

        /// <summary>
        /// Enable auto-sync info and settings
        /// </summary>
        public bool EnableAutoSyncInfoSetting { get; set; }

        /// <summary>
        /// Enable automatic token extension
        /// </summary>
        public bool EnableAutoExtendToken { get; set; }

        /// <summary>
        /// Enable user to upload Base64
        /// </summary>
        public bool EnableUserCustomUploadBase64 { get; set; } = true;

        /// <summary>
        /// Enable converting official links
        /// </summary>
        public bool EnableConvertOfficialLink { get; set; } = true;

        /// <summary>
        /// Enable converting to cloud/accelerated/OSS/COS/CDN links
        /// </summary>
        public bool EnableConvertAliyunLink { get; set; }

        /// <summary>
        /// Enable MJ translation
        /// </summary>
        public bool EnableMjTranslate { get; set; } = true;

        /// <summary>
        /// Enable Niji translation
        /// </summary>
        public bool EnableNijiTranslate { get; set; } = true;

        /// <summary>
        /// Convert Niji to MJ
        /// Enable this to automatically convert Niji · journey tasks to Midjourney tasks and add the --niji suffix to the task (the output effect is the same after conversion)
        /// </summary>
        public bool EnableConvertNijiToMj { get; set; }

        /// <summary>
        /// Convert --niji to Niji Bot
        /// When the prompt contains --niji, it will automatically convert to Niji·journey Bot task
        /// </summary>
        public bool EnableConvertNijiToNijiBot { get; set; }

        /// <summary>
        /// Enable auto-login
        /// </summary>
        public bool EnableAutoLogin { get; set; }

        /// <summary>
        /// Enable account sponsorship
        /// </summary>
        public bool EnableAccountSponsor { get; set; }
    }

    /// <summary>
    /// Banned limiting options when Banned prompt is detected
    /// </summary>
    public class BannedLimitingOptions
    {
        /// <summary>
        /// Whether to enable Banned limiting
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// Banned limiting rules. Key: daily trigger count, Value: block duration (minutes)
        /// </summary>
        public Dictionary<int, int> Rules { get; set; } = [];
    }
}