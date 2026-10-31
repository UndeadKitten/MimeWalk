"""MimeWalk: makes walking in place look more realistic in VRChat.

Run it next to VRChat with OSC turned on. You can control it through your radial menu in-game,
start and stop walking, set the tempo, surge, and speed. Everything else is in config.toml.

Usage: python mimewalk.py [path/to/config.toml]
"""

import math
import sys
import threading
import time
import tomllib
from functools import cache
from pathlib import Path

from pythonosc.dispatcher import Dispatcher
from pythonosc.osc_server import BlockingOSCUDPServer
from pythonosc.udp_client import SimpleUDPClient

PARAMS = "/avatar/parameters/MimeWalk/"  # start of every message to and from the avatar's MimeWalk menu
SEND_RATE = 60  # speed updates sent to VRChat per second

DEFAULT_CONFIG = """\
# MimeWalk settings. Save, then restart MimeWalk to use them.

# Starting values. The Tempo, Surge and Speed dials in your radial menu change these in-game.
bpm = 110            # steps per minute
surge = 0.20         # how much your speed rises and falls each step (0.2 = about 20% above and below average)
speed = 0.5          # average speed, 0 to 1 (1 = thumbstick pushed all the way)

# Shape of each surge: 2 = smooth, 1 = sharp kick on each step, 1.4 = most like real walking.
sharpness = 1.4

ramp_seconds = 0.5   # seconds to speed up from standing, and to slow down to a stop

# What the lowest and highest positions of the radial menu dials mean.
bpm_min = 40
bpm_max = 200
surge_max = 0.5

# Ports for talking to VRChat. Only change these if another OSC app needs them.
send_port = 9000
listen_port = 9001
"""


# --- Settings ---

def config_path() -> Path:
    """Finds config.toml: next to MimeWalk.exe, or next to this file when run with Python."""
    program = sys.executable if getattr(sys, "frozen", False) else __file__
    return Path(program).parent / "config.toml"


def load_config(path: Path) -> dict:
    """Reads the settings file, and creates it the first time.

    Settings missing from the file use the defaults, and unknown ones are ignored.
    A bad value raises an error that names the setting.
    """
    if not path.exists():
        path.write_text(DEFAULT_CONFIG, encoding="utf-8")

    config = tomllib.loads(DEFAULT_CONFIG)
    # utf-8-sig skips the hidden marker some Windows editors add to the start of a file.
    for key, value in tomllib.loads(path.read_text(encoding="utf-8-sig")).items():
        if key not in config:
            continue
        if isinstance(value, bool) or not isinstance(value, (int, float)):  # Python counts true/false as numbers
            raise ValueError(f"{key} should be a number, not {value!r}")
        config[key] = value

    if config["sharpness"] <= 0 or config["surge_max"] <= 0:
        raise ValueError("sharpness and surge_max must be above 0")
    if config["bpm_max"] <= config["bpm_min"]:
        raise ValueError("bpm_max must be higher than bpm_min")
    return config


# --- Walking rhythm ---

def clamp01(x: float) -> float:
    """Keeps x between 0 and 1."""
    return min(1.0, max(0.0, x))


@cache  # sharpness doesn't change while running, so this is only worked out once
def average_of_sine_power(p: float) -> float:
    """The average of |sin(πx)|^p over one step. Keeps your average speed the same at any sharpness."""
    return math.gamma((p + 1) / 2) / (math.sqrt(math.pi) * math.gamma(p / 2 + 1))


def surge_shape(phase: float, sharpness: float) -> float:
    """How much faster or slower than average to go at this point in a step.

    Phase is how far through the step you are: 0 as a foot lands, 0.5 halfway, 1 as the next foot
    lands. Like real walking, speed is highest as a foot lands and lowest halfway. The highest and
    lowest values are always 2 apart and average 0, so surge sets the size of the swing without
    changing your average speed.
    """
    return 2 * (average_of_sine_power(sharpness) - abs(math.sin(math.pi * phase)) ** sharpness)


