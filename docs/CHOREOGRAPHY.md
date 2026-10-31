# Making walking in place look real

Real walking isn't a constant glide. Your body vaults over the planted foot
like an upside-down pendulum. You're slowest at midstance, when your body is
highest over the planted foot, and fastest as the next foot lands. MimeWalk
copies that rhythm.

- **Plant a foot on every beat.** The beat is when the light flashes and the
  click plays. Speed peaks right on the beat and dips halfway between beats.
- **Pick the tempo first.** Walking is usually 90–125 steps per minute.
  Matching the music in the world looks great.
- **Surge:** real walking is about 5–10%, but other players see you through
  network smoothing, so 15–30% reads better. Above about 40% starts to look
  like lurching.
- **`sharpness`** (in `config.toml`): about 1.4 is a typical walk, 1 gives a
  sharper push on each step, and 2 is a smooth, floaty sine wave.
- **Desktop players** may need to hold Run in VRChat to reach full speed.
