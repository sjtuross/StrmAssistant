# MediaInfo Extract

## [中文介绍](README.zh.md)

## Purpose

1. Improve initial playback start speed
2. Solve potential no progress bar issue
3. Capture image for video without poster
4. Playback behavior-based intro and credits detection

## Update

1. Support concurrent tasks (default is 1, max is 10)
2. Add `Strm Only` option (default is True) to support non-strm media imported with ffprobe blocked
3. Include media extras (default is False)
4. Process media items by release date in the descending order
5. Add plugin config page with libraries selection
6. Enable image capture (**experimental**, default is False), check Wiki for details.
7. Introduce catch-up mode (**experimental**, default is False), check Wiki for details.
8. Playback behavior-based intro and credits detection for episodes (**experimental**, default is False), check Wiki for details.

**Note**: The minimum required Emby version is 4.8.0.80.

## Install

1. Download `StrmAssistant.dll` to the `plugins` folder
2. Restart Emby
3. Go to the Plugins page and check the plugin version and settings
4. Go to the Scheduled Tasks page to run
