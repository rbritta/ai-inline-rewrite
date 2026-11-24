"""Smoke test of the real app on the real desktop: types into a text box of its own, uses the AIR key, pick mode,
the follow arrow, the settings and their engine picker, and checks what happened through UI Automation and the text
box's content.

It takes over the keyboard and mouse for about 3 minutes: run it on an unlocked desktop nobody is using. It stops
at once if the focus leaves its own windows (someone clicked elsewhere) or the desktop is locked. Anything else that
breaks the run (an element that never shows up, a read that fails) ends it as one FAIL naming the last check that
passed, with the traceback in error.txt in the output folder. The user's AIR settings are saved first and put back
at the end.

    python tools/smoke/smoke_test.py <AiInlineRewrite.exe> <output folder for the screenshots>

Needs Python 3 with Pillow, and the engine in the settings ready (Ollama with the model, or an Anthropic key).
The engine picker checks switch to Claude and expect its no-key state, so they run only when the engine is Ollama
and this PC has no Anthropic credential; otherwise they print SKIP.
"""
import ctypes
import ctypes.wintypes as W
import json
import os
import shutil
import subprocess
import sys
import time
import traceback

from PIL import ImageGrab

import winput as wi

EXE, OUT = sys.argv[1], sys.argv[2]
# The bubble's statuses carry "·" and arrows; redirected to a file, the console code page cannot print them.
sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
SETTINGS = os.path.join(os.environ['APPDATA'], 'AiInlineRewrite', 'settings.json')
SETTINGS_WIN = 'AI Inline Rewrite Settings'
EN = 'i dont think we can finish this until friday'
PT = 'acho que a gente precisa conversar sobre o prazo amanhã'
results = []


class Abort(Exception):
    pass


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (f'  [{detail}]' if detail else ''), flush=True)


def within(seconds, probe):
    """Polls until the probe holds, at most that long: the waits follow the app instead of a fixed sleep."""
    end = time.time() + seconds
    while True:
        if probe():
            return True
        if time.time() > end:
            return False
        time.sleep(0.25)


def find(items, match, what):
    """The first item that matches, or an error that names what was missing (rather than a bare StopIteration)."""
    for e in items:
        if match(e):
            return e
    raise LookupError(f'{what} not found')


def desktop_unlocked():
    h = wi.user32.OpenInputDesktop(0, False, 0x0100)
    if not h:
        return False
    wi.user32.CloseDesktop(h)
    title = ctypes.create_unicode_buffer(256)
    wi.user32.GetWindowTextW(wi.user32.GetForegroundWindow(), title, 256)
    return title.value != 'Windows Default Lock Screen'  # the lock screen, before the password prompt


def guard(pids):
    if not desktop_unlocked():
        raise Abort('the desktop is locked')
    pid = W.DWORD()
    wi.user32.GetWindowThreadProcessId(wi.user32.GetForegroundWindow(), ctypes.byref(pid))
    if pid.value not in pids:
        raise Abort(f'the focus left the test windows (foreground process {pid.value})')


wi.user32.WindowFromPoint.argtypes = [W.POINT]
wi.user32.WindowFromPoint.restype = W.HWND


def aim(x, y, pids):
    """Stops when another window (an updater, a notification) sits where the mouse is about to press."""
    pid = W.DWORD()
    wi.user32.GetWindowThreadProcessId(wi.user32.WindowFromPoint(W.POINT(x, y)), ctypes.byref(pid))
    if pid.value not in pids:
        raise Abort(f'another window (process {pid.value}) covers the point to click, {x},{y}')


