# DigIn - two-minute judging demo

Before the judge arrives: launch the game (`Builds/Windows/DigIn.exe`) or press Play in the editor. The very first launch shows a short How to Play note (one click puts it away; it does not come back on later launches, and it is always in the Esc menu). Click into the window once and press **F12** to turn on demo mode (the badge appears at the top right). Demo jumps (F1-F5) put the spade in your hands.

| Time | Keys | What to show | What to say |
|---|---|---|---|
| 0:00 | (none) | The backyard at golden hour: the old house, the fence, the forest in the haze. Straight ahead, nothing tells you where to dig, but one bare patch draws the eye: a soft pool of warm light on it, a loose ring of stones and wildflowers around it, an old spade stuck upright in the dirt, a faint glint now and then. You start empty-handed. | "You start in your childhood backyard with an old spade. Nothing says 'dig here'; the light and the stones just make you curious." |
| 0:10 | Walk to the old spade, look at it, **E** (it pulls up out of the soil into your hands); on the patch "Hold left click to dig." appears, **hold left click** | Digging: the blade bites, earth flies off in real clods and piles up next to the hole. Dig sideways too. | "The ground is fully volumetric: you can dig down, sideways, through tunnels. Loose earth never floats; it falls." |
| 0:35 | Keep digging a few seconds, then look at the mineral and **E** | A mineral pops out within the first few scoops and rests glinting; aimed at, it lights up softly ("E  pick up"); E lifts it into the bag: "+1 Quartz" and its line ("Clear, like the mornings before school."). | "You sell minerals to buy better spades. But each one echoes a moment of a life." |
| 0:40 | **F5**, look at the kitten on the counter, **E** | Petting: the prompt says "E  pet"; the Munchkin kitten leans into your hand and tips its head, the purr swells, and sometimes it says a few words. | "The cat is your own pet. You can pet it." |
| 0:45 | Look at the counter, **E**. Hover a stone tile, click **Sell** on it, then **Sell all**; open **For the next dig** and click **Buy** on the Garden Spade | The family cat beside the paper price board: a tile with a rendered picture for each stone in the bag (under the mouse it lifts and shows the stone's line), the coins ticking up; then a card for every spade, bag and lantern tier: yours, the next one to buy, later ones greyed out. | "The shop is run by your own cat. Sometimes it says things a cat couldn't know." |
| 1:00 | Click **Close** (or Esc), **F2** | A tunnel at School Days depth, clay walls, lantern light. | "There is a hidden path that winds down like a life: childhood, school days, growing up. Memories lie along it." |
| 1:10 | **F3** | Stone, deeper and darker. | "The ground gets harder as you go; you need better tools." |
| 1:20 | **F4**, then click the glowing page | The blank page at the very bottom, and the ending text. | "At the bottom is today: a blank page." |
| 1:45 | (wait) | Fade to black, then you wake up in bed in the morning, the cat asleep on your chest, purring. "Morning." | "It was a dream all along: digging into your own life. Then you wake up." |
| 2:00 | Click | Back in the yard: the shelf by the door is full of memory cards. | (end) |

If time is short, skip from 0:45 straight to F4.

Other keys: **F1** back to the surface at the start, **R** up to the surface (and back down to where you were). **Esc** closes the shop, a card or a letter; otherwise it opens the pause menu (a notebook page over the blurred yard: Resume, How to Play, Settings for volume, music and sound effects (each with an on/off box and a slider, saved between launches) and mouse sensitivity, Quit Game). In the shop the number keys still work: **1** sell everything, **2** spade, **3** bag, **4** lantern.

The first time you walk past the shop before digging, the cat watches the bare patch: "Something's under the soft ground, I think."

## Review mode (checking every story moment)

**F12** cycles: off, then **Demo** (the keys above), then **Review** (a small REVIEW panel at the top right lists the keys), then off. In Review mode each key reloads the scene, so the moment starts clean and pressing the same key replays it. It works in the Editor and in the build, even over an open card or letter.

| Key | Moment |
|---|---|
| **F1-F9** | Memory 1-9, about one scoop away, with the right spade and every earlier memory already found. One dig brings the object up in the hole; click it for the card. Memories 3, 6 and 9 bring their letter right after the card. |
| **F10** | The shop counter with a full bag of mixed stones and enough coins for the next upgrade (press **E**). |
| **F11** | The bottom of the path facing the blank page: click it for the whole ending (the words, fade to black, the bedroom with the kitten asleep and purring, "Morning.", a click back to the yard with the full shelf). |

How memories are found in normal play: the first lies 1.7 m under the lit patch; after that, any scoop within about 3 m of an unfound memory brings up the next one in the story, and after each card a faint warm glow in the earth points toward the next.

Things to point out if asked:
- Digging terrain: a density field meshed with marching cubes in Burst jobs, 4 m chunks with their own colliders; a flood fill removes floating earth after every scoop.
- All art is realistic and sourced: Poly Haven scans (CC0), the Unity Terrain demo pack (SpeedTrees, grass), and Meshy models for the hero objects.
- Sound is soft recorded CC0 foley; the surface ambience muffles as you go underground.
