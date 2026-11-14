# CD Ready

Windows 桌面音频 CD 准备工具：**按播放列表排序 → 清理曲名 → 输出编号 WAV → 校验完整性**。

保留原始文件，生成可交给旧版 Windows Media Player 等刻录软件的 **44.1 kHz / 16-bit / 双声道 PCM WAV**。程序本身不控制光驱、不自动刻盘。

## 下载与运行

从 [Releases](https://github.com/Tim-lanniao/CDReady/releases) 下载 Windows x64 压缩包，解压到可写文件夹，双击 `CDReady.exe`。

- Windows 10/11 x64，.NET Framework 4.8。
- 另行安装含 **libsoxr** 的 FFmpeg 和 FFprobe，例如 [FFmpeg 下载页](https://ffmpeg.org/download.html) 链接的 Windows full 构建。
- 点“设置工具”选择同时包含 `ffmpeg.exe` 和 `ffprobe.exe` 的文件夹。也支持同目录 `tools`、当前用户 Scoop FFmpeg 和 `PATH`。
- 本项目不附带 FFmpeg、模型权重、音乐文件或任何个人播放列表。

## 使用流程

1. 选择音频文件夹，只读取该文件夹本层的音频，避免重复处理子文件夹中的旧副本。
2. 默认优先沿用播放列表。支持 **WPL、M3U8、M3U、PLS**，在本层和下一级文件夹寻找列表；多个列表时可自行选择。列表只负责排列当前文件夹中的音频，不导入外部文件。未匹配的曲目追加在末尾。
3. 可切换文件名自然排序、碟号/音轨号排序，或**按住任意曲目行上下拖动**。拖动时显示插入线，靠近列表边缘自动滚动，松开后更新顺序与文件编号。也支持上下移动按钮及 `Alt+↑/↓`。
4. 曲名优先来自标签；无标签时推断文件名。去掉曲序、歌手前缀和明确的格式标记，保留 Live、Remix、伴奏等版本信息。双击曲名可修改，并查看 `01 - 曲名.wav` 预览。
5. 选择输出位置，点“生成刻录文件”。全部校验通过后才产生正式的 `CDReady_日期_时间` 文件夹。
6. 打开输出的 `刻录顺序.wpl`，将曲目添加到刻录软件的 **音频 CD** 列表，按编号刻录。

输出包含编号 WAV、WPL / M3U8 列表、中文报告和 `cdready-manifest.json`。报告记录每首歌的原始规格、转换参数、峰值衰减、采样数及哈希。

## 音质边界

| 来源 | 处理方式 |
| --- | --- |
| 44.1 kHz / 16-bit / 双声道无损音频 | 保持解码 PCM 一致，验证源/输出 PCM MD5；更换为标准 WAV 封装 |
| 其他采样率或位深 | SoX Very High Quality 重采样（`precision=28`），三角抖动量化到 16-bit |
| 转换后的峰值过高 | 施加必要的最小整轨衰减，预留 0.01 dB；不做响度归一化 |
| 单声道 | 同一信号复制到左右声道 |
| 多声道 | 默认阻止；只有明确开启混音才转换为立体声 |
| MP3、AAC 等有损来源 | 解码后生成 CD PCM，标明有损来源；不能恢复已丢失的信息 |

**高规格音源转成标准音频 CD 无法逐位无损。** 原文件保留，程序不会以升采样或改标签伪装音质提升，也不生成 MQA。

验证包括 FLAC 内置 PCM MD5（可用时）、完整解码、规范 RIFF 标头、采样数与时长、原文件及输出文件哈希。每批最多 99 首、80 分钟（保守预留每轨 2 秒间隔），每首至少 4 秒。超过容量请分批准备。

取消或失败的文件夹保留“未完成”标记，不生成可供刻录的正式播放列表。实际光盘是否刻录成功仍取决于刻录软件、介质和光驱。

## 可选：本地 AI 辅助命名

普通规则够用时无需模型。点“本地 AI 校名”才调用服务；建议必须能在原文字段中核实，并经预览勾选后应用。

将 `local-ai.example.json` 复制为 `local-ai.json`，填写运行中的本地模型名称：

```json
{
  "Endpoint": "http://127.0.0.1:18081/v1/",
  "Model": "your-local-model-name",
  "Launcher": ""
}
```

服务需兼容 `GET /v1/models` 与 `POST /v1/chat/completions`。仅允许本机回环 HTTP(S) 地址，并禁止自动跟随重定向。发送文件名、曲名和歌手标签，不发送音频或文件绝对路径。模型不参与音频处理或排序。

可选 `Launcher` 是用户自己的可信 PowerShell 管理脚本绝对路径，接收 `status` / `start` / `stop` 参数；`status` 需返回 `{"running": []}` 形式的 JSON。开启“按需启停本地模型”时，软件只关闭本次自行启动的模型。配置文件已加入 `.gitignore`，不要上传私人路径或凭据。

## 从源码构建

使用 Windows PowerShell 5.1 或 PowerShell 7：

```powershell
.\build.ps1
.\test.ps1
.\package.ps1
```

构建使用 .NET Framework 自带 C# 编译器，无 NuGet 依赖。测试需要 FFmpeg（含 libsoxr / FLAC / libmp3lame），只生成合成音频，不依赖任何私人音乐库。压缩包生成到 `dist`，不附带 FFmpeg 或本地 AI 配置。

代码结构：`src/MainForm.cs` 为 WinForms 界面，`Library.cs` 负责探测、排序及列表，`AudioEngine.cs` 负责转换和校验，`LocalAi.cs` 负责可选本地模型调用。

批处理入口：

```powershell
.\CDReady.exe --prepare "C:\Music" "C:\CDOutput" "C:\CDOutput\result.json" "C:\Music\order.m3u8"
```

批处理示例中的结果目录需先存在；最后一个播放列表参数可省略。GUI 才支持人工曲名编辑和 AI 预览。

## 参考

- [FFmpeg resampler 文档](https://ffmpeg.org/ffmpeg-resampler.html)
- [Microsoft：CD Burning and Device Discovery with IMAPI](https://learn.microsoft.com/en-us/archive/msdn-magazine/2004/april/c-q-a-cd-burning-and-device-discovery-with-imapi)

FFmpeg 作为独立外部程序使用，遵循其安装包所附许可证；本项目不重新分发其二进制或模型权重。
