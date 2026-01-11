# Baseline playback stability checklist (manual)

Baseline commit: `b9264b98`

## Setup
- Ensure a playlist has at least 5 tracks.
- Ensure at least one track is long enough to test seeking (>= 2 minutes).
- Ensure shuffle can be toggled on/off.

## Play / Pause / Stop
- [ ] Select a track in the grid.
- [ ] Press Play.
- [ ] Press Pause.
- [ ] Press Play (resume).
- [ ] Press Stop.
- [ ] Press Play again (restarts expected track).

## Next / Prev
- [ ] While playing, press Next repeatedly (>= 5 times).
- [ ] While playing, press Prev repeatedly (>= 5 times).
- [ ] Verify no mismatch between audible track and the “playing” indicator.

## End-of-track auto-advance
- [ ] Start a track.
- [ ] Let it play to completion.
- [ ] Verify the next track starts automatically.
- [ ] Repeat once more from the newly started track.

## Seek
- [ ] Start a long track.
- [ ] Seek forward to ~50%.
- [ ] Seek backward to ~10%.
- [ ] Verify audio seeks correctly and UI position updates.

## Shuffle
- [ ] Turn shuffle ON.
- [ ] Press Next repeatedly.
- [ ] Verify tracks are visited in a shuffled order (non-trivial) and no repeats until expected.
- [ ] Turn shuffle OFF.
- [ ] Press Next.
- [ ] Verify Next follows the playlist order.

## Tab switching / selection safety
- [ ] While a track is playing, click different playlist tabs.
- [ ] Verify switching tabs does not change the audible track.
- [ ] Verify selection changes do not auto-trigger playback unless Play is invoked.
