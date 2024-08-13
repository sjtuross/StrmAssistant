# StrmExtract

## Update

1. Support concurrent tasks (default is 1, max is 10)
2. Add Strm Only option to support non-strm media imported with ffprobe blocked (default is True)
3. Include media extras (default is False)
4. Process media items by release date in the descending order
5. Add plugin config page
6. Introduce catch-up mode (**experimental**, default is False)
   1. Extract media info once movies or series are added to favorites (both strm and non-strm supported)
   2. Extract media info once new items are added for movies or series in any user's favorites (strm exclusive)
   3. Same applies to episode but its entire series is included for processing
   4. Non-blocking thread processes all queued items every 30 seconds
   5. Share the same max concurrent count with scheduled task

Note: The minimum required Emby version is 4.8.0.80.

## Install

1. Download StrmExtract.dll to the plugins folder
2. Restart Emby
3. Go to the Plugins page and check the plugin version and settings
4. Go to the Scheduled Tasks page to run
