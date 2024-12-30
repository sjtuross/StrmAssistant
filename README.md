# Emby神医助手

![logo](StrmAssistant/Properties/thumb.png "logo")

## [[English]](README.en.md)

## 用途

1. 提高首次播放的起播速度
2. 解决可能的无进度条问题
3. 视频截图预览缩略图增强
4. 片头片尾探测增强
5. 自动合并同目录视频为多版本
6. 独占模式提取媒体信息
7. 独立的外挂字幕扫描
8. 自定义`TMDB`备选语言
9. 使用替代`TMDB`配置
10. 演职人员增强`TMDB`
11. 获取原语言海报
12. 中文搜索增强
13. 拼音首字母排序
14. 媒体信息持久化
15. 支持代理服务器
16. 支持`TMDB`剧集组刮削

## 更新

1. 支持并发提取媒体信息 (默认最小为 1, 最大为 20), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/媒体信息提取-(MediaInfo-Extract))
2. 包含分集和附加内容处理
3. 按电影或剧集的发行日期倒序处理
4. 添加插件配置界面，可多选媒体库
5. 视频截图预览缩略图增强, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/视频截图预览增强)
6. 追更模式, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/追更模式-(Catch‐up-Mode))
7. 基于播放行为的片头片尾探测, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/片头探测-‐-播放行为)
8. 原生片头探测增强，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/片头探测-‐-原生增强)
9. 自动合并同目录视频为多版本, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/自动合并同目录多版本)
10. 仅允许本插件提取媒体信息 (ffprobe) 以及视频截图 (ffmpeg), 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/变相多线程入库)
11. 独立于扫库的外挂字幕扫描并更新，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/外挂字幕扫描-(External-Subtitle-Scan))
12. 尽可能从`TMDB`获取中文或日文元数据, 说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/自定义-TMDB-备选语言)
13. 使用替代`TMDB`配置，支持自定义配置API地址，图像地址和API密钥
14. 刷新`TMDB`中文演员并修复相关问题，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/中文演员-(Chinese-Actor))
15. 导入季的演职人员，更新节目系列的演职人员为各季合并
16. 优先使用原语言海报，支持`TMDB`, `TVDB`, `Fanart`，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/原语言海报--(Original-Poster))
17. `TMDB`备选语言元数据以及中文演员繁转简
18. 支持中文模糊搜索和拼音搜索，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/中文搜索增强)
19. 刷新元数据时自动生成拼音首字母为排序标题，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/拼音首字母排序)
20. 添加支持从`NFO`导入演员头像链接
21. 电影剧集页面隐藏无头像人员，非删除，仍可搜索
22. 首位管理员的自定义媒体库排序作用于所有用户
23. 剧集集标题自动补全
24. 复制媒体库，快速创建一个同配置的媒体库
25. 保存或加载媒体信息和章节片头信息至/自 JSON 文件，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/媒体信息持久化-(MediaInfo-Persist))
26. 查看缺少的集支持`TMDB`
27. 添加删除菜单项至合集媒体库
28. 支持对外`HTTP`请求使用代理服务器
29. 支持`TMDB`剧集组刮削，用指定的剧集组刮削剧集，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/TMDB-剧集组刮削-(Episode-Group))
30. 保存或加载剧集组至/自 JSON 文件，说明查看 [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/本地剧集组刮削)
31. 原生片头探测剧集黑名单
32. 支持单线程冷却时间 (默认最小为 0, 最大为 60)

## 安装

1. 下载 `StrmAssistant.dll` 放入配置目录中的 `plugins` 目录
2. 重启Emby
3. 至插件页面检查版本及设置

**注意**: Emby最低版本要求 `4.8.0.80`

## 赞赏

如果这个项目对你有帮助，不妨请我喝杯咖啡。如果你欣赏这个项目，欢迎为它点亮一颗⭐️。感谢你对开源精神的认可与支持！

![donate](donate.png "donate")

## 声明

本项目为开源项目，与 Emby LLC 没有任何关联，也未获得 Emby LLC 的授权或认可。本项目的目的是为合法购买并安装了 Emby 软件的用户提供额外的功能增强和使用便利。

### 使用须知

1. **合法使用**  
   本项目仅适用于合法安装和使用 Emby 软件的用户。使用本项目时，用户需自行确保遵守 Emby 软件的服务条款和使用许可协议。

2. **非商业用途**  
   本项目完全免费，仅限个人学习、研究和非商业用途。严禁将本项目或其衍生版本用于任何商业用途。

3. **不包含 Emby 专有组件**  
   本项目未包含 Emby 软件的任何专有组件（例如：DLL 文件、代码、图标或其他版权资源）。使用本项目不会直接修改或分发 Emby 软件本身。

4. **功能限制**  
   本项目不会绕过 Emby 的授权机制、数字版权保护 (DRM)，或以任何方式解锁其付费功能。本项目仅在运行时动态注入代码，且不会篡改 Emby 软件的核心功能。

5. **用户责任**  
   用户在使用本项目时，需自行承担遵守相关法律法规的责任。如果用户使用本项目违反了 Emby 的服务条款或相关法律法规，本项目开发者概不负责。

### 免责声明

1. 本项目开发者不对因使用本项目而可能导致的任何直接或间接后果（包括但不限于数据丢失、软件故障或法律纠纷）负责。
2. 如果认为本项目可能侵犯相关方的合法权益，请与开发者取得联系。
