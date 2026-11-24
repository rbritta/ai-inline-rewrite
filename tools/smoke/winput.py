"""Real keyboard and mouse input for the smoke test, through SendInput (what a person's typing looks like to AIR,
except for the injected flag, which AIR accepts only when started with --accept-injected)."""
import ctypes
import ctypes.wintypes as W
import json
import os
import subprocess
import time

user32 = ctypes.WinDLL('user32', use_last_error=True)
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # per-monitor aware: window rects in real pixels
user32.VkKeyScanW.restype = ctypes.c_short
user32.SendMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]
user32.SendMessageW.restype = ctypes.c_ssize_t

INPUT_MOUSE, INPUT_KEYBOARD = 0, 1
KEYUP, EXTENDED, UNICODE = 0x2, 0x1, 0x4
VK = {'shift': 0x10, 'ctrl': 0x11, 'rctrl': 0xA3, 'lctrl': 0xA2, 'rshift': 0xA1, 'enter': 0x0D, 'esc': 0x1B, 'up': 0x26, 'down': 0x28,
      'end': 0x23, 'back': 0x08, 'z': 0x5A, 'a': 0x41}
EXTENDED_KEYS = {0xA3, 0x26, 0x28, 0x23}


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [('wVk', W.WORD), ('wScan', W.WORD), ('dwFlags', W.DWORD), ('time', W.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', W.LONG), ('dy', W.LONG), ('mouseData', W.DWORD), ('dwFlags', W.DWORD), ('time', W.DWORD),
                ('dwExtraInfo', ctypes.c_size_t)]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [('uMsg', W.DWORD), ('wParamL', W.WORD), ('wParamH', W.WORD)]


class _U(ctypes.Union):
    _fields_ = [('ki', KEYBDINPUT), ('mi', MOUSEINPUT), ('hi', HARDWAREINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', W.DWORD), ('u', _U)]


def _send(*inputs):
    arr = (INPUT * len(inputs))(*inputs)
    sent = user32.SendInput(len(inputs), arr, ctypes.sizeof(INPUT))
    if sent != len(inputs):
        raise OSError(f'SendInput sent {sent} of {len(inputs)}: {ctypes.get_last_error()}')


def _key(vk, up=False, scan=0, uni=False):
    flags = (KEYUP if up else 0) | (EXTENDED if vk in EXTENDED_KEYS else 0) | (UNICODE if uni else 0)
    i = INPUT(type=INPUT_KEYBOARD)
    i.u.ki = KEYBDINPUT(vk, scan, flags, 0, 0)
    return i


def press(name_or_vk, hold=0.03):
    vk = VK.get(name_or_vk, name_or_vk) if isinstance(name_or_vk, str) else name_or_vk
    _send(_key(vk))
    time.sleep(hold)
    _send(_key(vk, True))
    time.sleep(0.05)


def chord(mod, key):
    m, k = VK[mod], VK.get(key, key)
    _send(_key(m))
    time.sleep(0.03)
    _send(_key(k), _key(k, True))
    time.sleep(0.03)
    _send(_key(m, True))
    time.sleep(0.08)


def type_text(text, delay=0.035, before=None):
    """Types each character; before(), when given, runs ahead of every one (a check that the focus is still where the
    text should go, which raises to stop)."""
    for ch in text:
        if before is not None:
            before()
        r = user32.VkKeyScanW(ord(ch))
        if r == -1 or (r & 0xFF) == 0xFF or (r >> 8) & 0x6:  # not on the layout, or needs Ctrl/Alt: send the character
            _send(_key(0, scan=ord(ch), uni=True), _key(0, True, scan=ord(ch), uni=True))
        else:
            vk, shift = r & 0xFF, (r >> 8) & 1
            seq = ([_key(VK['shift'])] if shift else []) + [_key(vk), _key(vk, True)] + ([_key(VK['shift'], True)] if shift else [])
            _send(*seq)
        time.sleep(delay)


def _mouse(x, y, flags):
    vx, vy, vw, vh = (user32.GetSystemMetrics(i) for i in (76, 77, 78, 79))
    i = INPUT(type=INPUT_MOUSE)
    i.u.mi = MOUSEINPUT(int((x - vx) * 65535 / (vw - 1)), int((y - vy) * 65535 / (vh - 1)), 0, flags | 0x8000 | 0x4000 | 0x1, 0, 0)
    _send(i)


def click(x, y):
    _mouse(x, y, 0)
    time.sleep(0.08)
    _mouse(x, y, 0x2)
    time.sleep(0.06)
    _mouse(x, y, 0x4)
    time.sleep(0.15)


def drag(x1, y1, x2, y2, steps=12):
    _mouse(x1, y1, 0)
    time.sleep(0.1)
    _mouse(x1, y1, 0x2)
    time.sleep(0.1)
    for s in range(1, steps + 1):
        _mouse(x1 + (x2 - x1) * s // steps, y1 + (y2 - y1) * s // steps, 0)
        time.sleep(0.03)
    _mouse(x2, y2, 0x4)
    time.sleep(0.3)


# ---------- windows ----------
def find_window(title, timeout=15):
    end = time.time() + timeout
    while time.time() < end:
        h = user32.FindWindowW(None, title)
        if h:
            return h
        time.sleep(0.2)
    raise TimeoutError(title)


def rect(hwnd):
    r = W.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    return r.left, r.top, r.right, r.bottom


def child_edit(hwnd, timeout=15):
    """The window's text box; waits for it, since a form gets its title before its controls."""
    proc = ctypes.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
    end = time.time() + timeout
    while True:
        found = []

        def cb(h, _):
            buf = ctypes.create_unicode_buffer(256)
            user32.GetClassNameW(h, buf, 256)
            if 'EDIT' in buf.value.upper():
                found.append(h)
            return True

        user32.EnumChildWindows(hwnd, proc(cb), 0)
        if found:
            return found[0]
        if time.time() > end:
            raise TimeoutError('no text box in the window')
        time.sleep(0.2)


def get_text(hwnd):
    n = user32.SendMessageW(hwnd, 0x000E, 0, 0)  # WM_GETTEXTLENGTH
    buf = ctypes.create_unicode_buffer(n + 1)
    user32.SendMessageW(hwnd, 0x000D, n + 1, ctypes.addressof(buf))  # WM_GETTEXT
    return buf.value


def set_text(hwnd, text):
    buf = ctypes.create_unicode_buffer(text)
    user32.SendMessageW(hwnd, 0x000C, 0, ctypes.addressof(buf))  # WM_SETTEXT


def uia(pid):
    """Every element of the process's windows. A dump that fails or does not decode raises, so a broken read is never
    taken for a window with nothing in it."""
    here = os.path.dirname(os.path.abspath(__file__))
    out = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(here, 'uia_dump.ps1'),
                          '-ProcessId', str(pid)], capture_output=True, timeout=60)
    if out.returncode != 0:
        raise RuntimeError('uia_dump.ps1 failed: ' + out.stderr.decode('utf-8', 'replace').strip())
    return json.loads(out.stdout.decode('utf-8') or '[]')


def uia_press(pid, window, name, control_type=''):
    """Presses a control by its name through UI Automation (Toggle, else Invoke); True when it was found."""
    here = os.path.dirname(os.path.abspath(__file__))
    args = ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(here, 'uia_press.ps1'),
            '-ProcessId', str(pid), '-Window', window, '-Name', name]
    if control_type:
        args += ['-ControlType', control_type]
    out = subprocess.run(args, capture_output=True, text=True, encoding='utf-8', timeout=60)
    return out.returncode == 0
