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

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// Storage Service
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Upload
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <param name="mimeType"></param>
        /// <returns></returns>
        UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType);

        /// <summary>
        /// Delete file
        /// </summary>
        /// <param name="isDeleteMedia">Indicate whether to delete record</param>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task DeleteAsync(bool isDeleteMedia = false, params string[] keys);

        /// <summary>
        /// Get file stream data
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Stream GetObject(string key);

        ///// <summary>
        ///// Get file stream data, return file type
        ///// </summary>
        ///// <param name="key"></param>
        ///// <param name="contentType"></param>
        ///// <returns></returns>
        //Stream GetObject(string key, out string contentType);

        /// <summary>
        /// Move file
        /// </summary>
        /// <param name="key"></param>
        /// <param name="newKey"></param>
        /// <param name="isCopy"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        Task MoveAsync(string key, string newKey, bool isCopy = false);

        /// <summary>
        /// Check if file exists
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// Overwrite save file
        /// </summary>
        /// <param name="mediaBinaryStream"></param>
        /// <param name="key"></param>
        /// <param name="mimeType"></param>
        void Overwrite(Stream mediaBinaryStream, string key, string mimeType);
    }
}