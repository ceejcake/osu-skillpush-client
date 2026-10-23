# osu! skillpush client

A personal fork of [osu!lazer](https://github.com/ppy/osu) focused on **practice and improvement**: tools for finding out *why* you're missing, a reworked spread-out mod, better collection management, and an offline profile that tracks your pp from local scores.

> This is an unofficial fork. It is not affiliated with or endorsed by ppy. For the official game, see [osu.ppy.sh](https://osu.ppy.sh) and [ppy/osu](https://github.com/ppy/osu).

The custom work lives on the `custom-features` branch, on top of upstream lazer.

---

## Practice tools

| Feature | How to use it |
|---|---|
| **Seek during a play** | **Left / Right** jump 5 seconds back or forward. Hold to keep seeking. A play you've seeked through shows results but isn't saved or submitted. |
| **Strain graph** | **Tab** pauses and shows the map's difficulty over time. Click anywhere on it to jump there. |
| **Misses from your last full run** | The strain graph marks where you missed (red lines) in your last complete playthrough of the map. Restarts and partial or seeked runs don't overwrite it; it clears when you play a different map. |
| **Practice checkpoint** | **F7** sets a point that retries start from. Clicking the strain graph moves it; F7 again clears it. |
| **Break skip** | A skip button appears during breaks with at least 3 seconds left. |
| **Music pitch** | The **Pitch** button in the mod menu changes the music's pitch without changing its speed (0.5x–2.0x). In the mod menu, the **arrow keys** also change it in 0.05 steps. |
| **Stream practice maps** | Right-click a beatmap in song select → **Generate stream practice map**. Makes a 1/4 stream map over the same song: one continuous stream, or bursts with rests. Settings → User Interface → Stream practice maps. |

## Aim feedback

These help you tell whether you're **lagging behind** or **overshooting**.

| Feature | How to use it |
|---|---|
| **Aim glow** | The sides of the screen glow **red** when you start missing because your aim is lagging behind, and **blue** when it's going too fast. Misses in quick succession build up a stronger glow. Settings → Gameplay → HUD → *Glow when aim causes misses* (on by default). |
| **Misaim marker** | **Ctrl+M** shows an X where you tapped when your cursor was off the note. **Ctrl+Shift+M** adds the missed circle, a line to your tap, and how far over or under you aimed. Also under Settings → Gameplay → General. |
| **Missed presses on the UR bar** | Taps that land on time but miss the note (cursor off it), and taps so early they count as a miss, show as red lines on the hit error bar. Settings → Gameplay → HUD → *Show missed presses on hit error bar*. |
| **Hit error numbers on 100s and 50s** | 100s and 50s show how many milliseconds early or late you were instead of the usual judgement. Settings → Gameplay → General. |
| **Hit error bar size** | Make bar hit error meters the same size on every skin. Settings → Gameplay → HUD. |

## Mods

| Mod / change | What it does |
|---|---|
| **Cross Screen (XS)** | Spreads the map across the whole playfield. Jumps between patterns get bigger while streams, triples and stacks keep their shape. It keeps the mapper's shapes and the contrast between calm and intense sections, stays off the playfield edges, and never stacks notes the mapper didn't. Optional *Scale sliders*. **Ctrl+Left / Ctrl+Right** in song select change its spacing. |
| **HR but only the CS (HRC)** | Hard Rock's smaller circles and vertical flip, without changing OD, AR or HP. Last in the Difficulty Increase list, with an upside-down HR icon. |
| **Score V2** | Scores the play the way osu!stable's Score V2 did. |
| **Score V2 by default** | Settings → Gameplay → General → *Use Score V2 by default* scores every play as Score V2. The mod menu then offers **Lazer Scoring** to score a play the usual way instead. Past scores aren't changed. |
| **BPM lock** | DT, NC, HT and DC can play every map at one target BPM. **F4** in song select sets it. |
| **Difficulty Adjust** | Each stat has its own on/off toggle, it's applied after every other mod, it can keep AR/OD feeling the same under DT/HT, and it stacks with EZ and HR. |
| **Mirror rotation** | Mirror can also rotate the map a quarter turn, stretched to fill the playfield. |

## Song select & collections

| Feature | How to use it |
|---|---|
| **Sort and filter with mods** | The **Sort with mods** toggle in the mod menu makes star rating filters, sorting and grouping use ratings with your current mods, for the selected collection. |
| **Collection folders** | Manage Collections works like a file explorer: folder tiles, a path bar, and drag and drop for folders and collections (drag a collection by its handle). Folders can be nested. |
| **Select a folder** | In the song select collection dropdown, selecting a folder shows every map in it, including subfolders. Click the folder icon to expand or collapse it. |
| **Import from osu!collector** | Settings → Maintenance → Collections. Paste any number of URLs or IDs. Missing maps are downloaded. |
| **Update osu!collector collections** | Re-fetches your imported collections, adds maps added since, and downloads anything still missing. Nothing is removed. |
| **Create or extend a collection from beatmap IDs** | Paste IDs or links into Settings → Maintenance → Collections, or use the paste button on a collection in Manage Collections. Duplicates and maps already in the collection are skipped, and missing maps are downloaded. |
| **Repair collections** | Settings → Maintenance → Collections. Finds every map in your collections that you don't have, puts in the version you have where possible, and downloads the rest. Results are cached, so later runs are quick. |
| **Missing count** | Manage Collections shows how many maps in each collection aren't downloaded. |

When you're not logged in, maps are downloaded from public mirrors (osu.direct, sayobot, beatconnect and nerinyan). They limit how much you can download, so big imports can take a while. The importer pauses and resumes when a mirror asks it to slow down.

## Profile & skins

| Feature | How to use it |
|---|---|
| **Guest player** | Settings → General → Guest player: set a name and profile picture to use while logged out. |
| **Offline profile** | While logged out, the login panel shows your pp, accuracy and best plays, worked out from your local scores the same way osu! does online. *Count unranked plays* includes everything. |
| **Today's top 5** | Click your total pp on the offline profile to switch to your top 5 plays of the day. Days reset at 4 AM. |
| **Favourite skins** | Click the heart next to a skin in the skin dropdown to pin it to the top. |
| **Cursor size per skin** | Each skin remembers its own gameplay cursor size. |

## Key bindings

All of these can be changed in Settings → Input → Key bindings.

| Key | Action | Where |
|---|---|---|
| Left / Right | Seek 5 seconds back / forward | Gameplay |
| Tab | Strain graph | Gameplay |
| Ctrl+Tab | In-game leaderboard (moved from Tab) | Gameplay |
| F7 | Practice checkpoint | Gameplay |
| Ctrl+M | Misaim marker | Gameplay |
| Ctrl+Shift+M | Misaim marker details | Gameplay |
| F4 | BPM lock | Song select |
| Ctrl+Left / Ctrl+Right | Cross Screen spacing | Song select, mod menu |
| Arrow keys | Music pitch | Mod menu |

---

## Building

You need the [.NET 8.0 SDK](https://dotnet.microsoft.com/download).

```shell
git clone https://github.com/ceejcake/osu-skillpush-client
cd osu-skillpush-client
git checkout custom-features
dotnet run --project osu.Desktop -c Release
```

To work on it in an IDE, open `osu.Desktop.slnf` rather than the main `.sln`. The `osu! (Tests)` configuration runs the visual test browser, and most custom features have tests there.

For everything else about building, testing and the codebase, see the [upstream README](https://github.com/ppy/osu#readme).

## Licence

*osu!*'s code and framework are licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please see [the licence file](LICENCE) for more information. [tl;dr](https://tldrlegal.com/license/mit-license) you can do whatever you want as long as you include the original copyright and license notice in any copy of the software/source.

Please note that this *does not cover* the usage of the "osu!" or "ppy" branding in any software, resources, advertising or promotion, as this is protected by trademark law.

Please also note that game resources are covered by a separate licence. Please see the [ppy/osu-resources](https://github.com/ppy/osu-resources) repository for clarifications.
