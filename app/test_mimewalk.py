"""Tests for mimewalk.py. Run them with: pytest"""

import math
import signal
import socket
import subprocess
import sys
import time
import tomllib
from pathlib import Path

import pytest

from fake_vrchat import FakeVRChat
from mimewalk import DEFAULT_CONFIG, Walker, load_config, surge_shape

HERE = Path(__file__).parent
BEAT = "/avatar/parameters/MimeWalk/Beat"


def walking(**settings) -> Walker:
    """A Walker with Walk on, using the default settings plus any given here."""
    walker = Walker(tomllib.loads(DEFAULT_CONFIG) | settings)
    walker.walking = True
    return walker


def walk_for(walker: Walker, seconds: float, dt: float = 1 / 120) -> list[tuple[float, bool]]:
    """Runs the Walker for this many seconds, recording its speed and foot at each moment."""
    return [(walker.tick(dt), walker.left_foot) for _ in range(round(seconds / dt))]


@pytest.mark.parametrize("sharpness", [0.8, 1.0, 1.4, 2.0, 4.0])
def test_surge_shape_averages_zero_and_spans_two(sharpness):
    values = [surge_shape(i / 2000, sharpness) for i in range(2000)]
    assert abs(sum(values) / len(values)) < 1e-3
    assert max(values) - min(values) == pytest.approx(2.0, abs=1e-3)
    assert values[0] == max(values)     # fastest as the foot lands
    assert values[1000] == min(values)  # slowest halfway through the step


def test_sharpness_2_is_a_sine_wave():
    for i in range(20):
        assert surge_shape(i / 20, 2.0) == pytest.approx(math.cos(2 * math.pi * i / 20))


def test_steps_follow_the_tempo_and_alternate_feet():
    feet = [left for _, left in walk_for(walking(bpm=120), 9.9)]
    assert feet[0] is True  # the left foot lands as you start
    assert sum(a != b for a, b in zip(feet, feet[1:])) == 19  # a new step every 0.5 s


def test_average_speed_and_swing():
    inputs = [value for value, _ in walk_for(walking(bpm=100, surge=0.3, speed=0.5, ramp_seconds=0), 6.0)]  # 10 steps
    assert sum(inputs) / len(inputs) == pytest.approx(0.5, rel=0.01)
    assert max(inputs) - min(inputs) == pytest.approx(2 * 0.3 * 0.5, rel=0.02)


def test_stopping_slows_to_zero_and_resets_the_feet():
    walker = walking()
    walk_for(walker, 2.0)
    walker.walking = False
    assert walk_for(walker, walker.config["ramp_seconds"] + 0.05)[-1] == (0.0, False)


def test_radials_map_to_the_config_ranges_and_back():
    walker = walking()
    walker.set_from_menu("BPM", 0.5)
    walker.set_from_menu("Surge", 1.0)
    walker.set_from_menu("Speed", 0.25)
    assert (walker.bpm, walker.surge, walker.speed) == (120, 0.5, 0.25)
    assert walker.menu_values() == {"On": True, "BPM": 0.5, "Surge": 1.0, "Speed": 0.25}


def test_config_is_created_and_partial_files_keep_defaults(tmp_path):
    path = tmp_path / "config.toml"
    assert load_config(path) == tomllib.loads(DEFAULT_CONFIG)
    path.write_text("bpm = 90\nprefix = 'old setting, ignored'\n")
    assert load_config(path)["bpm"] == 90 and load_config(path)["speed"] == 0.5
    path.write_text("bpm = 95\n", encoding="utf-8-sig")  # with the hidden marker some Windows editors add
    assert load_config(path)["bpm"] == 95


@pytest.mark.parametrize("text", ['bpm = "fast"', "speed = true", "bpm_max = 30", "surge_max = 0"])
def test_bad_config_values_are_explained(tmp_path, text):
    path = tmp_path / "config.toml"
    path.write_text(text + "\n")
    with pytest.raises(ValueError):
        load_config(path)


def free_port() -> int:
    """Finds a port that no other program is using."""
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def test_app_walks_from_the_menu_and_always_stops(tmp_path):
    send, listen = free_port(), free_port()
    config = tmp_path / "config.toml"
    config.write_text(f"send_port = {send}\nlisten_port = {listen}\n")
    fake = FakeVRChat(send, listen)
    app = subprocess.Popen([sys.executable, str(HERE / "mimewalk.py"), str(config)], stdout=subprocess.DEVNULL,
                           creationflags=getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
    try:
        time.sleep(1.5)
        # On start, the app sets the menu to its settings: Walk off, Tempo at 110 bpm.
        assert fake.values("/avatar/parameters/MimeWalk/On") == [False]
        assert fake.values("/avatar/parameters/MimeWalk/BPM") == [pytest.approx((110 - 40) / 160)]

        fake.menu("On", True)
        time.sleep(2.0)
        assert max(fake.values("/input/Vertical")) > 0.3
        assert fake.values(BEAT).count(True) >= 2  # left steps, with right steps in between

        fake.menu("On", False)  # stopping from the menu
        time.sleep(1.0)
        assert fake.values("/input/Vertical")[-1] == 0.0
        assert fake.values(BEAT)[-1] is False

        fake.menu("On", True)  # closing the window while walking
        time.sleep(1.0)
        app.send_signal(signal.CTRL_BREAK_EVENT if sys.platform == "win32" else signal.SIGINT)
        app.wait(timeout=5)
        time.sleep(0.2)
        assert fake.values("/input/Vertical")[-1] == 0.0
    finally:
        if app.poll() is None:
            app.kill()
        fake.close()
