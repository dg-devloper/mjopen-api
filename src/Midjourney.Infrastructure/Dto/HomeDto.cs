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
namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Home page information.
    /// </summary>
    public class HomeDto
    {
        /// <summary>
        /// Whether to display registration entry.
        /// </summary>
        public bool IsRegister { get; set; }

        /// <summary>
        /// Whether the guest entry is enabled.
        /// </summary>
        public bool IsGuest { get; set; }

        /// <summary>
        /// The website is configured for demo mode.
        /// </summary>
        public bool IsDemoMode { get; set; }

        /// <summary>
        /// Version number.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Today's drawings.
        /// </summary>
        public int TodayDraw { get; set; }

        /// <summary>
        /// Yesterday's drawings.
        /// </summary>
        public int YesterdayDraw { get; set; }

        /// <summary>
        /// Total drawings.
        /// </summary>
        public int TotalDraw { get; set; }

        /// <summary>
        /// Home page announcement.
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// Top 5 drawing clients.
        /// </summary>
        public Dictionary<string, int> Tops { get; set; } = new Dictionary<string, int>();
    }
}
