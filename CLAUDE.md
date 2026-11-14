# DigIn

A first-person 3D digging game for MadHacks 2026. Solo developer.

## Project

- Engine: Unity 6.6 (6000.6.0f1), URP, Windows.
- Repo: `starjune77/madhacks2026-dig-in`
- Deadline: Sunday 2026-11-15, 12:00 PM CST (Devpost). **Feature freeze Sunday 10:00 AM.**
- Judging: Sunday 1:00 PM, science-fair style. Assume each judge watches for 2-3 minutes.
- Rules: all code must be written during the event. Frameworks and packages are allowed. The repo needs multiple commits, so **commit after each working feature**.
- Theme: "Let's Dig In", used both literally (digging into the ground) and figuratively (digging into a life).

## Concept

You dig down with a toy shovel and unearth objects from ordinary moments you once passed by without noticing. The very bottom is "today".

## Locked design decisions (do not change)

- Shallow = childhood. Deeper = school days, then growing up. The ground gets harder as you go down.
- Three strata: Childhood (dirt) -> School Days (clay) -> Growing Up (stone). "Today" is at the bottom.
- Three shovels: Toy Shovel -> Garden Shovel -> Steel Shovel. Each stratum requires the next shovel.
- One resource only: scrap. You find it while digging and spend it on shovel upgrades. No coin economy.
- Nine memory objects, three per stratum, placed at fixed depths (not random) so the demo is reproducible.
- Picking up an object shows a 2D illustrated card with a one-line memory. 3D models only if time allows.
- One letter per stratum (three total). A shelf at the surface fills up with the cards you have found.
- Ending: at the bottom you find a blank page. The ending text appears, you return to the surface, and the shelf is full. No extra explanatory dialogue.
- The shovel is only a tool for going deeper. The real reward is the memories.
- The game starts like an ordinary digging game. The meaning should dawn on the player gradually.
- Out of scope: third-person view, pets, Databricks integration, complex inventory.

## Conventions

- **Assets over code. Code draws only what must be procedural.** Code generates only the deformable heightmap terrain (and its strata texture lookup), the dirt debris, and runtime state. Everything else (environment, props, shovel, player, hub, UI, lighting) is real Unity content: GameObjects saved in SampleScene, prefabs, materials and textures in `Assets/`, UI as a UGUI Canvas, all editable in the editor. Runtime code references them through serialized fields and never creates them.
- Because Claude cannot click in the editor, persistent content is created by the editor script `Assets/Editor/DigInSceneBuilder*.cs` (menu **DigIn > Build Scene**). It only creates what is missing, so re-running is safe; to regenerate a piece, delete it and run again. Prefabs carry a version marker child (e.g. "v2 bookcase"); bumping the version constant rebuilds that prefab and recreates its scene instances. New content goes into that script, not into runtime code.
- Kenney models are placed by their measured bounds (`Place`, `FitInside`, `Edge` helpers), never by assumed sizes: the kits use different units (cm, 10 cm, m) and pivots.
- **No sound.** Do not add any audio (procedural or files) until the developer explicitly asks for it.
- Use only the new Input System (`Keyboard.current`, `Mouse.current`). The legacy `Input` class throws errors in this project.
- Never call `Shader.Find` at runtime (shaders can be stripped from builds). In editor scripts it is fine, because the saved material asset keeps a hard reference to its shader.
- Text uses TextMesh Pro. The setup script imports the TMP Essential Resources if they are missing.
- Keep manual editor setup to a minimum. If a manual step is required, give exact steps.
- Claude cannot run Unity. After each change, say exactly what to run in the editor and what to check in Play mode.
- Do not touch `Library/`, `Temp/` or `Logs/`.
- All in-game text is in English.

## Project layout

