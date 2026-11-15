# DigIn - two-minute judging demo

Before the judge arrives: launch the game (`Builds/Windows/DigIn.exe`) or press Play in the editor. The very first launch shows a short How to Play note (one click puts it away; it does not come back on later launches, and it is always in the Esc menu). Click into the window once and press **F12** to turn on demo mode (the badge appears at the top right).

| Time | Keys | What to show | What to say |
|---|---|---|---|
| 0:00 | (none) | The backyard at golden hour: the old house, the fence, the forest in the haze. Straight ahead, nothing tells you where to dig, but one bare patch draws the eye: a soft pool of warm light on it, a loose ring of stones and wildflowers around it, a trowel stuck in the dirt, a faint glint now and then. | "You start in your childhood backyard with an old spade. Nothing says 'dig here'; the light and the stones just make you curious." |
| 0:10 | Walk onto the bare patch (only there, "Hold left click to dig." appears), **hold left click** | Digging: the blade bites, earth flies off in real clods and piles up next to the hole. Dig sideways too. | "The ground is fully volumetric: you can dig down, sideways, through tunnels. Loose earth never floats; it falls." |
| 0:35 | Keep digging a few seconds | A mineral pops out with a glint; walking over it shows its line ("Quartz: clear, like the mornings before school"). | "You sell minerals to buy better spades. But each one echoes a moment of a life." |
| 0:45 | **F5**, then **E**. Hover a stone tile, click **Sell** on it, then **Sell all**; open **For the next dig** and click **Buy** on the Garden Spade | The family cat beside the paper price board: a tile with a rendered picture for each stone in the bag (under the mouse it lifts and shows the stone's line), the coins ticking up; then a card for every spade, bag and lantern tier: yours, the next one to buy, later ones greyed out. | "The shop is run by your own cat. Sometimes it says things a cat couldn't know." |
| 1:00 | Click **Close** (or Esc), **F2** | A tunnel at School Days depth, clay walls, lantern light. | "There is a hidden path that winds down like a life: childhood, school days, growing up. Memories lie along it." |
| 1:10 | **F3** | Stone, deeper and darker. | "The ground gets harder as you go; you need better tools." |
| 1:20 | **F4**, then click the glowing page | The blank page at the very bottom, and the ending text. | "At the bottom is today: a blank page." |
| 1:45 | (wait) | Fade to black, then you wake up in bed in the morning, the cat asleep on your chest, purring. "Morning." | "It was a dream all along: digging into your own life. Then you wake up." |
| 2:00 | Click | Back in the yard: the shelf by the door is full of memory cards. | (end) |

If time is short, skip from 0:45 straight to F4.

Other keys: **F1** back to the surface at the start, **R** up to the surface (and back down to where you were). **Esc** closes the shop, a card or a letter; otherwise it opens the pause menu (a notebook page over the blurred yard: Resume, How to Play, Settings for volume, music and sound effects (each with an on/off box and a slider, saved between launches) and mouse sensitivity, Quit Game). In the shop the number keys still work: **1** sell everything, **2** spade, **3** bag, **4** lantern.

The first time you walk past the shop before digging, the cat watches the bare patch: "Something's under the soft ground, I think."

Things to point out if asked:
- Digging terrain: a density field meshed with marching cubes in Burst jobs, 4 m chunks with their own colliders; a flood fill removes floating earth after every scoop.
- All art is realistic and sourced: Poly Haven scans (CC0), the Unity Terrain demo pack (SpeedTrees, grass), and Meshy models for the hero objects.
- Sound is soft recorded CC0 foley; the surface ambience muffles as you go underground.
