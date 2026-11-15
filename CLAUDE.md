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

Underneath, the world is not quite the real one. It is a dream, or something like it, in which the protagonist digs into their own life. The family cat, their real pet, somehow runs the shop here.

## Locked design decisions (do not change)

- **Free 3D digging.** The ground is smooth volumetric soil; the player digs down, sideways and through curving tunnels. No shaft, no frame, no walls. The dig starts as a slight hollow in the backyard grass.
- **Soil behaves like soil.** It never floats: earth cut loose from the ground falls as clods. Clods are crumbly, irregular earth in the ground's own material, some breaking apart when they land; tossed earth settles into real heaps on the yard.
- **A hidden life path.** The story lies along a hidden 3D spline, 60-80 m long, that descends while wandering: sometimes steep, sometimes nearly level, curving left and right. Target 25-35 minutes of play.
- **Soft guidance only.** Along the path the soil is softer and warmer in colour, with roots, small glints and a faint warm light ahead. Off the path the soil is denser and slower to dig. No arrows, no markers, no walls.
- Earlier on the path = childhood. Further = school days, then growing up. The ground gets harder as you go.
- Three strata, by depth: Childhood (soft warm dirt) -> School Days (clay) -> Growing Up (stone). "Today" is at the end of the path.
- **Economy: dig -> find minerals -> carry them up -> sell at the shop -> buy upgrades.** 15-20 real mineral types (quartz, feldspar, mica, garnet, jasper, malachite, amethyst, opal...) with rarity and value by depth and stratum, embedded in the walls, popping out when dug, with a small glint. Each has a one-line description that quietly echoes a moment of life ("Quartz: clear, like the mornings before school."), subtle, never preachy.
- A bag with limited capacity, so the player goes back up to sell. R takes you up and back down.
- Shop upgrades: four spade tiers (one model, scaled: Old, Garden, Steel, Fine Steel; each digs a harder stratum or digs faster), bag capacity, a brighter lantern. Prices tuned so the full journey takes 25-35 minutes.
- **The shop is run by a cat**: the protagonist's own pet, a real cat shape (not humanoid), small and round, warm colours, sitting on a counter or crate near the house. Realistic but soft. Idle, tail swish, blink and purr/stretch animations. The shop UI is simple and warm, like a hand-written price board; the cat says short lines when you sell or buy.
- Memories, fragments and letters are never sold.
- Nine major memory objects, three per stratum, at fixed points on the path (not random) so the demo is reproducible. Between them, 15-20 small memory fragments (a marble, a bottle cap, a hair tie, a bus pass...), each with one short line in the same quiet tone. A few small open pockets along the way, each holding something.
- Picking up a major object shows a card with its illustration and a one-line memory. A fragment only shows its line.
- One letter per stratum (three total). A shelf at the surface fills up with the cards you have found.
- **The dream frame**, hinted softly and never stated until the end: the yard feels familiar but slightly unreal, no other people exist, the light is a little too golden, the cat sometimes says things a cat could not know.
- Ending (chosen by the developer): at the end of the path you find a blank page; its text appears; fade to black; you wake up in bed in the morning with the family cat curled up asleep on your chest, purring. One line: "Morning." A click returns to the yard, where the shelf is full.
- The shovel is only a tool for going further. The real reward is the memories.
- The game starts like an ordinary digging game. The meaning should dawn on the player gradually.
- **Realistic art direction**: photoreal quality close to modern realistic indie simulator games, warm, calm and nostalgic (golden hour, soft haze, warm grade). Never cold or gritty. No low-poly assets anywhere; do not mix styles. Every Kenney asset leaves the scene.
- **Sound**: natural, soft and gentle; recorded foley, never synthetic or procedural; a quiet, calm mix. Digging is the clearest sound and should feel like ASMR: close, crisp, layered (bite, lift, trickle, clods). Music is a slow, wistful solo piano, the lowest thing in the mix.
- Out: scrap, the workbench, the Kenney low-poly look. Also out of scope: third-person view, Databricks integration, complex inventory.

## Status (2026-11-15 morning)

Everything in the locked design is implemented on `overhaul` (merged into `main` for submission): volumetric digging, no floating soil, crumbly clods and heaps, photoscanned ground, the realistic yard (HDRI, SpeedTrees, real grass, Meshy house and fence, Poly Haven props), minerals and the cat's shop, realistic memory objects with card art, sixteen fragments, sound, the dream hints and the waking ending. Not yet play-tested by the developer; feel and pacing (25-35 minutes) are untested estimates.