class Walker:
    """Keeps the beat and works out your speed at each moment."""

    def __init__(self, config: dict):
        self.config = config
        self.bpm = config["bpm"]
        self.surge = config["surge"]
        self.speed = config["speed"]
        self.walking = False
        self.level = 0.0  # 0 = standing, 1 = full speed, in between while speeding up or slowing down
        self.phase = 0.0  # how far through the current step, 0 to 1
        self.steps = 0    # steps since you started walking; 0 = standing still

    @property
    def left_foot(self) -> bool:
        """True during left steps. The first step is always the left foot."""
        return self.steps % 2 == 1

    def tick(self, dt: float) -> float:
        """Moves time forward by dt seconds and returns the speed to send to VRChat, 0 to 1."""
        # Speed up or slow down evenly over ramp_seconds.
        ramp_seconds = self.config["ramp_seconds"]
        change = dt / ramp_seconds if ramp_seconds > 0 else 1.0
        if self.walking:
            self.level = min(1.0, self.level + change)
        else:
            self.level = max(0.0, self.level - change)

        if self.level == 0:
            self.phase, self.steps = 0.0, 0
            return 0.0

        if self.steps == 0:
            self.steps = 1  # just started: the first foot lands right away
        self.phase += dt * self.bpm / 60  # bpm / 60 = steps per second
        while self.phase >= 1:  # the next foot lands
            self.phase -= 1
            self.steps += 1

        surge = self.surge * surge_shape(self.phase, self.config["sharpness"])
        return clamp01(self.speed * self.level * (1 + surge))

    def set_from_menu(self, name: str, value) -> None:
        """Applies a change from the radial menu. Dials send 0 to 1, scaled here to the ranges in config.toml."""
        config = self.config
        match name:
            case "On":
                self.walking = bool(value)
            case "BPM":
                self.bpm = config["bpm_min"] + (config["bpm_max"] - config["bpm_min"]) * value
            case "Surge":
                self.surge = config["surge_max"] * value
            case "Speed":
                self.speed = value

    def menu_values(self) -> dict:
        """The radial menu positions for the current settings. The reverse of set_from_menu."""
        config = self.config
        return {
            "On": self.walking,
            "BPM": clamp01((self.bpm - config["bpm_min"]) / (config["bpm_max"] - config["bpm_min"])),
            "Surge": clamp01(self.surge / config["surge_max"]),
            "Speed": clamp01(self.speed),
        }


# --- Talking to VRChat ---

