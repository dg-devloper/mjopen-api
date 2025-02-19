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

using LiteDB;
using Midjourney.Infrastructure.Data;
using MongoDB.Driver.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Net;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure
{
    public static class Extensions
    {
        private static readonly char[] PathSeparator = ['/'];

        /// <summary>
        /// Remove leading and trailing ' ', '/', '\'
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string TrimPath(this string path)
        {
            return path?.Trim().Trim('/').Trim('\\').Trim('/').Trim();
        }

        /// <summary>
        /// Convert string to int
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int ToInt(this string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value?.Trim(), out int v))
            {
                return v;
            }
            return default;
        }

        /// <summary>
        /// Convert string to long
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long ToInt64(this string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && long.TryParse(value?.Trim(), out long v))
            {
                return v;
            }
            return default;
        }

        /// <summary>
        /// Remove whitespace characters, URLs, etc., keeping only the prompt for comparison
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static string FormatPrompt(this string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            // Remove <url>, e.g., <https://www.baidu.com> a cute girl -> acutegirl
            // Remove URL, e.g., https://www.baidu.com a cute girl -> acutegirl
            // Remove whitespace characters, e.g., a cute girl -> acutegirl

            // Fix -> v6.0 issue
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1:1--v6.0--iw2
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1: 1--v6--iw2

            str = GetPrimaryPrompt(str);

            return Regex.Replace(str, @"<[^>]*>|https?://\S+|\s+|\p{P}", "").ToLower();
        }

        /// <summary>
        /// Get formatted prompt for comparison
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        private static string GetPrimaryPrompt(string prompt)
        {
            // Remove parameters starting with --
            prompt = Regex.Replace(prompt, @"\x20+--[a-z]+.*$", string.Empty, RegexOptions.IgnoreCase);

            // Match and replace URL
            string regex = @"https?://[-a-zA-Z0-9+&@#/%?=~_|!:,.;]*[-a-zA-Z0-9+&@#/%=~_|]";
            prompt = Regex.Replace(prompt, regex, "<link>");

            // Replace redundant <<link>> with <link>
            // For " -- " discord will return empty
            return prompt.Replace("<<link>>", "<link>")
                .Replace(" -- ", " ")
                .Replace("  ", " ");
        }

        /// <summary>
        /// Format to keep only plain text and links (remove -- parameters)
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public static string FormatPromptParam(this string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // Remove <url>, e.g., <https://www.baidu.com> a cute girl -> <https://www.baidu.com>acutegirl
            // Remove URL, e.g., https://www.baidu.com a cute girl ->  https://www.baidu.comacutegirl
            // Remove whitespace characters, e.g., a cute girl -> acutegirl

            // Fix -> v6.0 issue
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1:1--v6.0--iw2
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1: 1--v6--iw2

            // Remove parameters starting with --
            prompt = Regex.Replace(prompt, @"\x20+--[a-z]+.*$", string.Empty, RegexOptions.IgnoreCase);


            // Replace redundant <<link>> with <link>
            // For " -- " discord will return empty
            prompt = prompt.Replace(" -- ", " ").Replace("  ", " ");
            return Regex.Replace(prompt, @"\s+|\p{P}", "").ToLower();
        }

        /// <summary>
        /// Convert to URL path
        /// e.g., from E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string ToUrlPath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            // Replace all backslashes with slashes
            // Split path, remove empty strings, then rejoin
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// Decompose the full path into a list of sub-paths
        /// e.g., /a/b/c -> [a, b, c]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string[] ToSubPaths(this string path)
        {
            return path?.ToUrlPath().Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        }

        /// <summary>
        /// Convert to URL path
        /// e.g., from E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removePrefix">Prefix to remove</param>
        /// <returns></returns>
        public static string TrimPrefix(this string path, string removePrefix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removePrefix))
            {
                if (path.StartsWith(removePrefix))
                {
                    path = path.Substring(removePrefix.Length);
                }
            }

            // Replace all backslashes with slashes
            // Split path, remove empty strings, then rejoin
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// Remove the suffix from the specified path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removeSuffix"></param>
        /// <returns></returns>
        public static string TrimSuffix(this string path, string removeSuffix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removeSuffix))
            {
                if (path.EndsWith(removeSuffix))
                {
                    path = path.Substring(0, path.Length - removeSuffix.Length);
                }
            }

            return path;
        }

        /// <summary>
        /// Get enum description or name
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum value)
        {
            if (value == null)
            {
                return null;
            }
            var type = value.GetType();
            var displayName = Enum.GetName(type, value);
            var fieldInfo = type.GetField(displayName);
            var attributes = (DisplayAttribute[])fieldInfo?.GetCustomAttributes(typeof(DisplayAttribute), false);
            if (attributes?.Length > 0)
            {
                displayName = attributes[0].Description ?? attributes[0].Name;
            }
            else
            {
                var desAttributes = (DescriptionAttribute[])fieldInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (desAttributes?.Length > 0)
                    displayName = desAttributes[0].Description;
            }
            return displayName;
        }

        /// <summary>
        /// Calculate the hash value of the data stream and return a hexadecimal string
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static string ToHex(this byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// Format file size
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static string ToFileSizeString(this double size)
        {
            if (size >= 1024 * 1024 * 1024)
            {
                return $"{size / 1024 / 1024 / 1024:F2} GB";
            }
            else if (size >= 1024 * 1024)
            {
                return $"{size / 1024 / 1024:F2} MB";
            }
            else if (size >= 1024)
            {
                return $"{size / 1024:F2} KB";
            }
            else
            {
                return $"{size:F2} B";
            }
        }

        /// <summary>
        /// Dynamically add query conditions based on multiple conditions, and support sorting and limiting the number of returned records.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="dataHelper">Data helper interface.</param>
        /// <param name="orderBy">Sorting field expression.</param>
        /// <param name="orderByAsc">Whether to sort in ascending order.</param>
        /// <param name="limit">Maximum number of records to return.</param>
        /// <param name="filters">A set of condition expressions and their corresponding boolean values.</param>
        /// <returns>List of entities that meet the conditions.</returns>
        public static List<T> WhereIf<T>(this IDataHelper<T> dataHelper, params (bool condition, Expression<Func<T, bool>> filter)[] filters) where T : IBaseId
        {
            // Get the initial query for all data
            var query = dataHelper.GetAll().AsQueryable();

            // Dynamically apply conditions
            foreach (var (condition, filter) in filters)
            {
                if (condition)
                {
                    query = query.Where(filter);
                }
            }

            return query.ToList();
        }

        /// <summary>
        /// Query condition extension
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// Lite DB query condition extension
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static ILiteQueryable<T> WhereIf<T>(this ILiteQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// MongoDB query condition extension method.
        /// Dynamically add query conditions based on conditions.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="query">MongoDB queryable object.</param>
        /// <param name="condition">Boolean value of the condition, determining whether to add the query condition.</param>
        /// <param name="predicate">Query condition expression to add.</param>
        /// <returns>Queryable object with optional conditions.</returns>
        public static IMongoQueryable<T> WhereIf<T>(this IMongoQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// Query condition extension
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// Query condition extension
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, int, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// Convert to visualized time
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static string ToDateTimeString(this long timestamp)
        {
            return timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        /// <summary>
        /// String to long time unix
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long ToLong(this string value)
        {
            return long.TryParse(value, out long result) ? result : 0;
        }

        /// <summary>
        /// Time slot input parsing
        /// Format is "HH:mm-HH:mm, HH:mm-HH:mm, ...", e.g., "09:00-17:00, 18:00-22:00"
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static List<TimeSlot> ToTimeSlots(this string input)
        {
            var timeSlots = new List<TimeSlot>();
            var slots = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var slot in slots)
            {
                var times = slot.Trim().Split('-');
                if (times.Length == 2 && TimeSpan.TryParse(times[0], out var start) && TimeSpan.TryParse(times[1], out var end))
                {
                    timeSlots.Add(new TimeSlot { Start = start, End = end });
                }
            }

            return timeSlots;
        }

        /// <summary>
        /// Determine if it is within working hours (if no value, default: true)
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool IsInWorkTime(this DateTime dateTime, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            var currentTime = dateTime.TimeOfDay;
            var ts = input.ToTimeSlots();
            foreach (var slot in ts)
            {
                if (slot.Start <= slot.End)
                {
                    // Normal time slot: e.g., 09:00-17:00
                    if (currentTime >= slot.Start && currentTime <= slot.End)
                    {
                        return true;
                    }
                }
                else
                {
                    // Time slot crossing midnight: e.g., 23:00-02:00
                    if (currentTime >= slot.Start || currentTime <= slot.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Determine if it is within fish time (if no value, default: false)
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool IsInFishTime(this DateTime dateTime, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var currentTime = dateTime.TimeOfDay;

            var ts = input.ToTimeSlots();
            foreach (var slot in ts)
            {
                if (slot.Start <= slot.End)
                {
                    // Normal time slot: e.g., 09:00-17:00
                    if (currentTime >= slot.Start && currentTime <= slot.End)
                    {
                        return true;
                    }
                }
                else
                {
                    // Time slot crossing midnight: e.g., 23:00-02:00
                    if (currentTime >= slot.Start || currentTime <= slot.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Sorting condition extension
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="where"></param>
        /// <param name="keySelector"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
        public static ILiteQueryable<T> OrderByIf<T>(this ILiteQueryable<T> query, bool where, Expression<Func<T, object>> keySelector, bool desc = true)
        {
            if (desc)
            {
                return where ? query.OrderByDescending(keySelector) : query;
            }
            else
            {
                return where ? query.OrderBy(keySelector) : query;
            }
        }

        /// <summary>
        /// URL add processing style
        /// </summary>
        /// <param name="url"></param>
        /// <param name="style"></param>
        /// <returns></returns>
        public static string ToStyle(this string url, string style)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            url = WebUtility.HtmlDecode(url);

            if (string.IsNullOrWhiteSpace(style))
            {
                return url;
            }

            if (url.IndexOf('?') > 0)
            {
                return url + "&" + style;
            }

            return url + "?" + style;
        }
    }

    /// <summary>
    /// Time slot parsing
    /// </summary>
    public class TimeSlot
    {
        /// <summary>
        /// Start time of the day
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// End time of the day
        /// </summary>
        public TimeSpan End { get; set; }
    }
}