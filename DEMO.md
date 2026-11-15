# DigIn - two-minute judging demo

Before the judge arrives: launch the game (`Builds/Windows/DigIn.exe`) or press Play in the editor, click into the window once, and press **F12** to turn on demo mode (the badge appears at the top right).

| Time | Keys | What to show | What to say |
|---|---|---|---|
| 0:00 | (none) | The backyard at golden hour: the old house, the fence, the forest in the haze. Straight ahead, a small hand-painted "Dig here" sign, a loose ring of stones around a bare patch, and a trowel stuck in the dirt. | "You start in your childhood backyard with an old spade. It looks like an ordinary digging game." |
| 0:10 | Walk to the bare patch (the prompt "Hold left click to dig." appears), **hold left click** | Digging: the blade bites, earth flies off in real clods and piles up next to the hole. Dig sideways too. | "The ground is fully volumetric: you can dig down, sideways, through tunnels. Loose earth never floats; it falls." |
| 0:35 | Keep digging a few seconds | A mineral pops out with a glint; walking over it shows its line ("Quartz: clear, like the mornings before school"). | "You sell minerals to buy better spades. But each one echoes a moment of a life." |
| 0:45 | **F5**, then **E**; click **Sell** on a stone, then **Sell all**, then **Buy** the Garden Spade | The family cat on the shop counter, the hand-written price board with real buttons (Buy is greyed out when you can't afford it). | "The shop is run by your own cat. Sometimes it says things a cat couldn't know." |
| 1:00 | Click **Close** (or Esc), **F2** | A tunnel at School Days depth, clay walls, lantern light. | "There is a hidden path that winds down like a life: childhood, school days, growing up. Memories lie along it." |
| 1:10 | **F3** | Stone, deeper and darker. | "The ground gets harder as you go; you need better tools." |
| 1:20 | **F4**, then click the glowing page | The blank page at the very bottom, and the ending text. | "At the bottom is today: a blank page." |
| 1:45 | (wait) | Fade to black, then you wake up in bed in the morning, the cat asleep on your chest, purring. "Morning." | "It was a dream all along: digging into your own life. Then you wake up." |
| 2:00 | Click | Back in the yard: the shelf by the door is full of memory cards. | (end) |

If time is short, skip from 0:45 straight to F4.

Other keys: **F1** back to the surface at the start, **R** up to the surface (and back down to where you were), **Esc** frees the mouse. In the shop the number keys still work: **1** sell everything, **2** spade, **3** bag, **4** lantern.

The first time you walk past the shop before digging, the cat nods at the sign: "The soft ground is over there."

Things to point out if asked:
- Digging terrain: a density field meshed with marching cubes in Burst jobs, 4 m chunks with their own colliders; a flood fill removes floating earth after every scoop.
- All art is realistic and sourced: Poly Haven scans (CC0), the Unity Terrain demo pack (SpeedTrees, grass), and Meshy models for the hero objects.
- Sound is soft recorded CC0 foley; the surface ambience muffles as you go underground.
