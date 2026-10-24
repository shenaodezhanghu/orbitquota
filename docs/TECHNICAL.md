# Codex Island

Windows 顶部悬浮工具，原生 WPF；不使用 Electron、浏览器或 Python 服务。

## 启动

双击 **启动.vbs**，或运行 `bin/Release/net7.0-windows/CodexIsland.exe`。

源码首次构建需要 .NET SDK。当前二进制以 net7.0-windows 编译、配置 LatestMajor，已验证在本机 .NET 10.0.11 Desktop Runtime 上运行。其他电脑建议安装受支持的 .NET 10 Desktop Runtime。

## 日月轨道版

收起时是透明底的二维同心圆小景，不是胶囊。深蓝暗衬保证浅色壁纸上也能辨认，暖金太阳、冷白月亮和稀疏固定星点由 WPF 绘制；地球采用本地生成并缓存的小尺寸平面贴图，无外部图片、视频或网页引擎。

- 太阳沿外圈走完一周窗口，光芒亮起的比例表示周剩余额度。
- 月亮沿内圈走完五小时窗口，月面亮起的面积表示五小时剩余额度。地球位于共同圆心。
- 位置计算：`clamp(1 - (重置时间 - 当前时间) / 窗口秒数, 0, 1)`。两条正面同心圆轨道都从12点方向起步，顺时针匀速运行，各自按真实窗口计算。不做倾斜、透视、前后遮挡，不跟实际天文昼夜同步。
- 地球是二维圆面：明暗分界随太阳的平面方位转动，日面明亮、夜面较暗且有少量灯光；无球面体积明暗或高光。月相仍表示额度，不模拟实际天文月相。
- 星体位置在本机持续计算，额度仍每120秒查询。默认250ms刷新小景，微弱光效不改变星体位置；减少动效时冻结光芒变化、每秒更新真实位置。
- 若重置时刻已过但未收到服务端新窗口，停在右端等待；不自行循环或假造额度恢复。没有某个窗口数据时不绘制该星体的虚构位置。旧数据会变淡。
- 最近一分钟有输出记录时，小景下方显示 `tokens /min`；输出停止60秒后消失，不替换星体。它是最近60秒输出量，不是模型解码速度或请求级TTFT。
- 悬停显示额度小标记；点击展开“额度 / 用量”分页，再点空白回到小景。图景在展开时保留。
- 拖动小景移动位置，松开自动保存，支持多显示器；右键恢复顶部居中、刷新、统计说明、隐藏到托盘或退出。托盘左键重新打开。
- 旧圆环外观已不作为这版入口，既有位置和动效偏好继续兼容。

## 固定日用量

用量页始终展示本地自然日00:00–24:00，24个固定小时槽，不横向移动、不轮播、不滚动时间窗。柱形表示各小时输出Token；大数字为当天所有Token。未来小时留空，已有小时按真实记录累计。

“今日平均输出 /min” = 今日输出Token / 今日已过分钟（分母至少1分钟），包含当天空闲时间；与小景的最近一分钟速率分开，不混作实时解码速率。跨天自动归零。

## 费用自动计算

无需选择模型或填写价格。读取每条请求的模型信息：优先响应记录自带的 model，否则使用该请求所在 turn_context 的 model。会话中切换模型后，各条请求独立匹配价格，不会按最后一个模型回算整个会话。

美元指标是 **标准 API 等价费用**，不是 ChatGPT 订阅实际扣费，也不是剩余额度的美元换算。输入、缓存输入、输出分别计价；缓存输入包含在总输入中，不重复计费。已实现 GPT-6.1 Sol、GPT-6 Sol、GPT-5.6 Luna 超 272K 输入时的长上下文倍率。统一按标准 API 档比较，不冒充账户实际 Fast/Ultrafast、地区、工具等完整账单。

内置价格快照于 **2026-10-22** 核验，覆盖当前常见公开模型和本机出现的公开模型，来源：

- [官方价格表](https://developers.openai.com/api/docs/pricing)
- [GPT-6.1 Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol)
- [GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol)
- [GPT-5.6 Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)

未公开定价的内部模型（例如 codex-auto-review）不会猜测价格，也不会视为免费。存在这类记录时显示“API 等价 · 部分”和 ≥ 金额；右键统计说明可看未计价记录数量。全部都无法计价时显示 —。价格快照不会后台联网自动改写，官方调价后应更新 PriceCatalog.cs。设置文件中的 Prices 仍可供开发者覆盖，但日常使用不需要配置。

## 统计口径

- 额度：每 120 秒通过本机 `codex app-server` 的 `account/rateLimits/read` 读取。按窗口长度识别，非固定 primary/secondary 顺序；额度属于账户，覆盖所有设备。
- 离线：回退到本机日志快照，界面显示离线 / 旧快照；缺失额度显示 —，不伪装成 0%。重置时间在右键统计说明中查看。
- 今日 Token：本机 sessions、archived_sessions 中本地自然日的记录，包含子代理。不是账户全设备 Token。
- 小景 /min：最近60秒日志记录的输出Token总数；用量页 /min：今日输出除以今日已过分钟；两者包含等待，不是模型实时解码速度。
- TTFT：当前日志没有完整计时数据，所以不显示。
- 当前模型：最近活跃主会话，后台子代理不会抢占标题。

日志每 2 秒增量读取。优先使用 token_usage_record，按 response_id 去重，支持半行写入。旧日志只有累计计数时，首条只作基线，可能少计首条；日志格式不是稳定公开接口，轮转、删除、迁移及格式变化可能导致不完整。

复用 Codex 已有登录态，不直接读取或展示 auth.json，不发送推理请求、不创建会话、不消耗推理 Token。官方进程进行额度所需的正常认证/网络请求。查询 20 秒超时会清理，工具退出时取消查询。无 HTTP 服务，不上传会话文本，不修改业务项目，不自动配置开机启动。

## 配置和验证

settings.json 保存在程序旁（源码运行时位于 bin/Release/net7.0-windows/）。位置、显示器、动效偏好自动保存；高级配置 CodexHome/CodexExe 修改后重启生效。

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.build'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build -c Release
Start-Process .\bin\Release\net7.0-windows\CodexIsland.exe -ArgumentList '--self-test' -Wait
```

--self-test 覆盖额度分类、缺失额度、计价、模型切换、去重、追加、日期过滤等。结果写 diagnostics/self-test.txt，失败写 self-test-error.txt。

--capture 验证日月小景、速览、展开、两页切换、固定日用量、收起、禁用 ToolTip、位置移动和设置渲染，保存截图后退出；请先退出常驻实例。--demo 是明确标记虚构数据的界面演示，正常运行不使用示例数值。

实现参考 [CodexBar](https://github.com/jspann21/codex-bar) 的本机额度读取思路、[DynamicWin](https://github.com/FlorianButz/DynamicWin) 的形态、[官方 App Server 文档](https://learn.chatgpt.com/docs/app-server)；窗口和逻辑独立编写，未复制上述项目代码。

## 视觉参考

本次从 [Imaginary Forces 的 Foundation 第二季片头项目](https://www.beta.imaginaryforces.com/project/foundation-season-2) 和 [NASA Eyes 可视化介绍](https://science.nasa.gov/eyes/museums/) 中寻找天体、光色和轨道表达方向。未复制影视素材，场景是独立实现的简化矢量仪表，不是天文模拟器。
