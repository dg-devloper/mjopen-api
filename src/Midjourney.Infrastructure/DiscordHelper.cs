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

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Discord helper class for handling Discord-related URLs and operations.
    /// </summary>
    public class DiscordHelper
    {
        private readonly ProxyProperties _properties;

        /// <summary>
        /// Initializes a new instance of the DiscordHelper class.
        /// </summary>
        public DiscordHelper()
        {
            _properties = GlobalConfiguration.Setting;
        }

        /// <summary>
        /// DISCORD_SERVER_URL.
        /// </summary>
        public const string DISCORD_SERVER_URL = "https://discord.com";

        /// <summary>
        /// DISCORD_CDN_URL.
        /// </summary>
        public const string DISCORD_CDN_URL = "https://cdn.discordapp.com";

        /// <summary>
        /// DISCORD_WSS_URL.
        /// </summary>
        public const string DISCORD_WSS_URL = "wss://gateway.discord.gg";

        /// <summary>
        /// DISCORD_UPLOAD_URL.
        /// </summary>
        public const string DISCORD_UPLOAD_URL = "https://discord-attachments-uploads-prd.storage.googleapis.com";

        /// <summary>
        /// Authentication URL, if 200 then it is normal.
        /// </summary>
        public const string DISCORD_VAL_URL = "https://discord.com/api/v9/users/@me/billing/country-code";

        /// <summary>
        /// ME channels.
        /// </summary>
        public const string ME_CHANNELS_URL = "https://discord.com/api/v9/users/@me/channels";

        /// <summary>
        /// Gets the Discord server URL.
        /// </summary>
        /// <returns>Discord server URL.</returns>
        public string GetServer()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Server))
            {
                return DISCORD_SERVER_URL;
            }

            string serverUrl = _properties.NgDiscord.Server;
            return serverUrl.EndsWith("/") ? serverUrl.Substring(0, serverUrl.Length - 1) : serverUrl;
        }

        /// <summary>
        /// Gets the Discord CDN URL.
        /// </summary>
        /// <returns>Discord CDN URL.</returns>
        public string GetCdn()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Cdn))
            {
                return DISCORD_CDN_URL;
            }

            string cdnUrl = _properties.NgDiscord.Cdn;
            return cdnUrl.EndsWith("/") ? cdnUrl.Substring(0, cdnUrl.Length - 1) : cdnUrl;
        }

        // ...existing code...

        /// <summary>
        /// Gets the Discord WebSocket URL.
        /// </summary>
        /// <returns>Discord WebSocket URL.</returns>
        public string GetWss()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.Wss))
            {
                return DISCORD_WSS_URL;
            }

            string wssUrl = _properties.NgDiscord.Wss;
            return wssUrl.EndsWith("/") ? wssUrl.Substring(0, wssUrl.Length - 1) : wssUrl;
        }

        /// <summary>
        /// Gets the Discord Resume WebSocket URL.
        /// </summary>
        /// <returns>Discord Resume WebSocket URL.</returns>
        public string GetResumeWss()
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.ResumeWss))
            {
                return null;
            }

            string resumeWss = _properties.NgDiscord.ResumeWss;
            return resumeWss.EndsWith("/") ? resumeWss.Substring(0, resumeWss.Length - 1) : resumeWss;
        }

        /// <summary>
        /// Gets the Discord upload URL.
        /// </summary>
        /// <param name="uploadUrl">Original upload URL.</param>
        /// <returns>Processed upload URL.</returns>
        public string GetDiscordUploadUrl(string uploadUrl)
        {
            if (string.IsNullOrWhiteSpace(_properties.NgDiscord.UploadServer) || string.IsNullOrWhiteSpace(uploadUrl))
            {
                return uploadUrl;
            }

            string uploadServer = _properties.NgDiscord.UploadServer;
            if (uploadServer.EndsWith("/"))
            {
                uploadServer = uploadServer.Substring(0, uploadServer.Length - 1);
            }

            return uploadUrl.Replace(DISCORD_UPLOAD_URL, uploadServer);
        }

        /// <summary>
        /// Gets the message hash from the image URL.
        /// </summary>
        /// <param name="imageUrl">Image URL.</param>
        /// <returns>Message hash.</returns>
        public string GetMessageHash(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            if (imageUrl.EndsWith("_grid_0.webp"))
            {
                int hashStartIndex = imageUrl.LastIndexOf("/");
                if (hashStartIndex < 0)
                {
                    return null;
                }
                return imageUrl.Substring(hashStartIndex + 1, imageUrl.Length - hashStartIndex - 1 - "_grid_0.webp".Length);
            }

            int startIndex = imageUrl.LastIndexOf("_");
            if (startIndex < 0)
            {
                return null;
            }

            return imageUrl.Substring(startIndex + 1).Split('.')[0];
        }
    }
}