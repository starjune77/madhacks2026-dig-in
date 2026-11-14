# DigIn

A first-person 3D digging game for MadHacks 2026. Solo developer.

## Project

- Engine: Unity 6.6 (6000.6.0f1), URP, Windows.
- Repo: `starjune77/madhacks2026-dig-in`
- Deadline: Sunday 2026-11-15, 12:00 PM CST (Devpost). **Feature freeze Sunday 10:00 AM.**
- Judging: Sunday 1:00 PM, science-fair style. Assume each judge watches for 2-3 minutes.
- Rules: all code must be written during the event. Frameworks and packages are allowed. The repo needs multiple commits, so **commit after each working feature**.
- Theme: "Let's Dig In", used both literally (digging into the ground) and figuratively (digging into a life).
- Branches: `main` is the safe low-poly version. The overhaul below happens on `overhaul`.

## Concept

You dig into the backyard with an old shovel and unearth objects from ordinary moments you once passed by without noticing. The dig is not a shaft: it winds, like a life. The very end of it is "today".

## Locked design decisions (do not change)

- **Free 3D digging.** The ground is smooth volumetric soil; the player digs down, sideways and through curving tunnels. No shaft, no frame, no walls. The dig starts as a slight hollow in the backyard grass.
- **A hidden life path.** The story lies along a hidden 3D spline, 60-80 m long, that descends while wandering: sometimes steep, sometimes nearly level, curving left and right. Target 25-35 minutes of play.
- **Soft guidance only.** Along the path the soil is softer and warmer in colour, with roots, small glints and a faint warm light ahead. Off the path the soil is denser and slower to dig. No arrows, no markers, no walls.
- Earlier on the path = childhood. Further = school days, then growing up. The ground gets harder as you go.
- Three strata, by depth: Childhood (soft warm dirt) -> School Days (clay) -> Growing Up (stone). "Today" is at the end of the path.
- Three shovels: Toy Shovel -> Garden Shovel -> Steel Shovel. Each stratum requires the next shovel.
- One resource only: scrap. You find it while digging and spend it on shovel upgrades. No coin economy.
- Nine major memory objects, three per stratum, at fixed points on the path (not random) so the demo is reproducible. Between them, 15-20 small memory fragments (a marble, a bottle cap, a hair tie, a bus pass...), each with one short line in the same quiet tone. A few small open pockets along the way, each holding something.
- Picking up a major object shows a card with its illustration and a one-line memory. A fragment only shows its line.
- One letter per stratum (three total). A shelf at the surface fills up with the cards you have found.
- Ending: at the end of the path you find a blank page. The ending text appears, you return to the surface, and the shelf is full. No extra explanatory dialogue.
- The shovel is only a tool for going further. The real reward is the memories.
- The game starts like an ordinary digging game. The meaning should dawn on the player gradually.
- **Realistic art direction**: photoreal materials and models, but warm, calm and nostalgic (golden hour, soft haze, warm grade). Never cold or gritty. No low-poly assets; do not mix styles.
- Out of scope: third-person view, pets, Databricks integration, complex inventory.

## Overhaul plan (2026-11-14, branch `overhaul`)

The low-poly, straight-shaft version was play-tested and rejected: soil like shiny cement, stiff digging, a boxy shaft, a cheap look, far too short. References for feel, guidance and mood (ideas only, never assets or exact designs): A Game About Digging A Hole, Astroneer, What Remains of Edith Finch, modern simulator games with physical piles of small objects.

Order, one commit per working step, Play mode screenshots checked after each:

1. **Terrain** - chunked density field, marching cubes, Burst jobs, a collider per chunk. `[in progress]`
2. **Digging feel** (top priority) - animation-clip swing with anticipation, impact, follow-through; ~40 ms hit-stop; soft camera kick; the scoop forms over a short tween and crumbles at its edges; physical clods and crumbs that pile up; dust puffs; hold-to-dig rhythm; a subtle preview of the next scoop; smooth movement. **Stop here for a developer play-test.** `[next]`
3. **Soil materials** - CC0 photoscanned PBR soil, clay and rock (Poly Haven / ambientCG, 2K), triplanar, fully matte, blended by stratum; dug surfaces darker and moister, with pebbles and roots.
4. **Sky, light and mood** - Evening Field HDRI as visible sky and lighting, height fog, soft shadows, SSAO, soft bloom, depth of field, warm filmic grade, subtle grain and vignette, dust motes; cozy lantern underground.
5. **Environment** - realistic house, fence, trees, grass, rocks (Poly Haven models, free Asset Store packs the developer imports). Remove every Kenney asset.
6. **Life path and content** - the final spline, softness and warmth guidance, pockets, 15-20 fragments, pacing for 25-35 minutes.
7. **Shovel and memory objects** - realistic Meshy shovel (one first, for approval), gloved hands if feasible, then the memory objects. The Bicycle Bell is regenerated in the realistic style once the terrain works.