def anthropic_credential():
    """Whether AIR, started from here, would find an Anthropic credential: the variables the SDK reads, or a profile
    such as the one `ant auth login` saves."""
    names = ('ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'ANTHROPIC_PROFILE', 'ANTHROPIC_FEDERATION_RULE_ID')
    if any(os.environ.get(n) for n in names):
        return True
    config = os.environ.get('ANTHROPIC_CONFIG_DIR') or os.path.join(os.environ['APPDATA'], 'Anthropic')
    return os.path.isdir(os.path.join(config, 'configs')) or os.path.exists(os.path.join(config, 'active_config'))


def shot(name, box=None):
    ImageGrab.grab(bbox=box, all_screens=True).save(os.path.join(OUT, name))


def wake(path):
    """AIR asleep (a Right Shift tap, or off in the tray) shows no bubble, so the run starts it awake; the backup puts
    the user's state back at the end."""
    with open(path, encoding='utf-8-sig') as f:
        s = json.load(f)
    if s.get('On') is False:
        s['On'] = True
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(s, f, indent=2)


def main():
    if not desktop_unlocked():
        print('The desktop is locked: nothing to drive. Unlock it and run again.')
        return 2
    os.makedirs(OUT, exist_ok=True)
    backup = None
    if os.path.exists(SETTINGS):
        backup = os.path.join(OUT, 'settings.backup.json')
        shutil.copyfile(SETTINGS, backup)
        wake(SETTINGS)

    air = subprocess.Popen([EXE, '--accept-injected'])
    tgt = subprocess.Popen(['powershell', '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(HERE, 'target.ps1')])
    pids = {air.pid, tgt.pid}
    try:
        run(air.pid, tgt.pid, pids)
    except Abort as e:
        check('run to the end', False, str(e))
    except Exception as e:
        with open(os.path.join(OUT, 'error.txt'), 'w', encoding='utf-8') as f:
            traceback.print_exc(file=f)
        last = next((r[0] for r in reversed(results) if r[1]), 'the start')
        check('run to the end', False, f'{type(e).__name__} after "{last}": {e} (traceback in error.txt)')
    finally:
        air.kill()
        air.wait(timeout=10)  # gone before its settings file is put back
        subprocess.run(['taskkill', '/PID', str(tgt.pid), '/T', '/F'], capture_output=True)
        if backup:
            shutil.copyfile(backup, SETTINGS)
    failed = [r for r in results if not r[1]]
    print(f'\n{len(results) - len(failed)} of {len(results)} checks passed')
    return 1 if failed else 0


def run(air_pid, tgt_pid, pids):
    form = wi.find_window('AIR smoke test')
    edit = wi.child_edit(form)
    l, t, r, b = wi.rect(form)

    def ui():
        return [e for e in wi.uia(air_pid) if e['win'] == 'AI Inline Rewrite']

    def status():
        return ' '.join(e['name'] for e in ui() if e['type'] == 'Text' and e['name'].startswith('·'))

    def rows():
        return [e for e in ui() if e['type'] == 'ListItem']

    def row_text(e):
        return e['name'].split(': ', 1)[1]

    def button(name):
        return find(ui(), lambda e: e['type'] == 'Button' and e['name'] == name, f'the bubble button "{name}"')

    def follow():
        return button('Follow the text cursor')['toggle']

    def center(e):
        return e['x'] + e['w'] // 2, e['y'] + e['h'] // 2

    def click(x, y):
        aim(x, y, pids)
        wi.click(x, y)

    def wait_ready(timeout=90):
        end = time.time() + timeout
        while time.time() < end:
            s = status()
            if any(k in s for k in ('corrected', 'Translated', 'Looks right', 'not answering', 'model')):
                return s
            time.sleep(1)
        return status()

    def focus_field():
        click((l + r) // 2, (t + b) // 2)
        guard(pids)

    def fresh(text):
        wi.set_text(edit, '')
        focus_field()
        wi.press('end')
        type_here(text)
        # a window that took the focus for a moment gets some of the keys: the box then misses them, and every check
        # after this one would fail for the wrong reason
        if not within(3, lambda: wi.get_text(edit) == text):
            raise Abort(f'the typing was disturbed (the focus left for a moment): the box holds {wi.get_text(edit)!r}')
        return wait_ready()

    def type_here(text):
        """Types into the focused test window, checking before every key that it still has the focus."""
        wi.type_text(text, before=lambda: guard(pids))

    def picking():
        return 'Esc' in status()

    def tap():
        """The AIR key; True once pick mode shows."""
        guard(pids)
        wi.press('rctrl')
        return within(3, picking)

    def selected():
        return next((i for i, e in enumerate(rows()) if e['status'] == 'Selected'), None)

    def settings_ui():
        return [e for e in wi.uia(air_pid) if e['win'] == SETTINGS_WIN]

    def settings_say(text):
        return any(e['type'] == 'Text' and e['name'] == text for e in settings_ui())

    # the app is up once its bubble answers through UI Automation
    if not within(30, lambda: any(e['type'] == 'Button' and e['name'] == 'Follow the text cursor' for e in ui())):
        raise LookupError('the bubble did not show up within 30 s')

    # the follow arrow: on, so the bubble comes to the text cursor
    focus_field()
    if follow() != 'On':
        click(*center(button('Follow the text cursor')))
    check('follow arrow on', within(3, lambda: follow() == 'On'))

    # English: corrected versions, pick mode with the arrows and Enter
    s = fresh(EN)
    rs = rows()
    check('English sample written', 'corrected' in s and len(rs) >= 3, f'{s} | ' + ' / '.join(row_text(x) for x in rs))
    bub = wi.find_window('AI Inline Rewrite')
    shot('1-english.png', wi.rect(bub))
    check('AIR key opens pick mode on the first version', tap() and selected() == 0, status())
    shot('2-pick.png', wi.rect(bub))
    guard(pids); wi.press('up')
    check('up on the first goes to the last', within(3, lambda: selected() == len(rs) - 1), str(selected()))
    guard(pids); wi.press('down')
    check('down on the last goes back to the first', within(3, lambda: selected() == 0), str(selected()))
    guard(pids); wi.press('down')
    within(3, lambda: selected() == 1)
    want = row_text(rows()[1])
    guard(pids); wi.press('enter')
    check('Enter applies the selected version', within(3, lambda: wi.get_text(edit) == want),
          f'{wi.get_text(edit)!r} vs {want!r}')
    shot('3-applied.png', (l, t, r, b))
    guard(pids); wi.chord('ctrl', 'z')
    check('Ctrl+Z brings the typed sentence back', within(3, lambda: wi.get_text(edit) == EN), repr(wi.get_text(edit)))

    # Portuguese: translated, a number applies directly
    s = fresh(PT)
    rs = rows()
    check('Portuguese sample translated', 'Translated' in s and len(rs) >= 3, f'{s} | ' + ' / '.join(row_text(x) for x in rs))
    shot('4-portuguese.png', wi.rect(bub))
    want = row_text(rs[0])
    tap()
    guard(pids); wi.press(0x31)
    check('1 applies the first version', within(3, lambda: wi.get_text(edit) == want), repr(wi.get_text(edit)))

    # Esc closes, another key types, a chord is not a tap, 4 s close it
    fresh(EN)
    tap()
    guard(pids); wi.press('esc')
    check('Esc closes pick mode and leaves the text', within(3, lambda: not picking()) and wi.get_text(edit) == EN,
          status())
    tap()
    type_here('x')
    check('another key closes pick mode and types', within(3, lambda: wi.get_text(edit) == EN + 'x') and not picking(),
          repr(wi.get_text(edit)))
    guard(pids); wi.chord('rctrl', 'a')
    time.sleep(0.6)  # nothing to wait for: the check is that pick mode does not open
    check('Right Ctrl in a chord is not a tap', not picking(), status())
    pressed_at = time.time()
    opened = tap()
    time.sleep(max(0.0, pressed_at + 4.6 - time.time()))  # the 4 s are what this check measures
    check('pick mode closes after 4 s without a key', opened and not picking(), status())

    # the off key: a capital with Right Shift types; a tap of Right Shift alone puts AIR to sleep, the AIR key wakes it
    guard(pids); wi.chord('rshift', 'a')
    time.sleep(0.6)  # nothing to wait for: the check is that the bubble stays
    check('a capital with Right Shift is not the off key', wi.user32.IsWindowVisible(bub), status())
    guard(pids); wi.press('rshift')
    check('a tap of Right Shift puts AIR to sleep and hides the bubble',
          within(3, lambda: not wi.user32.IsWindowVisible(bub)))
    type_here(' ok')
    time.sleep(2)  # nothing to wait for: the check is that no bubble comes back while AIR sleeps
    check('typing while AIR sleeps shows nothing', not wi.user32.IsWindowVisible(bub))
    guard(pids); wi.press('rctrl')
    check('the AIR key wakes AIR and the bubble is back', within(3, lambda: wi.user32.IsWindowVisible(bub)), status())

    # drag the bubble: follow turns off; the arrow turns it back on
    air_label = find(ui(), lambda e: e['type'] == 'Text' and e['name'] == 'AIR', 'the AIR label of the bubble')
    x, y = center(air_label)
    aim(x, y, pids)
    wi.drag(x, y, x + 40, y + 160)
    check('dragging the bubble turns follow off', within(3, lambda: follow() == 'Off'))
    shot('5-dragged.png', wi.rect(bub))
    click(*center(button('Follow the text cursor')))
    check('the arrow turns follow back on', within(3, lambda: follow() == 'On'))

    # settings from the gear, closed with its X
    click(*center(button('Settings')))

    def settings_window():
        return wi.user32.FindWindowW(None, SETTINGS_WIN)

    shown = within(5, lambda: bool(settings_window()) and wi.user32.IsWindowVisible(settings_window())
                   and any(e['name'] == 'Close' for e in settings_ui()))
    sw = settings_window()
    check('the gear opens the settings', shown)
    if sw:
        # beside the bubble: side by side, overlapping in height (both windows have transparent room for the shadow)
        k = wi.user32.GetDpiForWindow(bub) / 96
        bl, bt, br, bb = [v + d * k for v, d in zip(wi.rect(bub), (20, 20, -20, -20))]
        sl, st, sr, sb = [v + d * k for v, d in zip(wi.rect(sw), (24, 24, -24, -24))]
        check('the settings open beside the bubble', (sl >= br or sr <= bl) and st < bb and sb > bt,
              f'bubble {bl:.0f},{bt:.0f},{br:.0f},{bb:.0f} settings {sl:.0f},{st:.0f},{sr:.0f},{sb:.0f}')
    switched = False
    if sw:
        shot('6-settings.png', wi.rect(sw))

        # the engine picker, on the Engine page, in the running app: Claude (no credential here, so its no-key state at
        # once), then back
        paged = wi.uia_press(air_pid, SETTINGS_WIN, 'Engine', 'TabItem')
        check('the menu opens the Engine page',
              paged and within(3, lambda: any(e['type'] == 'RadioButton' and e['name'] == 'Built-in' for e in settings_ui())),
              f'pressed {paged}')
        ollama = next((e for e in settings_ui() if e['type'] == 'RadioButton' and e['name'] == 'Ollama'), None)
        if anthropic_credential() or not ollama or ollama['toggle'] != 'On':
            print('SKIP engine picker: it needs Ollama as the engine and no Anthropic credential on this PC', flush=True)
        else:
            guard(pids)
            pressed = wi.uia_press(air_pid, SETTINGS_WIN, 'Claude', 'RadioButton')
            check('Engine Claude: the settings say No Anthropic API key at once',
                  pressed and within(5, lambda: settings_say('No Anthropic API key')), f'pressed {pressed}')
            check('Engine Claude: the bubble says No Anthropic API key, same process, no restart',
                  within(5, lambda: 'No Anthropic API key' in status()), status())
            model = next((e for e in settings_ui() if e['name'] == 'Model'), None)
            check('Engine Claude: the model list offers the Claude models',
                  model is not None and (model['status'] or '').startswith('Claude'), model and model['status'])
            shot('7-settings-claude.png', wi.rect(sw))
            guard(pids)
            pressed = wi.uia_press(air_pid, SETTINGS_WIN, 'Ollama', 'RadioButton')
            switched = pressed
            check('Engine Ollama again: the settings say Ready',
                  pressed and within(15, lambda: settings_say('Ready')), f'pressed {pressed}')

        close = find(settings_ui(), lambda e: e['name'] == 'Close', 'the X of the settings')
        click(*center(close))
        check('the X closes the settings', within(3, lambda: not wi.user32.IsWindowVisible(sw)))

    if switched:
        s = fresh(EN)
        rs = rows()
        check('Engine Ollama again: a fresh sample comes back corrected', 'corrected' in s and len(rs) >= 3,
              f'{s} | ' + ' / '.join(row_text(x) for x in rs))


if __name__ == '__main__':
    sys.exit(main())
