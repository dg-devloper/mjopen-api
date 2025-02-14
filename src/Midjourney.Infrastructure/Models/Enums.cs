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
    /// User status
    /// </summary>
    public enum EUserStatus
    {
        /// <summary>
        /// Normal
        /// </summary>
        NORMAL = 0,

        /// <summary>
        /// Disabled
        /// </summary>
        DISABLED = 1
    }

    /// <summary>
    /// User role
    /// </summary>
    public enum EUserRole
    {
        /// <summary>
        /// Regular user
        /// </summary>
        USER = 0,

        /// <summary>
        /// Administrator
        /// </summary>
        ADMIN = 1,
    }

    /// <summary>
    /// Bot type enumeration
    /// </summary>
    public enum EBotType
    {
        /// <summary>
        /// Midjourney
        /// </summary>
        MID_JOURNEY = 0,

        /// <summary>
        /// Niji
        /// </summary>
        NIJI_JOURNEY = 1,

        /// <summary>
        /// Face swap
        /// </summary>
        INSIGHT_FACE = 2
    }

    /// <summary>
    /// Account selection rule
    /// </summary>
    public enum AccountChooseRule
    {
        /// <summary>
        /// Optimal idle mode
        /// </summary>
        BestWaitIdle = 0,

        /// <summary>
        /// Random mode
        /// </summary>
        Random = 1,

        /// <summary>
        /// Weight mode
        /// </summary>
        Weight = 2,

        /// <summary>
        /// Polling mode
        /// </summary>
        Polling = 3
    }

    public enum TranslateWay
    {
        /// <summary>
        /// Baidu Translate
        /// </summary>
        BAIDU,

        /// <summary>
        /// GPT Translate
        /// </summary>
        GPT,

        /// <summary>
        /// No translation
        /// </summary>
        NULL
    }

    /// <summary>
    /// Image storage type
    /// </summary>
    public enum ImageStorageType
    {
        /// <summary>
        /// Do not store
        /// </summary>
        NONE = 0,

        /// <summary>
        /// Local store
        /// </summary>
        LOCAL = 1,

        /// <summary>
        /// Alibaba Cloud OSS
        /// </summary>
        OSS = 2,

        /// <summary>
        /// Tencent Cloud COS
        /// </summary>
        COS = 3,

        /// <summary>
        /// Cloudflare R2
        /// </summary>
        R2 = 4,
    }

    /// <summary>
    /// Task status enumeration.
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// Not started.
        /// </summary>
        NOT_START = 0,

        /// <summary>
        /// Submitted.
        /// </summary>
        SUBMITTED = 1,

        /// <summary>
        /// In progress.
        /// </summary>
        IN_PROGRESS = 3,

        /// <summary>
        /// Failed.
        /// </summary>
        FAILURE = 4,

        /// <summary>
        /// Succeeded.
        /// </summary>
        SUCCESS = 5,

        /// <summary>
        /// Modal
        /// </summary>
        MODAL = 6,

        /// <summary>
        /// Cancelled
        /// </summary>
        CANCEL = 7
    }

    public static class TaskStatusExtensions
    {
        public static int GetOrder(this TaskStatus status)
        {
            // This method should return an integer that represents the order of the status
            // Replace the following line with the actual implementation
            return status switch
            {
                TaskStatus.NOT_START => 0,
                TaskStatus.SUBMITTED => 1,
                TaskStatus.IN_PROGRESS => 3,
                TaskStatus.FAILURE => 4,
                TaskStatus.SUCCESS => 5,
                _ => 0
            };
        }
    }

    /// <summary>
    /// Task action enumeration.
    /// </summary>
    public enum TaskAction
    {
        /// <summary>
        /// Generate image.
        /// </summary>
        IMAGINE,

        /// <summary>
        /// Upscale the selected image.
        /// </summary>
        UPSCALE,

        /// <summary>
        /// Select one of the images and generate four similar ones.
        /// </summary>
        VARIATION,

        /// <summary>
        /// Rerun.
        /// </summary>
        REROLL,

        /// <summary>
        /// Image to prompt.
        /// </summary>
        DESCRIBE,

        /// <summary>
        /// Blend multiple images.
        /// </summary>
        BLEND,

        /// <summary>
        /// Submit action
        /// </summary>
        ACTION,

        /// <summary>
        /// Pan
        /// </summary>
        PAN,

        /// <summary>
        /// Outpaint
        /// </summary>
        OUTPAINT,

        /// <summary>
        /// Inpaint
        /// </summary>
        INPAINT,

        /// <summary>
        /// Custom zoom
        /// </summary>
        ZOOM,

        /// <summary>
        /// SHOW command
        /// </summary>
        SHOW,

        /// <summary>
        /// Short prompt instruction
        /// </summary>
        SHORTEN,

        /// <summary>
        /// Face swap task
        /// </summary>
        SWAP_FACE,

        /// <summary>
        /// Video face swap task
        /// </summary>
        SWAP_VIDEO_FACE
    }

    /// <summary>
    /// Message type enumeration.
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Create.
        /// </summary>
        CREATE,

        /// <summary>
        /// Update.
        /// </summary>
        UPDATE,

        /// <summary>
        /// Delete.
        /// </summary>
        DELETE,

        /// <summary>
        ///
        /// </summary>
        INTERACTION_CREATE,

        /// <summary>
        ///
        /// </summary>
        INTERACTION_SUCCESS,

        /// <summary>
        ///
        /// </summary>
        INTERACTION_IFRAME_MODAL_CREATE,

        /// <summary>
        ///
        /// </summary>
        INTERACTION_MODAL_CREATE
    }

    /// <summary>
    /// Generation speed mode enumeration.
    /// </summary>
    public enum GenerationSpeedMode
    {
        RELAX,
        FAST,
        TURBO
    }

    public static class MessageTypeExtensions
    {
        /// <summary>
        /// Converts a string to the corresponding message type enumeration.
        /// </summary>
        /// <param name="type">Message type string.</param>
        /// <returns>The corresponding message type enumeration.</returns>
        public static MessageType? Of(string type)
        {
            return type switch
            {
                "MESSAGE_CREATE" => MessageType.CREATE,
                "MESSAGE_UPDATE" => MessageType.UPDATE,
                "MESSAGE_DELETE" => MessageType.DELETE,
                "INTERACTION_CREATE" => MessageType.INTERACTION_CREATE,
                "INTERACTION_SUCCESS" => MessageType.INTERACTION_SUCCESS,
                "INTERACTION_IFRAME_MODAL_CREATE" => MessageType.INTERACTION_IFRAME_MODAL_CREATE,
                "INTERACTION_MODAL_CREATE" => MessageType.INTERACTION_MODAL_CREATE,
                _ => null
            };
        }
    }

    /// <summary>
    /// Image blending dimension enumeration.
    /// </summary>
    public enum BlendDimensions
    {
        /// <summary>
        /// Portrait.
        /// </summary>
        PORTRAIT,

        /// <summary>
        /// Square.
        /// </summary>
        SQUARE,

        /// <summary>
        /// Landscape.
        /// </summary>
        LANDSCAPE
    }

    public static class BlendDimensionsExtensions
    {
        /// <summary>
        /// Get the string value of the image blending dimension.
        /// </summary>
        /// <param name="dimension">Image blending dimension.</param>
        /// <returns>The string value of the image blending dimension.</returns>
        public static string GetValue(this BlendDimensions dimension)
        {
            return dimension switch
            {
                BlendDimensions.PORTRAIT => "2:3",
                BlendDimensions.SQUARE => "1:1",
                BlendDimensions.LANDSCAPE => "3:2",
                _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
            };
        }
    }
}