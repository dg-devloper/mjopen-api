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

using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using System.Net;

using TaskStatus = Midjourney.Infrastructure.TaskStatus;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// Face Swap Controller
    /// </summary>
    [ApiController]
    [Route("mj/insight-face")]
    [Route("mj-fast/mj/insight-face")]
    [Route("mj-turbo/mj/insight-face")]
    [Route("mj-relax/mj/insight-face")]
    public class InsightFaceController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly string _ip;

        private readonly GenerationSpeedMode? _mode;
        private readonly WorkContext _workContext;
        private readonly FaceSwapInstance _faceSwapInstance;
        private readonly VideoFaceSwapInstance _videoFaceSwapInstance;

        public InsightFaceController(
            ILogger<InsightFaceController> logger,
            IHttpContextAccessor httpContextAccessor,
            WorkContext workContext,
            FaceSwapInstance faceSwapInstance,
            VideoFaceSwapInstance videoFaceSwapInstance)
        {
            _logger = logger;
            _workContext = workContext;

            var user = _workContext.GetUser();

            // If not in demo mode and guest access is not enabled, return 403 error if not logged in
            if (GlobalConfiguration.IsDemoMode != true && GlobalConfiguration.Setting.EnableGuest != true)
            {
                if (user == null)
                {
                    // If a regular user and not an anonymous controller, return 403
                    httpContextAccessor.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    httpContextAccessor.HttpContext.Response.WriteAsync("Not logged in");
                    return;
                }
            }

            _ip = httpContextAccessor.HttpContext.Request.GetIP();

            var mode = httpContextAccessor.HttpContext.Items["Mode"]?.ToString()?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _mode = mode switch
                {
                    "turbo" => GenerationSpeedMode.TURBO,
                    "relax" => GenerationSpeedMode.RELAX,
                    "fast" => GenerationSpeedMode.FAST,
                    _ => null
                };
            }

            _faceSwapInstance = faceSwapInstance;
            _videoFaceSwapInstance = videoFaceSwapInstance;
        }

        /// <summary>
        /// Submit face swap task (supports base64 or URL)
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("swap")]
        public async Task<ActionResult<SubmitResultVO>> FaceSwap([FromBody] InsightFaceSwapDto dto)
        {
            var repl = GlobalConfiguration.Setting.Replicate;

            if (repl?.EnableFaceSwap != true || string.IsNullOrWhiteSpace(repl.Token))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Service not supported"));
            }

            if (string.IsNullOrWhiteSpace(dto.SourceBase64) && string.IsNullOrWhiteSpace(dto.SourceUrl))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Source face image cannot be empty"));
            }

            if (string.IsNullOrWhiteSpace(dto.TargetBase64) && string.IsNullOrWhiteSpace(dto.TargetUrl))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Target image cannot be empty"));
            }

            if (!string.IsNullOrWhiteSpace(dto.SourceBase64) && dto.SourceBase64 == dto.TargetBase64)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Source face image and target image cannot be the same"));
            }

            if (!string.IsNullOrWhiteSpace(dto.SourceUrl) && dto.SourceUrl == dto.TargetUrl)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Source face image and target image cannot be the same"));
            }

            var task = NewTask(dto);
            task.Action = TaskAction.SWAP_FACE;
            task.BotType = EBotType.INSIGHT_FACE;

            NewTaskDoFilter(task, dto.AccountFilter);

            var data = await _faceSwapInstance.SubmitTask(dto, task);

            return Ok(data);
        }

        /// <summary>
        /// Submit video face swap task (supports base64 or URL)
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("video-swap")]
        public async Task<ActionResult<SubmitResultVO>> VideoFaceSwap([FromForm] InsightVideoFaceSwapDto dto)
        {
            var user = _workContext.GetUser();
            if (user == null || !user.IsWhite)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Due to national laws and security issues, video face swap is not open to guests. If needed, please contact the administrator"));
            }

            var repl = GlobalConfiguration.Setting.Replicate;

            if (repl?.EnableFaceSwap != true || string.IsNullOrWhiteSpace(repl.Token))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Service not supported"));
            }

            if (string.IsNullOrWhiteSpace(dto.SourceBase64) && string.IsNullOrWhiteSpace(dto.SourceUrl))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Source face image cannot be empty"));
            }

            if (dto.TargetFile == null && string.IsNullOrWhiteSpace(dto.TargetUrl))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "Target video cannot be empty"));
            }

            var task = NewTask(dto);
            task.Action = TaskAction.SWAP_VIDEO_FACE;
            task.BotType = EBotType.INSIGHT_FACE;

            NewTaskDoFilter(task, dto.AccountFilter);

            var data = await _videoFaceSwapInstance.SubmitTask(dto, task);

            return Ok(data);
        }


        /// <summary>
        /// Create a new task object
        /// </summary>
        /// <param name="baseDTO"></param>
        /// <returns></returns>
        private TaskInfo NewTask(BaseSubmitDTO baseDTO)
        {
            var user = _workContext.GetUser();

            var task = new TaskInfo
            {
                Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomUtils.RandomNumbers(3)}",
                SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State = baseDTO.State,
                Status = TaskStatus.NOT_START,
                ClientIp = _ip,
                Mode = _mode,
                UserId = user?.Id,
            };

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();

            // Calculate the number of drawings for the current IP today
            // If not a whitelist user, calculate IP drawing limit
            if (user == null || user.IsWhite != true)
            {
                if (GlobalConfiguration.Setting.GuestDefaultDayLimit > 0)
                {
                    var ipTodayDrawCount = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now && x.ClientIp == _ip);
                    if (ipTodayDrawCount > GlobalConfiguration.Setting.GuestDefaultDayLimit)
                    {
                        throw new LogicException("Today's drawing limit has been reached");
                    }
                }
            }

            // Calculate the number of drawings for the current user today
            if (!string.IsNullOrWhiteSpace(user?.Id))
            {
                if (user.DayDrawLimit > 0)
                {
                    var userTodayDrawCount = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now && x.UserId == user.Id);
                    if (userTodayDrawCount > user.DayDrawLimit)
                    {
                        throw new LogicException("Today's drawing limit has been reached");
                    }
                }
            }

            var properties = GlobalConfiguration.Setting;
            var notifyHook = string.IsNullOrWhiteSpace(baseDTO.NotifyHook) ? properties.NotifyHook : baseDTO.NotifyHook;
            task.SetProperty(Constants.TASK_PROPERTY_NOTIFY_HOOK, notifyHook);

            var nonce = SnowFlake.NextId();
            task.Nonce = nonce;

            task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

            return task;
        }

        /// <summary>
        /// Process account filter and mode
        /// </summary>
        /// <param name="task"></param>
        /// <param name="accountFilter"></param>
        private void NewTaskDoFilter(TaskInfo task, AccountFilter accountFilter)
        {
            task.AccountFilter = accountFilter;

            if (_mode != null)
            {
                if (task.AccountFilter == null)
                {
                    task.AccountFilter = new AccountFilter();
                }

                if (!task.AccountFilter.Modes.Contains(_mode.Value))
                {
                    task.AccountFilter.Modes.Add(_mode.Value);
                }
            }
        }
    }
}