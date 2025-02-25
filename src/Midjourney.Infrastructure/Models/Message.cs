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
namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// General message class for encapsulating return results.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// Status code.
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; }

        protected Message(int code, string description)
        {
            Code = code;
            Description = description;
        }

        /// <summary>
        /// Returns a success message.
        /// </summary>
        public static Message Success() => new Message(ReturnCode.SUCCESS, "Success");

        /// <summary>
        /// Returns a success message.
        /// </summary>
        public static Message Success(string message) => new Message(ReturnCode.SUCCESS, message);

        /// <summary>
        /// Returns a not found message.
        /// </summary>
        public static Message NotFound() => new Message(ReturnCode.NOT_FOUND, "Data not found");

        /// <summary>
        /// Returns a validation error message.
        /// </summary>
        public static Message ValidationError() => new Message(ReturnCode.VALIDATION_ERROR, "Validation error");

        /// <summary>
        /// Returns a system error message.
        /// </summary>
        public static Message Failure() => new Message(ReturnCode.FAILURE, "System error");

        /// <summary>
        /// Returns a system error message with a custom description.
        /// </summary>
        public static Message Failure(string description) => new Message(ReturnCode.FAILURE, description);

        /// <summary>
        /// Returns a message with a custom status code and description.
        /// </summary>
        public static Message Of(int code, string description) => new Message(code, description);
    }

    /// <summary>
    /// General message class for encapsulating return results.
    /// </summary>
    /// <typeparam name="T">Message type.</typeparam>
    public class Message<T> : Message
    {
        /// <summary>
        /// Return result.
        /// </summary>
        public T Result { get; }

        protected Message(int code, string description, T result = default)
            : base(code, description)
        {
            Result = result;
        }

        /// <summary>
        /// Returns a success message.
        /// </summary>
        /// <param name="result">Result.</param>
        public static Message<T> Success(T result) => new Message<T>(ReturnCode.SUCCESS, "Success", result);

        /// <summary>
        /// Returns a success message with a custom status code and description.
        /// </summary>
        public static Message<T> Success(int code, string description, T result) => new Message<T>(code, description, result);

        /// <summary>
        /// Returns a message with a custom status code, description, and result.
        /// </summary>
        public static Message<T> Of(int code, string description, T result) => new Message<T>(code, description, result);
    }
}