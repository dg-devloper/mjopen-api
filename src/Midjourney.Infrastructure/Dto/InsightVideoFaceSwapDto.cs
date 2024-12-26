﻿// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
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
using Microsoft.AspNetCore.Http;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 视频换脸 InsightFace 提交参数。
    /// </summary>
    public class InsightVideoFaceSwapDto : BaseSubmitDTO
    {
        /// <summary>
        /// 人脸源图片base64 或 URL
        /// </summary>
        public string SourceBase64 { get; set; }

        /// <summary>
        /// 人脸源图片URL 或 base64
        /// </summary>
        public string SourceUrl { get; set; }

        /// <summary>
        /// 目标文件 - 视频文件
        /// </summary>
        public IFormFile TargetFile { get; set; }

        /// <summary>
        /// 目标文件URL - 视频 URL
        /// </summary>
        public string TargetUrl { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }
    }
}