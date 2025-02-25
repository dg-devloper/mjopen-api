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
using System.ComponentModel.DataAnnotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Captcha verification request.
    /// </summary>
    public class CaptchaVerfyRequest
    {
        /// <summary>
        /// CF popup link
        /// Example: https://936929561302675456.discordsays.com/captcha/api/c/hIlZOI0ZQI3qQjpXhzS4GTgw_DuRTjYiyyww38dJuTzmqA8pa3OC60yTJbTmK6jd3i6Q0wZNxiuEp2dW/ack?hash=1
        /// </summary>
        [MaxLength(4000)]
        public string Url { get; set; }

        /// <summary>
        /// Custom parameter = ChannelId
        /// </summary>
        [MaxLength(4000)]
        public string State { get; set; }

        /// <summary>
        /// Notify callback secret key to prevent tampering
        /// </summary>
        [MaxLength(4000)]
        public string Secret { get; set; }

        /// <summary>
        /// Callback address, the global notifyHook is used if empty.
        /// </summary>
        [MaxLength(4000)]
        public string NotifyHook { get; set; }

        /// <summary>
        /// Whether verification succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Message.
        /// </summary>
        [MaxLength(4000)]
        public string Message { get; set; }
    }
}
