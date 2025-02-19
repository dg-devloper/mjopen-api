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
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Storage;

namespace Midjourney.API
{
    public static class ServiceCollectionExtensions
    {
        public static void AddMidjourneyServices(this IServiceCollection services, ProxyProperties config)
        {
            // Register all handlers

            // Bot message handlers
            services.AddTransient<BotMessageHandler, BotErrorMessageHandler>();
            services.AddTransient<BotMessageHandler, BotImagineSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotRerollSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotStartAndProgressHandler>();
            services.AddTransient<BotMessageHandler, BotUpscaleSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotVariationSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotDescribeSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotActionSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotBlendSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotShowSuccessHandler>();

            // User message handlers
            services.AddTransient<UserMessageHandler, UserErrorMessageHandler>();
            services.AddTransient<UserMessageHandler, UserImagineSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserActionSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserUpscaleSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserBlendSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserDescribeSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserShowSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserVariationSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserStartAndProgressHandler>();
            services.AddTransient<UserMessageHandler, UserRerollSuccessHandler>();

            services.AddTransient<UserMessageHandler, UserShortenSuccessHandler>();

            // Face swap services
            services.AddSingleton<FaceSwapInstance>();
            services.AddSingleton<VideoFaceSwapInstance>();

            // Notification service
            services.AddSingleton<INotifyService, NotifyServiceImpl>();

            // Translation service
            if (config.TranslateWay == TranslateWay.GPT)
            {
                services.AddSingleton<ITranslateService, GPTTranslateService>();
            }
            else
            {
                services.AddSingleton<ITranslateService, BaiduTranslateService>();
            }

            // Storage service
            StorageHelper.Configure();

            // Storage service
            // In-memory
            //services.AddSingleton<ITaskStoreService, InMemoryTaskStoreServiceImpl>();
            // LiteDB
            services.AddSingleton<ITaskStoreService>(new TaskRepository());

            // Account load balancing service
            switch (config.AccountChooseRule)
            {
                case AccountChooseRule.BestWaitIdle:
                    services.AddSingleton<IRule, BestWaitIdleRule>();
                    break;
                case AccountChooseRule.Random:
                    services.AddSingleton<IRule, RandomRule>();
                    break;
                case AccountChooseRule.Weight:
                    services.AddSingleton<IRule, WeightRule>();
                    break;
                case AccountChooseRule.Polling:
                    services.AddSingleton<IRule, RoundRobinRule>();
                    break;
                default:
                    services.AddSingleton<IRule, BestWaitIdleRule>();
                    break;
            }

            // Discord load balancer
            services.AddSingleton<DiscordLoadBalancer>();

            // Discord account helper
            services.AddSingleton<DiscordAccountHelper>();

            // Discord helper
            services.AddSingleton<DiscordHelper>();

            // Task service
            services.AddSingleton<ITaskService, TaskService>();
        }
    }
}