Performance budget: 60 fps at 1080p on the developer's laptop. Check frame time in Play mode after each visual change and report anything heavy. Repo size: 2K textures, never 4K or 8K.

## Conventions

- **Assets over code. Code draws only what must be procedural.** Code generates only the diggable volume (density field, chunk meshes and colliders), the debris pool, the scoop preview and runtime state. Everything else (environment, props, shovel, player, hub, UI, lighting, animation clips, particle systems) is real Unity content: GameObjects saved in SampleScene, prefabs, materials, textures and clips in `Assets/`, UI as a UGUI Canvas, all editable in the editor. Runtime code references them through serialized fields and never creates them.
- Persistent content is created by the editor script `Assets/Editor/DigInSceneBuilder*.cs` (menu **DigIn > Build Scene**). It only creates what is missing, so re-running is safe; to regenerate a piece, delete it and run again. Prefabs and versioned scene objects carry a version marker child (e.g. "v2 bookcase"); bumping the version constant rebuilds that piece. New content goes into that script, not into runtime code.
- Models are placed by their measured bounds (`Place`, `FitInside`, `Edge` helpers), never by assumed sizes: packs use different units and pivots.
- **No sound** until the developer asks for it. Do not add any audio, procedural or files.
- First-person actions (shovel swing, dirt impact, object popping out, card reveal) use Unity animation assets where that makes sense.
- Use only the new Input System (`Keyboard.current`, `Mouse.current`). The legacy `Input` class throws errors in this project.
- Never call `Shader.Find` at runtime (shaders can be stripped from builds). In editor scripts it is fine, because the saved material asset keeps a hard reference to its shader.
- Text uses TextMesh Pro. The setup script imports the TMP Essential Resources if they are missing.
- Keep manual editor setup to a minimum. If a manual step is required, give exact steps.
- Do not touch `Library/`, `Temp/` or `Logs/` (reading package sources in `Library/PackageCache` is fine).
- All in-game text is in English.

## Checking work in the editor

Claude drives the open Editor with the Unity CLI (`unity command ...`, Pipeline package `com.unity.pipeline`).

