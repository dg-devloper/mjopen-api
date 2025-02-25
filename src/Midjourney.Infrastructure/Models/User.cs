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
using Midjourney.Infrastructure.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// User
    /// </summary>
    [BsonCollection("user")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    public class User : DomainObject
    {
        public User()
        {

        }

        /// <summary>
        /// User
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Email
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Phone number
        /// </summary>
        public string Phone { get; set; }

        /// <summary>
        /// Avatar
        /// </summary>
        public string Avatar { get; set; }

        /// <summary>
        /// Role (ADMIN or USER)
        /// </summary>
        public EUserRole? Role { get; set; }

        /// <summary>
        /// Status
        /// </summary>
        public EUserStatus? Status { get; set; }

        /// <summary>
        /// User token
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Last login IP
        /// </summary>
        public string LastLoginIp { get; set; }

        /// <summary>
        /// Last login time
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? LastLoginTime { get; set; }

        /// <summary>
        /// Last login time formatted
        /// </summary>
        public string LastLoginTimeFormat => LastLoginTime?.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// Register IP
        /// </summary>
        public string RegisterIp { get; set; }

        /// <summary>
        /// Register time
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime RegisterTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Register time formatted
        /// </summary>
        public string RegisterTimeFormat => RegisterTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// Daily drawing limit, default 0 means no limit
        /// </summary>
        public int DayDrawLimit { get; set; }

        /// <summary>
        /// Whitelist user (not subject to rate-limiting)
        /// </summary>
        public bool IsWhite { get; set; } = false;

        /// <summary>
        /// Creation time
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Update time
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}