"""Pretends to be VRChat, so you can test MimeWalk without the game.

Give MimeWalk a test config with send_port = 19000 and listen_port = 19001, start it, then run:

    python fake_vrchat.py 19000 19001

The spare ports keep the test away from VRChat, in case the game is running.
"""

import sys
import threading
import time

from pythonosc.dispatcher import Dispatcher
from pythonosc.osc_server import BlockingOSCUDPServer
from pythonosc.udp_client import SimpleUDPClient


class FakeVRChat:
    """Records what MimeWalk sends, and sends it menu changes."""

    def __init__(self, listen_port: int, app_port: int):
        self.messages = []  # (time received, address, value)
        dispatcher = Dispatcher()
        dispatcher.set_default_handler(self.record)
        self.server = BlockingOSCUDPServer(("127.0.0.1", listen_port), dispatcher)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.app = SimpleUDPClient("127.0.0.1", app_port)

    def record(self, address: str, *args) -> None:
        self.messages.append((time.perf_counter(), address, args[0] if args else None))

    def menu(self, name: str, value) -> None:
        """Changes a setting as if from the avatar's radial menu."""
        self.app.send_message("/avatar/parameters/MimeWalk/" + name, value)

    def values(self, address: str, since: float = 0.0) -> list:
        """Everything MimeWalk sent to this address, oldest first."""
        return [value for t, a, value in self.messages if a == address and t >= since]

    def close(self) -> None:
        self.server.shutdown()
        self.server.server_close()


def main() -> None:
    if len(sys.argv) > 2:
        listen_port, app_port = int(sys.argv[1]), int(sys.argv[2])
    else:
        listen_port, app_port = 9000, 9001
    fake = FakeVRChat(listen_port, app_port)
    fake.menu("BPM", (120 - 40) / 160)  # 120 steps per minute with the default range
    fake.menu("Surge", 0.4)             # 20% with the default range
    fake.menu("Speed", 0.5)
    fake.menu("On", True)
    start = time.perf_counter()
    time.sleep(6)
    inputs = fake.values("/input/Vertical", start + 1)  # skip the speed-up
    beats = fake.values("/avatar/parameters/MimeWalk/Beat", start)
    fake.menu("On", False)
    time.sleep(1.5)
    if not inputs:
        print("Nothing received. Is MimeWalk running with these ports?")
        return
    print(f"speed: average {sum(inputs) / len(inputs):.3f} (want 0.500), "
          f"swing {max(inputs) - min(inputs):.3f} (want 0.200)")
    print(f"steps: {len(beats)} in 6 s (want about 12 at 120 bpm)")
    print(f"speed after stopping: {fake.values('/input/Vertical')[-1]} (want 0.0)")
    fake.close()


if __name__ == "__main__":
    main()