class App:
    """Sends your speed to VRChat over OSC, and listens for changes from your radial menu."""

    def __init__(self, config: dict):
        self.walker = Walker(config)
        self.vrchat = SimpleUDPClient("127.0.0.1", config["send_port"])
        self.stopping = threading.Event()  # set when the window is closing
        self.value = 0.0  # the speed last sent, for the status line
        dispatcher = Dispatcher()
        dispatcher.set_default_handler(self.on_osc_message)
        # Fails here if another program is already using the listen port.
        self.server = BlockingOSCUDPServer(("127.0.0.1", config["listen_port"]), dispatcher)

    def send_param(self, name: str, value) -> None:
        self.vrchat.send_message(PARAMS + name, value)

    def on_osc_message(self, address: str, *args) -> None:
        if address == "/avatar/change":
            # You switched avatars. Stop walking, and set the new avatar's menu once it has loaded.
            self.walker.walking = False
            threading.Timer(2.0, self.update_menu).start()
        elif address.startswith(PARAMS) and args:
            self.walker.set_from_menu(address.removeprefix(PARAMS), args[0])

    def update_menu(self) -> None:
        """Sets the avatar's menu to match the settings MimeWalk is using."""
        for name, value in self.walker.menu_values().items():
            self.send_param(name, value)

    def stop_moving(self) -> None:
        """Stops your avatar, and resets Beat so the next walk starts on the left foot."""
        self.vrchat.send_message("/input/Vertical", 0.0)
        self.send_param("Beat", False)

    def close(self) -> None:
        """Runs when you close the window or press Ctrl+C. Ends the loop first, so the stop is sent last."""
        self.stopping.set()
        time.sleep(0.2)
        self.stop_moving()

    def run(self) -> None:
        """Sends your speed to VRChat until the window closes."""
        threading.Thread(target=self.server.serve_forever, daemon=True).start()  # listen in the background
        threading.Thread(target=self.show_status, daemon=True).start()
        self.stop_moving()  # in case MimeWalk was closed mid-walk last time
        self.update_menu()

        walker = self.walker
        was_moving = False
        sent_left_foot = None
        last = time.perf_counter()
        try:
            while not self.stopping.is_set():
                now = time.perf_counter()
                value = walker.tick(now - last)
                self.value = value
                last = now

                # Send speed while moving, then one last 0 to stop.
                if value > 0 or was_moving:
                    self.vrchat.send_message("/input/Vertical", value)
                was_moving = value > 0

                # Tell the avatar each time a new step starts, for the light and click.
                if walker.left_foot != sent_left_foot:
                    sent_left_foot = walker.left_foot
                    self.send_param("Beat", sent_left_foot)
                time.sleep(1 / SEND_RATE)
        except KeyboardInterrupt:  # Ctrl+C
            pass
        finally:
            self.stop_moving()  # always, however the loop ends
            self.server.shutdown()
            print()

    def show_status(self) -> None:
        """Updates the status line 4 times a second.

        It runs apart from the main loop, so if the window's output is paused, only the status line
        stops. Your movement keeps being sent, and stopping still works.
        """
        walker = self.walker
        while not self.stopping.wait(0.25):
            state = "WALKING" if walker.walking else "stopped"
            foot = ("L" if walker.left_foot else "R") if walker.steps else "-"
            print(f"\r{state}  {walker.bpm:5.1f} bpm  surge {walker.surge:5.1%}  speed {walker.speed:.2f}  "
                  f"input {self.value:.2f}  {foot}  ", end="", flush=True)


# --- Console window ---

def setup_windows_console(on_close) -> None:
    """Runs on_close when you close the window or press Ctrl+C, so you aren't left walking.

    Also turns off QuickEdit. With it on, clicking in the window freezes the status line until you
    press Esc.
    """
    if sys.platform != "win32":
        return
    import ctypes  # lets Python call Windows directly

    STD_INPUT_HANDLE = -10
    ENABLE_QUICK_EDIT_MODE = 0x40
    ENABLE_EXTENDED_FLAGS = 0x80  # needed for the QuickEdit change to stick

    kernel32 = ctypes.windll.kernel32
    console = kernel32.GetStdHandle(STD_INPUT_HANDLE)
    mode = ctypes.c_uint()
    if kernel32.GetConsoleMode(console, ctypes.byref(mode)):
        kernel32.SetConsoleMode(console, (mode.value & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS)

    @ctypes.WINFUNCTYPE(ctypes.c_int, ctypes.c_uint)
    def handler(event):
        on_close()
        return 1  # handled, so the main loop can finish on its own

    kernel32.SetConsoleCtrlHandler(handler, True)
    setup_windows_console.handler = handler  # keep it, or Python would delete it while Windows still needs it


def fail(message: str) -> int:
    """Shows an error. In the exe, waits for Enter so the window doesn't close before you can read it."""
    print(message)
    if getattr(sys, "frozen", False):
        try:
            input("Press Enter to close.")
        except EOFError:  # no keyboard (started by another program)
            pass
    return 1


def main() -> int:
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else config_path()
    try:
        config = load_config(path)
    except (OSError, tomllib.TOMLDecodeError, ValueError) as error:
        return fail(f"Problem with {path}: {error}")

    try:
        app = App(config)
    except OSError as error:
        return fail(f"Can't listen on port {config['listen_port']} (is another OSC app using it?): {error}")

    setup_windows_console(app.close)
    print(f"MimeWalk: settings in {path}")
    print("Use the avatar's MimeWalk menu to walk. Close this window (or press Ctrl+C) to quit.")
    app.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
