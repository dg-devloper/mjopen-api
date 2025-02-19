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

using Serilog;

namespace Midjourney.Infrastructure.Storage
{
    /// <summary>
    /// Local storage service
    /// </summary>
    public class LocalStorageService : IStorageService
    {
        private readonly ILogger _logger;

        public LocalStorageService()
        {
            _logger = Log.Logger;
        }

        /// <summary>
        /// Save file to local storage
        /// </summary>
        public UploadResult SaveAsync(Stream mediaBinaryStream, string key, string mimeType)
        {
            if (mediaBinaryStream == null || mediaBinaryStream.Length <= 0)
                throw new ArgumentNullException(nameof(mediaBinaryStream));

            var filePath = GetFilePath(key);
            var directory = Path.GetDirectoryName(filePath);

            // Create target directory
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save file
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                mediaBinaryStream.CopyTo(fileStream);
            }

            _logger.Information("File saved to local storage: {FilePath}", filePath);

            var opt = GlobalConfiguration.Setting.LocalStorage;

            return new UploadResult
            {
                FileName = Path.GetFileName(key),
                Key = key,
                Path = filePath,
                Size = mediaBinaryStream.Length,
                ContentType = mimeType,
                Url = $"{opt.CustomCdn}/{key}"
            };
        }

        /// <summary>
        /// Delete file from local storage
        /// </summary>
        public async Task DeleteAsync(bool isDeleteMedia = false, params string[] keys)
        {
            foreach (var key in keys)
            {
                var filePath = GetFilePath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.Information("File deleted: {FilePath}", filePath);
                }
                else
                {
                    _logger.Warning("File not found: {FilePath}", filePath);
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Get file stream
        /// </summary>
        public Stream GetObject(string key)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.Error("File not found: {FilePath}", filePath);
                throw new FileNotFoundException("File not found", key);
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// Get file stream and content type
        /// </summary>
        public Stream GetObject(string key, out string contentType)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.Error("File not found: {FilePath}", filePath);
                throw new FileNotFoundException("File not found", key);
            }

            contentType = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(filePath));
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "image/png";
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// Move file
        /// </summary>
        public async Task MoveAsync(string key, string newKey, bool isCopy = false)
        {
            var sourcePath = GetFilePath(key);
            var destinationPath = GetFilePath(newKey);

            if (!File.Exists(sourcePath))
            {
                _logger.Warning("Source file not found: {SourcePath}", sourcePath);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(destinationPath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            }

            if (isCopy)
            {
                File.Copy(sourcePath, destinationPath);
                _logger.Information("File copied to new location: {DestinationPath}", destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
                _logger.Information("File moved to new location: {DestinationPath}", destinationPath);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Check if file exists
        /// </summary>
        public async Task<bool> ExistsAsync(string key)
        {
            var filePath = GetFilePath(key);
            bool exists = File.Exists(filePath);
            _logger.Information("File existence status: {Key} - {Exists}", key, exists);
            return await Task.FromResult(exists);
        }

        /// <summary>
        /// Overwrite and save file
        /// </summary>
        public void Overwrite(Stream mediaBinaryStream, string key, string mimeType)
        {
            SaveAsync(mediaBinaryStream, key, mimeType);
        }

        /// <summary>
        /// Generate access path for local file (simulate signed URL)
        /// </summary>
        public Uri GetSignKey(string key, int minutes = 60)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found", key);
            }

            // Generate a simulated local URL (e.g., file:// local file path)
            return new Uri($"file://{filePath}");
        }

        /// <summary>
        /// Get full storage path for file
        /// </summary>
        private string GetFilePath(string key)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", key.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }
    }
}