- `Assets/Scripts/`: runtime code.
  - `DigGame.cs` (on "Game"): rules and state (scrap, shovel level, memory finds, strike handling, workbench interaction). Tuning values are serialized fields.
  - `DigTerrain.cs` (on "Dig Site/Shaft Floor", must stay at the world origin): the 21 x 21 heightmap (5 m, 0.25 m cells). `Dig()` lowers vertices with a flat core and smooth rim, clamped to the current shovel's floor, then rebuilds the MeshCollider. Static helpers make the strata texture and the shaft wall mesh for the setup script. **The digging deformation is approved; do not change it.**
  - `DigPlayer.cs` (Player prefab): movement with acceleration, slope projection, sliding on steep slopes, look, swing timing, camera smoothing and kick.
  - `DigShovel.cs` (Player prefab, under the camera): one model per shovel level, keyframed swing (keys editable in the inspector).
  - `DigDebris.cs` (on "Dig Site/Debris"): pooled flying dirt clods, simulated by hand.
  - `DigHud.cs` (HUD prefab): updates the Canvas UI. Controls hint fades out after a few seconds.
  - `DigAtmosphere.cs` (on "Game"): dimmer, warmer ambient light and fog with depth; drives the lantern light on the player.
  - `MemoryData.cs`: ScriptableObject per memory (title, line, depth, stratum, card sprite). Assets in `Assets/Data/Memories/`.
  - `MemoryFind.cs` (Memory Find prefabs, one per stratum, and the Blank Page prefab): a found object lying in the dirt.
  - `MemoryShelf.cs` (Memory Shelf prefab): nine framed card slots, rows top to bottom = Childhood, School Days, Growing Up.
  - `DigStory.cs` (on "Game"): memory card, letter after a stratum's third memory, the ending sequence. Locks player input while open.
  - `LetterData.cs`: ScriptableObject per letter (title, lines). Assets in `Assets/Data/Letters/`; the ending is letter 4. The blank page is `Assets/Data/Blank Page.asset`.
  - `DigDemo.cs` (on "Game"): demo mode.
  - `Workbench.cs` (Workbench prefab): upgrade spot. Walk up and press E.
- `Assets/Editor/DigInSceneBuilder.cs`: the setup script (assets, prefabs, lighting, shaft, hub, wiring).
- `Assets/Editor/DigInSceneBuilder.Environment.cs`: the world: terrain (heightmap from `GroundHeight`, strata-banded terrace faces, pines), house, yard, shaft rim, excavation props, rock formations.
- `Assets/Materials`, `Assets/Textures`, `Assets/Meshes`, `Assets/Prefabs`, `Assets/Data`, `Assets/Fonts`: content made by the setup script, then edited by hand.
- `Assets/ThirdParty/`: imported art packs (see Art below). Do not edit files there; make prefab variants instead.

Notes:

- Strata colours use a gradient texture looked up by depth (UV.y), not vertex colours: the default URP Lit material ignores vertex colours.
- Memory objects appear at fixed depths, at whatever spot the player is digging when they first reach that depth. Picking one up fills its shelf slot and shows its line as HUD text (placeholder for the card).
- Objects named "(placeholder)" are primitives standing in for imported art. Replace them, do not polish them.

## Art

- Use real art assets, not primitives and flat colours. All packs are CC0 (Kenney, Poly Haven) or OFL (Lora); list them in the Devpost credits anyway.
- Imported: `Assets/ThirdParty/Kenney/{NatureKit,SurvivalKit,FurnitureKit,FantasyTownKit}` (FBX only), `Assets/ThirdParty/PolyHaven/evening_field_2k.hdr`, `Assets/Fonts/Lora-{Regular,Italic}.ttf`. Do not edit files there.
- The HDRI is photographic, so it is only used as the reflection cubemap; the visible sky is a procedural skybox, and ambient is Trilight (DigAtmosphere darkens it with depth).
- World layout (metres, shaft at x,z 0..5): yard x -12..17, z -16..10, fenced; house back wall along z = -16 with the door at x = 4 and the shelf at x = 0.8; workbench under a lean-to at (12.3, -7); spawn (3, -8) facing north. Excavation north of the yard: elliptical terraces centred (2.5, 50), 3 m steps, floor at -12, ramp at x = 21. Terrain is 256 m square from (-126, -126), 0.5 m cells, with a hole over the shaft.
- Look: golden hour, light fog, a vast excavation landscape next to a small family backyard. Deeper in the shaft: dimmer, warmer, lit by the lantern.

