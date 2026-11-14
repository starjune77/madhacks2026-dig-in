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

- Use only the new Input System (`Keyboard.current`, `Mouse.current`). The legacy `Input` class throws errors in this project.
- Do not create materials with `Shader.Find` (it can be stripped from builds). Clone the default primitive material and set `_BaseColor` (see `DigGame.MakeMaterial`).
- Keep manual editor setup to a minimum and build things from code where possible. If a manual step is required, give exact steps.
- Claude cannot run Unity. After each change, briefly say what to check in Play mode.
- Do not touch `Library/`, `Temp/` or `Logs/`.
- All in-game text is in English.
- **No sound.** Do not add any audio (procedural or files) until the developer explicitly asks for it.

## Current code

Everything is built at runtime; the scene only holds the "Game" object and the Main Camera.

- `Assets/Scripts/DigGame.cs`: attached to the "Game" object in SampleScene. Builds the yard, the terrain and the player. Holds the rules and state: scrap, shovel level, memory objects, strike handling. All tuning constants (stratum depth, shovel power/radius, upgrade costs) are at the top. Temporary OnGUI HUD.
- `Assets/Scripts/DigTerrain.cs`: the shaft floor as a 21 x 21 heightmap mesh (5 m, 0.25 m cells). `Dig()` lowers vertices with a flat core and smooth rim, clamped to the current shovel's floor, then rebuilds the MeshCollider. Also draws the layered shaft walls.
- `Assets/Scripts/DigPlayer.cs`: first-person movement (acceleration, slope projection, sliding on steep slopes), look, swing timing, camera smoothing and kick.
- `Assets/Scripts/DigShovel.cs`: first-person shovel models (one per level) and the keyframed swing.
- `Assets/Scripts/DigDebris.cs`: pooled flying dirt clods, simulated by hand.

Notes:

- Strata colours use a generated gradient texture looked up by depth (UV.y), not vertex colours: the default URP Lit material ignores vertex colours, and a custom shader would need `Shader.Find` or untested HLSL.
- Memory objects appear at fixed depths, at whatever spot the player is digging when they first reach that depth. Picking one up currently shows its line as HUD text (placeholder for the card).

## Digging feel (roadmap step 1)

The first prototype used 1 m cube blocks that vanish whole. It felt like Minecraft, not like digging, and movement was choppy. The goal is for it to feel like really digging into soil, satisfying and smooth.

Direction:

- Replace the cube grid with a single deformable heightmap mesh for the shaft floor: 5 m x 5 m, about 0.25 m grid spacing. Each shovel stroke lowers the vertices around the aim point with a smooth falloff, leaving a rounded scoop. The shaft is vertical, so no overhangs are needed. Update the MeshCollider after each stroke.
- Use vertex colors by depth to show dirt, clay and stone.
- Implement "too hard for this shovel" by clamping how low vertices can go for the current shovel level.
- Feel, in priority order: dirt particles and flying clumps -> a visible shovel with a swing animation -> camera kick -> object discovery moment.
- Player movement should have acceleration and deceleration and handle the dug slopes smoothly.
- Status: first pass implemented, needs Play mode tuning.
- The cube version is committed (`7579305`), so we can revert if the new approach is not working within 2-3 hours.

## Demo mode (required later)

F1-F4 jump to the start of each stratum and to the ending, enabled only in a separate DemoMode. Target demo flow: 30 seconds of digging -> first object -> jump to the ending.

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

1. Digging feel (above)
2. Three memory objects + card display
3. Letters, shelf and ending, playable start to finish
4. Remaining six objects
5. Particles, lighting polish (sound only if the developer asks)
6. Demo mode, bug fixes
7. Build and Devpost submission

If step 3 is not done by Saturday 10:30 PM, cut to three objects and one letter.