Performance budget: 60 fps at 1080p on the developer's laptop. A built player run with `-benchmark <file>` visits the yard, each stratum, the shop and the ending and writes the frame rate at each. Repo size: 2K textures, never 4K or 8K.

## Conventions

- **Assets over code. Code draws only what must be procedural.** Code generates only the diggable volume (density field, chunk meshes and colliders), the debris pool, the scoop preview and runtime state. Everything else (environment, props, shovel, player, hub, UI, lighting, animation clips, particle systems) is real Unity content: GameObjects saved in SampleScene, prefabs, materials, textures and clips in `Assets/`, UI as a UGUI Canvas, all editable in the editor. Runtime code references them through serialized fields and never creates them.
- Persistent content is created by the editor script `Assets/Editor/DigInSceneBuilder*.cs` (menu **DigIn > Build Scene**). It only creates what is missing, so re-running is safe; to regenerate a piece, delete it and run again. Prefabs and versioned scene objects carry a version marker child (e.g. "v2 bookcase"); bumping the version constant rebuilds that piece. New content goes into that script, not into runtime code.
- Models are placed by their measured bounds (`Place`, `FitInside`, `Edge` helpers), never by assumed sizes: packs use different units and pivots.
- **Sound** is recorded only, never procedural. Allowed sources: the Sonniss GDC Game Audio Bundle (royalty-free), Pixabay (Pixabay Content License), Freesound with the CC0 filter only. Never BBC Sound Effects or anything CC-BY-NC. Sonniss and Pixabay block automated downloads: ask the developer to fetch files into a `DigInAudio` folder in their Downloads. Freesound's public HQ OGG previews can be fetched directly. Cut sources with `Assets/Editor/DigAudioCutter.cs` (sources go in `Assets/AudioIn`, never committed). 6-10 variations per layer, no immediate repeats, pitch +-4%, volume +-2 dB; route everything through `Assets/Audio/DigIn.mixer` (Music, SFX, Ambience, UI; `MasterVolume` exposed). Every source and licence goes in `CREDITS.md`.
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
- Every third-party asset (textures, models, sounds) is listed with its source and licence in `CREDITS.md` when it is added.
- Build Scene: `unity command script eval -- --code 'DigInSceneBuilder.BuildSceneUnattended(); return 1;'` (never use `BuildScene()` from automation: it can open a save prompt that blocks the Editor). Screenshots: `unity command capture game_view -- --source screen --output-path <absolute path outside the repo>`.
- Run `unity` from Git Bash, not PowerShell 5.1 (it strips the quotes inside C# snippets).
- `runtime simulate_key` does not reach the game while the Editor window is in the background, but input queued through the Input System does: from `script eval`, `InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = ..., buttons = 1 })` (then `buttons = 0` in a later call), or `KeyboardState(Key.Escape)`, without calling `InputSystem.Update()`. That clicks real UI buttons (positions in Game view pixels, origin bottom left). Set the Game view to a fixed 1920x1080 first (`UnityEditor.GameViewSizes` by reflection) so screenshots and clicks match a 1080p screen. Otherwise drive the game through its public methods (`DigGame.I.DemoJump(n)`, `DemoEnding()`, `DigGame.I.Dig(...)`, `TryPickUp`, `player.Teleport` / `SetLook`). Feel still needs the developer.
- Screenshots cannot show feel. Anything about feel (swing, impact, debris, movement) needs a developer play-test before it counts as done.

## Meshy (generated 3D assets)

- **Budget: 128 credits left, and no more budget** (117 went to the Munchkin cat on 2026-11-15). Before any generation, say what it is for and its cost. Never regenerate or refine for small improvements. The only allowed use was the nine memory objects (already done); everything else (minerals, spades, props, house, fence, furniture) comes from Poly Haven or the Unity demo pack, with material and scale variants.
- Models already generated and kept: the house, fence, spade, bookshelf, shop counter, the Munchkin kitten (sitting, paw-licking, sleeping; the old cat models are unused), bed, eleven minerals and nine memory objects.
- Style: **realistic PBR** (base colour, normal, roughness, metallic), worn and lived-in, warm and nostalgic. Not low poly, not stylized. 2K textures.
- Report credits used after each batch.
- Save Meshy assets under `Assets/ThirdParty/Meshy/<AssetName>/`. They are generated on a paid plan with full commercial rights.

## Project layout

- `Assets/Scripts/`: runtime code.
  - `DigGame.cs` (on "Game"): rules and state (coins, bag, spade level, upgrades, what a stroke does, memory and fragment finds, demo jumps, R to the surface and back). Tuning values are serialized fields.
  - `DigVolume.cs` (on "Dig Site/Volume"): the diggable ground. A density field (0.25 m voxels, 48 x 40 x 48 m, 4 m chunks) meshed with marching cubes in Burst jobs; one MeshCollider per chunk. `Carve()` removes a ragged scoop; `CollapseLoose()` drops earth that lost its hold; `Deposit()` builds heaps. Strata harder than the spade and protected ground (the outer shell, the surface outside the bare patch) stay.
  - `MarchTables.cs`: the marching cubes triangle table (Paul Bourke's public table).
  - `DigPath.cs` (on "Dig Site/Life Path"): the hidden life path (control points in the scene).
  - `DigScoop.cs`, `DigDebris.cs` (+ `DigClodBody.cs`), `DigAim.cs`: the scoop tween, the clods/crumbs/dust and heaps, the aim ring.
  - `DigGrass.cs` (on "Dig Site/Grass"): instanced grass clumps on the diggable yard, removed where the ground is dug or covered.
  - `DigMinerals.cs` + `MineralData.cs`: mineral spots, showing, popping out, picking up. Data in `Assets/Data/Minerals/`.
  - `DigShop.cs` (on "Game"): the cat's price board (sell tiles and tier cards with pictures rendered from the models, `DigShopTile.cs` lifts them under the mouse), prices, the cat's lines (including the ones a cat couldn't know).
  - `DigPause.cs` (on "Game"): the Esc pause menu (notebook page over the blurred "Pause Blur" volume; Settings saved in PlayerPrefs) and the first-launch How to Play note.
  - `DigCat.cs`: the cat's breathing and purring (Animator, `Assets/Animations/Cat.controller`), turning to watch you.
  - `DigAudio.cs` (on "Game"): all sound: layered digging per stratum on the swing's impact event, footsteps, yard loops and bird calls, room tone and tunnel reverb, the cat's purr, music with depth crossfade and ducking; clips in `Assets/Audio/`.
  - `DigPlayer.cs`, `DigShovel.cs`: movement, look, dig input; the animated spade (clips in `Assets/Animations/`).
  - `DigHud.cs`, `DigStory.cs`: HUD, cards, letters, the ending and the waking scene.
  - `DigAtmosphere.cs`: darker, warmer light and fog with depth; the lantern.
  - `MemoryData.cs` (memories and fragments), `MemoryFind.cs`, `MemoryShelf.cs`, `LetterData.cs`, `Workbench.cs` (the shop counter's interact spot), `DigDemo.cs` (demo mode and `-benchmark`).
- `Assets/Shaders/`: `DigSoil.shader` (the ground: photoscanned triplanar soil/clay/stone/grass, matte), `DigClod.shader` (loose earth), `DigTriplanar.hlsl`.
- `Assets/Editor/DigInSceneBuilder*.cs`: the setup script, split by topic: main (assets, prefabs, dig site, hub, wiring), Environment (terrain, house, yard, trees), Realistic (sky, post, model import), Soil, Feel (swing clips, clods, particles), Economy (minerals, shop UI), ShopArt (shop pictures, spade tier materials), DigSpot (the lure around the bare patch), Pause (pause menu, How to Play, blur volume), Cat, Bedroom (the ending), Fragments, Meshy (memory finds and card art), Audio.
- `Assets/ThirdParty/`: `PolyHaven/` (textures, `Models/`), `UnityTerrainDemo/` (only what is used), `Meshy/`. Do not edit files there.

## Demo mode

F12 toggles demo mode (badge at top right). Then F1 = surface with the Old Spade, F2 = start of School Days with the Garden Spade, F3 = start of Growing Up with the Steel Spade, F4 = the end of the path with the blank page in front of you, F5 = the cat's shop with a bag of stones. A jump digs a tunnel along the life path up to that point and skips the memories before it. The judging script is `DEMO.md`.

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
