## A) Highest-payoff / lowest-risk deletes

1. MediaTabViewModel.OnTrackSelectionChanged Library reassert block
File: MediaTabViewModel.cs around lines ~459–476
Why churn: non-user Library branch force-sets grid selection + scroll from VM side.
Risk: low if tab-owned SelectedTrack + code-behind tab restore remain.
2. TabControl_SelectionChanged fallback to vm.SelectedTrack for Library
File: MediaTabPanel.xaml.cs around lines ~1122–1125
Why churn: mixes global selection into tab-local restore path.
Risk: low; prefer tabData.SelectedTrack only.
3. LoadPlaylistTabs explicit manual call to OnSelectedTabIndexChanged(savedIndex)
File: MediaTabViewModel.cs lines ~654–662
Why churn: bypasses normal property-change semantics and duplicates activation semantics.
Risk: medium-low; keep only if needed for index=0 edge, otherwise remove by using a clean init pattern.

---

## B) Medium-payoff simplifications (split responsibilities)

4. DataGrid_Loaded is still multi-responsibility (~100 lines)

File: MediaTabPanel.xaml.cs lines ~753–860
Contains:
• column regeneration
• sort restore
• sort handler wiring
• miscellaneous UI handler wiring
Why churn: one event does many things; hard to reason about side effects.
Recommendation: split into:
• InitializeGridColumns(dg)
• RestoreGridSort(dg)
• AttachGridBehaviorHandlers(dg)
5. TracksTable_OnSelectionChanged still has passive deselection repair logic
File: MediaTabPanel.xaml.cs lines ~962–1003
Why churn: event tries to infer user vs lifecycle and patch deselection.
Recommendation: keep only user-driven path; if passive deselection persists, fix source (grid/view refresh flow), not this handler.
---

## C) High-risk hotspots (don’t touch casually, but document)

6. OnGoToActiveTrack and OnActiveTrackChanged
File: MediaTabPanel.xaml.cs lines ~1246+ and ~1343+
Why churn: heavy imperative selection/tab/scroll coordination, temporarily unsubscribes handlers, sets VM state directly.
Risk: high because it affects playback UX.
Recommendation: leave for dedicated refactor with tests.

1. RegenerateColumns mega-method
File: MediaTabPanel.xaml.cs (earlier section, large method)
Why churn: full column rebuild can reset DataGrid state and trigger restore chains.
Risk: high; behavior-sensitive.
Recommendation: later extract and reduce full rebuild frequency.

---

## D) Current churn score in this area

• Before session: ~9/10
• Now: ~5/10
• After top 3 deletes + DataGrid_Loaded split: likely ~3.5–4/10
---

## E) Suggested safe execution order (small commits)

1. Remove Library global fallback in TabControl_SelectionChanged.
2. Remove VM-side Library reassert branch in OnTrackSelectionChanged.
3. Split DataGrid_Loaded (no behavior change, pure extraction).
4. Re-evaluate if OnSelectedTabIndexChanged(savedIndex) manual call is still needed.
