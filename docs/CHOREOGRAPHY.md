# Making walking in place look real

As you walk, your body vaults over the planted foot like an upside-down
pendulum. You're slowest at midstance, when your body is highest over the
planted foot, and fastest as the next foot lands. MimeWalk tries to recreate
that somewhat.

- **Plant a foot on every beat.** The beat is when the light flashes and the
  click plays. Speed peaks right on the beat and dips halfway between beats.
- **Pick the tempo first.** Walking is usually 90–125 steps per minute.
  You can also match it with music if you'd like.
- **Surge:** real walking is about 5–10%, but other players see you through
  network smoothing, so 15–30% reads better. Above about 40% starts to look
  like lurching.
- **`sharpness`** (in `config.toml`): about 1.4 is a typical walk, 1 gives a
  sharper push on each step, and 2 is a smooth, floaty sine wave.