- **After every visual change, enter Play mode, take screenshots, look at them, and fix problems before reporting to the developer. Read the Console after every change.**
- Build Scene: `unity command script eval -- --code 'DigInSceneBuilder.BuildScene(); return 1;'`. Screenshots: `unity command capture game_view -- --source screen --output-path <absolute path outside the repo>`.
- Run `unity` from Git Bash, not PowerShell 5.1 (it strips the quotes inside C# snippets).
- Simulated keys and clicks do not reach the game while the Editor window is in the background. Drive the game through its public methods instead (`DigGame.I.DemoJump(n)`, `DemoEnding()`, `DigGame.I.Dig(...)`, `TryPickUp`, `player.Teleport` / `SetLook`), and ask the developer to check anything that needs real input.
- Screenshots cannot show feel. Anything about feel (swing, impact, debris, movement) needs a developer play-test before it counts as done.

## Meshy (generated 3D assets)

- Use Meshy for all hero assets: the nine memory objects, the shovels and key props.
- Style: **realistic PBR** (base colour, normal, roughness, metallic), worn and lived-in, warm and nostalgic. Not low poly, not stylized. 2K textures.
- Generate one asset first and show the developer a screenshot of it in the scene before generating the rest. Report credits used after each batch.
- Save Meshy assets under `Assets/ThirdParty/Meshy/<AssetName>/`. They are generated on a paid plan with full commercial rights.

## Project layout

- `Assets/Scripts/`: runtime code.
  - `DigGame.cs` (on "Game"): rules and state (scrap, shovel level, memory finds, digging rules, workbench interaction). Tuning values are serialized fields.
  - `DigVolume.cs` (on "Dig Site/Volume"): the diggable ground. A density field (0.25 m voxels, 48 x 40 x 48 m, 4 m chunks) meshed with marching cubes in Burst jobs; one MeshCollider per chunk. `Carve()` removes a noisy, rounded scoop; strata harder than the current shovel are left alone, and the outer shell and the ground under the house, fence and bench cannot be dug. The starting surface is read from the Unity Terrain's heightmap.
  - `MarchTables.cs`: the marching cubes triangle table (Paul Bourke's public table).
  - `DigPath.cs` (on "Dig Site/Life Path"): the hidden life path. Control points (editable in the scene), sampled to a polyline; gives positions by distance and the distance from any point to the path (softness, warmth).
  - `DigPlayer.cs` (Player prefab): movement with acceleration, slope projection, slow scrambling up steep slopes, sliding only on near-vertical ones, look, dig input and rhythm, camera smoothing, kick and bob.
  - `DigShovel.cs` (Player prefab, under the camera): one model per shovel level; plays the swing clips through an Animator and relays their Impact and Toss events; hit-stop; idle sway and walk bob.
  - `DigScoop.cs` (on "Dig Site/Scoops"): runs each scoop as a short tween (the hole grows, then its rim crumbles).
  - `DigDebris.cs` (on "Dig Site/Debris"): pooled physical clods (Rigidbodies, capped) plus crumb and dust particle systems.
  - `DigAim.cs` (on "Dig Site/Aim Ring"): the subtle ring that shows where the next scoop will land.
  - `DigHud.cs` (HUD prefab): updates the Canvas UI. Controls hint fades out after a few seconds.
  - `DigAtmosphere.cs` (on "Game"): dimmer, warmer ambient light and fog with depth; drives the lantern light on the player.
  - `MemoryData.cs`: ScriptableObject per memory (title, line, path distance, stratum, card sprite, optional own find prefab). Assets in `Assets/Data/Memories/`.
  - `MemoryFind.cs` (Memory Find prefabs and the Blank Page prefab): a found object lying in the dirt.
  - `MemoryShelf.cs` (Memory Shelf prefab): nine framed card slots, rows top to bottom = Childhood, School Days, Growing Up.
  - `DigStory.cs` (on "Game"): memory card, letter after a stratum's third memory, the ending sequence. Locks player input while open.
  - `LetterData.cs`: ScriptableObject per letter (title, lines). Assets in `Assets/Data/Letters/`; the ending is letter 4. The blank page is `Assets/Data/Blank Page.asset`.
  - `DigDemo.cs` (on "Game"): demo mode.
  - `Workbench.cs` (Workbench prefab): upgrade spot. Walk up and press E.
- `Assets/Shaders/DigSoil.shader`: the ground shader (URP, hand-written HLSL): matte, world-space, grass on the untouched surface, strata by depth, warmer near the life path.
- `Assets/Animations/`: shovel swing clips and their Animator Controller (made by the setup script, editable in the Animation window).
- `Assets/Editor/DigInSceneBuilder.cs`: the setup script (assets, prefabs, lighting, dig site, hub, wiring).
- `Assets/Editor/DigInSceneBuilder.Environment.cs`: the world around the yard: Unity Terrain (heightmap from `GroundHeight`, pines), house, yard.
- `Assets/Editor/DigInSceneBuilder.Feel.cs`: swing clips, Animator Controller, debris meshes, particle systems, aim ring.
- `Assets/Editor/DigInSceneBuilder.Meshy.cs`: turns a Meshy asset into a material, a find prefab and a card illustration.
- `Assets/Materials`, `Assets/Textures`, `Assets/Meshes`, `Assets/Prefabs`, `Assets/Data`, `Assets/Fonts`, `Assets/Animations`: content made by the setup script, then edited by hand.
- `Assets/ThirdParty/`: imported art packs (see Art below). Do not edit files there; make prefab variants instead.

Notes:

- The dig volume covers x, z -22..26 and y -36..4; the Unity Terrain has a hole over that square and only shows beyond it. The volume's top surface comes from the same heightmap, so the two meet.
- Stratum boundaries are at 10.5 m and 21.5 m below the surface, wobbling by about a metre.
- Major memories lie at fixed distances along the path. One turns up when the player digs within reach of its spot. The ending does not depend on having found all of them: it fills the shelf.
- R returns to the surface; pressed again near the spawn point, it takes you back down to where you left.
- Debris clods live on the built-in "Ignore Raycast" layer, so aiming and pick-up rays pass through them; the player's controller ignores them, so piles never jitter the camera.
- Objects named "(placeholder)" are primitives standing in for imported art. Replace them, do not polish them.

## Art

- Realistic, warm, nostalgic. CC0 sources (Poly Haven, ambientCG), free Asset Store packs the developer imports, and Meshy for hero assets. List every source in the Devpost credits.
- **Kenney is on its way out**: the packs under `Assets/ThirdParty/Kenney` still dress the yard until step 5 replaces them. Do not add new Kenney content.
- Imported so far: `Assets/ThirdParty/Kenney/{NatureKit,SurvivalKit,FurnitureKit,FantasyTownKit}` (FBX only, to be removed), `Assets/ThirdParty/PolyHaven/evening_field_2k.hdr`, `Assets/Fonts/Lora-{Regular,Italic}.ttf`.
- World layout (metres): yard x -12..17, z -16..10, fenced; house back wall along z = -16 with the door at x = 4 and the shelf at x = 0.8; workbench under a lean-to at (12.3, -7); spawn (3, -8) facing north; the dig starts in a hollow at (2.5, 2.5). Terrain is 256 m square from (-126, -126), 0.5 m cells, with a hole over the dig volume. Pine-covered hills ring everything.
- Look: golden hour, soft haze, a small family backyard. Underground: gentle darkness with a warm lantern glow; tunnels should feel cozy, not scary.

## Demo mode

F12 toggles demo mode (badge at top right). Then F1 = surface with the Toy Shovel, F2 = start of School Days with the Garden Shovel, F3 = start of Growing Up with the Steel Shovel, F4 = the end of the path with the blank page in front of you. A jump digs a tunnel along the life path up to that point and skips the memories before it. Target demo flow: 30 seconds of digging -> first object -> F4 -> ending.

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
