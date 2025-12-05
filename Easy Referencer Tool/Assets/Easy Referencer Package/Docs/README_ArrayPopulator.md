# Array Populator (Editor)

Bulk-fill serialized arrays/lists from Selection, Parent & Children, Folder, or Drag & Drop. Type-safe for `GameObject`, `Component` subtypes, and all `UnityEngine.Object` assets (SOs, Textures, AudioClips, etc.). Undo supported.

## Open
- **Tools → Array Populator**
- Component context menu: **Populate Arrays/Lists…** (Ctrl/Cmd+Alt+P)

## Workflow
1. **Target**: pick GameObject → Component (script) → array/list field.
2. **Sources**: Selection / Parent & Children / Folder / Drag & Drop.
3. **Options**: Append/Replace, Unique, Sort A→Z, Shuffle.
4. **Apply**. Status line shows “Added/Skipped/Size”.

## Notes
- Component fields (e.g., `List<MeshRenderer>`) will extract matching components from Scene GOs and Prefabs.
- Undo is recorded; you can revert with Ctrl/Cmd+Z.

## Known good tests
- 20 GOs with MeshRenderer → add to `List<MeshRenderer>`.
- Parent with inactive children → Include Inactive → add colliders.
- Folder of prefabs → extract components → add.
