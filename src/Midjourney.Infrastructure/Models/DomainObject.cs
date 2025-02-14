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
using System.Runtime.Serialization;

using JsonIgnoreAttribute = Newtonsoft.Json.JsonIgnoreAttribute;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// Base domain object class, supporting extended properties and thread synchronization.
    /// </summary>
    //[DataContract] // 由于继承关系，不需要再次标记
    public class DomainObject : IBaseId // , ISerializable
    {
        [JsonIgnore]
        private readonly object _lock = new object();

        private Dictionary<string, object> _properties;

        /// <summary>
        /// Object ID.
        /// </summary>
        [DataMember]
        public string Id { get; set; }

        /// <summary>
        /// Put the current thread to sleep until awakened.
        /// </summary>
        public void Sleep()
        {
            lock (_lock)
            {
                Monitor.Wait(_lock);
            }
        }

        /// <summary>
        /// Wake up all threads waiting on this object's lock.
        /// </summary>
        public void Awake()
        {
            lock (_lock)
            {
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>
        /// Set an extended property.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <param name="value">Property value.</param>
        /// <returns>Current object instance.</returns>
        public DomainObject SetProperty(string name, object value)
        {
            Properties[name] = value;

            // 同时赋值将 Discord 实例 ID  = 频道 ID
            if (name == Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID)
            {
                Properties[Constants.TASK_PROPERTY_DISCORD_CHANNEL_ID] = value;
            }

            return this;
        }

        /// <summary>
        /// Remove an extended property.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Current object instance.</returns>
        public DomainObject RemoveProperty(string name)
        {
            Properties.Remove(name);
            return this;
        }

        /// <summary>
        /// Get the value of an extended property.
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Property value.</returns>
        public object GetProperty(string name)
        {
            Properties.TryGetValue(name, out var value);
            return value;
        }

        /// <summary>
        /// Get the value of an extended property as a generic type.
        /// </summary>
        /// <typeparam name="T">Property type.</typeparam>
        /// <param name="name">Property name.</param>
        /// <returns>Property value.</returns>
        public T GetPropertyGeneric<T>(string name)
        {
            return (T)GetProperty(name);
        }

        /// <summary>
        /// Get the value of an extended property with a default value.
        /// </summary>
        /// <typeparam name="T">Property type.</typeparam>
        /// <param name="name">Property name.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <returns>Property value or default value.</returns>
        public T GetProperty<T>(string name, T defaultValue)
        {
            // return Properties.TryGetValue(name, out var value) ? (T)value : defaultValue;

            if (Properties.TryGetValue(name, out var value))
            {
                try
                {
                    // 检查值是否是目标类型
                    if (value is T t)
                    {
                        return t; // 类型一致，直接返回
                    }

                    // 如果类型不一致，尝试强制转换
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (InvalidCastException)
                {
                    // 捕获转换异常，返回默认值
                    return defaultValue;
                }
                catch (FormatException)
                {
                    // 捕获格式异常，返回默认值
                    return defaultValue;
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Id", Id);
            info.AddValue("Properties", Properties);
        }

        /// <summary>
        /// Get or initialize the extended properties dictionary.
        /// </summary>
        //[JsonIgnore]
        public Dictionary<string, object> Properties
        {
            get => _properties ??= new Dictionary<string, object>();
            set => _properties = value;
        }

        /// <summary>
        /// Clone this object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Clone<T>()
        {
            return (T)MemberwiseClone();
        }
    }
}