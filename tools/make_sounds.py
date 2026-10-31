"""Makes the MimeWalk click sounds.

Writes MimeWalk_Tick.wav (left foot) and MimeWalk_Tock.wav (right foot) into the Unity package's
Audio folder. Each click is under 60 ms and starts and ends silent, so it never pops.

    python tools/make_sounds.py
"""

import math
import struct
import wave
from pathlib import Path

RATE = 44100  # samples per second
PEAK = 0.89  # loudness, just under the maximum of 1
OUT = Path(__file__).resolve().parent.parent / "unity" / "MimeWalkDev" / "Assets" / "MimeWalk" / "Audio"


def click(seconds: float, tones: list[tuple[float, float, float]], decay: float, fade: float) -> list[float]:
    """Makes one click from a few tones that die away quickly.

    Each tone is (pitch in Hz, volume, seconds to die away). decay is how fast the whole click dies
    away, and fade is how long the final fade to silence takes.
    """
    length = int(seconds * RATE)
    attack = max(2, int(0.0008 * RATE))  # fade in over 0.8 ms
    fade_length = max(2, int(fade * RATE))
    samples = []
    for i in range(length):
        t = i / RATE
        tone = sum(volume * math.sin(2 * math.pi * pitch * t) * math.exp(-t / die_away)
                   for pitch, volume, die_away in tones)
        fade_in = 0.5 - 0.5 * math.cos(math.pi * i / attack) if i < attack else 1.0
        fade_out = 1.0
        if i >= length - fade_length:
            fade_out = 0.5 + 0.5 * math.cos(math.pi * (i - (length - fade_length)) / fade_length)
        samples.append(tone * fade_in * math.exp(-t / decay) * fade_out)
    samples[0] = samples[-1] = 0.0
    loudest = max(abs(v) for v in samples) or 1.0
    return [v * PEAK / loudest for v in samples]


# Left foot: higher and shorter. Right foot: lower and longer.
SOUNDS = {
    "MimeWalk_Tick": lambda: click(0.045, [(2200, 1.0, 0.012), (4400, 0.35, 0.006), (3300, 0.2, 0.008)], 0.015, 0.010),
    "MimeWalk_Tock": lambda: click(0.055, [(1350, 1.0, 0.016), (2700, 0.3, 0.008), (2025, 0.18, 0.010)], 0.020, 0.012),
}


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in SOUNDS.items():
        samples = make()
        path = OUT / f"{name}.wav"
        with wave.open(str(path), "wb") as wav:
            wav.setnchannels(1)  # mono
            wav.setsampwidth(2)  # 16-bit
            wav.setframerate(RATE)
            wav.writeframes(b"".join(struct.pack("<h", int(round(v * 32767))) for v in samples))
        print(f"{path.name}: {len(samples) / RATE * 1000:.0f} ms")


if __name__ == "__main__":
    main()
