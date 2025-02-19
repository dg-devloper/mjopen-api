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

using Midjourney.Infrastructure.Dto;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord Load Balancer.
    /// </summary>
    public class DiscordLoadBalancer
    {
        private readonly IRule _rule;
        private readonly HashSet<DiscordInstance> _instances = [];

        public DiscordLoadBalancer(IRule rule)
        {
            _rule = rule;
        }

        /// <summary>
        /// Get all instances.
        /// </summary>
        /// <returns>List of all instances.</returns>
        public List<DiscordInstance> GetAllInstances() => _instances.ToList();

        /// <summary>
        /// Get alive instances.
        /// </summary>
        /// <returns>List of alive instances.</returns>
        public List<DiscordInstance> GetAliveInstances() =>
            _instances.Where(c => c != null && c.IsAlive == true).Where(c => c != null).ToList() ?? [];

        /// <summary>
        /// Choose an instance.
        /// </summary>
        /// <returns>Chosen instance.</returns>
        /// <param name="accountFilter"></param>
        /// <param name="isNewTask">Filter instances that only accept new tasks</param>
        /// <param name="botType">Filter accounts that have specified bot enabled</param>
        /// <param name="blend">Filter accounts that support Blend</param>
        /// <param name="describe">Filter accounts that support Describe</param>
        /// <param name="isDomain">Filter vertical domain accounts</param>
        /// <param name="domainIds">Filter vertical domain IDs</param>
        /// <param name="ids">Specify account IDs</param>
        /// <param name="shorten"></param>
        public DiscordInstance ChooseInstance(
            AccountFilter accountFilter = null,
            bool? isNewTask = null,
            EBotType? botType = null,
            bool? blend = null,
            bool? describe = null,
            bool? isDomain = null,
            List<string> domainIds = null,
            List<string> ids = null,
            bool? shorten = null)
        {
            if (!string.IsNullOrWhiteSpace(accountFilter?.InstanceId))
            {
                // Get instance by specified ID
                var model = GetDiscordInstance(accountFilter.InstanceId);

                // If the specified instance is drawing
                // Check if it meets the filter criteria
                if (model != null)
                {
                    // If filtering niji journey accounts, but the account has not enabled niji journey, it does not meet the criteria
                    if (botType == EBotType.NIJI_JOURNEY && model.Account.EnableNiji != true)
                    {
                        return null;
                    }

                    // If filtering mid journey accounts, but the account has not enabled mid journey, it does not meet the criteria
                    if (botType == EBotType.MID_JOURNEY && model.Account.EnableMj != true)
                    {
                        return null;
                    }

                    // If filtering speed mode, but the account has not set speed mode or is not in the filter list, it does not meet the criteria
                    if (accountFilter.Modes.Count > 0 && model.Account.Mode != null && !accountFilter.Modes.Contains(model.Account.Mode.Value))
                    {
                        return null;
                    }

                    // If filtering remix = true, but the account has not enabled remix or remix is set to auto-submit, it does not meet the criteria
                    if (accountFilter.Remix == true && (model.Account.MjRemixOn != true || model.Account.RemixAutoSubmit))
                    {
                        return null;
                    }

                    // If filtering remix = false, but the account has enabled remix and remix is not set to auto-submit, it does not meet the criteria
                    if (accountFilter.Remix == false && model.Account.MjRemixOn == true && !model.Account.RemixAutoSubmit)
                    {
                        return null;
                    }

                    // If filtering niji remix = true, but the account has not enabled niji remix or niji remix is set to auto-submit, it does not meet the criteria
                    if (accountFilter.NijiRemix == true && (model.Account.NijiRemixOn != true || model.Account.RemixAutoSubmit))
                    {
                        return null;
                    }

                    // If filtering niji remix = false, but the account has enabled niji remix and niji remix is not set to auto-submit, it does not meet the criteria
                    if (accountFilter.NijiRemix == false && model.Account.NijiRemixOn == true && !model.Account.RemixAutoSubmit)
                    {
                        return null;
                    }

                    // If filtering remix auto-submit, it does not meet the criteria
                    if (accountFilter.RemixAutoConsidered.HasValue && model.Account.RemixAutoSubmit != accountFilter.RemixAutoConsidered)
                    {
                        return null;
                    }

                    // If filtering instances that only accept new tasks, but the instance does not accept new tasks, it does not meet the criteria
                    if (isNewTask == true && model.Account.IsAcceptNewTask != true)
                    {
                        return null;
                    }

                    // If filtering accounts that support blend, but the account has not enabled blend, it does not meet the criteria
                    if (blend == true && model.Account.IsBlend != true)
                    {
                        return null;
                    }

                    // If filtering accounts that support describe, but the account has not enabled describe, it does not meet the criteria
                    if (describe == true && model.Account.IsDescribe != true)
                    {
                        return null;
                    }

                    // If filtering accounts that support shorten, but the account has not enabled shorten, it does not meet the criteria
                    if (shorten == true && model.Account.IsShorten != true)
                    {
                        return null;
                    }

                    // If filtering vertical domain accounts, but the account has not enabled vertical domain or does not have any matching vertical domain IDs, it does not meet the criteria
                    if (isDomain == true && (model.Account.IsVerticalDomain != true || !model.Account.VerticalDomainIds.Any(x => domainIds.Contains(x))))
                    {
                        return null;
                    }

                    // If filtering non-vertical domain accounts, but the account has enabled vertical domain, it does not meet the criteria
                    if (isDomain == false && model.Account.IsVerticalDomain == true)
                    {
                        return null;
                    }

                    // If specifying account IDs, but the account is not in the specified ID list, it does not meet the criteria
                    if (ids?.Count > 0 && !ids.Contains(model.Account.ChannelId))
                    {
                        return null;
                    }
                }

                return model;
            }
            else
            {
                var list = GetAliveInstances()

                    // Filter instances with idle queues
                    .Where(c => c.IsIdleQueue)

                    // Filter by specified speed mode
                    .WhereIf(accountFilter?.Modes.Count > 0, c => c.Account.Mode == null || accountFilter.Modes.Contains(c.Account.Mode.Value))

                    // Allow speed mode filtering
                    // Or have intersections
                    .WhereIf(accountFilter?.Modes.Count > 0, c => c.Account.AllowModes == null || c.Account.AllowModes.Count <= 0 || c.Account.AllowModes.Any(x => accountFilter.Modes.Contains(x)))

                    // If speed mode includes fast mode, filter out instances that do not support fast mode
                    .WhereIf(accountFilter?.Modes.Contains(GenerationSpeedMode.FAST) == true ||
                    accountFilter?.Modes.Contains(GenerationSpeedMode.TURBO) == true,
                    c => c.Account.FastExhausted == false)

                    // Midjourney Remix filtering
                    .WhereIf(accountFilter?.Remix == true, c => c.Account.MjRemixOn == accountFilter.Remix || !c.Account.RemixAutoSubmit)
                    .WhereIf(accountFilter?.Remix == false, c => c.Account.MjRemixOn == accountFilter.Remix)

                    // Niji Remix filtering
                    .WhereIf(accountFilter?.NijiRemix == true, c => c.Account.NijiRemixOn == accountFilter.NijiRemix || !c.Account.RemixAutoSubmit)
                    .WhereIf(accountFilter?.NijiRemix == false, c => c.Account.NijiRemixOn == accountFilter.NijiRemix)

                    // Remix auto-submit filtering
                    .WhereIf(accountFilter?.RemixAutoConsidered.HasValue == true, c => c.Account.RemixAutoSubmit == accountFilter.RemixAutoConsidered)

                    // Filter instances that only accept new tasks
                    .WhereIf(isNewTask == true, c => c.Account.IsAcceptNewTask == true)

                    // Filter accounts that have niji mj enabled
                    .WhereIf(botType == EBotType.NIJI_JOURNEY, c => c.Account.EnableNiji == true)
                    .WhereIf(botType == EBotType.MID_JOURNEY, c => c.Account.EnableMj == true)

                    // Filter accounts that have features enabled
                    .WhereIf(blend == true, c => c.Account.IsBlend)
                    .WhereIf(describe == true, c => c.Account.IsDescribe)
                    .WhereIf(shorten == true, c => c.Account.IsShorten)

                    // Domain filtering
                    .WhereIf(isDomain == true && domainIds?.Count > 0, c => c.Account.IsVerticalDomain && c.Account.VerticalDomainIds.Any(x => domainIds.Contains(x)))
                    .WhereIf(isDomain == false, c => c.Account.IsVerticalDomain != true)

                    // Filter specified accounts
                    .WhereIf(ids?.Count > 0, c => ids.Contains(c.Account.ChannelId))
                    .ToList();

                return _rule.Choose(list);
            }
        }

        /// <summary>
        /// Get instance by specified ID (regardless of whether it is alive)
        /// </summary>
        /// <param name="channelId">Instance ID/Channel ID</param>
        /// <returns>Instance.</returns>
        public DiscordInstance GetDiscordInstance(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId);
        }

        /// <summary>
        /// Get instance by specified ID (must be alive)
        /// </summary>
        /// <param name="channelId">Instance ID/Channel ID</param>
        /// <returns>Instance.</returns>
        public DiscordInstance GetDiscordInstanceIsAlive(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId && c.IsAlive);
        }

        /// <summary>
        /// Get the set of queued task IDs.
        /// </summary>
        /// <returns>Set of queued task IDs.</returns>
        public HashSet<string> GetQueueTaskIds()
        {
            var taskIds = new HashSet<string>();
            foreach (var instance in GetAliveInstances())
            {
                foreach (var taskId in instance.GetRunningFutures().Keys)
                {
                    taskIds.Add(taskId);
                }
            }
            return taskIds;
        }

        /// <summary>
        /// Get the list of queued tasks.
        /// </summary>
        /// <returns>List of queued tasks.</returns>
        public List<TaskInfo> GetQueueTasks()
        {
            var tasks = new List<TaskInfo>();

            var ins = GetAliveInstances();
            if (ins?.Count > 0)
            {
                foreach (var instance in ins)
                {
                    var ts = instance.GetQueueTasks();
                    if (ts?.Count > 0)
                    {
                        tasks.AddRange(ts);
                    }
                }
            }

            return tasks;
        }

        /// <summary>
        /// Add a Discord instance
        /// </summary>
        /// <param name="instance"></param>
        public void AddInstance(DiscordInstance instance) => _instances.Add(instance);

        /// <summary>
        /// Remove
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveInstance(DiscordInstance instance) => _instances.Remove(instance);
    }
}