## Digging feel (roadmap step 1)

The first prototype used 1 m cube blocks that vanish whole. It felt like Minecraft, not like digging, and movement was choppy. The goal is for it to feel like really digging into soil, satisfying and smooth.

Direction:

- Replace the cube grid with a single deformable heightmap mesh for the shaft floor: 5 m x 5 m, about 0.25 m grid spacing. Each shovel stroke lowers the vertices around the aim point with a smooth falloff, leaving a rounded scoop. The shaft is vertical, so no overhangs are needed. Update the MeshCollider after each stroke.
- Show dirt, clay and stone by depth (done with a gradient texture, see Notes).
- Implement "too hard for this shovel" by clamping how low vertices can go for the current shovel level.
- Feel, in priority order: dirt particles and flying clumps -> a visible shovel with a swing animation -> camera kick -> object discovery moment.
- Player movement should have acceleration and deceleration and handle the dug slopes smoothly.
- Status: done. The deformation is approved as is.
- The cube version is committed (`7579305`), so we can revert if the new approach is not working within 2-3 hours.

## Demo mode

F12 toggles demo mode (badge at top right). Then F1 = surface with the Toy Shovel, F2 = 8 m down with the Garden Shovel, F3 = 16 m down with the Steel Shovel, F4 = the bottom with the blank page in front of you. A jump lowers the whole shaft floor to that depth (`DigTerrain.LowerTo`) and skips memories above it. Target demo flow: 30 seconds of digging -> first object -> F4 -> ending.

## Memory objects

Childhood

- Bicycle Bell: "Someone was holding the back of the seat. I didn't notice when they let go."
- Crayon: "I drew a house with four windows. The real house only had two."
- Toy Car: "One wheel was missing, but I still raced it across the kitchen floor."

School Days

- Graded Test Paper: "I folded the paper before anyone could see the number at the top."
- Worn Eraser: "We kept borrowing this from each other, even though the teacher had a whole box."
- Folded Note: "It just said, 'Meet me after class.' I kept checking the clock."

Growing Up

- House Key: "I stood outside the door for a moment, holding the key to a place that was finally mine."
- Train Ticket: "I watched the platform disappear, even after there was nothing left to see."
- Coffee Mug: "I reheated the same cup twice. By evening, it was still sitting beside my work."

## Letters

- Letter 1 - Childhood: "I used to think afternoons lasted forever. The floor was a road, a cardboard box was a spaceship, and someone always called me in when it got dark. I don't remember what we had for dinner. I remember wanting five more minutes outside."
- Letter 2 - School Days: "I spent so much time waiting for the next thing. The next bell, the next weekend, the next exam to be over. Somewhere between all that waiting, we laughed at things that weren't even funny. I wonder if the others remember them too."
- Letter 3 - Growing Up: "My days became full of things I had to finish. There were messages to answer, places to be, and plans that kept changing. Sometimes I missed a call and forgot to call back. I thought I'd have more time later."
- Ending - The Blank Page: "There was nothing written on the last page. Not because nothing happened today. But because today isn't over yet. Someday, this ordinary day might be something worth digging up."

## Roadmap

Status (2026-11-14): steps 1-4 and demo mode are implemented; not yet play-tested together.

1. Digging feel (above)
2. Three memory objects + card display
3. Letters, shelf and ending, playable start to finish
4. Remaining six objects
5. Particles, lighting polish (sound only if the developer asks)
6. Demo mode, bug fixes
7. Build and Devpost submission

If step 3 is not done by Saturday 10:30 PM, cut to three objects and one letter.
