## LinkerPlayer TODO

### Problems

- PROBLEM WENT AWAY - ~~When the app first launches, the Music Library tab shows `( 0 )` when there are actually thousands of files. I've noticed that when I load a new playlist, it tends to "fix" the count in the Music Library tab.~~
- PROBLEM WENT AWAY - ~~TrackInfo does not change when selecting a track. Switching to and from a tab will sync TrackInfo~~
- When I have a selected track, and then I select a different track and click Play, it plays the track that was previously selected.
- DONE! - ~~I added a Ratings control and found out that the ratings apparently do not write to the file itself. I recreated the database and there are no ratings at all - at least I am not seeing any. Also, I had an icon showing whether a track has an album cover or not, but I do not see the icon anymore.~~

### Features

- Library/Playlists - Grouping by artist/album (choice to turn off)
- Library - Add ReplayGain context menu (allow selection of multiple tracks)
  - Analyze by Track
  - Analyze by Album
  - Remove ReplayGain analysis
- DONE! - ~~Library - Add ReplayGain column to indicate if the track has been analyzed~~
    - ~~Blank - not analyzed~~
    - ~~Track - ReplayGain by track~~
    - ~~Album - ReplayGain by album~~
    - ~~Able to query ReplayGain in search~~
- Restore double click bottom-left corner "Playback stopped" to go to playing track.
  If no track is playing, go to selected track in current playlist.
- Check DataGrid in XAML for default column sort - make a binding for variable sorts
  - Save in Settings
- Settings-Musicbrainz should show Username and Password (dots) to show that is has been initialized.
