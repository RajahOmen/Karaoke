# Karaoke FFXIV Plugin

Time-synced lyric playback plugin for songs in FFXIV. When a song with lyrics is played in game, the plugin will show the lyrics to that song, highlighting the current place in the active lyric line.

Lyric files fetched on plugin start from the [karaoke-lyrics](https://github.com/RajahOmen/karaoke-lyrics) github repository. Updates can be manually fetched from the config menu ("Reload Lyric Files"), or with `/karaoke reload`.

As a backup, also fetches song names from the [Orchestrion](https://github.com/perchbirdd/OrchestrionPlugin) spreadsheet.

## Usage

Commands
- `/karaoke`: Opens the lyric player window. If the current BGM track has lyrics, they will be populated and synced to the current track position.
- `/karaoke config`: Opens the configuration menu, where the lyric player window's appearance can be customized, as well as other settings changed.
- `/karaoke reload`: Reloads the current song's lyrics and updates lyric files from remote source

If multiple lyric files are found for a song, you can select a specific one by using the dropdown selector found by hovering over the lyric player window.

## Custom lyric files

The plugin will scan the `pluginConfigs/Karaoke/lyrics/` directory for any `.lrc` files that follow the expected format. See [karaoke-lyrics](https://github.com/RajahOmen/karaoke-lyrics/blob/main/README.md) for documentation on the custom additions made to the enhanced LRC format.

## Feedback

Please direct any feedback relating to specific songs, lyrics, or lyric timings to the [karaoke-lyrics](https://github.com/RajahOmen/karaoke-lyrics) repository, keep issues here relating to the plugin itself.

## Known Issues

Due to how the plugin tracks the current time in a song, there are a few issues with the syncing that can appear

1. If the plugin is (re)loaded when a song has been playing for a long time (~2hrs+), the time may appear to be stuck at zero or not otherwise not correct. Completely restarting/resetting the currently-playing song from the beginning should fix the issue.
2. The syncing may get slightly off over long periods of playback, due to some manner of desync in the game's reported elapsed time and the actual song's elapsed time. I'm not aware of a fix to this issue, but I've included a `Time rate mult` option to tinker with, which will artifically alter what the game reports. I've preconfigured it to how my testing worked best, but this may require per-user adjustment.