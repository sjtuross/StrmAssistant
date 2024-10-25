# Strm Assistant

![logo](StrmAssistant/Properties/thumb.png "logo")

## Purpose

1. Improve initial playback start speed
2. Solve potential no progress bar issue
3. Image capture enhanced
4. Playback behavior-based intro and credits detection
5. Independent external subtitle scan

## Update

1. Support concurrent tasks (default is 1, max is 10), check [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/媒体信息提取-(MediaInfo-Extract))
2. Add `Strm Only` option (default is True) to support non-strm media imported with ffprobe blocked
3. Include media extras (default is False)
4. Process media items by release date in the descending order
5. Add plugin config page with libraries selection
6. Image capture enhanced (default is False), check [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/视频截图增强-(Image-Capture-Enhanced)) for details.
7. Introduce catch-up mode (default is False), check [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/追更模式-(Catch‐up-Mode)) for details.
8. Playback behavior-based intro and credits detection for episodes (default is False), check [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/片头探测-(Intro-Detection)) for details.
9. Independent external subtitle scan, check [Wiki](https://github.com/sjtuross/StrmAssistant/wiki/独立的外挂字幕扫描-(External-Subtitle-Scan)) for details.

## Install

1. Download `StrmAssistant.dll` to the `plugins` folder
2. Restart Emby
3. Go to the Plugins page and check the plugin version and settings

**Note**: The minimum required Emby version is 4.8.0.80.
