# STRM 助手

![logo](StrmAssistant/Properties/thumb.png "logo")

## [[English]](README.en.md)

## 用途

1. 提高首次播放的起播速度
2. 解决可能的无进度条问题
3. 对缺失封面元数据的视频做视频截图
4. 基于播放行为探测片头片尾
5. 自动合并同目录视频为多版本
6. 尽可能从`TMDB`获得中文元数据

## 更新

1. 支持并发提取媒体信息 (默认最小为 1, 最大为 10), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/媒体信息提取-(MediaInfo-Extract))
2. 添加 `仅支持Strm` 的选项 (默认开启)，关闭后可以支持软链接或直连的场景
3. 包含附加内容处理 (默认关闭)
4. 按电影或剧集的发行日期倒序处理
5. 添加插件配置界面，可多选媒体库
6. 启用视频截图 (默认关闭), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/视频截图-(Image-Capture))
7. 追更模式 (默认关闭), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/追更模式-(Catch‐up-Mode))
8. 基于播放行为的剧集片头片尾探测 (默认关闭), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/片头探测-(Intro-Detection))
9. 自动合并同目录视频为多版本, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/自动合并同目录多版本)
10. 尽可能从`TMDB`获取中文元数据, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/中文化-TMDB-元数据)

**注意**: Emby最低版本要求 `4.8.0.80`

## 安装

1. 下载 `StrmAssistant.dll` 放入配置目录中的 `plugins` 目录
2. 重启Emby
3. 至插件页面检查版本及设置
